using System.Windows.Forms;
using HsWin.App.Drawing;
using HsWin.Core.Geometry;
using HsWin.Core.Screens;

namespace HsWin.App.Screens;

internal sealed class NativeScreenProvider : IScreenProvider
{
    public IReadOnlyList<ScreenSnapshot> AllScreens()
    {
        var previous = DrawingNative.SetThreadDpiAwarenessContext(DrawingNative.PerMonitorAwareV2);
        try
        {
            return Screen.AllScreens.Select(screen => new ScreenSnapshot(screen.DeviceName, screen.DeviceName,
                screen.Primary, Frame(screen.Bounds), Frame(screen.WorkingArea))).ToArray();
        }
        finally
        {
            if (previous != 0) DrawingNative.SetThreadDpiAwarenessContext(previous);
        }
    }

    private static PixelRectangle Frame(System.Drawing.Rectangle frame) => new(frame.X, frame.Y, frame.Width, frame.Height);
}
