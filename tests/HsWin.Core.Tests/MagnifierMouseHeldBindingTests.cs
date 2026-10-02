using HsWin.Core.Hotkeys;
using HsWin.Core.Magnifier;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class MagnifierMouseHeldBindingTests
{
    private static PhysicalMouseButtonEvent Input(bool down = true, HotkeyModifiers modifiers = HotkeyModifiers.None,
        HotkeyMouseButton button = HotkeyMouseButton.XButton1) => new(button, down, modifiers);

    [Theory]
    [InlineData("mouse.back", HotkeyMouseButton.XButton1)]
    [InlineData("mouse.forward", HotkeyMouseButton.XButton2)]
    [InlineData("mouse.middle", HotkeyMouseButton.Middle)]
    public void ScriptMouseHoldShowsOnceAndHidesOnReleaseWithChangedModifiers(string name, HotkeyMouseButton button)
    {
        var mouse = new MagnifierTestMouse();
        var magnifier = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { MouseButtons = mouse, Magnifier = magnifier });
        runtime.Reload($"hs.magnifier.bindHeld([], '{name}', {{diameter:720, zoom:2, fps:240}});");
        var lens = Assert.Single(magnifier.Sessions);
        mouse.Callback!(Input(button: button));
        mouse.Callback(Input(button: button));
        Assert.True(lens.IsVisible);
        Assert.Equal(1, lens.Shows);
        mouse.Callback(Input(false, HotkeyModifiers.Shift, button));
        Assert.False(lens.IsVisible);
        Assert.Equal(1, lens.Hides);
        mouse.Callback(Input(false, button: button));
        Assert.Equal(1, lens.Hides);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("failedReload")]
    [InlineData("emergencyStop")]
    [InlineData("dispose")]
    public void LifecycleDisposesMouseHoldAndIgnoresAlreadyQueuedCallbacks(string action)
    {
        var mouse = new MagnifierTestMouse();
        var magnifier = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { MouseButtons = mouse, Magnifier = magnifier });
        runtime.Reload("hs.magnifier.bindHeld([], 'mouse.back');");
        var lens = Assert.Single(magnifier.Sessions);
        var callback = mouse.Callback!;
        callback(Input());
        switch (action)
        {
            case "reload": runtime.Reload(""); break;
            case "failedReload": Assert.ThrowsAny<Exception>(() => runtime.Reload("throw new Error('failed');")); break;
            case "emergencyStop": runtime.EmergencyStop(); break;
            case "dispose": runtime.Dispose(); break;
        }
        callback(Input());
        callback(Input(false));
        Assert.True(mouse.Disposed);
        Assert.Equal(1, lens.Shows);
        Assert.Equal(1, lens.Disposals);
        Assert.False(lens.IsVisible);
    }

    [Fact]
    public void WrongButtonOrModifiersDoNotActivateLens()
    {
        var mouse = new MagnifierTestMouse();
        var lens = new CapturingMagnifierSession(new());
        using var binding = new MagnifierMouseHeldBinding(mouse,
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.Control, HotkeyMouseButton.XButton1), lens);
        mouse.Callback!(Input());
        mouse.Callback(Input(modifiers: HotkeyModifiers.Control, button: HotkeyMouseButton.XButton2));
        Assert.Equal(0, lens.Shows);
        mouse.Callback(Input(modifiers: HotkeyModifiers.Control));
        Assert.True(lens.IsVisible);
        binding.Dispose();
        binding.Dispose();
        Assert.Equal(1, lens.Disposals);
    }

    [Fact]
    public void RegistrationFailureDisposesLens()
    {
        var mouse = new MagnifierTestMouse { FailRegistration = true };
        var lens = new CapturingMagnifierSession(new());
        Assert.Throws<InvalidOperationException>(() => new MagnifierMouseHeldBinding(mouse,
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1), lens));
        Assert.Equal(1, lens.Disposals);
    }
}
