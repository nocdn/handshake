using HsWin.Core.Alerts;

namespace HsWin.App.Tests;

public sealed class ScrittoEasingTests
{
    [Fact]
    public void SpringEasingMatchesCoreSamples()
    {
        var easing = new ScrittoSpringEasing();
        var stops = ScrittoTiming.ParseLinearStops(ScrittoTiming.DefaultEasing);

        foreach (var t in new[] { 0.0, 0.1, 0.25, 0.465, 0.5, 0.75, 1.0 })
        {
            Assert.Equal(ScrittoTiming.SampleSpring(stops, t), easing.Ease(t), precision: 9);
        }
    }

    [Fact]
    public void SpringEasingOvershootsByAboutOneAndAHalfPercent()
    {
        var easing = new ScrittoSpringEasing();
        var peak = Enumerable.Range(0, 101).Select(i => easing.Ease(i / 100.0)).Max();

        Assert.Equal(0, easing.Ease(0));
        Assert.Equal(1, easing.Ease(1));
        Assert.InRange(peak, 1.01, 1.02);
    }

    [Fact]
    public void BounceEasingOvershootsMoreThanDefault()
    {
        var calm = new ScrittoSpringEasing();
        var bounce = new ScrittoSpringEasing(bounce: true);

        var calmPeak = Enumerable.Range(0, 201).Select(i => calm.Ease(i / 200.0)).Max();
        var bouncePeak = Enumerable.Range(0, 201).Select(i => bounce.Ease(i / 200.0)).Max();

        Assert.InRange(bouncePeak, 1.04, 1.05);
        Assert.True(bouncePeak > calmPeak);
    }

    [Fact]
    public void ShrinkEasingIsEaseOut()
    {
        var easing = new ScrittoShrinkEasing();

        Assert.Equal(0, easing.Ease(0));
        Assert.Equal(1, easing.Ease(1));
        Assert.True(easing.Ease(0.5) > 0.5);

        var previous = 0.0;
        for (var i = 1; i <= 10; i++)
        {
            var current = easing.Ease(i / 10.0);
            Assert.True(current >= previous);
            previous = current;
        }
    }
}
