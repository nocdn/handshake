using HsWin.Core.Input;
using HsWin.Core.Scripting;

namespace HsWin.Core.Mouse;

public static class MouseScriptOptionsParser
{
    public static MouseRepeatOptions ParseRepeatOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return MouseRepeatOptions.Default;
        }

        var intervalValue = ScriptArgumentReader.GetPropertyValue(value, "intervalMs", "interval");
        var intervalMs = ScriptArgumentReader.IsMissing(intervalValue)
            ? MouseRepeatOptions.Default.IntervalMs
            : RepeatTiming.ParseIntervalMs(intervalValue);

        var inputMethod = InputMethodParser.Parse(
            ScriptArgumentReader.GetPropertyValue(value, "inputMethod", "method"));

        return new MouseRepeatOptions(intervalMs, inputMethod);
    }

    public static MouseScrollWatchOptions ParseScrollWatchOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return MouseScrollWatchOptions.Default;
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
        var axes = ScriptArgumentReader.GetPropertyValue(value, "axes", "axis");
        var prepend = ScriptArgumentReader.GetPropertyValue(value, "prepend", "priority", "first");

        return new MouseScrollWatchOptions(
            ScriptArgumentReader.OptionalBoolean(includeInjected, "includeInjected", MouseScrollWatchOptions.Default.IncludeInjected),
            ScriptArgumentReader.OptionalBoolean(blocking, "preventDefault", MouseScrollWatchOptions.Default.Blocking),
            ScriptArgumentReader.IsMissing(axes)
                ? MouseScrollWatchOptions.Default.Axes
                : ParseScrollAxes(axes),
            ScriptArgumentReader.OptionalBoolean(prepend, "prepend", defaultValue: false));
    }

    public static MouseScrollAxis ParseScrollAxes(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return MouseScrollWatchOptions.Default.Axes;
        }

        if (value is string text)
        {
            return ParseScrollAxisToken(text);
        }

        var axes = MouseScrollAxis.None;
        var sawToken = false;
        foreach (var item in ScriptArgumentReader.EnumerateIndexedValues(value))
        {
            sawToken = true;
            axes |= ParseScrollAxisToken(ScriptArgumentReader.RequireText(item, "axes"));
        }

        if (!sawToken)
        {
            return ParseScrollAxisToken(ScriptArgumentReader.RequireText(value, "axes"));
        }

        if (axes == MouseScrollAxis.None)
        {
            throw new ArgumentException("axes must include vertical, horizontal, or both.");
        }

        return axes;
    }

    private static MouseScrollAxis ParseScrollAxisToken(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "vertical" or "v" or "y" or "wheel" => MouseScrollAxis.Vertical,
            "horizontal" or "h" or "x" or "tilt" or "hwheel" => MouseScrollAxis.Horizontal,
            "both" or "all" or "any" => MouseScrollAxis.Both,
            _ => throw new ArgumentException(
                $"Unknown mouse scroll axis '{value}'. Use vertical, horizontal, or both.",
                nameof(value))
        };
    }
}
