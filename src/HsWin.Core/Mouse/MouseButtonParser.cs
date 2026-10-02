using System.Globalization;
using HsWin.Core.Scripting;

namespace HsWin.Core.Mouse;

public static class MouseButtonParser
{
    private static readonly IReadOnlyDictionary<string, MouseButton> Aliases =
        new Dictionary<string, MouseButton>(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = MouseButton.Left,
            ["mouse.left"] = MouseButton.Left,
            ["button1"] = MouseButton.Left,
            ["mouse.button1"] = MouseButton.Left,
            ["mousebutton1"] = MouseButton.Left,

            ["right"] = MouseButton.Right,
            ["mouse.right"] = MouseButton.Right,
            ["button2"] = MouseButton.Right,
            ["mouse.button2"] = MouseButton.Right,
            ["mousebutton2"] = MouseButton.Right,

            ["middle"] = MouseButton.Middle,
            ["middlemouse"] = MouseButton.Middle,
            ["mouse.middle"] = MouseButton.Middle,
            ["button3"] = MouseButton.Middle,
            ["mouse.button3"] = MouseButton.Middle,
            ["mousebutton3"] = MouseButton.Middle,

            ["back"] = MouseButton.XButton1,
            ["backward"] = MouseButton.XButton1,
            ["thumb1"] = MouseButton.XButton1,
            ["xbutton1"] = MouseButton.XButton1,
            ["mouse.back"] = MouseButton.XButton1,
            ["mouse.backward"] = MouseButton.XButton1,
            ["mouse.xbutton1"] = MouseButton.XButton1,
            ["button4"] = MouseButton.XButton1,
            ["mouse.button4"] = MouseButton.XButton1,
            ["mousebutton4"] = MouseButton.XButton1,

            ["forward"] = MouseButton.XButton2,
            ["thumb2"] = MouseButton.XButton2,
            ["xbutton2"] = MouseButton.XButton2,
            ["mouse.forward"] = MouseButton.XButton2,
            ["mouse.xbutton2"] = MouseButton.XButton2,
            ["button5"] = MouseButton.XButton2,
            ["mouse.button5"] = MouseButton.XButton2,
            ["mousebutton5"] = MouseButton.XButton2
        };

    public static MouseButton Parse(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            throw new ArgumentException("Mouse button is required.", nameof(value));
        }

        var button = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(button))
        {
            throw new ArgumentException("Mouse button cannot be empty.", nameof(value));
        }

        if (TryParse(button, out var parsedButton))
        {
            return parsedButton;
        }

        throw new ArgumentException($"Unsupported mouse button '{button}'.", nameof(value));
    }

    public static bool TryParse(string name, out MouseButton button)
    {
        return Aliases.TryGetValue(Normalize(name), out button);
    }

    public static string GetDisplayName(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => "left",
            MouseButton.Right => "right",
            MouseButton.Middle => "middle",
            MouseButton.XButton1 => "back",
            MouseButton.XButton2 => "forward",
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
    }

    private static string Normalize(string value)
    {
        return value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
