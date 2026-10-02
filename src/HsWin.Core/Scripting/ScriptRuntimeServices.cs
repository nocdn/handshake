using HsWin.Core.Alerts;
using HsWin.Core.Applications;
using HsWin.Core.Audio;
using HsWin.Core.Clipboard;
using HsWin.Core.Hotkeys;
using HsWin.Core.Http;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Magnifier;
using HsWin.Core.Media;
using HsWin.Core.Mouse;
using HsWin.Core.Shell;
using HsWin.Core.Timers;
using HsWin.Core.Windows;

namespace HsWin.Core.Scripting;

public sealed record ScriptRuntimeServices
{
    public IMagnifierService Magnifier { get; init; } = NullMagnifierService.Instance;

    public IAlertPresenter Alerts { get; init; } = NullAlertPresenter.Instance;

    public IHotkeyRegistrar Hotkeys { get; init; } = NullHotkeyRegistrar.Instance;

    public IScriptConsoleLogger Console { get; init; } = NullScriptConsoleLogger.Instance;

    public IApplicationProvider Applications { get; init; } = NullApplicationProvider.Instance;

    public IMediaController Media { get; init; } = NullMediaController.Instance;

    public IKeyboardEventService KeyboardEvents { get; init; } = NullKeyboardEventService.Instance;

    public IKeyboardInputService KeyboardInput { get; init; } = NullKeyboardInputService.Instance;

    public IScriptTimerService Timers { get; init; } = NullScriptTimerService.Instance;

    public IScriptCallbackScheduler CallbackScheduler { get; init; } = InlineScriptCallbackScheduler.Instance;

    public IClipboardService Clipboard { get; init; } = NullClipboardService.Instance;

    public IShellService Shell { get; init; } = NullShellService.Instance;

    public IAudioDeviceController AudioDevices { get; init; } = NullAudioDeviceController.Instance;

    public IAudioCaptureService AudioCapture { get; init; } = NullAudioCaptureService.Instance;

    public IMouseService Mouse { get; init; } = NullMouseService.Instance;

    public IMouseInputService MouseInput { get; init; } = NullMouseInputService.Instance;

    public IMouseEventService MouseEvents { get; init; } = NullMouseEventService.Instance;

    public IMouseButtonEventService MouseButtons { get; init; } = NullMouseButtonEventService.Instance;

    public IHttpService Http { get; init; } = NullHttpService.Instance;

    public IWindowService Windows { get; init; } = NullWindowService.Instance;

    public IRuntimeLogger Logger { get; init; } = NullRuntimeLogger.Instance;

    /// <summary>
    /// How long a config's top-level code may run before it is interrupted. Config setup takes
    /// milliseconds; this only stops an accidental infinite loop from hanging reload or lint.
    /// </summary>
    public TimeSpan ConfigExecutionTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
