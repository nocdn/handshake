using System.Globalization;
using System.Text;

namespace HsWin.Core.Config;

/// <summary>
/// The small JavaScript lexing helpers the static lint analyzers share. They recognize just enough
/// syntax (strings, comments, identifiers, numbers, braces) to find literal API calls; anything
/// dynamic is left to the runtime pass.
/// </summary>
internal static class ConfigSourceScanner
{
    /// <summary>Skips a string, template literal or comment starting at <paramref name="index"/>.</summary>
    public static bool TrySkipNonCode(string source, int index, out int nextIndex)
    {
        nextIndex = index;
        if (source[index] is '\'' or '"' or '`')
        {
            var quote = source[index];
            for (var current = index + 1; current < source.Length; current++)
            {
                if (source[current] == '\\')
                {
                    current++;
                    continue;
                }

                if (source[current] == quote)
                {
                    nextIndex = current + 1;
                    return true;
                }
            }

            nextIndex = source.Length;
            return true;
        }

        if (index + 1 >= source.Length || source[index] != '/')
        {
            return false;
        }

        if (source[index + 1] == '/')
        {
            var newline = source.IndexOf('\n', index + 2);
            nextIndex = newline < 0 ? source.Length : newline + 1;
            return true;
        }

        if (source[index + 1] == '*')
        {
            var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
            nextIndex = end < 0 ? source.Length : end + 2;
            return true;
        }

        return false;
    }

    /// <summary>Matches <paramref name="prefix"/> at a token start (not inside a longer member chain).</summary>
    public static bool IsCallPrefix(string source, int index, string prefix) =>
        IsTokenBoundaryBefore(source, index) && HasPrefix(source, index, prefix);

    public static bool HasPrefix(string source, int index, string prefix) =>
        index >= 0
        && index <= source.Length - prefix.Length
        && source.AsSpan(index, prefix.Length).SequenceEqual(prefix);

    public static int SkipWhiteSpace(string source, int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return index;
    }

    public static bool TryReadIdentifier(string source, int index, out string identifier, out int nextIndex)
    {
        identifier = string.Empty;
        nextIndex = index;
        if (index >= source.Length || !IsIdentifierStart(source[index]))
        {
            return false;
        }

        var current = index + 1;
        while (current < source.Length && IsIdentifierPart(source[current]))
        {
            current++;
        }

        identifier = source[index..current];
        nextIndex = current;
        return true;
    }

    public static bool TryReadQuotedValue(string source, int index, out string value, out int nextIndex)
    {
        value = string.Empty;
        nextIndex = index;
        if (index >= source.Length || source[index] is not ('\'' or '"'))
        {
            return false;
        }

        var quote = source[index];
        var builder = new StringBuilder();
        for (var current = index + 1; current < source.Length; current++)
        {
            if (source[current] == '\\' && current + 1 < source.Length)
            {
                builder.Append(source[++current]);
                continue;
            }

            if (source[current] == quote)
            {
                value = builder.ToString();
                nextIndex = current + 1;
                return true;
            }

            builder.Append(source[current]);
        }

        nextIndex = source.Length;
        return false;
    }

    /// <summary>Reads a decimal literal with optional sign, fraction, exponent and <c>_</c> separators.</summary>
    public static bool TryReadNumber(string source, int index, out double value, out int nextIndex)
    {
        value = 0;
        nextIndex = index;
        var current = index;
        if (current < source.Length && source[current] is '+' or '-')
        {
            current++;
        }

        var hasDigit = SkipDigits(source, ref current);
        if (current < source.Length && source[current] == '.')
        {
            current++;
            hasDigit |= SkipDigits(source, ref current);
        }

        if (!hasDigit)
        {
            return false;
        }

        if (current < source.Length && source[current] is 'e' or 'E')
        {
            var exponent = current + 1;
            if (exponent < source.Length && source[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (SkipDigits(source, ref exponent))
            {
                current = exponent;
            }
        }

        var literal = source[index..current].Replace("_", string.Empty, StringComparison.Ordinal);
        if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        nextIndex = current;
        return true;
    }

    /// <summary>Reads a whole number, decimal or <c>0x</c> hexadecimal.</summary>
    public static bool TryReadInteger(string source, int index, out int value, out int nextIndex)
    {
        value = 0;
        nextIndex = index;
        if (HasPrefix(source, index, "0x") || HasPrefix(source, index, "0X"))
        {
            var current = index + 2;
            while (current < source.Length && Uri.IsHexDigit(source[current]))
            {
                current++;
            }

            if (current == index + 2
                || !int.TryParse(source[(index + 2)..current], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            nextIndex = current;
            return true;
        }

        if (!TryReadNumber(source, index, out var number, out nextIndex)
            || number != Math.Truncate(number)
            || number is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    /// <summary>The index just past the brace that closes the one at <paramref name="start"/>.</summary>
    public static int FindMatchingBrace(string source, int start)
    {
        var depth = 0;
        for (var index = start; index < source.Length;)
        {
            if (TrySkipNonCode(source, index, out var nextIndex))
            {
                index = nextIndex;
                continue;
            }

            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    public static (int Line, int Column) GetLocation(string source, int index)
    {
        var line = 1;
        var column = 1;
        for (var current = 0; current < index && current < source.Length; current++)
        {
            if (source[current] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    public static ConfigLintDiagnostic CreateError(string source, int index, string code, string message)
    {
        var (line, column) = GetLocation(source, index);
        return new ConfigLintDiagnostic(ConfigLintSeverity.Error, code, message, line, column);
    }

    private static bool SkipDigits(string source, ref int index)
    {
        var hasDigit = false;
        while (index < source.Length && (char.IsAsciiDigit(source[index]) || source[index] == '_'))
        {
            hasDigit |= char.IsAsciiDigit(source[index]);
            index++;
        }

        return hasDigit;
    }

    private static bool IsTokenBoundaryBefore(string source, int index) =>
        index == 0 || (!IsIdentifierPart(source[index - 1]) && source[index - 1] != '.');

    private static bool IsIdentifierStart(char value) => char.IsAsciiLetter(value) || value is '_' or '$';

    private static bool IsIdentifierPart(char value) => char.IsAsciiLetterOrDigit(value) || value is '_' or '$';
}
