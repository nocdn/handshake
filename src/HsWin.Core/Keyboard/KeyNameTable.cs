namespace HsWin.Core.Keyboard;

/// <summary>
/// The single table of key names. Hotkey/keyboard parsing accepts every name and alias here
/// (case-insensitively); <see cref="KeyboardKeyRules.GetDisplayName"/> reports the first name, so
/// what the app prints can always be parsed back.
/// </summary>
internal static class KeyNameTable
{
    private static readonly (uint VirtualKey, string DisplayName, string[] Aliases)[] NamedKeys =
    [
        (0x08, "backspace", []),
        (0x09, "tab", []),
        (0x0D, "enter", ["return"]),
        (0x10, "shift", []),
        (0x11, "ctrl", ["control"]),
        (0x12, "alt", ["option", "opt", "menu"]),
        (0x13, "pause", ["break"]),
        (0x14, "capslock", ["caps"]),
        (0x1B, "escape", ["esc"]),
        (0x20, "space", ["spacebar"]),
        (0x21, "pageup", ["pgup"]),
        (0x22, "pagedown", ["pgdn"]),
        (0x23, "end", []),
        (0x24, "home", []),
        (0x25, "left", ["leftarrow"]),
        (0x26, "up", ["uparrow"]),
        (0x27, "right", ["rightarrow"]),
        (0x28, "down", ["downarrow"]),
        (0x2C, "printscreen", ["prtsc", "print", "snapshot"]),
        (0x2D, "insert", ["ins"]),
        (0x2E, "delete", ["del", "forwarddelete"]),
        (0x5B, "win", ["windows", "cmd", "command", "leftwin", "lwin"]),
        (0x5C, "win", ["rightwin", "rwin"]),
        (0x5D, "apps", ["menukey", "contextmenu", "application"]),
        (0x6A, "padmultiply", ["pad*", "numpadmultiply", "multiply"]),
        (0x6B, "padadd", ["pad+", "numpadadd", "numpadplus"]),
        (0x6D, "padsubtract", ["pad-", "numpadsubtract", "numpadminus"]),
        (0x6E, "paddecimal", ["pad.", "numpaddecimal", "numpadperiod"]),
        (0x6F, "paddivide", ["pad/", "numpaddivide"]),
        (0x90, "numlock", []),
        (0x91, "scrolllock", []),
        (0xA0, "shift", ["leftshift", "lshift"]),
        (0xA1, "shift", ["rightshift", "rshift"]),
        (0xA2, "ctrl", ["leftctrl", "lctrl", "leftcontrol"]),
        (0xA3, "ctrl", ["rightctrl", "rctrl", "rightcontrol"]),
        (0xA4, "alt", ["leftalt", "lalt"]),
        (0xA5, "alt", ["rightalt", "ralt", "altgr"]),
        (0xAD, "mute", ["volumemute"]),
        (0xAE, "volumedown", []),
        (0xAF, "volumeup", []),
        (0xB0, "nexttrack", ["medianext"]),
        (0xB1, "previoustrack", ["prevtrack", "mediaprevious", "mediaprev"]),
        (0xB2, "stop", ["mediastop"]),
        (0xB3, "playpause", ["mediaplaypause", "play"]),
        (0xBA, ";", ["semicolon"]),
        (0xBB, "=", ["equals", "equal", "plus"]),
        (0xBC, ",", ["comma"]),
        (0xBD, "-", ["minus", "hyphen", "dash"]),
        (0xBE, ".", ["period", "dot"]),
        (0xBF, "/", ["slash"]),
        (0xC0, "`", ["backquote", "backtick", "grave", "~", "tilde"]),
        (0xDB, "[", ["leftbracket", "openbracket"]),
        (0xDC, "\\", ["backslash"]),
        (0xDD, "]", ["rightbracket", "closebracket"]),
        (0xDE, "'", ["quote", "apostrophe"])
    ];

    /// <summary>Display names indexed by virtual key; null where no name exists.</summary>
    public static string?[] BuildDisplayNames()
    {
        var names = new string?[256];
        foreach (var (virtualKey, displayName, _) in EnumerateAll())
        {
            names[virtualKey] ??= displayName;
        }

        return names;
    }

    /// <summary>Every accepted name, mapped to its virtual key. A shared name maps to its first key.</summary>
    public static IReadOnlyDictionary<string, uint> BuildNameLookup()
    {
        var lookup = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (virtualKey, displayName, aliases) in EnumerateAll())
        {
            lookup.TryAdd(displayName, virtualKey);
            foreach (var alias in aliases)
            {
                lookup.TryAdd(alias, virtualKey);
            }
        }

        return lookup;
    }

    private static IEnumerable<(uint VirtualKey, string DisplayName, string[] Aliases)> EnumerateAll()
    {
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            yield return (letter, char.ToLowerInvariant(letter).ToString(), []);
        }

        for (var digit = '0'; digit <= '9'; digit++)
        {
            yield return (digit, digit.ToString(), []);
        }

        for (var pad = 0; pad <= 9; pad++)
        {
            yield return ((uint)(0x60 + pad), $"pad{pad}", [$"numpad{pad}", $"keypad{pad}"]);
        }

        for (var functionKey = 1; functionKey <= 24; functionKey++)
        {
            yield return ((uint)(0x70 + functionKey - 1), $"f{functionKey}", []);
        }

        foreach (var entry in NamedKeys)
        {
            yield return entry;
        }
    }
}
