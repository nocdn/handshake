using HsWin.Core.Alerts;
using HsWin.Core.Applications;
using HsWin.Core.Commands;
using HsWin.Core.Config;
using HsWin.Core.Hotkeys;
using HsWin.Core.Http;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Scripting;
using HsWin.App.Audio;
using HsWin.App.Clipboard;
using HsWin.App.Hotkeys;
using HsWin.App.Input;
using HsWin.App.Keyboard;
using HsWin.App.Media;
using HsWin.App.Mouse;
using HsWin.App.Scripting;
using HsWin.App.Shell;
using HsWin.App.Timers;
using HsWin.App.Windows;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace HsWin.App;

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
    private readonly DispatcherScriptTimerService _timerService;
    private readonly NativeClipboardService _clipboardService;
    private readonly ProcessShellService _shellService;
    private readonly NativeAudioDeviceController _audioDeviceController;
    private readonly NativeAudioCaptureService _audioCaptureService;
    private readonly NativeMouseService _mouseService;
    private readonly NativeMouseEventService _mouseEventService;
    private readonly NativeWindowService _windowService;
    private readonly ScriptRuntime _scriptRuntime;
    private readonly StartupService _startupService;
    private readonly CliInstallService _cliInstallService;
    private readonly HsWinCommandServer _commandServer;
    private readonly TrayIconService _trayIconService;
    private readonly Dispatcher _dispatcher;
    private readonly object _scriptReloadGate = new();
    private readonly IDisposable _emergencyStopHotkey;

    private int _reloadGeneration;
    private int _cliInstallInProgress;
    private int _emergencyStopInProgress;
    private int _emergencyStopChordLatched;
    private bool _disposed;

    private const uint EmergencyStopVirtualKey = 0x1B; // VK_ESCAPE
    private const HotkeyModifiers EmergencyStopModifiers =
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    /// <summary>Ctrl+Alt+Shift+Esc — host-owned, not part of config.js.</summary>
    internal static readonly HotkeyDefinition EmergencyStopHotkeyDefinition =
        HotkeyDefinition.CreateKeyboard(EmergencyStopModifiers, EmergencyStopVirtualKey);

    public AppController()
    {
        var paths = HsWinPaths.FromAppData();
        _configFileService = new ConfigFileService(paths.ConfigFilePath);
        _logger = FileLogger.CreateForLaunch(paths.RuntimeLogDirectory);
        _singleInstanceGuard = SingleInstanceGuard.Acquire(_logger);
        _scriptConsoleLogger = new ReloadScriptConsoleLogger(paths.ConfigLogDirectory);
        _toastPresenter = new ToastPresenter(_logger);
        _keyboardEventService = new NativeKeyboardEventService(_logger);
        _hotkeyService = new NativeHotkeyService(_logger, _keyboardEventService);
        _keyboardInputService = new KeyboardInputService(_logger, _keyboardEventService);
        _mouseInputService = new MouseInputService(_logger);
        _timerService = new DispatcherScriptTimerService(WpfApplication.Current.Dispatcher);
        _clipboardService = new NativeClipboardService(WpfApplication.Current.Dispatcher, _logger);
        _shellService = new ProcessShellService(_logger);
        _audioDeviceController = new NativeAudioDeviceController(_logger);
        _audioCaptureService = new NativeAudioCaptureService(paths.RecordingDirectory, _logger);
        _mouseService = new NativeMouseService();
        // Share the single WH_MOUSE_LL host with mouse-button hotkeys (no second global hook).
        _mouseEventService = new NativeMouseEventService(_hotkeyService.MouseHook);
        _dispatcher = WpfApplication.Current.Dispatcher;
        _windowService = new NativeWindowService(
            _logger,
            new WindowHookThreadScheduler(new DispatcherSynchronizationContext(_dispatcher)));
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
            MouseEvents = _mouseEventService,
            Timers = _timerService,
            CallbackScheduler = new DispatcherScriptCallbackScheduler(WpfApplication.Current.Dispatcher),
            Clipboard = _clipboardService,
            Shell = _shellService,
            AudioDevices = _audioDeviceController,
            AudioCapture = _audioCaptureService,
            Mouse = _mouseService,
            Windows = _windowService,
            Http = new SystemHttpService(_logger),
            Logger = _logger
        });
        var executablePath = ResolveExecutablePath();
        _startupService = new StartupService(AppBranding.DisplayName, executablePath, "HsWin");
        _cliInstallService = new CliInstallService(
            Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("Could not resolve the current executable directory."));
        _commandServer = new HsWinCommandServer(HandleCommand, _logger);
        _trayIconService = new TrayIconService(
            openConfig: OpenConfig,
            reloadConfig: ReloadConfig,
            emergencyStop: EmergencyStop,
            isStartAtLoginEnabled: _startupService.IsEnabled,
            setStartAtLoginEnabled: SetStartAtLoginEnabled,
            isCliInstalled: _cliInstallService.IsInstalled,
            installCli: InstallCli,
            quit: Quit);

        // Dual path for emergency stop:
        // 1) WH_KEYBOARD_LL priority (works when UI is wedged; swallows the chord before config)
        // 2) RegisterHotKey (works when the LL hook thread is stuck inside a blocking JS callback)
        _keyboardEventService.SetHostPriorityHandler(TryHandleEmergencyStopKey);
        _emergencyStopHotkey = _hotkeyService.Register(EmergencyStopHotkeyDefinition, EmergencyStop);
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

    private HsWinCommandResponse HandleCommand(HsWinCommandRequest request)
    {
        _logger.Info($"Command requested name='{request.Command}'.");
        return request.Command switch
        {
            HsWinCommandNames.ConfigReload => HandleConfigReloadCommand(),
            _ => HsWinCommandResponse.Error($"Unknown command: {request.Command}")
        };
    }

    private HsWinCommandResponse HandleConfigReloadCommand()
    {
        ReloadConfig();
        return HsWinCommandResponse.Ok("Config reload requested.");
    }

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

    /// <summary>
    /// Host safety valve: stop all injected input immediately, then tear down the script runtime
    /// on a ThreadPool worker (never block the tray UI). Bound to Ctrl+Alt+Shift+Esc and tray.
    /// </summary>
    public void EmergencyStop()
    {
        RequestEmergencyStop(source: "tray/RegisterHotKey");
    }

    /// <summary>
    /// Keyboard-hook path: match Ctrl+Alt+Shift+Esc, kill injection on this thread, schedule full stop.
    /// Returns true to swallow the key so games/apps never see it.
    /// </summary>
    private bool TryHandleEmergencyStopKey(KeyboardEventSnapshot snapshot)
    {
        if (snapshot.IsInjected)
        {
            return false;
        }

        // Clear latch on Escape key-up so a later press can trigger again.
        if (snapshot.IsKeyUp && snapshot.KeyCode == EmergencyStopVirtualKey)
        {
            Volatile.Write(ref _emergencyStopChordLatched, 0);
            return false;
        }

        if (!snapshot.IsKeyDown || snapshot.KeyCode != EmergencyStopVirtualKey)
        {
            return false;
        }

        var modifiers = (HotkeyModifiers)snapshot.ModifierFlags;
        if ((modifiers & EmergencyStopModifiers) != EmergencyStopModifiers)
        {
            return false;
        }

        // Debounce auto-repeat while Esc is held.
        if (Interlocked.Exchange(ref _emergencyStopChordLatched, 1) != 0)
        {
            return true;
        }

        _logger.Warning("Emergency stop chord matched on keyboard hook (Ctrl+Alt+Shift+Esc).");
        RequestEmergencyStop(source: "keyboard-hook");
        return true;
    }

    private void RequestEmergencyStop(string source)
    {
        // Always kill injected input first — even if a full stop is already in progress.
        KillInjectedInputImmediate();

        if (Interlocked.CompareExchange(ref _emergencyStopInProgress, 1, 0) != 0)
        {
            _logger.Info($"Emergency stop ({source}): input killed; full teardown already in progress.");
            return;
        }

        _logger.Warning($"Emergency stop requested ({source}).");
        try
        {
            if (!ThreadPool.QueueUserWorkItem(static state => ((AppController)state!).RunEmergencyStopWorker(), this))
            {
                _logger.Warning("Emergency stop: ThreadPool.QueueUserWorkItem returned false; running teardown inline.");
                RunEmergencyStopWorker();
            }
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _emergencyStopInProgress, 0);
            _logger.Error("Emergency stop: failed to queue worker.", exception);
            // Still attempt inline teardown so a queue failure is not a dead end.
            try
            {
                RunEmergencyStopWorker();
            }
            catch (Exception inlineException)
            {
                _logger.Error("Emergency stop: inline teardown failed.", inlineException);
            }
        }
    }

    private void RunEmergencyStopWorker()
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            if (_disposed)
            {
                return;
            }

            KillInjectedInputImmediate();
            TeardownScriptRuntimeBestEffort();
            if (!_disposed)
            {
                ShowEmergencyStopToast("Stopped");
            }

            _logger.Warning(
                $"Emergency stop worker completed elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop worker failed.", exception);
            if (!_disposed)
            {
                ShowEmergencyStopToast($"Emergency stop failed: {exception.Message}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _emergencyStopInProgress, 0);
        }
    }

    private void KillInjectedInputImmediate()
    {
        try
        {
            _mouseInputService.StopActiveRepeat();
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop: mouse StopActiveRepeat failed.", exception);
        }

        try
        {
            _keyboardInputService.StopActiveRepeat();
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop: keyboard StopActiveRepeat failed.", exception);
        }
    }

    private void TeardownScriptRuntimeBestEffort()
    {
        var lockTaken = false;
        try
        {
            // Do not hang forever if ReloadConfig holds the gate on a stuck Execute.
            Monitor.TryEnter(_scriptReloadGate, TimeSpan.FromMilliseconds(500), ref lockTaken);
            if (!lockTaken)
            {
                _logger.Warning(
                    "Emergency stop: script reload gate busy; interrupting engine without full lock, then retrying dispose.");
                try
                {
                    // Interrupt is safe without the gate; dispose still needs exclusive access.
                    _scriptRuntime.InterruptEngineOnly();
                }
                catch (Exception exception)
                {
                    _logger.Error("Emergency stop: InterruptEngineOnly failed.", exception);
                }

                Monitor.TryEnter(_scriptReloadGate, TimeSpan.FromSeconds(2), ref lockTaken);
            }

            if (lockTaken)
            {
                _scriptRuntime.EmergencyStop();
            }
            else
            {
                _logger.Warning(
                    "Emergency stop: could not acquire script gate for full teardown; injected input is stopped. Reload or quit the app.");
            }
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_scriptReloadGate);
            }
        }
    }

    private void ShowEmergencyStopToast(string message)
    {
        // Success path uses a short plain toast; failures stay error-styled.
        var kind = string.Equals(message, "Stopped", StringComparison.Ordinal)
            ? AlertKind.Normal
            : AlertKind.Error;
        var durationMs = kind == AlertKind.Normal ? 2000 : 6000;

        try
        {
            if (_dispatcher.CheckAccess())
            {
                _toastPresenter.Show(AlertRequest.Create(message, kind, durationMs));
                return;
            }

            _ = _dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _toastPresenter.Show(AlertRequest.Create(message, kind, durationMs));
                }
                catch (Exception exception)
                {
                    _logger.Error("Emergency stop toast failed.", exception);
                }
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop toast schedule failed.", exception);
        }
    }

    public void ReloadConfig()
    {
        var generation = Interlocked.Increment(ref _reloadGeneration);
        var startedAt = Stopwatch.GetTimestamp();
        _logger.Info($"Reload Config requested generation={generation}.");
        _toastPresenter.Show(ConfigReloadAlerts.CreateReloadingAlert());
        var toastBaseline = CaptureStandardToastBaseline();

        Task.Run(() =>
        {
            Exception? failure = null;
            var superseded = false;
            try
            {
                _configFileService.EnsureConfigFile();
                var configPath = _configFileService.ConfigFilePath;
                lock (_scriptReloadGate)
                {
                    if (generation != Volatile.Read(ref _reloadGeneration) || _disposed)
                    {
                        superseded = true;
                        _logger.Info($"Config reload superseded generation={generation}.");
                    }
                    else
                    {
                        var reloadStartedAt = Stopwatch.GetTimestamp();
                        _scriptRuntime.ReloadFromFile(configPath);
                        _logger.Info(
                            $"Script runtime reload completed generation={generation} elapsedMs={Stopwatch.GetElapsedTime(reloadStartedAt).TotalMilliseconds:F3}.");
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (superseded)
            {
                return;
            }

            var remainingReloadingTime = TimeSpan.FromMilliseconds(ConfigReloadAlerts.MinimumReloadingVisibleMs) - Stopwatch.GetElapsedTime(startedAt);
            if (remainingReloadingTime > TimeSpan.Zero)
            {
                Thread.Sleep(remainingReloadingTime);
            }

            _dispatcher.BeginInvoke(() => CompleteReloadOnDispatcher(generation, failure, startedAt, toastBaseline));
        });
    }

    /// <summary>
    /// Counts the reload loader itself, so only toasts shown after it move
    /// the baseline. Barriers through the dispatcher off-thread at Send
    /// priority — the loader <see cref="ToastPresenter.Show"/> is queued just
    /// before at the same priority, so it is always counted first. Exact
    /// when already on the dispatcher.
    /// </summary>
    private long CaptureStandardToastBaseline()
    {
        if (_dispatcher.CheckAccess())
        {
            return _toastPresenter.StandardContentShows;
        }

        return _dispatcher.Invoke(
            () => _toastPresenter.StandardContentShows,
            DispatcherPriority.Send);
    }

    private void CompleteReloadOnDispatcher(int generation, Exception? failure, long startedAt, long toastBaseline)
    {
        if (_disposed || generation != Volatile.Read(ref _reloadGeneration))
        {
            return;
        }

        if (failure is not null)
        {
            _logger.Error("Config reload failed.", failure);
            _toastPresenter.Show(ConfigReloadAlerts.CreateReloadFailedAlert(failure));
            return;
        }

        if (_toastPresenter.StandardContentShows != toastBaseline)
        {
            _logger.Info(
                $"Config reloaded toast skipped generation={generation}: a newer toast is already showing.");
            return;
        }

        _logger.Info(
            $"Config reloaded generation={generation} elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}. " +
            $"Console log: {_scriptConsoleLogger.CurrentLogFilePath}");
        _toastPresenter.Show(ConfigReloadAlerts.CreateReloadedAlert());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Increment(ref _reloadGeneration);

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

        try
        {
            _mouseInputService.StopActiveRepeat();
            _keyboardInputService.StopActiveRepeat();
        }
        catch (Exception exception)
        {
            _logger.Error("Shutdown StopActiveRepeat failed.", exception);
        }

        lock (_scriptReloadGate)
        {
            _scriptRuntime.Dispose();
        }

        _logger.Info("Script runtime disposed.");
        _hotkeyService.Dispose();
        _logger.Info("Hotkey service disposed.");
        _keyboardEventService.Dispose();
        _logger.Info("Keyboard event service disposed.");
        // Mouse scroll watches live on the hotkey mouse hook; disposing hotkeys tears them down.
        _clipboardService.Dispose();
        _logger.Info("Clipboard service disposed.");
        _windowService.Dispose();
        _logger.Info("Window service disposed.");
        _toastPresenter.Dispose();
        _logger.Info("Toast presenter disposed.");
        _singleInstanceGuard.Dispose();
        _logger.Info("Single instance guard disposed.");
        _scriptConsoleLogger.Dispose();
        _disposed = true;
        _logger.Dispose();
    }

    private void InstallCli()
    {
        if (Interlocked.Exchange(ref _cliInstallInProgress, 1) != 0)
        {
            _logger.Info("hspn CLI install request ignored because an install is already in progress.");
            _toastPresenter.Show(CliInstallAlerts.CreateInstallingAlert());
            return;
        }

        _logger.Info("hspn CLI install requested.");
        _toastPresenter.Show(CliInstallAlerts.CreateInstallingAlert());

        Task.Run(() =>
        {
            CliInstallResult? result = null;
            Exception? failure = null;

            try
            {
                result = _cliInstallService.Install();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (_disposed)
            {
                Interlocked.Exchange(ref _cliInstallInProgress, 0);
                return;
            }

            _dispatcher.BeginInvoke(() => CompleteCliInstall(result, failure));
        });
    }

    private void CompleteCliInstall(CliInstallResult? result, Exception? failure)
    {
        Interlocked.Exchange(ref _cliInstallInProgress, 0);
        if (_disposed)
        {
            return;
        }

        if (failure is not null)
        {
            _logger.Error("Installing hspn CLI failed.", failure);
            _toastPresenter.Show(CliInstallAlerts.CreateInstallFailedAlert(failure));
            return;
        }

        if (result == CliInstallResult.AlreadyInstalled)
        {
            _logger.Info("hspn CLI already installed.");
            _toastPresenter.Show(CliInstallAlerts.CreateAlreadyInstalledAlert());
            return;
        }

        _logger.Info($"hspn CLI installed path='{_cliInstallService.CliPath}'.");
        _toastPresenter.Show(CliInstallAlerts.CreateInstalledAlert());
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

    internal static AlertRequest CreateConfigReloadedAlert() => ConfigReloadAlerts.CreateReloadedAlert();

    internal static AlertRequest CreateConfigReloadingAlert() => ConfigReloadAlerts.CreateReloadingAlert();

    private static string ResolveExecutablePath()
    {
        return Environment.ProcessPath
            ?? Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Could not resolve the current executable path.");
    }
}
