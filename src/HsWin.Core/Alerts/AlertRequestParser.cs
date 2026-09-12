using HsWin.Core.Scripting;

namespace HsWin.Core.Alerts;

public static class AlertRequestParser
{
    public static AlertRequest FromScriptArguments(object? text, object? optionsOrKind = null, object? durationMs = null)
    {
        var message = ScriptArgumentReader.RequireText(text, "text");
        var kind = AlertRequest.DefaultKind;
        var icon = AlertRequest.DefaultIcon;
        var style = AlertRequest.DefaultStyle;
        int? duration = null;
        var animateRoll = false;
        var trend = 0;
        int? rollDurationMs = null;
        var bounce = false;

        if (!ScriptArgumentReader.IsMissing(optionsOrKind))
        {
            if (TryReadOptions(
                optionsOrKind,
                out var optionsKind,
                out var optionsDurationMs,
                out var optionsIcon,
                out var optionsStyle,
                out var optionsAnimateRoll,
                out var optionsTrend,
                out var optionsRollDurationMs,
                out var optionsBounce))
            {
                kind = optionsKind ?? kind;
                duration = optionsDurationMs;
                icon = optionsIcon ?? icon;
                style = optionsStyle ?? style;
                animateRoll = optionsAnimateRoll ?? animateRoll;
                trend = optionsTrend ?? trend;
                rollDurationMs = optionsRollDurationMs ?? rollDurationMs;
                bounce = optionsBounce ?? bounce;
            }
            else
            {
                kind = AlertRequest.ParseKind(ScriptArgumentReader.RequireText(optionsOrKind, "type"));
            }
        }

        if (!ScriptArgumentReader.IsMissing(durationMs))
        {
            duration = ConvertToDurationMs(durationMs, "durationMs");
        }

        return AlertRequest.Create(message, kind, duration, icon, style, animateRoll, trend, rollDurationMs, bounce);
    }

    private static bool TryReadOptions(
        object? value,
        out AlertKind? kind,
        out int? durationMs,
        out AlertIcon? icon,
        out AlertStyle? style,
        out bool? animateRoll,
        out int? trend,
        out int? rollDurationMs,
        out bool? bounce)
    {
        kind = null;
        durationMs = null;
        icon = null;
        style = null;
        animateRoll = null;
        trend = null;
        rollDurationMs = null;
        bounce = null;
        if (!ScriptArgumentReader.IsOptionsObject(value))
        {
            return false;
        }

        kind = ReadKind(ScriptArgumentReader.GetPropertyValue(value, "type", "kind", "state", "status"));
        durationMs = ReadDuration(ScriptArgumentReader.GetPropertyValue(value, "durationMs", "duration"));
        icon = ReadIcon(
            ScriptArgumentReader.GetPropertyValue(value, "icon", "indicator"),
            ScriptArgumentReader.GetPropertyValue(value, "loading", "loader", "spinner"));
        style = ReadStyle(ScriptArgumentReader.GetPropertyValue(value, "style", "variant"));
        animateRoll = ReadAnimateRoll(ScriptArgumentReader.GetPropertyValue(value, "scritto", "roll", "animate", "animated"));
        trend = ReadTrend(ScriptArgumentReader.GetPropertyValue(value, "trend", "direction"));
        rollDurationMs = ReadRollDuration(value);
        bounce = ReadBounce(ScriptArgumentReader.GetPropertyValue(value, "bounce", "bouncy"));
        return true;
    }

    private static AlertKind? ReadKind(object? value)
    {
        return ScriptArgumentReader.IsMissing(value) ? null : AlertRequest.ParseKind(ScriptArgumentReader.RequireText(value, "type"));
    }

    private static int? ReadDuration(object? value)
    {
        return ScriptArgumentReader.IsMissing(value) ? null : ConvertToDurationMs(value, "durationMs");
    }

    private static AlertIcon? ReadIcon(object? iconValue, object? loadingValue)
    {
        if (!ScriptArgumentReader.IsMissing(iconValue))
        {
            return AlertRequest.ParseIcon(ScriptArgumentReader.RequireText(iconValue, "icon"));
        }

        if (ScriptArgumentReader.IsMissing(loadingValue))
        {
            return null;
        }

        return ScriptArgumentReader.RequireBoolean(loadingValue, "loading")
            ? AlertIcon.Loader
            : AlertIcon.Auto;
    }

    private static AlertStyle? ReadStyle(object? value)
    {
        return ScriptArgumentReader.IsMissing(value)
            ? null
            : AlertRequest.ParseStyle(ScriptArgumentReader.RequireText(value, "style"));
    }

    private static bool? ReadAnimateRoll(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return null;
        }

        if (value is bool flag)
        {
            return flag;
        }

        var text = ScriptArgumentReader.RequireText(value, "scritto").Trim().ToLowerInvariant();
        return text switch
        {
            "scritto" or "roll" or "true" or "yes" or "on" or "always" or "animated" or "animate" or "1" => true,
            "false" or "no" or "off" or "never" or "none" or "0" => false,
            _ => throw new ArgumentException(
                "Unknown scritto option. Use true/false.",
                "scritto")
        };
    }

    private static int? ReadTrend(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return null;
        }

        if (value is bool)
        {
            throw new ArgumentException("Alert trend must be -1, 0, or 1.", "trend");
        }

        var text = ScriptArgumentReader.RequireText(value, "trend").Trim().ToLowerInvariant();
        var trend = text switch
        {
            "1" or "up" or "rise" or "rising" or "increase" => 1,
            "0" or "auto" or "detect" => 0,
            "-1" or "down" or "fall" or "falling" or "decrease" => -1,
            _ => (int?)null
        };

        if (trend.HasValue)
        {
            return trend;
        }

        try
        {
            trend = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException("Alert trend must be -1, 0, or 1.", "trend", exception);
        }

        if (trend is not -1 and not 0 and not 1)
        {
            throw new ArgumentOutOfRangeException("trend", "Alert trend must be -1, 0, or 1.");
        }

        return trend;
    }

    private static int? ReadRollDuration(object? options)
    {
        var direct = ScriptArgumentReader.GetPropertyValue(
            options,
            "rollDurationMs",
            "rollDuration",
            "animationDurationMs",
            "animationDuration",
            "transitionDurationMs",
            "transitionDuration");
        if (!ScriptArgumentReader.IsMissing(direct))
        {
            return ConvertToRollDurationMs(direct, "rollDurationMs");
        }

        var transition = ScriptArgumentReader.GetPropertyValue(options, "transition");
        if (!ScriptArgumentReader.IsMissing(transition) && ScriptArgumentReader.IsOptionsObject(transition))
        {
            var nested = ScriptArgumentReader.GetPropertyValue(transition, "durationMs", "duration");
            if (!ScriptArgumentReader.IsMissing(nested))
            {
                return ConvertToRollDurationMs(nested, "transition.duration");
            }
        }

        return null;
    }

    private static bool? ReadBounce(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return null;
        }

        try
        {
            return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException)
        {
            throw new ArgumentException("Alert bounce must be a boolean.", "bounce", exception);
        }
    }

    private static int ConvertToRollDurationMs(object? value, string argumentName)
    {
        var durationMs = ScriptArgumentReader.RequireInt32(value, argumentName, "a number of milliseconds");
        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(argumentName, "Alert roll duration cannot be negative.");
        }

        return durationMs;
    }

    private static int ConvertToDurationMs(object? value, string argumentName)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return AlertRequest.DefaultDurationMs;
        }

        var durationMs = ScriptArgumentReader.RequireInt32(value, argumentName, "a number of milliseconds");
        if (durationMs < 0)
        {
            throw new ArgumentOutOfRangeException(argumentName, "Alert duration cannot be negative.");
        }

        return durationMs;
    }
}
