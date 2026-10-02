using HsWin.App.Magnifier;
using HsWin.App.Native;

namespace HsWin.App.Tests;

public sealed class MagnifierCaptureTests
{
    [Fact]
    public void DesktopDuplicationCursorOnlyDuplicateAndStaleTimestampsAreNotFreshCapturedImages()
    {
        var freshness = new DesktopDuplicationFrameFreshness();
        Assert.False(freshness.Accept(0));
        Assert.True(freshness.Accept(12345));
        Assert.False(freshness.Accept(12345));
        Assert.False(freshness.Accept(0));
        Assert.False(freshness.Accept(12344));
        Assert.True(freshness.Accept(12346));
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1920, -1080)]
    [InlineData(1920, 200)]
    public void GPUCopyUsesOnlyLensSourceInOutputCoordinates(int left, int top)
    {
        var monitor = new NativeRect(left, top, left + 1920, top + 1080);
        var geometry = MagnifierGeometry.Calculate(monitor, 720, 3, 2);
        var box = MagnifierCaptureRegion.Create(geometry.Source, monitor);
        Assert.Equal(357, box.Right - box.Left);
        Assert.Equal(357, box.Bottom - box.Top);
        Assert.InRange(box.Left + (box.Right - box.Left) / 2f, 959f, 961f);
        Assert.InRange(box.Top + (box.Bottom - box.Top) / 2f, 539f, 541f);
        Assert.Equal(0, box.Front);
        Assert.Equal(1, box.Back);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1920, 300)]
    public void DirectWindowCaptureRequiresExactFullscreenCoordinates(int left, int top)
    {
        var monitor = new NativeRect(left, top, left + 1920, top + 1080);
        Assert.True(MagnifierCaptureTarget.IsFullscreen(monitor, monitor));
        Assert.False(MagnifierCaptureTarget.IsFullscreen(new(left + 1, top, left + 1920, top + 1080), monitor));
        Assert.False(MagnifierCaptureTarget.IsFullscreen(new(left, top + 1, left + 1920, top + 1080), monitor));
        Assert.False(MagnifierCaptureTarget.IsFullscreen(new(left, top, left + 1919, top + 1080), monitor));
        Assert.False(MagnifierCaptureTarget.IsFullscreen(new(left, top, left + 1920, top + 1079), monitor));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(144)]
    [InlineData(240)]
    [InlineData(360)]
    public void FrameBudgetAcceptsEveryFrameFromJitteryTargetRateSource(int fps)
    {
        var gate = new MagnifierFrameRateGate(fps, fps * 10000L);
        const long start = 100000;
        Assert.True(gate.IsDue(start));
        gate.Presented(start);
        for (var i = 1; i < 200; i++)
        {
            var arrival = start + i * 10000 + (i % 2 == 0 ? 2500 : -2500);
            Assert.True(gate.IsDue(arrival));
            gate.Presented(arrival);
        }
    }

    [Fact]
    public void OverTargetSourceStaysRateLimitedWithOnlyOneFrameOfBurstCredit()
    {
        var gate = new MagnifierFrameRateGate(240, 240000);
        var accepted = 0;
        for (var now = 100000; now < 340000; now += 500)
        {
            if (!gate.IsDue(now)) continue;
            accepted++;
            gate.Presented(now);
        }
        Assert.InRange(accepted, 240, 241);
    }

    [Fact]
    public void IdleTimeCannotBuildAnUnboundedBurstAndRestartIsImmediatelyDue()
    {
        var gate = new MagnifierFrameRateGate(240, 240000);
        gate.Presented(100000);
        Assert.True(gate.IsDue(200400));
        gate.Presented(200400);
        Assert.True(gate.IsDue(200500)); // One extra frame absorbs cadence jitter.
        gate.Presented(200500);
        Assert.False(gate.IsDue(200600));
        Assert.True(gate.IsDue(201400));
        gate.Reset();
        Assert.True(gate.IsDue(200500));
    }
}
