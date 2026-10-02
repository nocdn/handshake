using HsWin.Core.Keyboard;

namespace HsWin.Core.Hotkeys;

public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public HotkeyInputKind InputKind { get; init; } = HotkeyInputKind.Keyboard;

    public HotkeyMouseButton? MouseButton { get; init; }

    public static HotkeyDefinition CreateKeyboard(HotkeyModifiers modifiers, uint virtualKey)
    {
        return new HotkeyDefinition(modifiers, virtualKey);
    }

    public static HotkeyDefinition CreateMouseButton(HotkeyModifiers modifiers, HotkeyMouseButton mouseButton)
    {
        return new HotkeyDefinition(modifiers, 0)
        {
            InputKind = HotkeyInputKind.MouseButton,
            MouseButton = mouseButton
        };
    }

    public override string ToString()
    {
        if (InputKind == HotkeyInputKind.MouseButton)
        {
            return $"{Modifiers}+{MouseButton}";
        }

        return $"{Modifiers}+{FormatKey(VirtualKey)}";
    }

    /// <summary>Letters and function keys read as printed on a keycap (R, F12); other keys use their script names.</summary>
    private static string FormatKey(uint virtualKey)
    {
        var name = KeyboardKeyRules.GetDisplayName(virtualKey);
        var isLetter = name.Length == 1 && char.IsAsciiLetter(name[0]);
        var isFunctionKey = name.Length is 2 or 3 && name[0] == 'f' && char.IsAsciiDigit(name[1]);
        return isLetter || isFunctionKey ? name.ToUpperInvariant() : name;
    }
}
