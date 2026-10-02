namespace HsWin.App.Magnifier;
using HsWin.App.Native;

internal readonly record struct MagnifierGeometry(int X, int Y, int Diameter, NativeRect Source)
{
    internal static MagnifierGeometry Calculate(NativeRect monitor, int requestedDiameter, int border, double zoom)
    {
        // Fit the entire lens on small monitors; coordinates are physical pixels, including negative origins.
        var diameter = Math.Min(requestedDiameter, Math.Min(monitor.Right - monitor.Left, monitor.Bottom - monitor.Top));
        var centerX = monitor.Left + (monitor.Right - monitor.Left) / 2;
        var centerY = monitor.Top + (monitor.Bottom - monitor.Top) / 2;
        var sourceSize = Math.Max(1, (int)Math.Round((diameter - border * 2) / zoom));
        var left = centerX - sourceSize / 2;
        var top = centerY - sourceSize / 2;
        return new(centerX - diameter / 2, centerY - diameter / 2, diameter,
            new NativeRect(left, top, left + sourceSize, top + sourceSize));
    }
}
