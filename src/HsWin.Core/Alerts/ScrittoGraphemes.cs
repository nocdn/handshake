using System.Globalization;

namespace HsWin.Core.Alerts;

/// <summary>
/// Grapheme splitter ported from Scritto's <c>splitGraphemes</c> helper
/// (<c>packages/core/src/helpers.ts</c>).
/// Values are split into graphemes before anything else looks at them so a
/// multi-code-point glyph (surrogate-pair emoji, combining marks, CRLF) is
/// never torn apart. Plain ASCII takes a fast per-code-unit path; anything
/// else goes through <see cref="StringInfo"/>, the closest available
/// equivalent of the original's <c>Intl.Segmenter</c>. Known delta: ZWJ
/// sequences and regional-indicator flags may split where the segmenter
/// would keep them whole; accepted because toast values are numbers, prices
/// and short labels. Ordinary spaces become non-breaking spaces so a
/// value holds one line mid-roll.
/// </summary>
public static class ScrittoGraphemes
{
    public const char NonBreakingSpace = '\u00A0';

    public static IReadOnlyList<string> Split(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        for (var i = 0; i < value.Length; i++)
        {
            var code = value[i];
            if (code > 0x7F || (code == '\r' && i + 1 < value.Length && value[i + 1] == '\n'))
            {
                return SplitWithTextElements(value);
            }
        }

        if (value.Length == 0)
        {
            return [];
        }

        var ascii = new string[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            ascii[i] = value[i] == ' ' ? "\u00A0" : value[i].ToString(CultureInfo.InvariantCulture);
        }

        return ascii;
    }

    private static IReadOnlyList<string> SplitWithTextElements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        var result = new List<string>();
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            result.Add(element == " " ? "\u00A0" : element);
        }

        return result;
    }
}
