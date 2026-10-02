using HsWin.Core.Hotkeys;
using HsWin.Core.Input;
using HsWin.Core.Scripting;

namespace HsWin.Core.Keyboard;

public static class KeyboardScriptOptionsParser
{
    public static KeyboardEventWatchOptions ParseWatchOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return KeyboardEventWatchOptions.Default;
        }

        var includeInjected = ScriptArgumentReader.GetPropertyValue(value, "includeInjected");
        var blocking = ScriptArgumentReader.GetPropertyValue(
            value,
            "blocking",
            "synchronous",
            "sync",
            "swallow",
            "preventDefault",
            "prevent",
            "capture");
        var keys = ScriptArgumentReader.GetPropertyValue(value, "keys", "key", "keyCodes", "keyCode");
        var prepend = ScriptArgumentReader.GetPropertyValue(value, "prepend", "priority", "first");
        return new KeyboardEventWatchOptions(
            ScriptArgumentReader.OptionalBoolean(includeInjected, "includeInjected", KeyboardEventWatchOptions.Default.IncludeInjected),
            ScriptArgumentReader.OptionalBoolean(blocking, "blocking", KeyboardEventWatchOptions.Default.Blocking),
            ParseKeyFilter(keys),
            ScriptArgumentReader.OptionalBoolean(prepend, "prepend", KeyboardEventWatchOptions.Default.Prepend));
    }

    public static KeyboardTapOptions ParseTapOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return KeyboardTapOptions.Default;
        }

        var suppressValue = ScriptArgumentReader.GetPropertyValue(
            value,
            "suppressPhysicalModifiers",
            "suppressModifiers",
            "withoutModifiers");
        var modifiersValue = ScriptArgumentReader.GetPropertyValue(
            value,
            "modifiers",
            "withModifiers",
            "holdModifiers");
        var inputMethod = InputMethodParser.Parse(
            ScriptArgumentReader.GetPropertyValue(value, "inputMethod", "method"));

        return new KeyboardTapOptions(
            ScriptArgumentReader.IsMissing(suppressValue)
                ? KeyboardTapOptions.Default.SuppressPhysicalModifiers
                : HotkeyParser.ParseModifiers(suppressValue),
            ScriptArgumentReader.IsMissing(modifiersValue)
                ? KeyboardTapOptions.Default.Modifiers
                : HotkeyParser.ParseModifiers(modifiersValue),
            inputMethod);
    }

    /// <summary>The repeat option names (and aliases) for the held key-down phase.</summary>
    public static IReadOnlyList<string> KeyDownPropertyNames { get; } = ["keyDownMs", "holdMs", "pressDurationMs"];

    public static KeyboardRepeatOptions ParseRepeatOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return KeyboardRepeatOptions.Default;
        }

        var intervalValue = ScriptArgumentReader.GetPropertyValue(value, "intervalMs", "interval");
        var keyDownValue = ScriptArgumentReader.GetPropertyValue(value, [.. KeyDownPropertyNames]);
        var tapOptions = ParseTapOptions(value);
        var intervalMs = ScriptArgumentReader.IsMissing(intervalValue)
            ? KeyboardRepeatOptions.Default.IntervalMs
            : RepeatTiming.ParseIntervalMs(intervalValue);
        var keyDownMs = ScriptArgumentReader.IsMissing(keyDownValue)
            ? KeyboardRepeatOptions.Default.KeyDownMs
            : RepeatTiming.ParseKeyDownMs(keyDownValue, intervalMs);

        return new KeyboardRepeatOptions(
            intervalMs,
            tapOptions.SuppressPhysicalModifiers,
            tapOptions.InputMethod,
            keyDownMs,
            tapOptions.Modifiers);
    }

    private static IReadOnlySet<uint>? ParseKeyFilter(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return null;
        }

        var keys = new HashSet<uint>();
        if (value is string or int or uint)
        {
            keys.Add(HotkeyParser.ParseVirtualKey(value));
            return keys;
        }

        foreach (var item in ScriptArgumentReader.EnumerateIndexedValues(value))
        {
            keys.Add(HotkeyParser.ParseVirtualKey(item));
        }

        if (keys.Count == 0)
        {
            keys.Add(HotkeyParser.ParseVirtualKey(value));
        }

        return keys;
    }
}
