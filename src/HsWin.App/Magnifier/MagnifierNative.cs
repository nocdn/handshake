using System.ComponentModel;
using System.Runtime.InteropServices;
using HsWin.App.Native;

namespace HsWin.App.Magnifier;

internal static class MagnifierNative
{
    // Extended window styles (WS_EX_*).
    private const uint ExTopmost = 0x00000008;
    private const uint ExTransparent = 0x00000020;
    private const uint ExToolWindow = 0x00000080;
    private const uint ExLayered = 0x00080000;
    private const uint ExNoActivate = 0x08000000;

    /// <summary>The GPU path draws through DirectComposition, so the host needs no redirection bitmap.</summary>
    internal const uint ExNoRedirectionBitmap = 0x00200000;

    // Window styles (WS_* and SS_*).
    private const uint StylePopup = 0x80000000;
    private const uint StyleClipChildren = 0x02000000;
    private const uint StaticWhiteRect = 0x00000006;
    private const uint StyleChild = 0x40000000;

    /// <summary>Click-through, always-on-top overlay that never takes focus.</summary>
    internal const uint HostExtendedStyle = ExTopmost | ExTransparent | ExToolWindow | ExLayered | ExNoActivate;

    internal const uint HostStyle = StylePopup | StyleClipChildren | StaticWhiteRect;

    /// <summary>Hidden until the native renderer is selected; the GPU path never shows it.</summary>
    internal const uint LensStyle = StyleChild;

    /// <summary><c>GWL_EXSTYLE</c> for <see cref="SetWindowLongPtr"/>.</summary>
    internal const int ExtendedStyleIndex = -20;

    /// <summary><c>WDA_EXCLUDEFROMCAPTURE</c>: keeps the overlay out of desktop duplication.</summary>
    internal const uint ExcludeFromCapture = 0x00000011;

    internal const uint DisplayAffinityNone = 0;

    /// <summary><c>LWA_ALPHA</c> for <see cref="SetLayeredWindowAttributes"/>.</summary>
    internal const uint LayeredAlpha = 0x00000002;

    internal const byte FullyOpaque = 255;

    /// <summary><c>MW_FILTERMODE_EXCLUDE</c> for <see cref="MagSetWindowFilterList"/>.</summary>
    internal const uint FilterModeExclude = 0;

    // SetWindowPos flags and insert-after handles.
    internal const uint PositionNoZOrder = 0x00000004;
    internal const uint PositionNoActivate = 0x00000010;
    internal const nint InsertAfterTop = 0;
    internal const nint InsertAfterTopmost = -1;

    // Monitor lookup and DWM queries.
    internal const uint MonitorDefaultToPrimary = 1;
    internal const uint MonitorDefaultToNearest = 2;
    internal const uint DwmExtendedFrameBounds = 9;

    /// <summary><c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>.</summary>
    internal const nint PerMonitorAwareV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Transform(float zoom)
    {
        public float M00 = zoom;
        public float M01 = 0;
        public float M02 = 0;
        public float M10 = 0;
        public float M11 = zoom;
        public float M12 = 0;
        public float M20 = 0;
        public float M21 = 0;
        public float M22 = 1;
    }

    /// <summary>DXGI HRESULTs the frame loop treats as normal back-pressure, not failures.</summary>
    internal static class DxgiResult
    {
        /// <summary><c>DXGI_ERROR_WAIT_TIMEOUT</c>: no new desktop frame yet.</summary>
        internal const int WaitTimeout = unchecked((int)0x887A0027);

        /// <summary><c>DXGI_ERROR_WAS_STILL_DRAWING</c>: the GPU queue is full.</summary>
        internal const int WasStillDrawing = unchecked((int)0x887A000A);
    }

    internal static void Check(bool success, string operation)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Magnifier: {operation} failed.");
    }

    internal static void SetCircle(nint window, int diameter)
    {
        var region = CreateEllipticRgn(0, 0, diameter + 1, diameter + 1);
        Check(region != 0, "create circular region");
        if (SetWindowRgn(window, region, true) == 0)
        {
            DeleteObject(region); // Windows owns the region only on success.
            Check(false, "apply circular region");
        }
    }

    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool MagInitialize();
    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool MagUninitialize();
    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool MagSetWindowTransform(nint window, ref Transform transform);
    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool MagSetWindowSource(nint window, NativeRect source);
    [DllImport("Magnification.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool MagSetWindowFilterList(nint window, uint mode, int count, nint[] windows);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out NativeRect bounds, int size);
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    internal static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
