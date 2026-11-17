using HsWin.Core.Geometry;
using HsWin.Core.Scripting;

namespace HsWin.Core.Drawing;

internal static class DrawingScriptArguments
{
    public static PixelRectangle Frame(object? value)
    {
        var x = Pixel(Property(value, "x"), "x");
        var y = Pixel(Property(value, "y"), "y");
        var width = Pixel(Property(value, "width", "w"), "width", positive: true);
        var height = Pixel(Property(value, "height", "h"), "height", positive: true);
        if ((long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Drawing frame exceeds desktop pixel coordinates.");
        return new(x, y, width, height);
    }

    public static DrawingColor Color(object? value)
    {
        if (!ScriptArgumentReader.IsOptionsObject(value))
            throw new ArgumentException("Fill color must be an object with red, green, blue and optional alpha channels.");
        return new(Channel(value, "red", 0), Channel(value, "green", 0), Channel(value, "blue", 0), Channel(value, "alpha", 1));
    }

    private static object? Property(object? value, params string[] names) => ScriptArgumentReader.GetPropertyValue(value, names);

    private static double Channel(object? value, string name, double fallback)
    {
        var raw = Property(value, name);
        var channel = ScriptArgumentReader.IsMissing(raw) ? fallback : Number(raw, name);
        if (!double.IsFinite(channel) || channel < 0 || channel > 1)
            throw new ArgumentOutOfRangeException(name, "Color channels must be finite numbers from 0 to 1.");
        return channel;
    }

    private static int Pixel(object? raw, string name, bool positive = false)
    {
        var number = Number(raw, name);
        if (!double.IsFinite(number) || number != Math.Truncate(number) || number < (positive ? 1 : int.MinValue) || number > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, positive ? "Drawing size must be a positive integer." : "Drawing coordinate must be a 32-bit integer.");
        return (int)number;
    }

    private static double Number(object? raw, string name)
    {
        if (raw is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
            throw new ArgumentException($"{name} must be a number.", name);
        return ScriptArgumentReader.RequireDouble(raw, name, "a number");
    }
}
