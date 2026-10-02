namespace HsWin.Core.Alerts;

/// <summary>
/// Timing constants and pure helpers ported from Scritto
/// (<c>packages/core/src/const.ts</c> and <c>packages/core/src/helpers.ts</c>).
/// One roll is one spring, one duration per glyph, plus a wave of delays
/// across the glyphs that changed. The width and its indent ride a plain
/// ease-out, deliberately not the roll's spring, so kept runs never outrun a
/// shrink.
/// </summary>
public static class ScrittoTiming
{
    /// <summary>Vertical travel of an entering/leaving glyph, in em.</summary>
    public const double GlyphYEm = 0.35;

    /// <summary>Scale of a glyph at the far end of its travel.</summary>
    public const double GlyphScale = 0.6;

    /// <summary>Blur of a glyph at the far end of its travel, in em.</summary>
    public const double GlyphBlurEm = 0.1;

    /// <summary>Rotation of a glyph at the far end of its travel, in degrees.</summary>
    public const double GlyphRotateDeg = 2;

    /// <summary>The stagger wave spans at most this fraction of the duration.</summary>
    public const double Stagger = 0.3;

    /// <summary>Default per-glyph duration in milliseconds.</summary>
    public const int DefaultDurationMs = 550;

    /// <summary>Default spring: 1.5% overshoot, crosses target 46.5% through.</summary>
    public const string DefaultEasing =
        "linear(0,.1052,.3155,.532,.7112,.8414,.9265,.9765,1.0023,1.013,1.0151,1.0133,1.01,1.0068,1.0041,1.0022,1.001,1)";

    /// <summary>Bounce preset: same shape with a 4.5% overshoot.</summary>
    public const string BounceEasing =
        "linear(0,0.0843,0.2682,0.4765,0.6659,0.8165,0.9238,0.9918,1.0289,1.044,1.0453,1.039,1.0298,1.0204,1.0123,1.0062,1.0021,1)";

    /// <summary>Width changes below half a pixel do not animate the box.</summary>
    public const double MinWidthDeltaPx = 0.5;

    public static double GlyphOffsetPx(double fontSizePx) => fontSizePx * GlyphYEm;

    public static double GlyphBlurPx(double fontSizePx) => fontSizePx * GlyphBlurEm;

    /// <summary>
    /// Parses a CSS <c>linear()</c> easing into its stops. Returns an empty
    /// list when the easing is unreadable, mirroring <c>crossingFraction</c>
    /// falling back to the conservative answer.
    /// </summary>
    public static IReadOnlyList<double> ParseLinearStops(string easing)
    {
        if (string.IsNullOrEmpty(easing) || !easing.StartsWith("linear(", StringComparison.Ordinal)
            || !easing.EndsWith(")", StringComparison.Ordinal))
        {
            return [];
        }

        var inner = easing.Substring("linear(".Length, easing.Length - "linear(".Length - 1);
        var parts = inner.Split(',');
        var stops = new List<double>(parts.Length);
        foreach (var part in parts)
        {
            if (!double.TryParse(
                part.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var stop))
            {
                return [];
            }

            stops.Add(stop);
        }

        return stops.Count >= 2 ? stops : [];
    }

    /// <summary>
    /// Samples a <c>linear()</c> spring at normalized time <c>t</c> by linear
    /// interpolation between its evenly spaced stops.
    /// </summary>
    public static double SampleSpring(IReadOnlyList<double> stops, double t)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0)
        {
            return t;
        }

        if (t <= 0)
        {
            return stops[0];
        }

        if (t >= 1)
        {
            return stops[^1];
        }

        var scaled = t * (stops.Count - 1);
        var index = (int)Math.Floor(scaled);
        var fraction = scaled - index;
        return stops[index] + (stops[index + 1] - stops[index]) * fraction;
    }

    /// <summary>
    /// Cubic-bezier y(x) solver ported from Scritto's <c>cubicBezierEase</c>
    /// (Newton iteration, 8 rounds). Used for the width/indent curve.
    /// </summary>
    public static Func<double, double> CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        var cx = 3 * x1;
        var bx = 3 * (x2 - x1) - cx;
        var ax = 1 - cx - bx;
        var cy = 3 * y1;
        var by = 3 * (y2 - y1) - cy;
        var ay = 1 - cy - by;

        double SampleX(double t) => ((ax * t + bx) * t + cx) * t;
        double SlopeX(double t) => (3 * ax * t + 2 * bx) * t + cx;

        return x =>
        {
            if (x <= 0)
            {
                return 0;
            }

            if (x >= 1)
            {
                return 1;
            }

            var t = x;
            for (var i = 0; i < 8; i++)
            {
                var off = SampleX(t) - x;
                if (Math.Abs(off) < 1e-5)
                {
                    break;
                }

                var slope = SlopeX(t);
                if (Math.Abs(slope) < 1e-6)
                {
                    break;
                }

                t -= off / slope;
            }

            t = Math.Clamp(t, 0, 1);
            return ((ay * t + by) * t + cy) * t;
        };
    }

    /// <summary>Shrink-curve sampler for the box width and indent.</summary>
    public static Func<double, double> ShrinkEase() => CubicBezierEase(0.22, 1, 0.36, 1);

    /// <summary>
    /// Stagger delays ported from Scritto's <c>_sweep</c>: delay comes from
    /// where a glyph sits along the changed stretch, not from its index, so a
    /// glyph leaving and the one replacing it move together. The wave is
    /// normalized over the changed stretch alone and spans at most
    /// <see cref="Stagger"/> of the duration.
    /// </summary>
    public static double[] StaggerDelays(IReadOnlyList<double> offsets, int durationMs)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        var count = offsets.Count;
        var delays = new double[count];
        if (count == 0)
        {
            return delays;
        }

        var lo = double.PositiveInfinity;
        var last = double.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            lo = Math.Min(lo, offsets[i]);
            last = Math.Max(last, offsets[i]);
        }

        var reach = last > lo ? last - lo : 1;
        var step = durationMs * Stagger * Math.Max(count - 1, 0) / count;
        for (var i = 0; i < count; i++)
        {
            delays[i] = step * Math.Clamp(offsets[i] - lo, 0, reach) / reach;
        }

        return delays;
    }
}
