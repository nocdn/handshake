using System.Runtime.InteropServices;
using HsWin.App.Drawing;
using HsWin.App.Native;
using HsWin.App.Screens;
using HsWin.Core.Drawing;
using HsWin.Core.Geometry;
using HsWin.Core.Logging;

namespace HsWin.App.Tests;

public sealed class NativeDrawingServiceTests
{
    [Fact]
    public void RectangleHasExactPhysicalBoundsOpacityAndNonActivatingClickThroughStyles()
    {
        using var service = new NativeDrawingService(NullRuntimeLogger.Instance);
        using var drawing = (RectangleDrawing)service.CreateRectangle(new(-100, -50, 300, 200));
        var handle = drawing.WindowHandle;
        Assert.False(IsWindowVisible(handle));
        Assert.Equal((nint)DrawingNative.ExtendedStyle, GetWindowLongPtr(handle, -20) & DrawingNative.ExtendedStyle);
        Assert.True(GetWindowRect(handle, out var bounds));
        Assert.Equal((-100, -50, 300, 200), (bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        drawing.SetFillColor(new(1, 0.25, 0.5, 0.5));
        Assert.True(GetLayeredWindowAttributes(handle, out _, out var alpha, out _));
        Assert.Equal(128, alpha);
        drawing.Show();
        Assert.True(drawing.IsVisible);
        Assert.True(IsWindowVisible(handle));
        drawing.SetFrame(new(10, 20, 123, 234));
        Assert.True(GetWindowRect(handle, out bounds));
        Assert.Equal((10, 20, 123, 234), (bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        drawing.Hide();
        Assert.False(drawing.IsVisible);
        Assert.False(IsWindowVisible(handle));
        drawing.Dispose();
        Assert.True(SpinWait.SpinUntil(() => !IsWindow(handle), TimeSpan.FromSeconds(5)));
        Assert.Throws<ObjectDisposedException>(drawing.Show);
    }

    [Fact]
    public void ServiceDisposalDestroysEveryDrawingAndRejectsFurtherCreation()
    {
        var service = new NativeDrawingService(NullRuntimeLogger.Instance);
        using var first = (RectangleDrawing)service.CreateRectangle(new(0, 0, 10, 10));
        using var second = (RectangleDrawing)service.CreateRectangle(new(10, 10, 10, 10));
        var handles = new[] { first.WindowHandle, second.WindowHandle };
        first.Show(); second.Show();
        service.Dispose();
        Assert.All(handles, handle => Assert.False(IsWindow(handle)));
        Assert.False(first.IsVisible);
        Assert.False(second.IsVisible);
        Assert.Throws<ObjectDisposedException>(() => service.CreateRectangle(new(0, 0, 1, 1)));
        service.Dispose();
    }

    [Fact]
    public void EveryConnectedScreenHasFullAndWorkingPixelBounds()
    {
        var screens = new NativeScreenProvider().AllScreens();
        Assert.NotEmpty(screens);
        Assert.Single(screens, screen => screen.IsPrimary);
        Assert.Equal(screens.Count, screens.Select(screen => screen.Id).Distinct().Count());
        Assert.All(screens, screen =>
        {
            Assert.True(screen.Bounds.Width > 0 && screen.Bounds.Height > 0);
            Assert.True(screen.WorkingArea.Width <= screen.Bounds.Width);
            Assert.True(screen.WorkingArea.Height <= screen.Bounds.Height);
        });
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint color, out byte alpha, out uint flags);
}
