using HsWin.App.Magnifier;

namespace HsWin.App.Tests;

public sealed class MagnifierGeometryTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1080, 800, 380, 882, 462)]
    [InlineData(-1920, -1080, 0, 0, -1120, -700, -1038, -618)]
    [InlineData(1920, 200, 3840, 1280, 2720, 580, 2802, 662)]
    public void LensAndSourceAreCenteredInPhysicalMonitorBounds(int left, int top, int right, int bottom,
        int x, int y, int sourceX, int sourceY)
    {
        var result = MagnifierGeometry.Calculate(new(left, top, right, bottom), 320, 3, 2);
        Assert.Equal(x, result.X);
        Assert.Equal(y, result.Y);
        Assert.Equal(320, result.Diameter);
        Assert.Equal(sourceX, result.Source.Left);
        Assert.Equal(sourceY, result.Source.Top);
        Assert.Equal(157, result.Source.Right - result.Source.Left);
    }

    [Theory]
    [InlineData(1, 400)]
    [InlineData(2, 200)]
    [InlineData(4, 100)]
    public void ZoomChangesSourceAreaWithoutMovingLens(double zoom, int expectedSource)
    {
        var result = MagnifierGeometry.Calculate(new(0, 0, 1920, 1080), 400, 0, zoom);
        Assert.Equal(760, result.X);
        Assert.Equal(340, result.Y);
        Assert.Equal(expectedSource, result.Source.Right - result.Source.Left);
    }

    [Fact]
    public void LargeLensFitsSmallScreen()
    {
        var result = MagnifierGeometry.Calculate(new(0, 0, 800, 600), 1600, 3, 2);
        Assert.Equal(600, result.Diameter);
        Assert.Equal(100, result.X);
        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void LargeDefaultLensMaintainsCenterAndTwoTimesZoom()
    {
        var result = MagnifierGeometry.Calculate(new(0, 0, 2560, 1440), 720, 3, 2);
        Assert.Equal(920, result.X);
        Assert.Equal(360, result.Y);
        Assert.Equal(720, result.Diameter);
        Assert.Equal(357, result.Source.Right - result.Source.Left);
        Assert.Equal(357, result.Source.Bottom - result.Source.Top);
    }

    [Fact]
    public void WindowStylesKeepLensTopmostClickThroughAndNonActivating()
    {
        Assert.NotEqual(0u, MagnifierNative.HostExtendedStyle & 0x00000008); // TOPMOST
        Assert.NotEqual(0u, MagnifierNative.HostExtendedStyle & 0x00000020); // TRANSPARENT
        Assert.NotEqual(0u, MagnifierNative.HostExtendedStyle & 0x08000000); // NOACTIVATE
        Assert.NotEqual(0u, MagnifierNative.HostExtendedStyle & 0x00080000); // LAYERED, required by Magnification API
    }
}
