using HsWin.Core.Alerts;

namespace HsWin.Core.Tests;

public sealed class ScrittoTimingTests
{
    [Fact]
    public void DefaultSpringHasSmallOvershoot()
    {
        var stops = ScrittoTiming.ParseLinearStops(ScrittoTiming.DefaultEasing);

        Assert.Equal(18, stops.Count);
        Assert.Equal(0, stops[0]);
        Assert.Equal(1, stops[^1]);
        Assert.Equal(1.0151, stops.Max(), precision: 4);
    }

    [Fact]
    public void BounceSpringHasLargerOvershoot()
    {
        var stops = ScrittoTiming.ParseLinearStops(ScrittoTiming.BounceEasing);

        Assert.Equal(18, stops.Count);
        Assert.Equal(1.0453, stops.Max(), precision: 4);
    }

    [Fact]
    public void UnreadableEasingFallsBackToConservativeAnswers()
    {
        Assert.Empty(ScrittoTiming.ParseLinearStops("bogus"));
        Assert.Equal(1, ScrittoTiming.CrossingFraction("bogus", 0.15));
        Assert.Equal(0.5, ScrittoTiming.SampleSpring([], 0.5), precision: 9);
    }

    [Fact]
    public void CrossingFractionStaysInsideTheRun()
    {
        var showsAt = ScrittoTiming.CrossingFraction(ScrittoTiming.DefaultEasing, 0.15);
        var solidAt = ScrittoTiming.CrossingFraction(ScrittoTiming.DefaultEasing, 0.35);

        Assert.InRange(showsAt, 0, 1);
        Assert.InRange(solidAt, 0, 1);
        Assert.True(showsAt < solidAt);
    }

    [Fact]
    public void SingleGlyphStartsImmediately()
    {
        Assert.Equal([0], ScrittoTiming.StaggerDelays([42], 400));
    }

    [Fact]
    public void StaggerWaveSpansThirtyPercentOfDuration()
    {
        var delays = ScrittoTiming.StaggerDelays([0, 100], 400);

        Assert.Equal(0, delays[0]);
        Assert.Equal(60, delays[1]);
    }

    [Fact]
    public void StaggerDelayComesFromPositionNotIndex()
    {
        var delays = ScrittoTiming.StaggerDelays([10, 10, 110], 500);

        Assert.Equal(delays[0], delays[1]);
        Assert.True(delays[2] > delays[0]);
    }

    [Fact]
    public void StaggerNormalizesOverChangedStretch()
    {
        var delays = ScrittoTiming.StaggerDelays([1000, 1100], 400);

        Assert.Equal(0, delays[0]);
        Assert.Equal(60, delays[1]);
    }

    [Fact]
    public void ShrinkEaseIsEaseOut()
    {
        var ease = ScrittoTiming.ShrinkEase();

        Assert.Equal(0, ease(0));
        Assert.Equal(1, ease(1));
        Assert.True(ease(0.5) > 0.5);
        Assert.True(ease(0.25) < ease(0.75));
    }
}
