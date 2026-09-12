using HsWin.Core.Alerts;
using System.Windows.Media.Animation;

namespace HsWin.App;

/// <summary>
/// The box curve as a WPF easing function: <c>cubic-bezier(0.22, 1, 0.36, 1)</c>.
/// Scritto rides the host width and its indent compensation on this ease-out,
/// deliberately not the roll's spring, so a kept run never outruns a shrink
/// (<c>packages/core/src/index.ts</c>, <c>_transitionWidth</c>).
/// </summary>
internal sealed class ScrittoShrinkEasing : IEasingFunction
{
    private static readonly Func<double, double> EaseFunc = ScrittoTiming.ShrinkEase();

    public double Ease(double normalizedTime)
    {
        return EaseFunc(normalizedTime);
    }
}
