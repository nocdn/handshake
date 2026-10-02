using System.Numerics;
using System.Runtime.InteropServices;
using HsWin.App.Magnifier;
using HsWin.App.Native;

namespace HsWin.App.Tests;

public sealed class MagnifierShaderTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1920, -1080)]
    [InlineData(1920, 200)]
    public void GPUCoordinatesStayCenteredOnSelectedMonitor(int left, int top)
    {
        var monitor = new NativeRect(left, top, left + 1920, top + 1080);
        var geometry = MagnifierGeometry.Calculate(monitor, 720, 3, 2);
        var settings = MagnifierShaderParameters.Create(geometry, monitor, 3);
        var sourceCenter = new Vector2(settings.SourceArea.X + settings.SourceArea.Z / 2,
            settings.SourceArea.Y + settings.SourceArea.W / 2);
        Assert.InRange(sourceCenter.X, 0.499f, 0.501f);
        Assert.InRange(sourceCenter.Y, 0.499f, 0.501f);
        Assert.Equal(357f / 1920, settings.SourceArea.Z);
        Assert.Equal(357f / 1080, settings.SourceArea.W);
        Assert.Equal(new Vector4(720, 3, 0, 0), settings.Lens);
    }

    [Fact]
    public void ConstantBufferUsesTwoAlignedFloatVectors()
    {
        Assert.Equal(32, Marshal.SizeOf<MagnifierShaderParameters>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    public void CachedLensFootprintSupportsZoomChangesWithoutRetainingPoolFrames(double zoom)
    {
        var monitor = new NativeRect(-1920, -1080, 0, 0);
        var footprint = MagnifierGeometry.Calculate(monitor, 720, 3, 1).Source;
        var geometry = MagnifierGeometry.Calculate(monitor, 720, 3, zoom);
        var settings = MagnifierShaderParameters.Create(geometry, footprint, 3);
        Assert.Equal(714, footprint.Right - footprint.Left);
        Assert.Equal(714, footprint.Bottom - footprint.Top);
        Assert.InRange(settings.SourceArea.X, 0, 0.5f);
        Assert.InRange(settings.SourceArea.Y, 0, 0.5f);
        Assert.InRange(settings.SourceArea.X + settings.SourceArea.Z, 0.5f, 1f);
        Assert.InRange(settings.SourceArea.Y + settings.SourceArea.W, 0.5f, 1f);
        Assert.InRange(settings.SourceArea.Z, (float)(1 / zoom) - 0.002f, (float)(1 / zoom) + 0.002f);
    }
}
