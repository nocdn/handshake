using HsWin.Core.Alerts;
using System.Windows.Media.Animation;

namespace HsWin.App;

/// <summary>
/// The roll's spring as a WPF easing function, sampled straight from
/// Scritto's CSS <c>linear()</c> stops (<c>packages/core/src/const.ts</c>).
/// The default curve overshoots its target by 1.5%; <see cref="Bounce"/>
/// swaps in the same shape with a 4.5% overshoot. Implemented as a plain
/// <see cref="IEasingFunction"/> (not <c>EasingFunctionBase</c>) so no
/// easing-mode transform distorts the sampled spring.
/// </summary>
internal sealed class ScrittoSpringEasing : IEasingFunction
{
    private static readonly IReadOnlyList<double> DefaultStops =
        ScrittoTiming.ParseLinearStops(ScrittoTiming.DefaultEasing);

    private static readonly IReadOnlyList<double> BounceStops =
        ScrittoTiming.ParseLinearStops(ScrittoTiming.BounceEasing);

    public ScrittoSpringEasing(bool bounce = false)
    {
        Bounce = bounce;
    }

    public bool Bounce { get; }

    public double Ease(double normalizedTime)
    {
        return ScrittoTiming.SampleSpring(Bounce ? BounceStops : DefaultStops, normalizedTime);
    }
}
