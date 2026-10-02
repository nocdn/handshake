using HsWin.Core.Keyboard;
using static HsWin.Core.Config.ConfigSourceScanner;

namespace HsWin.Core.Config;

internal static class ConfigKeyboardRepeatLiteralAnalyzer
{
    private const string KeyboardPrefix = "hs.keyboard.";

    public static IReadOnlyList<ConfigLintDiagnostic> Analyze(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<ConfigLintDiagnostic>();
        for (var index = 0; index < source.Length;)
        {
            if (TrySkipNonCode(source, index, out var nextIndex))
            {
                index = nextIndex;
                continue;
            }

            if (TryAnalyzeCall(source, index, out var diagnostic, out nextIndex))
            {
                if (diagnostic is not null)
                {
                    diagnostics.Add(diagnostic);
                }

                index = nextIndex;
                continue;
            }

            index++;
        }

        return diagnostics;
    }

    private static bool TryAnalyzeCall(
        string source,
        int index,
        out ConfigLintDiagnostic? diagnostic,
        out int nextIndex)
    {
        diagnostic = null;
        nextIndex = index + 1;
        if (!IsCallPrefix(source, index, KeyboardPrefix))
        {
            return false;
        }

        var methodIndex = index + KeyboardPrefix.Length;
        string methodName;
        if (HasPrefix(source, methodIndex, "repeatPulse"))
        {
            methodName = "repeatPulse";
            methodIndex += "repeatPulse".Length;
        }
        else if (HasPrefix(source, methodIndex, "repeat"))
        {
            methodName = "repeat";
            methodIndex += "repeat".Length;
        }
        else
        {
            return false;
        }

        var callIndex = SkipWhiteSpace(source, methodIndex);
        if (callIndex >= source.Length || source[callIndex] != '(')
        {
            return false;
        }

        var keyIndex = SkipWhiteSpace(source, callIndex + 1);
        if (!TryReadKeyLiteral(source, keyIndex, out var isModifier, out var afterKey))
        {
            return true;
        }

        if (methodName == "repeat" && isModifier)
        {
            diagnostic = CreateError(
                source,
                index,
                "HSWIN002",
                "Modifier keys cannot use `hs.keyboard.repeat`; use `hs.keyboard.repeatPulse` with `keyDownMs > 0`.");
            return true;
        }

        if (methodName != "repeatPulse")
        {
            return true;
        }

        var commaIndex = SkipWhiteSpace(source, afterKey);
        if (commaIndex >= source.Length || source[commaIndex] != ',')
        {
            diagnostic = CreateMissingDurationDiagnostic(source, index);
            return true;
        }

        var optionsIndex = SkipWhiteSpace(source, commaIndex + 1);
        if (optionsIndex >= source.Length || source[optionsIndex] != '{')
        {
            // A dynamic options object cannot be proven invalid statically. Runtime validation
            // still rejects a missing or non-positive duration when this call executes.
            return true;
        }

        var optionsEnd = FindMatchingBrace(source, optionsIndex);
        nextIndex = optionsEnd;
        var duration = FindDurationProperty(source, optionsIndex + 1, optionsEnd - 1);
        if (duration is DurationStatus.Missing or DurationStatus.NonPositive)
        {
            diagnostic = duration == DurationStatus.Missing
                ? CreateMissingDurationDiagnostic(source, index)
                : CreateError(
                    source,
                    index,
                    "HSWIN003",
                    "`hs.keyboard.repeatPulse` keyDownMs must be greater than 0.");
        }

        return true;
    }

    private static DurationStatus FindDurationProperty(string source, int start, int end)
    {
        var depth = 0;
        for (var index = start; index < end;)
        {
            if (TrySkipNonCode(source, index, out var nextIndex))
            {
                if ((source[index] is '\'' or '"') && depth == 0
                    && TryReadQuotedValue(source, index, out var propertyName, out var afterName)
                    && IsDurationProperty(propertyName))
                {
                    return ReadDurationValue(source, afterName, end);
                }

                index = nextIndex;
                continue;
            }

            if (source[index] is '{' or '[' or '(')
            {
                depth++;
                index++;
                continue;
            }

            if (source[index] is '}' or ']' or ')')
            {
                depth = Math.Max(0, depth - 1);
                index++;
                continue;
            }

            if (depth == 0 && TryReadIdentifier(source, index, out var identifier, out nextIndex))
            {
                if (IsDurationProperty(identifier))
                {
                    return ReadDurationValue(source, nextIndex, end);
                }

                index = nextIndex;
                continue;
            }

            index++;
        }

        return DurationStatus.Missing;
    }

    private static DurationStatus ReadDurationValue(string source, int afterName, int end)
    {
        var colonIndex = SkipWhiteSpace(source, afterName);
        if (colonIndex >= end || source[colonIndex] != ':')
        {
            return DurationStatus.Unknown;
        }

        var valueIndex = SkipWhiteSpace(source, colonIndex + 1);
        if (!TryReadNumber(source, valueIndex, out var value, out _))
        {
            return DurationStatus.Unknown;
        }

        return value > 0 ? DurationStatus.Positive : DurationStatus.NonPositive;
    }

    private static bool TryReadKeyLiteral(
        string source,
        int index,
        out bool isModifier,
        out int nextIndex)
    {
        isModifier = false;
        nextIndex = index;
        if (index >= source.Length)
        {
            return false;
        }

        if (source[index] is '\'' or '"')
        {
            if (!TryReadQuotedValue(source, index, out var value, out nextIndex))
            {
                return false;
            }

            // Only names the runtime accepts; an unknown key is reported by the runtime pass instead.
            isModifier = KeyboardKeyRules.TryParseKeyName(value, out var namedKey)
                && KeyboardKeyRules.IsModifierVirtualKey(namedKey);
            return true;
        }

        if (!TryReadInteger(source, index, out var virtualKey, out nextIndex))
        {
            return false;
        }

        isModifier = virtualKey >= 0 && KeyboardKeyRules.IsModifierVirtualKey((uint)virtualKey);
        return true;
    }

    private static ConfigLintDiagnostic CreateMissingDurationDiagnostic(string source, int index) =>
        CreateError(
            source,
            index,
            "HSWIN003",
            "`hs.keyboard.repeatPulse` requires `keyDownMs > 0` so sampled applications observe the key-down phase.");

    private static bool IsDurationProperty(string value) =>
        KeyboardScriptOptionsParser.KeyDownPropertyNames.Contains(value);

    private enum DurationStatus
    {
        Missing,
        NonPositive,
        Positive,
        Unknown
    }
}
