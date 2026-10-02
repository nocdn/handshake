using HsWin.Core.Hotkeys;

namespace HsWin.Core.Keyboard;

public static class KeyboardKeyRules
{
    public const uint VkShift = 0x10;
    public const uint VkControl = 0x11;
    public const uint VkMenu = 0x12;
    public const uint VkLeftShift = 0xA0;
    public const uint VkRightShift = 0xA1;
    public const uint VkLeftControl = 0xA2;
    public const uint VkRightControl = 0xA3;
    public const uint VkLeftMenu = 0xA4;
    public const uint VkRightMenu = 0xA5;
    public const uint VkLeftWin = 0x5B;
    public const uint VkRightWin = 0x5C;

    /// <summary>
    /// Every virtual key <see cref="IsModifierVirtualKey"/> reports as a modifier. Callers that
    /// must observe all modifier events (for example watch key filters that need modifier
    /// key-ups) MUST derive their set from this list rather than copying it.
    /// </summary>
    public static readonly uint[] AllModifierVirtualKeys =
    [
        VkShift,
        VkControl,
        VkMenu,
        VkLeftShift,
        VkRightShift,
        VkLeftControl,
        VkRightControl,
        VkLeftMenu,
        VkRightMenu,
        VkLeftWin,
        VkRightWin
    ];

    private const int DisplayNameTableSize = 256;

    // Resolved once per process: GetDisplayName runs on the WH_KEYBOARD_LL path for every
    // keystroke system-wide, so the common VK range must be a plain table lookup.
    private static readonly string?[] DisplayNameByVk = KeyNameTable.BuildDisplayNames();

    private static readonly IReadOnlyDictionary<string, uint> VirtualKeysByName = KeyNameTable.BuildNameLookup();

    // Cached per HotkeyModifiers combination; GetModifierNames also runs per keystroke/scroll
    // event. Returned arrays are SHARED — callers must treat them as read-only.
    private static readonly string[][] ModifierNamesByFlags = BuildModifierNames();

    private static string[][] BuildModifierNames()
    {
        var all = HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;
        var tables = new string[16][];
        for (var flags = 0; flags < tables.Length; flags++)
        {
            var modifiers = (HotkeyModifiers)flags & all;
            var names = new List<string>(4);
            if ((modifiers & HotkeyModifiers.Control) != 0)
            {
                names.Add("ctrl");
            }

            if ((modifiers & HotkeyModifiers.Alt) != 0)
            {
                names.Add("alt");
            }

            if ((modifiers & HotkeyModifiers.Shift) != 0)
            {
                names.Add("shift");
            }

            if ((modifiers & HotkeyModifiers.Win) != 0)
            {
                names.Add("win");
            }

            tables[flags] = [.. names];
        }

        return tables;
    }

    public static bool IsModifierVirtualKey(uint virtualKey)
    {
        return virtualKey switch
        {
            VkShift or VkControl or VkMenu or VkLeftShift or VkRightShift or VkLeftControl or VkRightControl
                or VkLeftMenu or VkRightMenu or VkLeftWin or VkRightWin => true,
            _ => false
        };
    }

    public static HotkeyModifiers ModifierForVirtualKey(uint virtualKey)
    {
        return virtualKey switch
        {
            VkShift or VkLeftShift or VkRightShift => HotkeyModifiers.Shift,
            VkControl or VkLeftControl or VkRightControl => HotkeyModifiers.Control,
            VkMenu or VkLeftMenu or VkRightMenu => HotkeyModifiers.Alt,
            VkLeftWin or VkRightWin => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None
        };
    }

    /// <summary>Every generic, left and right virtual key for the given modifier flags.</summary>
    public static IReadOnlySet<uint> GetAllModifierVirtualKeys(HotkeyModifiers modifiers)
    {
        return AllModifierVirtualKeys
            .Where(virtualKey => (ModifierForVirtualKey(virtualKey) & modifiers) != 0)
            .ToHashSet();
    }

    /// <summary>
    /// Keys whose scan code carries the E0 prefix (the navigation cluster, right-hand modifiers,
    /// numpad divide, Apps, PrintScreen and the browser/media keys).
    /// </summary>
    public static bool IsExtendedVirtualKey(uint virtualKey)
    {
        return virtualKey switch
        {
            >= 0x21 and <= 0x28 or 0x2C or 0x2D or 0x2E or 0x5D or 0x6F or 0x90
                or VkRightControl or VkRightMenu or VkLeftWin or VkRightWin
                or >= 0xA6 and <= 0xB7 => true,
            _ => false
        };
    }

    /// <summary>Resolves a key name or alias (case-insensitive) to its virtual key.</summary>
    public static bool TryParseKeyName(string name, out uint virtualKey)
    {
        return VirtualKeysByName.TryGetValue(name, out virtualKey);
    }

    public static string GetDisplayName(uint virtualKey)
    {
        if (virtualKey < DisplayNameTableSize && DisplayNameByVk[virtualKey] is { } name)
        {
            return name;
        }

        return $"vk:0x{virtualKey:X2}";
    }

    public static IReadOnlyList<uint> GetModifierVirtualKeys(HotkeyModifiers modifiers)
    {
        var virtualKeys = new List<uint>(4);
        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            virtualKeys.Add(VkControl);
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            virtualKeys.Add(VkMenu);
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            virtualKeys.Add(VkShift);
        }

        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            virtualKeys.Add(VkLeftWin);
        }

        return virtualKeys;
    }

    /// <summary>
    /// Returns the script-facing modifier names for the given flags in the stable order
    /// ctrl, alt, shift, win. The returned array is cached and shared across calls —
    /// callers must not mutate it.
    /// </summary>
    public static string[] GetModifierNames(HotkeyModifiers modifiers)
    {
        var flags = (uint)modifiers
            & (uint)(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win);
        return ModifierNamesByFlags[flags];
    }
}
