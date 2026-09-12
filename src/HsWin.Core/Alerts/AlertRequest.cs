using System.Globalization;

namespace HsWin.Core.Alerts;

public sealed record AlertRequest(string Text, AlertKind Kind, int DurationMs)
{
    public const AlertKind DefaultKind = AlertKind.Success;
    public const AlertIcon DefaultIcon = AlertIcon.Auto;
    public const AlertStyle DefaultStyle = AlertStyle.Standard;
    public const int DefaultDurationMs = 2000;

    public AlertIcon Icon { get; init; } = DefaultIcon;

    public AlertStyle Style { get; init; } = DefaultStyle;

    /// <summary>
    /// When true, a text change on the visible toast rolls glyph-by-glyph in
    /// the style of Scritto (SwiftUI's numeric text transition on the web)
    /// instead of snapping: surviving glyphs slide to their new places while
    /// the rest roll out and their replacements roll in.
    /// </summary>
    public bool AnimateRoll { get; init; }

    /// <summary>
    /// Roll direction: 1 arrives from below, -1 from above, 0 reads it off
    /// the numbers in the value. Mirrors Scritto's <c>trend</c>.
    /// </summary>
    public int Trend { get; init; }

    /// <summary>Per-glyph roll duration in milliseconds (Scritto default 550).</summary>
    public int RollDurationMs { get; init; } = ScrittoTiming.DefaultDurationMs;

    /// <summary>When true, swaps the roll spring for the bouncier preset.</summary>
    public bool Bounce { get; init; }

    public AlertIcon EffectiveIcon =>
        Style is AlertStyle.Following
            ? AlertIcon.None
            : Icon is AlertIcon.Auto
            ? Kind is AlertKind.Normal ? AlertIcon.None : AlertIcon.Dot
            : Icon;

    public static AlertRequest Create(
        string? text,
        AlertKind? kind = null,
        int? durationMs = null,
        AlertIcon? icon = null,
        AlertStyle? style = null,
        bool animateRoll = false,
        int trend = 0,
        int? rollDurationMs = null,
        bool bounce = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Alert text cannot be empty.", nameof(text));
        }

        if (trend is not -1 and not 0 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(trend), "Alert trend must be -1, 0, or 1.");
        }

        var normalizedRollDurationMs = rollDurationMs ?? ScrittoTiming.DefaultDurationMs;
        if (normalizedRollDurationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rollDurationMs), "Alert roll duration cannot be negative.");
        }

        var normalizedDurationMs = durationMs ?? DefaultDurationMs;
        if (normalizedDurationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Alert duration cannot be negative.");
        }

        return new AlertRequest(text, kind ?? DefaultKind, normalizedDurationMs)
        {
            Icon = icon ?? DefaultIcon,
            Style = style ?? DefaultStyle,
            AnimateRoll = animateRoll,
            Trend = trend,
            RollDurationMs = normalizedRollDurationMs,
            Bounce = bounce
        };
    }

    public static AlertKind ParseKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultKind;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "normal" or "none" or "plain" or "info" => AlertKind.Normal,
            "success" or "ok" or "done" => AlertKind.Success,
            "error" or "failure" or "failed" or "fail" => AlertKind.Error,
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Unknown alert type '{value}'. Use normal, success, or error."),
                nameof(value))
        };
    }

    public static AlertIcon ParseIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultIcon;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" or "default" => AlertIcon.Auto,
            "none" or "no" or "off" or "false" => AlertIcon.None,
            "dot" or "status" => AlertIcon.Dot,
            "loader" or "loading" or "spinner" or "progress" or "busy" => AlertIcon.Loader,
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Unknown alert icon '{value}'. Use auto, none, dot, or loader."),
                nameof(value))
        };
    }

    public static AlertStyle ParseStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultStyle;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "standard" or "default" or "normal" => AlertStyle.Standard,
            "following" or "follow" or "cursor" or "cursor-following" => AlertStyle.Following,
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Unknown alert style '{value}'. Use standard or following."),
                nameof(value))
        };
    }
}
