namespace HsWin.Core.Alerts;

/// <summary>
/// Result of <see cref="ScrittoDiff.Compute"/>. Counts mirror Scritto's
/// <c>diff()</c> return shape (<c>packages/core/src/helpers.ts</c>):
/// <c>PrefixCount</c> is the common prefix, <c>SuffixCount</c> the kept run
/// length, <c>OldSuffix</c> the index in the old value where that run starts,
/// <c>MidEnd</c> the index in the new labels where it starts, and
/// <c>Labels</c> the new value split into graphemes.
/// </summary>
public sealed record ScrittoDiffResult(
    int PrefixCount,
    int SuffixCount,
    int OldSuffix,
    int MidEnd,
    IReadOnlyList<string> Labels);

/// <summary>
/// Three-pass keeper ported from Scritto's <c>diff()</c>: longest common
/// prefix, then the longest run either value ends with, then one floating run
/// flush with neither end (the case SwiftUI does not handle).
/// A floating run must be at least two glyphs and pays for its travel with
/// its own length plus two glyph slots, corrected by the box anchor.
/// </summary>
public static class ScrittoDiff
{
    private const int RunBand = 2;
    private const int MinFloatRun = 2;
    private const int GroupWidth = 2;

    public static ScrittoDiffResult Compute(
        IReadOnlyList<string> previous,
        string nextValue,
        double anchor = 0)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(nextValue);

        var labels = ScrittoGraphemes.Split(nextValue);
        var lenOld = previous.Count;
        var lenNew = labels.Count;

        var start = 0;
        while (start < lenOld && start < lenNew
            && string.Equals(previous[start], labels[start], StringComparison.Ordinal))
        {
            start++;
        }

        var best = 0;
        var maxSuffix = Math.Min(lenOld - start, lenNew - start);
        while (best < maxSuffix
            && string.Equals(previous[lenOld - 1 - best], labels[lenNew - 1 - best], StringComparison.Ordinal))
        {
            best++;
        }

        var oldSuffix = lenOld - best;
        var midEnd = lenNew - best;

        var near = lenNew < lenOld ? 1 : -1;
        for (var step = 0; step <= RunBand * 2; step++)
        {
            var shift = lenNew - lenOld + ((step + 1) >> 1) * ((step & 1) != 0 ? near : -near);
            var lo = shift < 0 ? start - shift : start;
            var run = 0;
            for (var i = Math.Min(lenOld, lenNew - shift) - 1; i >= lo; i--)
            {
                var j = i + shift;
                if (j >= 0 && j < lenNew
                    && string.Equals(previous[i], labels[j], StringComparison.Ordinal))
                {
                    run++;
                    if (run > best && run >= MinFloatRun
                        && EarnsTravel(run, shift - anchor * (lenNew - lenOld)))
                    {
                        best = run;
                        oldSuffix = i;
                        midEnd = j;
                    }
                }
                else
                {
                    run = 0;
                }

                if (run + i - lo <= best)
                {
                    break;
                }
            }
        }

        return new ScrittoDiffResult(start, best, oldSuffix, midEnd, labels);
    }

    private static bool EarnsTravel(int run, double travel)
    {
        return Math.Abs(travel) <= run + GroupWidth;
    }
}
