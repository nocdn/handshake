using System.Globalization;
using System.Text.RegularExpressions;

namespace HsWin.Core.Alerts;

/// <summary>
/// Trend reader ported from Scritto's <c>trendOf()</c>
/// (<c>packages/core/src/helpers.ts</c>). The first number that differs
/// decides the roll direction; values that gain or lose a number read as a
/// rise. Returns <c>1</c> for up, <c>-1</c> for down.
/// </summary>
public static partial class ScrittoTrend
{
    public static int Of(string previous, string next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        var before = NumbersOf(previous);
        var after = NumbersOf(next);
        if (before.Count == 0 || before.Count != after.Count)
        {
            return 1;
        }

        for (var i = 0; i < before.Count; i++)
        {
            if (after[i] != before[i])
            {
                return after[i] > before[i] ? 1 : -1;
            }
        }

        return 1;
    }

    private static List<double> NumbersOf(string value)
    {
        var result = new List<double>();
        foreach (Match match in NumberPattern().Matches(value))
        {
            var parsed = ReadNumber(match.Value);
            if (parsed.HasValue)
            {
                result.Add(parsed.Value);
            }
        }

        return result;
    }

    private static double? ReadNumber(string match)
    {
        var digits = NonDigitOrDotPattern().Replace(match, string.Empty);
        if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
        {
            return null;
        }

        return match.Length > 0 && (match[0] == '-' || match[0] == '−') ? -parsed : parsed;
    }

    // Mirrors /[-\u2212]?\d[\d,_' \u00A0\u202F]*(?:\.\d+)?/g with ASCII-only
    // digits so .NET matches exactly what JavaScript \d matches. The escapes
    // below are space, no-break space (U+00A0) and narrow no-break space
    // (U+202F); the leading class is hyphen-minus and minus sign (U+2212).
    [GeneratedRegex("[-\\u2212]?[0-9][0-9,_' \\u00A0\\u202F]*(?:\\.[0-9]+)?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex("[^0-9.]")]
    private static partial Regex NonDigitOrDotPattern();
}
