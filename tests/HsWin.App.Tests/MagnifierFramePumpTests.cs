using HsWin.App.Magnifier;

namespace HsWin.App.Tests;

public sealed class MagnifierFramePumpTests
{
    [Fact]
    public void RateLimitedDrawsStillDrainCaptureAndPresentTheNewestImage()
    {
        var pump = new MagnifierFramePump(240, 240000);
        var image = 0;
        var presented = new List<int>();
        bool Capture() { image++; return true; }
        bool Present() { presented.Add(image); return true; }
        Assert.True(pump.Render(100000, Capture, Present));
        Assert.True(pump.Render(100000, Capture, Present));
        for (var i = 1; i <= 9; i++) Assert.False(pump.Render(100000 + i * 100, Capture, Present));
        Assert.Equal(11, image);
        Assert.Equal(9, pump.RateLimited);
        Assert.True(pump.Render(101000, () => false, Present));
        Assert.Equal(new[] { 1, 2, 11 }, presented);
    }

    [Fact]
    public void FullPresentationQueueRetainsTheNewestImageWithoutChargingFrameBudget()
    {
        var pump = new MagnifierFramePump(240, 240000);
        var image = 0;
        Assert.False(pump.Render(100000, () => { image = 1; return true; }, () => false));
        Assert.False(pump.Render(100000, () => { image = 2; return true; }, () => false));
        Assert.True(pump.Render(100000, () => false, () => { Assert.Equal(2, image); return true; }));
        Assert.False(pump.Render(100001, () => false, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void StaticCaptureIsNotCountedAsNewPresentations()
    {
        var pump = new MagnifierFramePump(240, 240000);
        Assert.False(pump.Render(100000, () => false, () => throw new InvalidOperationException()));
        pump.SetPendingImage(true); // Geometry/zoom changed, so redraw the cached image once.
        Assert.True(pump.Render(100000, () => false, () => true));
        Assert.False(pump.Render(101000, () => false, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void RestartCanImmediatelyPresentWithoutAccumulatingAnIdleBurst()
    {
        var pump = new MagnifierFramePump(240, 240000);
        pump.Render(100000, () => true, () => true);
        pump.Render(100000, () => true, () => true);
        Assert.False(pump.Render(100001, () => true, () => true));
        pump.ResetClock();
        Assert.True(pump.Render(100001, () => false, () => true));
        Assert.True(pump.Render(100001, () => true, () => true));
        Assert.False(pump.Render(100001, () => true, () => true));
    }

    [Fact]
    public void FailedPresentationKeepsPendingImageForRetry()
    {
        var pump = new MagnifierFramePump(240, 240000);
        Assert.Throws<InvalidOperationException>(() => pump.Render(100000, () => true, () => throw new InvalidOperationException()));
        Assert.True(pump.Render(100000, () => false, () => true));
    }
}
