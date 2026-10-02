namespace HsWin.App.Magnifier;
using HsWin.App.Native;

internal static class MagnifierCaptureTarget
{
    // Only exact monitor-sized windows can use window capture without changing screen coordinates.
    internal static bool IsFullscreen(NativeRect window, NativeRect monitor) =>
        window.Left == monitor.Left && window.Top == monitor.Top
        && window.Right == monitor.Right && window.Bottom == monitor.Bottom;
}
