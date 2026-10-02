using HsWin.Core.Alerts;
using HsWin.Core.Applications;
using HsWin.Core.Commands;
using HsWin.Core.Config;
using HsWin.Core.Http;
using HsWin.Core.Logging;
using HsWin.Core.Scripting;
using HsWin.App.Audio;
using HsWin.App.Clipboard;
using HsWin.App.Hotkeys;
using HsWin.App.Input;
using HsWin.App.Keyboard;
using HsWin.App.Lifecycle;
using HsWin.App.Media;
using HsWin.App.Magnifier;
using HsWin.App.Mouse;
using HsWin.App.Scripting;
using HsWin.App.Shell;
using HsWin.App.Threading;
using HsWin.App.Timers;
using HsWin.App.Windows;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using WpfApplication = System.Windows.Application;

namespace HsWin.App;

/// <summary>
/// The composition root: builds the native services and script runtime, wires the tray and command
/// pipe, and tears everything down in dependency order. Reload, emergency stop and CLI install live
/// in their own coordinators.
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly ConfigFileService _configFileService;
    private readonly FileLogger _logger;
    private readonly SingleInstanceGuard _singleInstanceGuard;
    private readonly ReloadScriptConsoleLogger _scriptConsoleLogger;
    private readonly ToastPresenter _toastPresenter;
    private readonly NativeHotkeyService _hotkeyService;
    private readonly NativeKeyboardEventService _keyboardEventService;
    private readonly KeyboardInputService _keyboardInputService;
    private readonly MouseInputService _mouseInputService;
    private readonly PreciseScriptTimerService _timerService;
    private readonly NativeClipboardService _clipboardService;
    private readonly NativeMouseHotkeyHook _mouseHook;
    private readonly NativeWindowService _windowService;
    private readonly NativeMagnifierService _magnifierService;
    private readonly ScriptRuntime _scriptRuntime;
    private readonly StartupService _startupService;
    private readonly CliInstallService _cliInstallService;
    private readonly HsWinCommandServer _commandServer;
    private readonly TrayIconService _trayIconService;
    private readonly IInputKillSwitch _inputKillSwitch;
    private readonly ScriptReloadCoordinator _reload;
    private readonly EmergencyStopController _emergencyStop;
    private readonly CliInstallCoordinator _cliInstall;
    private readonly IDisposable _emergencyStopHotkey;

    public AppController()
    {
        var paths = HsWinPaths.FromAppData();
        var dispatcher = WpfApplication.Current.Dispatcher;
        var ui = new DispatcherUiThread(dispatcher);
        _configFileService = new ConfigFileService(paths.ConfigFilePath);
        _logger = FileLogger.CreateForLaunch(paths.RuntimeLogDirectory);
        PreciseTimerScheduler.UseLogger(_logger);
        LogRetentionTask.Start(_logger, paths.RuntimeLogDirectory, paths.ConfigLogDirectory);
        _singleInstanceGuard = SingleInstanceGuard.Acquire(_logger);
        _scriptConsoleLogger = new ReloadScriptConsoleLogger(paths.ConfigLogDirectory);
        _toastPresenter = new ToastPresenter(_logger);
        _keyboardEventService = new NativeKeyboardEventService(_logger);
        // One WH_MOUSE_LL host shared by mouse-button hotkeys and scroll watches (no second global hook).
        _mouseHook = new NativeMouseHotkeyHook(
            _logger,
            Win32MouseHookPlatform.Instance,
            SynchronizationContext.Current,
            keyboardEvents: _keyboardEventService);
        _hotkeyService = new NativeHotkeyService(_logger, _mouseHook);
        _keyboardInputService = new KeyboardInputService(_logger, _keyboardEventService);
        _mouseInputService = new MouseInputService(_logger);
        _timerService = new PreciseScriptTimerService(dispatcher);
        _clipboardService = new NativeClipboardService(dispatcher, _logger);
        _magnifierService = new NativeMagnifierService(_logger);
        var mouseEventService = new NativeMouseEventService(_mouseHook);
        _windowService = new NativeWindowService(_logger);
        _scriptRuntime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = _toastPresenter,
            Hotkeys = _hotkeyService,
            Console = _scriptConsoleLogger,
            Applications = new ProcessApplicationProvider(_logger),
            Media = new NativeMediaController(_logger),
            KeyboardEvents = _keyboardEventService,
            KeyboardInput = _keyboardInputService,
            MouseInput = _mouseInputService,
            MouseEvents = mouseEventService,
            MouseButtons = mouseEventService,
            Timers = _timerService,
            CallbackScheduler = new DispatcherScriptCallbackScheduler(dispatcher),
            Clipboard = _clipboardService,
            Shell = new ProcessShellService(_logger),
            AudioDevices = new NativeAudioDeviceController(_logger),
            AudioCapture = new NativeAudioCaptureService(paths.RecordingDirectory, _logger),
            Mouse = new NativeMouseService(),
            Windows = _windowService,
            Magnifier = _magnifierService,
            Http = new SystemHttpService(_logger),
            Logger = _logger
        });

        _inputKillSwitch = new InputKillSwitch(_mouseInputService, _keyboardInputService, _logger);
        _reload = new ScriptReloadCoordinator(
            _logger,
            _configFileService,
            _scriptRuntime,
            _inputKillSwitch,
            _toastPresenter,
            ui,
            () => _scriptConsoleLogger.CurrentLogFilePath);
        _emergencyStop = new EmergencyStopController(_logger, _inputKillSwitch, _reload, _toastPresenter, ui);

        var executablePath = ResolveExecutablePath();
        _startupService = new StartupService(AppBranding.DisplayName, executablePath, "HsWin");
        _cliInstallService = new CliInstallService(
            Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("Could not resolve the current executable directory."));
        _cliInstall = new CliInstallCoordinator(_logger, _cliInstallService, _toastPresenter, ui);
        _commandServer = new HsWinCommandServer(HandleCommand, _logger);
        _trayIconService = new TrayIconService(new TrayMenuActions(
            OpenConfig: OpenConfig,
            ReloadConfig: ReloadConfig,
            EmergencyStop: () => _emergencyStop.Request("tray"),
            Quit: Quit,
            StartAtLogin: new TrayToggle(_startupService.IsEnabled, SetStartAtLoginEnabled),
            Cli: new TrayInstaller(_cliInstallService.IsInstalled, () => _ = _cliInstall.InstallAsync())));

        // Dual path for emergency stop:
        // 1) WH_KEYBOARD_LL priority (works when UI is wedged; swallows the chord before config)
        // 2) RegisterHotKey (works when the LL hook thread is stuck inside a blocking JS callback)
        _keyboardEventService.SetHostPriorityHandler(_emergencyStop.TryHandleKey);
        _emergencyStopHotkey = _hotkeyService.Register(
            EmergencyStopController.Hotkey,
            () => _emergencyStop.Request("RegisterHotKey"));
        _logger.Info("Emergency stop registered: Ctrl+Alt+Shift+Esc (keyboard hook + RegisterHotKey).");
    }

    public void Start()
    {
        var startupStartedAt = Stopwatch.GetTimestamp();
        try
        {
            _logger.Info($"Starting {AppBranding.DisplayName} processId={Environment.ProcessId}.");
            _logger.Info($"Runtime log: {_logger.LogFilePath}");
            _logger.Info($"Config file: {_configFileService.ConfigFilePath}");
            var configStartedAt = Stopwatch.GetTimestamp();
            _configFileService.EnsureConfigFile();
            _logger.Info($"Startup config ensure completed elapsedMs={Stopwatch.GetElapsedTime(configStartedAt).TotalMilliseconds:F3}.");
            _commandServer.Start();
            _logger.Info("Command server started.");
            var trayStartedAt = Stopwatch.GetTimestamp();
            _trayIconService.Show();
            _logger.Info($"Tray icon shown elapsedMs={Stopwatch.GetElapsedTime(trayStartedAt).TotalMilliseconds:F3}.");
            var prewarmStartedAt = Stopwatch.GetTimestamp();
            _toastPresenter.Prewarm();
            _logger.Info($"Toast presenter prewarmed elapsedMs={Stopwatch.GetElapsedTime(prewarmStartedAt).TotalMilliseconds:F3}.");
            ReloadConfig();
            _logger.Info($"Startup sequence queued config reload elapsedMs={Stopwatch.GetElapsedTime(startupStartedAt).TotalMilliseconds:F3}.");
        }
        catch (Exception exception)
        {
            _logger.Error("Startup failed.", exception);
            _toastPresenter.Show(AlertRequest.Create($"Startup failed: {exception.Message}", AlertKind.Error, 6000));
        }
    }

    public void ReloadConfig() => _ = _reload.ReloadAsync();

    public void OpenConfig()
    {
        try
        {
            _logger.Info("Open Config requested.");
            _configFileService.EnsureConfigFile();
            EditorLauncher.Open(_configFileService.ConfigFilePath);
            _logger.Info("Open Config completed.");
        }
        catch (Exception exception)
        {
            _logger.Error("Open config failed.", exception);
            _toastPresenter.Show(AlertRequest.Create($"Could not open config: {exception.Message}", AlertKind.Error, 6000));
        }
    }

    public void Dispose()
    {
        if (_reload.IsShutDown)
        {
            return;
        }

        _reload.BeginShutdown();
        _cliInstall.Shutdown();

        _trayIconService.Dispose();
        _logger.Info("Tray icon disposed.");
        _commandServer.Dispose();
        _logger.Info("Command server disposed.");

        try
        {
            _keyboardEventService.SetHostPriorityHandler(null);
            _logger.Info("Emergency stop keyboard priority handler cleared.");
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop keyboard priority handler clear failed.", exception);
        }

        try
        {
            _emergencyStopHotkey.Dispose();
            _logger.Info("Emergency stop RegisterHotKey disposed.");
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop RegisterHotKey dispose failed.", exception);
        }

        _inputKillSwitch.StopRepeats("Shutdown");
        _reload.DisposeRuntime();

        // After the runtime, so remaps released during its disposal are already accounted for, and
        // before the keyboard hook, whose disposal drains the deferred key-ups.
        _inputKillSwitch.ReleaseHeldKeys("Shutdown");

        _magnifierService.Dispose();
        _hotkeyService.Dispose();
        _logger.Info("Hotkey service disposed.");
        _mouseHook.Dispose();
        _logger.Info("Mouse hook disposed.");
        _keyboardEventService.Dispose();
        _logger.Info("Keyboard event service disposed.");
        _clipboardService.Dispose();
        _logger.Info("Clipboard service disposed.");
        _windowService.Dispose();
        _logger.Info("Window service disposed.");
        _toastPresenter.Dispose();
        _logger.Info("Toast presenter disposed.");
        _singleInstanceGuard.Dispose();
        _logger.Info("Single instance guard disposed.");
        _scriptConsoleLogger.Dispose();
        _logger.Dispose();
    }

    private HsWinCommandResponse HandleCommand(HsWinCommandRequest request)
    {
        _logger.Info($"Command requested name='{request.Command}'.");
        return request.Command switch
        {
            HsWinCommandNames.ConfigReload => HandleConfigReloadCommand(),
            HsWinCommandNames.AppQuit => HandleQuitCommand(),
            _ => HsWinCommandResponse.Error($"Unknown command: {request.Command}")
        };
    }

    private HsWinCommandResponse HandleConfigReloadCommand()
    {
        ReloadConfig();
        return HsWinCommandResponse.Ok("Config reload requested.");
    }

    private HsWinCommandResponse HandleQuitCommand()
    {
        // A newly launched instance asks this one to exit so held keys, repeats and child apps
        // are handled by a normal shutdown rather than a kill.
        _logger.Info("Quit requested by a newly launched instance.");
        WpfApplication.Current.Dispatcher.BeginInvoke(Quit);
        return HsWinCommandResponse.Ok("Quitting.");
    }

    private void SetStartAtLoginEnabled(bool enabled)
    {
        try
        {
            _startupService.SetEnabled(enabled);
            var state = enabled ? "enabled" : "disabled";
            _logger.Info($"Start at login {state}.");
            _toastPresenter.Show(AlertRequest.Create($"Start at login {state}.", AlertKind.Success, 2000));
        }
        catch (Exception exception)
        {
            _logger.Error("Updating start at login failed.", exception);
            _toastPresenter.Show(AlertRequest.Create($"Could not update start at login: {exception.Message}", AlertKind.Error, 6000));
        }
    }

    private static void Quit()
    {
        WpfApplication.Current.Shutdown();
    }

    private static string ResolveExecutablePath()
    {
        return Environment.ProcessPath
            ?? Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Could not resolve the current executable path.");
    }
}
