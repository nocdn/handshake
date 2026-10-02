using HsWin.Core.Scripting;

namespace HsWin.Core.Magnifier;

/// <summary>Turns the script's <c>options</c> object into validated <see cref="MagnifierOptions"/>.</summary>
public static class MagnifierScriptOptionsParser
{
    public static MagnifierOptions Parse(object? value)
    {
        if (!ScriptArgumentReader.IsMissing(value) && !ScriptArgumentReader.IsOptionsObject(value))
        {
            throw new ArgumentException("Magnifier options must be an object.");
        }

        var defaults = MagnifierOptions.Defaults;
        var zoom = ReadNumber(value, "zoom", defaults.Zoom);
        MagnifierOptions.ValidateZoom(zoom);
        var diameter = ReadInteger(value, "diameter", defaults.Diameter, MagnifierOptions.MinDiameter, MagnifierOptions.MaxDiameter);
        var border = ReadInteger(value, "borderWidth", defaults.BorderWidth, MagnifierOptions.MinBorderWidth, MagnifierOptions.MaxBorderWidth);
        var fps = ReadInteger(value, "fps", defaults.Fps, MagnifierOptions.MinFps, MagnifierOptions.MaxFps);
        var screen = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "screen"))?.ToLowerInvariant() switch
        {
            null => defaults.Screen,
            "foreground" => MagnifierScreen.Foreground,
            "primary" => MagnifierScreen.Primary,
            "mouse" => MagnifierScreen.Mouse,
            _ => throw new ArgumentException("Magnifier screen must be foreground, primary, or mouse.")
        };
        return new MagnifierOptions(zoom, diameter, border, screen, fps);
    }

    private static double ReadNumber(object? options, string name, double fallback)
    {
        var value = ScriptArgumentReader.GetPropertyValue(options, name);
        return ScriptArgumentReader.IsMissing(value) ? fallback : ScriptArgumentReader.RequireDouble(value, name, "a number");
    }

    private static int ReadInteger(object? options, string name, int fallback, int min, int max)
    {
        var value = ReadNumber(options, name, fallback);
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < min || value > max)
        {
            throw new ArgumentException($"Magnifier {name} must be an integer from {min} to {max}.");
        }

        return (int)value;
    }
}
