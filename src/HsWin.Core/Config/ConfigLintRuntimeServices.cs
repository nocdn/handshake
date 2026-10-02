using HsWin.Core.Alerts;
using HsWin.Core.Applications;
using HsWin.Core.Audio;
using HsWin.Core.Clipboard;
using HsWin.Core.Hotkeys;
using HsWin.Core.Http;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Media;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;
using HsWin.Core.Shell;
using HsWin.Core.Timers;
using HsWin.Core.Windows;

namespace HsWin.Core.Config;

internal static class ConfigLintRuntimeServices
{
    /// <summary>Lint never waits on real I/O, so a config still running after this is looping.</summary>
    public static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromSeconds(5);

    public static ScriptRuntimeServices Create(TimeSpan executionTimeout)
    {
        var shell = new LintShellService();
        return new ScriptRuntimeServices
        {
            Alerts = NullAlertPresenter.Instance,
            Hotkeys = LintHotkeyRegistrar.Instance,
            Console = NullScriptConsoleLogger.Instance,
            Applications = NullApplicationProvider.Instance,
            Media = LintMediaController.Instance,
            KeyboardEvents = NullKeyboardEventService.Instance,
            KeyboardInput = NullKeyboardInputService.Instance,
            Timers = LintTimerService.Instance,
            CallbackScheduler = NoopCallbackScheduler.Instance,
            Clipboard = LintClipboardService.Instance,
            Shell = shell,
            AudioDevices = LintAudioDeviceController.Instance,
            AudioCapture = LintAudioCaptureService.Instance,
            Mouse = NullMouseService.Instance,
            MouseInput = NullMouseInputService.Instance,
            MouseEvents = NullMouseEventService.Instance,
            Windows = NullWindowService.Instance,
            Http = LintHttpService.Instance,
            Logger = NullRuntimeLogger.Instance,
            ConfigExecutionTimeout = executionTimeout
        };
    }

    private sealed class LintHotkeyRegistrar : IHotkeyRegistrar
    {
        public static LintHotkeyRegistrar Instance { get; } = new();

        public IDisposable Register(HotkeyDefinition hotkey, Action pressed) => NoopDisposable.Instance;

        public IDisposable RegisterHeld(HotkeyDefinition hotkey, Action pressed, Action released, bool blocking) =>
            NoopDisposable.Instance;
    }

    private sealed class LintTimerService : IScriptTimerService
    {
        public static LintTimerService Instance { get; } = new();

        public IDisposable DoAfter(int delayMs, Action callback) => NoopDisposable.Instance;

        public IDisposable DoEvery(int intervalMs, Action callback) => NoopDisposable.Instance;
    }

    private sealed class NoopCallbackScheduler : IScriptCallbackScheduler
    {
        public static NoopCallbackScheduler Instance { get; } = new();

        public void Schedule(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
        }
    }

    private sealed class LintClipboardService : IClipboardService
    {
        public static LintClipboardService Instance { get; } = new();

        public string GetText() => string.Empty;

        public bool SetText(string text) => true;

        public IDisposable Watch(Action<ClipboardChangeSnapshot> callback) => NoopDisposable.Instance;
    }

    private sealed class LintShellService : IShellService
    {
        public ShellExecutionResult Execute(string command, ShellExecutionOptions options, CancellationToken cancellationToken = default) =>
            new(command, Success: true, ExitCode: 0, Output: string.Empty, Error: string.Empty, TimedOut: false);

        public LaunchResult Launch(string target, LaunchOptions options) =>
            new(target, Success: false, ProcessId: null, Error: "Application launch is skipped during config lint.");
    }

    private sealed class LintHttpService : IHttpService
    {
        public static LintHttpService Instance { get; } = new();

        public IDisposable Send(HsWin.Core.Http.HttpRequestOptions options, Action<HttpResponseSnapshot> callback) => NoopDisposable.Instance;
    }

    private sealed class LintMediaController : IMediaController
    {
        public static LintMediaController Instance { get; } = new();

        public MediaCommandResult PlayPause() => MediaCommandResult.Sent("playPause", "lint");

        public MediaCommandResult PreviousTrack() => MediaCommandResult.Sent("previousTrack", "lint");

        public MediaCommandResult NextTrack() => MediaCommandResult.Sent("nextTrack", "lint");
    }

    private sealed class LintAudioDeviceController : IAudioDeviceController
    {
        private static readonly AudioDeviceSnapshot DefaultOutput = new("lint-output", "Lint output", IsDefault: true, Volume: 0, Muted: false);
        private static readonly AudioDeviceSnapshot DefaultInput = new("lint-input", "Lint input", IsDefault: true, Volume: 0, Muted: false);
        private static readonly AudioDeviceVolumeSnapshot OutputVolume = new("lint-output", "Lint output", Volume: 0, Muted: false);
        private static readonly AudioDeviceVolumeSnapshot InputVolume = new("lint-input", "Lint input", Volume: 0, Muted: false);

        public static LintAudioDeviceController Instance { get; } = new();

        public AudioDeviceSnapshot GetDefaultOutputDevice() => DefaultOutput;

        public IReadOnlyList<AudioDeviceSnapshot> GetOutputDevices() => [DefaultOutput];

        public AudioDeviceSnapshot GetDefaultInputDevice() => DefaultInput;

        public IReadOnlyList<AudioDeviceSnapshot> GetInputDevices() => [DefaultInput];

        public AudioDeviceVolumeSnapshot GetVolume(string? deviceId) => OutputVolume;

        public AudioDeviceVolumeSnapshot SetVolume(string? deviceId, double volume) => OutputVolume with { Volume = volume };

        public AudioDeviceVolumeSnapshot SetMuted(string? deviceId, bool muted) => OutputVolume with { Muted = muted };

        public AudioDeviceVolumeSnapshot ToggleMute(string? deviceId) => OutputVolume with { Muted = true };

        public AudioDeviceVolumeSnapshot GetInputVolume(string? deviceId) => InputVolume;

        public AudioDeviceVolumeSnapshot SetInputVolume(string? deviceId, double volume) => InputVolume with { Volume = volume };

        public AudioDeviceVolumeSnapshot SetInputMuted(string? deviceId, bool muted) => InputVolume with { Muted = muted };

        public AudioDeviceVolumeSnapshot ToggleInputMute(string? deviceId) => InputVolume with { Muted = true };
    }

    private sealed class LintAudioCaptureService : IAudioCaptureService
    {
        public static LintAudioCaptureService Instance { get; } = new();

        public IAudioRecordingSession Record(AudioRecordingOptions options, Action<AudioCaptureEvent> callback) =>
            new LintAudioRecordingSession(options.Path ?? "<lint-recording>");

        public IDisposable WatchLevels(AudioLevelWatchOptions options, Action<AudioCaptureEvent> callback) => NoopDisposable.Instance;
    }

    private sealed class LintAudioRecordingSession : IAudioRecordingSession
    {
        public LintAudioRecordingSession(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public bool IsRecording => false;

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
