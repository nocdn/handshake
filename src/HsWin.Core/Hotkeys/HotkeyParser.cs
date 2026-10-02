using System.Collections;
using System.Globalization;
using HsWin.Core.Keyboard;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;
using Microsoft.ClearScript;

namespace HsWin.Core.Hotkeys;

public static class HotkeyParser
{
    private static readonly IReadOnlyDictionary<string, HotkeyModifiers> ModifierAliases =
        new Dictionary<string, HotkeyModifiers>(StringComparer.OrdinalIgnoreCase)
        {
            ["alt"] = HotkeyModifiers.Alt,
            ["option"] = HotkeyModifiers.Alt,
            ["opt"] = HotkeyModifiers.Alt,
            ["ctrl"] = HotkeyModifiers.Control,
            ["control"] = HotkeyModifiers.Control,
            ["shift"] = HotkeyModifiers.Shift,
            ["cmd"] = HotkeyModifiers.Win,
            ["command"] = HotkeyModifiers.Win,
            ["win"] = HotkeyModifiers.Win,
            ["windows"] = HotkeyModifiers.Win,
            ["meta"] = HotkeyModifiers.Win
        };

    public static HotkeyDefinition Parse(object? modifiers, object? key)
    {
        var parsedModifiers = ParseModifiers(modifiers);
        if (TryParseMouseButton(key, out var mouseButton))
        {
            return HotkeyDefinition.CreateMouseButton(parsedModifiers, mouseButton);
        }

        return HotkeyDefinition.CreateKeyboard(parsedModifiers, ParseVirtualKey(key));
    }

    public static HotkeyModifiers ParseModifiers(object? value)
    {
        var modifiers = HotkeyModifiers.None;

        foreach (var item in EnumerateModifierValues(value))
        {
            var name = Convert.ToString(item, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!ModifierAliases.TryGetValue(name.Trim(), out var modifier))
            {
                throw new ArgumentException($"Unsupported hotkey modifier '{name}'.", nameof(value));
            }

            modifiers |= modifier;
        }

        return modifiers;
    }

    public static uint ParseVirtualKey(object? value)
    {
        if (value is null || ReferenceEquals(value, Undefined.Value))
        {
            throw new ArgumentException("Hotkey key is required.", nameof(value));
        }

        if (value is int intValue)
        {
            return CheckedVirtualKey(intValue);
        }

        if (value is uint uintValue)
        {
            return CheckedVirtualKey(uintValue);
        }

        var key = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Hotkey key cannot be empty.", nameof(value));
        }

        if (TryParseMouseButtonName(key, out _))
        {
            throw new ArgumentException($"Mouse hotkey key '{key}' does not have a virtual-key code.", nameof(value));
        }

        // Exact names first so punctuation names such as "-" or "pad-" survive normalization.
        if (KeyboardKeyRules.TryParseKeyName(key, out var virtualKey)
            || KeyboardKeyRules.TryParseKeyName(NormalizeKeyName(key), out virtualKey))
        {
            return virtualKey;
        }

        throw new ArgumentException($"Unsupported hotkey key '{key}'.", nameof(value));
    }

    private static bool TryParseMouseButton(object? value, out HotkeyMouseButton mouseButton)
    {
        mouseButton = default;

        if (value is null || ReferenceEquals(value, Undefined.Value))
        {
            return false;
        }

        if (value is int or uint)
        {
            return false;
        }

        var key = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return !string.IsNullOrWhiteSpace(key) && TryParseMouseButtonName(key, out mouseButton);
    }

    /// <summary>
    /// Only the middle and side buttons can be hotkeys; "left" and "right" stay arrow-key names.
    /// </summary>
    private static bool TryParseMouseButtonName(string key, out HotkeyMouseButton mouseButton)
    {
        mouseButton = default;
        if (!MouseButtonParser.TryParse(key, out var button))
        {
            return false;
        }

        switch (button)
        {
            case MouseButton.Middle:
                mouseButton = HotkeyMouseButton.Middle;
                return true;
            case MouseButton.XButton1:
                mouseButton = HotkeyMouseButton.XButton1;
                return true;
            case MouseButton.XButton2:
                mouseButton = HotkeyMouseButton.XButton2;
                return true;
            default:
                return false;
        }
    }

    private static IEnumerable<object?> EnumerateModifierValues(object? value)
    {
        if (value is string singleModifier)
        {
            return [singleModifier];
        }

        if (ScriptArgumentReader.IsMissing(value) || value is ScriptObject or IEnumerable)
        {
            return ScriptArgumentReader.EnumerateIndexedValues(value);
        }

        throw new ArgumentException("Hotkey modifiers must be a string or an array.", nameof(value));
    }

    private static uint CheckedVirtualKey(int value)
    {
        if (value is < 0 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Virtual-key code must fit in one byte.");
        }

        return (uint)value;
    }

    private static uint CheckedVirtualKey(uint value)
    {
        if (value > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Virtual-key code must fit in one byte.");
        }

        return value;
    }

    private static string NormalizeKeyName(string value)
    {
        return value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
