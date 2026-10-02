using System.Text.RegularExpressions;
using HsWin.Core.Hotkeys;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

/// <summary>
/// bootstrap.js decodes modifier flags handed over by the host with its own constants; keep them
/// pinned to <see cref="HotkeyModifiers"/> so the two can never drift apart.
/// </summary>
public sealed partial class BootstrapModifierFlagTests
{
    [Theory]
    [InlineData("ALT", HotkeyModifiers.Alt)]
    [InlineData("CONTROL", HotkeyModifiers.Control)]
    [InlineData("SHIFT", HotkeyModifiers.Shift)]
    [InlineData("WIN", HotkeyModifiers.Win)]
    public void ScriptModifierFlagMatchesTheHostEnum(string name, HotkeyModifiers expected)
    {
        var flags = ReadScriptFlags();

        Assert.Equal((uint)expected, flags[name]);
    }

    [Fact]
    public void ScriptDeclaresExactlyTheHostModifiers()
    {
        var hostFlags = Enum.GetValues<HotkeyModifiers>().Where(flag => flag != HotkeyModifiers.None).Select(flag => (uint)flag);

        Assert.Equal(hostFlags.Order(), ReadScriptFlags().Values.Order());
    }

    private static Dictionary<string, uint> ReadScriptFlags() =>
        FlagPattern().Matches(ScriptBootstrap.Load())
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => Convert.ToUInt32(match.Groups["value"].Value, 16));

    [GeneratedRegex(@"const MODIFIER_FLAG_(?<name>[A-Z]+) = 0x(?<value>[0-9A-Fa-f]+);")]
    private static partial Regex FlagPattern();
}
