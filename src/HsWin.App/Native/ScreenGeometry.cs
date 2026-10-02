using System.Runtime.InteropServices;

namespace HsWin.App.Native;

/// <summary>
/// Shared cursor, monitor and DPI queries. Positions are physical pixels; <see cref="GetScale"/>
/// converts a monitor's effective DPI to the WPF device-to-DIP factor.
/// </summary>
internal static partial class ScreenGeometry
{
    public const double DefaultDpi = 96d;

    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    public static bool TryGetCursor(out NativePoint cursor) => GetCursorPos(out cursor);

    public static bool TryGetWindowRect(nint windowHandle, out NativeRect rect) => GetWindowRect(windowHandle, out rect);

    public static nint MonitorFromPoint(NativePoint point) => MonitorFromPointNative(point, MonitorDefaultToNearest);

    public static bool TryGetMonitorInfo(nint monitor, out NativeMonitorInfo info)
    {
        info = NativeMonitorInfo.Create();
        return monitor != nint.Zero && GetMonitorInfo(monitor, ref info);
    }

    /// <summary>Physical pixels per DIP on <paramref name="monitor"/> (1 at 96 DPI, 1.5 at 144 DPI).</summary>
    public static double GetScale(nint monitor)
    {
        return monitor != nint.Zero && GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
            ? dpiX / DefaultDpi
            : 1d;
    }

    /// <summary>The scale of the monitor nearest to a physical-pixel point.</summary>
    public static double GetScaleAt(int x, int y) => GetScale(MonitorFromPoint(new NativePoint { X = x, Y = y }));

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint windowHandle, out NativeRect rect);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    private static partial nint MonitorFromPointNative(NativePoint point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref NativeMonitorInfo info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
