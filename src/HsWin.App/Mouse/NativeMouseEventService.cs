using HsWin.App.Hotkeys;
using HsWin.Core.Hotkeys;
using HsWin.Core.Mouse;

namespace HsWin.App.Mouse;

/// <summary>
/// Script-facing mouse event service. Scroll watches share the single WH_MOUSE_LL host used
/// for mouse-button hotkeys so multipurpose configs do not install a second low-level hook.
/// </summary>
internal sealed class NativeMouseEventService : IMouseEventService, IMouseButtonEventService
{
    private readonly NativeMouseHotkeyHook _mouseHook;
    public IDisposable WatchPhysicalButtons(Action<PhysicalMouseButtonEvent> callback) => _mouseHook.WatchPhysicalButtons(callback);

    public NativeMouseEventService(NativeMouseHotkeyHook mouseHook)
    {
        _mouseHook = mouseHook ?? throw new ArgumentNullException(nameof(mouseHook));
    }

    public IDisposable WatchScroll(MouseScrollWatchOptions options, Action<MouseScrollEventSnapshot> callback)
    {
        return _mouseHook.WatchScroll(options, callback);
    }

    /// <summary>
    /// Decodes WH_MOUSE_LL wheel messages. Exposed for unit tests and shared with the hook host.
    /// </summary>
    internal static bool TryCreateScrollEvent(
        int message,
        uint mouseData,
        uint flags,
        int x,
        int y,
        HotkeyModifiers pressedModifiers,
        out MouseScrollEventSnapshot snapshot)
    {
        return NativeMouseHotkeyHook.TryCreateScrollEvent(
            message,
            mouseData,
            flags,
            x,
            y,
            pressedModifiers,
            out snapshot);
    }
}
