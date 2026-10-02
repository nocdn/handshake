using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;

namespace HsWin.Core.Tests;

public sealed class KeyboardKeyRulesTests
{
    [Theory]
    [InlineData(KeyboardKeyRules.VkLeftShift, true)]
    [InlineData(KeyboardKeyRules.VkMenu, true)]
    [InlineData((uint)'W', false)]
    public void IsModifierVirtualKeyIdentifiesModifiers(uint virtualKey, bool expected)
    {
        Assert.Equal(expected, KeyboardKeyRules.IsModifierVirtualKey(virtualKey));
    }

    [Fact]
    public void GetModifierNamesReturnsStableScriptNames()
    {
        Assert.Equal(
            ["ctrl", "alt", "shift"],
            KeyboardKeyRules.GetModifierNames(
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift));
    }

    [Theory]
    [InlineData(0x25, true)]
    [InlineData(0x41, false)]
    public void IsExtendedVirtualKeyIdentifiesNavigationKeys(uint virtualKey, bool expected)
    {
        Assert.Equal(expected, KeyboardKeyRules.IsExtendedVirtualKey(virtualKey));
    }

    [Theory]
    [InlineData((uint)'W', "w")]
    [InlineData((uint)'0', "0")]
    [InlineData((uint)'9', "9")]
    [InlineData(0x21, "pageup")]
    [InlineData(0xC0, "`")]
    [InlineData(0x70, "f1")]
    [InlineData(0x81, "f18")]
    [InlineData(0x87, "f24")]
    [InlineData(0xE9, "vk:0xE9")]
    [InlineData(0x1FF, "vk:0x1FF")]
    public void GetDisplayNameReturnsScriptFriendlyNames(uint virtualKey, string expected)
    {
        Assert.Equal(expected, KeyboardKeyRules.GetDisplayName(virtualKey));
    }

    [Fact]
    public void GetModifierNamesReturnsCachedSharedArraysInStableOrder()
    {
        Assert.Equal(
            ["ctrl", "alt", "shift", "win"],
            KeyboardKeyRules.GetModifierNames(
                HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win));
        Assert.Equal([], KeyboardKeyRules.GetModifierNames(HotkeyModifiers.None));
        Assert.Equal(["alt"], KeyboardKeyRules.GetModifierNames(HotkeyModifiers.Alt));
        Assert.Equal(["shift", "win"], KeyboardKeyRules.GetModifierNames(HotkeyModifiers.Shift | HotkeyModifiers.Win));

        // Repeated calls share one array per flag combination (per-event allocation-free).
        var first = KeyboardKeyRules.GetModifierNames(HotkeyModifiers.Control);
        var second = KeyboardKeyRules.GetModifierNames(HotkeyModifiers.Control);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetModifierNamesIgnoresBitsThatAreNotModifiers()
    {
        // 0x4000 is Win32's MOD_NOREPEAT; it must never surface as a modifier name.
        const HotkeyModifiers noRepeatBit = (HotkeyModifiers)0x4000;

        Assert.Equal(["ctrl"], KeyboardKeyRules.GetModifierNames(HotkeyModifiers.Control | noRepeatBit));
    }
}
