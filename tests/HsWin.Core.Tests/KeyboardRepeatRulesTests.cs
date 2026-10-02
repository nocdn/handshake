using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;

namespace HsWin.Core.Tests;

public sealed class KeyboardRepeatRulesTests
{
    private const uint KeyA = 0x41;
    private const uint ShiftKey = 0x10;

    private static KeyboardRepeatOptions Options(int keyDownMs) =>
        new(50, HotkeyModifiers.None, KeyDownMs: keyDownMs);

    [Fact]
    public void InstantaneousRepeatOfARegularKeyIsAllowed()
    {
        KeyboardRepeatRules.Validate(KeyA, Options(0), pulse: false);
    }

    [Fact]
    public void PulseWithAHeldPhaseIsAllowedEvenForModifiers()
    {
        KeyboardRepeatRules.Validate(KeyA, Options(20), pulse: true);
        KeyboardRepeatRules.Validate(ShiftKey, Options(20), pulse: true);
    }

    [Fact]
    public void PulseWithoutAHeldPhaseIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => KeyboardRepeatRules.Validate(KeyA, Options(0), pulse: true));

        Assert.Contains("keyDownMs", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatWithAHeldPhaseMustUseRepeatPulse()
    {
        var exception = Assert.Throws<ArgumentException>(() => KeyboardRepeatRules.Validate(KeyA, Options(20), pulse: false));

        Assert.Contains("repeatPulse", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstantaneousModifierRepeatIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => KeyboardRepeatRules.Validate(ShiftKey, Options(0), pulse: false));

        Assert.Contains("repeatPulse", exception.Message, StringComparison.Ordinal);
    }
}
