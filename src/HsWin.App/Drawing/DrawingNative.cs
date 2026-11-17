using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HsWin.App.Drawing;

internal static class DrawingNative
{
    internal const int ExtendedStyle = 0x080800A8; // NOACTIVATE | LAYERED | TOOLWINDOW | TRANSPARENT | TOPMOST
    internal const nint PerMonitorAwareV2 = -4;

    [DllImport("user32.dll")]
    internal static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);

    internal static void SetAlpha(nint window, double alpha)
    {
        if (!SetLayeredWindowAttributes(window, 0, (byte)Math.Round(alpha * 255), 2))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set drawing opacity.");
    }
}
