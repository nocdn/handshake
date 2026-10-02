using System.Collections;
using System.Globalization;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public static class ScriptArgumentReader
{
    public static bool IsMissing(object? value)
    {
        return value is null || ReferenceEquals(value, Undefined.Value);
    }

    public static bool IsOptionsObject(object? value)
    {
        return value is ScriptObject or IReadOnlyDictionary<string, object?> or IDictionary;
    }

    public static bool HasIndexedValues(object? value)
    {
        if (IsMissing(value))
        {
            return false;
        }

        if (value is ScriptObject scriptObject)
        {
            return scriptObject.PropertyIndices.Any();
        }

        if (value is IDictionary)
        {
            return false;
        }

        return value is IEnumerable and not string;
    }

    public static object? GetPropertyValue(object? value, params string[] names)
    {
        if (IsMissing(value))
        {
            return null;
        }

        if (value is ScriptObject scriptObject)
        {
            // Exact-case aliases are the common case and need no property enumeration.
            foreach (var name in names)
            {
                var exact = scriptObject.GetProperty(name);
                if (!IsMissing(exact))
                {
                    return exact;
                }
            }

            // Fall back to one enumeration for case-insensitive spellings ("TimeoutMs").
            var propertyNames = scriptObject.PropertyNames.ToArray();
            foreach (var name in names)
            {
                foreach (var propertyName in propertyNames)
                {
                    if (string.Equals(propertyName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return scriptObject.GetProperty(propertyName);
                    }
                }
            }

            return null;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            foreach (var name in names)
            {
                foreach (var item in readOnlyDictionary)
                {
                    if (string.Equals(item.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return item.Value;
                    }
                }
            }

            return null;
        }

        if (value is IDictionary dictionary)
        {
            foreach (var name in names)
            {
                foreach (DictionaryEntry item in dictionary)
                {
                    if (item.Key is string key && string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return item.Value;
                    }
                }
            }
        }

        return null;
    }

    public static IEnumerable<object?> EnumerateIndexedValues(object? value)
    {
        if (IsMissing(value))
        {
            yield break;
        }

        if (value is ScriptObject scriptObject)
        {
            foreach (var index in scriptObject.PropertyIndices.Order())
            {
                yield return scriptObject.GetProperty(index);
            }

            yield break;
        }

        if (value is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }

    public static IReadOnlyDictionary<string, string> ReadStringMap(object? value, string argumentName)
    {
        if (IsMissing(value))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (value is ScriptObject scriptObject)
        {
            foreach (var propertyName in scriptObject.PropertyNames)
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    continue;
                }

                var propertyValue = scriptObject.GetProperty(propertyName);
                if (!IsMissing(propertyValue))
                {
                    result[propertyName] = RequireText(propertyValue, propertyName);
                }
            }

            return result;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            foreach (var item in readOnlyDictionary)
            {
                if (!IsMissing(item.Value))
                {
                    result[item.Key] = RequireText(item.Value, item.Key);
                }
            }

            return result;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry item in dictionary)
            {
                if (item.Key is string key && !IsMissing(item.Value))
                {
                    result[key] = RequireText(item.Value, key);
                }
            }

            return result;
        }

        throw new ArgumentException($"{argumentName} must be an object.", argumentName);
    }

    public static string RequireText(object? value, string argumentName)
    {
        if (IsMissing(value))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public static string RequireNonWhiteSpaceString(object? value, string argumentName)
    {
        var text = RequireText(value, argumentName);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        return text;
    }

    public static string? OptionalString(object? value)
    {
        if (IsMissing(value))
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static bool RequireBoolean(object? value, string argumentName)
    {
        if (IsMissing(value))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        return value switch
        {
            bool boolean => boolean,
            string text when bool.TryParse(text.Trim(), out var parsed) => parsed,
            _ => throw new ArgumentException($"{argumentName} must be true or false.", argumentName)
        };
    }

    public static bool OptionalBoolean(object? value, string argumentName, bool defaultValue)
    {
        return IsMissing(value) ? defaultValue : RequireBoolean(value, argumentName);
    }

    /// <summary>
    /// Reads a whole number. JavaScript numbers arrive as doubles; fractions round half away from
    /// zero (1.5 -> 2), and NaN, infinities and out-of-range values are rejected with the argument name.
    /// </summary>
    public static int RequireInt32(object? value, string argumentName, string expectedDescription)
    {
        var number = RequireDouble(value, argumentName, expectedDescription);
        var rounded = Math.Round(number, MidpointRounding.AwayFromZero);
        if (rounded is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be {expectedDescription}.");
        }

        return (int)rounded;
    }

    public static int RequireInt32InRange(object? value, string argumentName, int minimum, int maximum)
    {
        var result = RequireInt32(value, argumentName, "a whole number");
        if (result < minimum || result > maximum)
        {
            throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be between {minimum} and {maximum}.");
        }

        return result;
    }

    /// <summary>JavaScript truthiness for callback return values (undefined, null, false, 0 and "" are false).</summary>
    public static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            string text => text.Length > 0,
            double number => number != 0 && !double.IsNaN(number),
            int number => number != 0,
            _ => !IsMissing(value)
        };
    }

    public static int RequirePositiveInt32(object? value, string argumentName)
    {
        var result = RequireInt32(value, argumentName, "a positive integer");
        if (result < 1)
        {
            throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be at least 1.");
        }

        return result;
    }

    /// <summary>Reads a finite number; NaN and infinities are never valid script arguments.</summary>
    public static double RequireDouble(object? value, string argumentName, string expectedDescription)
    {
        if (IsMissing(value))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        double number;
        try
        {
            number = value is string text
                ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"{argumentName} must be {expectedDescription}.", argumentName, exception);
        }

        if (!double.IsFinite(number))
        {
            throw new ArgumentException($"{argumentName} must be {expectedDescription}.", argumentName);
        }

        return number;
    }
}
