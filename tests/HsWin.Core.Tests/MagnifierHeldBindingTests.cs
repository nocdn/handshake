using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Magnifier;

namespace HsWin.Core.Tests;

public sealed class MagnifierHeldBindingTests
{
    private const HotkeyModifiers Chord = HotkeyModifiers.Control | HotkeyModifiers.Alt;
    private static KeyboardEventSnapshot Event(uint key = 0x5A, bool down = true, HotkeyModifiers mods = Chord, bool injected = false) =>
        new(down ? "keydown" : "keyup", key, "z", [], (uint)mods, down, !down,
            KeyboardKeyRules.IsModifierVirtualKey(key), injected, false);

    [Fact]
    public void PressShowsOnceAndReleaseHidesWithoutScriptCallbacks()
    {
        var keyboard = new MagnifierTestKeyboard();
        var lens = new CapturingMagnifierSession(new());
        using var binding = new MagnifierHeldBinding(keyboard, HotkeyDefinition.CreateKeyboard(Chord, 0x5A), lens);
        Assert.True(keyboard.Options!.Blocking);
        Assert.True(keyboard.Options.Prepend);
        Assert.False(keyboard.Options.IncludeInjected);
        Assert.Contains(0x5Au, keyboard.Options.KeyFilter!);
        Assert.Contains(0xA2u, keyboard.Options.KeyFilter!);
        Assert.True(keyboard.Callback!(Event()));
        Assert.True(keyboard.Callback(Event()));
        Assert.Equal(1, lens.Shows);
        Assert.True(keyboard.Callback(Event(down: false)));
        Assert.False(lens.IsVisible);
        Assert.Equal(1, lens.Hides);
        Assert.False(keyboard.Callback(Event(down: false)));
    }

    [Theory]
    [InlineData(0xA2, HotkeyModifiers.Alt)]
    [InlineData(0xA4, HotkeyModifiers.Control)]
    [InlineData(0xA0, Chord | HotkeyModifiers.Shift)]
    public void ModifierChangeHidesAndDoesNotSwallowModifier(uint key, HotkeyModifiers mods)
    {
        var keyboard = new MagnifierTestKeyboard();
        var lens = new CapturingMagnifierSession(new());
        using var binding = new MagnifierHeldBinding(keyboard, HotkeyDefinition.CreateKeyboard(Chord, 0x5A), lens);
        Assert.True(keyboard.Callback!(Event()));
        Assert.False(keyboard.Callback(Event(key, down: key == 0xA0, mods: mods)));
        Assert.False(lens.IsVisible);
        Assert.True(keyboard.Callback(Event(mods: mods))); // No leaked autorepeat after a modifier release.
        Assert.Equal(1, lens.Shows);
        Assert.True(keyboard.Callback(Event(down: false, mods: mods)));
        Assert.True(keyboard.Callback(Event()));
        Assert.Equal(2, lens.Shows);
    }

    [Fact]
    public void WrongChordAndInjectedInputAreIgnoredIncludingInjectedRelease()
    {
        var keyboard = new MagnifierTestKeyboard();
        var lens = new CapturingMagnifierSession(new());
        using var binding = new MagnifierHeldBinding(keyboard, HotkeyDefinition.CreateKeyboard(Chord, 0x5A), lens);
        Assert.False(keyboard.Callback!(Event(mods: HotkeyModifiers.Control)));
        Assert.False(keyboard.Callback(Event(mods: Chord | HotkeyModifiers.Shift)));
        Assert.False(keyboard.Callback(Event(injected: true)));
        Assert.False(keyboard.Callback(Event(key: 0x58)));
        Assert.False(lens.IsVisible);
        Assert.True(keyboard.Callback(Event()));
        Assert.False(keyboard.Callback(Event(down: false, injected: true)));
        Assert.True(lens.IsVisible);
        Assert.True(keyboard.Callback(Event(down: false)));
    }

    [Fact]
    public void StopHidesAndStaleCallbacksCannotReopenLens()
    {
        var keyboard = new MagnifierTestKeyboard();
        var lens = new CapturingMagnifierSession(new());
        var binding = new MagnifierHeldBinding(keyboard, HotkeyDefinition.CreateKeyboard(Chord, 0x5A), lens);
        keyboard.Callback!(Event());
        binding.Dispose();
        binding.Dispose();
        Assert.True(keyboard.Disposed);
        Assert.Equal(1, lens.Disposals);
        Assert.False(lens.IsVisible);
        Assert.False(keyboard.Callback(Event()));
    }

    [Fact]
    public void FailedRegistrationDestroysPrewarmedLens()
    {
        var keyboard = new MagnifierTestKeyboard { FailRegistration = true };
        var lens = new CapturingMagnifierSession(new());
        Assert.Throws<InvalidOperationException>(() => new MagnifierHeldBinding(keyboard,
            HotkeyDefinition.CreateKeyboard(Chord, 0x5A), lens));
        Assert.Equal(1, lens.Disposals);
    }
}
