using Vortice.Mathematics;
using HsWin.App.Native;

namespace HsWin.App.Magnifier;

internal static class MagnifierCaptureRegion
{
    internal static Box Create(NativeRect source, NativeRect monitor) => new(
        source.Left - monitor.Left, source.Top - monitor.Top, 0,
        source.Right - monitor.Left, source.Bottom - monitor.Top, 1);
}
