using HsWin.Core.Alerts;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class HotkeyScriptRuntimeTests
{
    [Fact]
    public void ReloadRegistersHotkeyBindings()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload("""hs.hotkey.bind(["ctrl", "alt"], "R", () => hs.alert.show("Pressed"));""");
        hotkeys.TriggerOnlyRegistration();

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Pressed", request.Text);
        var registration = Assert.Single(hotkeys.Registrations);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, registration.Hotkey.Modifiers);
        Assert.Equal((uint)'R', registration.Hotkey.VirtualKey);
    }

    [Fact]
    public void ReloadRegistersMouseHotkeyBindings()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload("""hs.hotkey.bind(["ctrl"], "mouse.back", () => hs.alert.show("Mouse"));""");
        hotkeys.TriggerOnlyRegistration();

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Mouse", request.Text);
        var registration = Assert.Single(hotkeys.Registrations);
        Assert.Equal(HotkeyInputKind.MouseButton, registration.Hotkey.InputKind);
        Assert.Equal(HotkeyModifiers.Control, registration.Hotkey.Modifiers);
        Assert.Equal(HotkeyMouseButton.XButton1, registration.Hotkey.MouseButton);
    }

    [Fact]
    public void ReloadRegistersHeldHotkeyCallbacks()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""
            hs.hotkey.bindHeld(["ctrl", "alt"], "space", event => {
              hs.alert.show(`down:${event.key}`, "normal", 1);
            }, event => {
              hs.alert.show(`up:${event.key}`, "normal", 1);
            });
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.True(watch.Options.Blocking);
        Assert.False(watch.Options.IncludeInjected);

        var down = watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            0x20,
            "space",
            ["ctrl", "alt"],
            (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt),
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false));
        var repeat = watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            0x20,
            "space",
            ["ctrl", "alt"],
            (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt),
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false));
        var up = watch.Callback(new KeyboardEventSnapshot(
            "keyup",
            0x20,
            "space",
            ["ctrl", "alt"],
            (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt),
            IsKeyDown: false,
            IsKeyUp: true,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false));

        Assert.True(down);
        Assert.True(repeat);
        Assert.True(up);
        Assert.Collection(
            presenter.Requests,
            request => Assert.Equal("down:space", request.Text),
            request => Assert.Equal("up:space", request.Text));
    }

    [Fact]
    public void HeldHotkeyReleasesWhenRequiredModifierIsReleased()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""
            hs.hotkey.whileHeld(["ctrl", "alt"], "R", () => hs.alert.show("start", "normal", 1), () => hs.alert.show("stop", "normal", 1));
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        _ = watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            (uint)'R',
            "r",
            ["ctrl", "alt"],
            (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt),
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false));
        var releasedModifier = watch.Callback(new KeyboardEventSnapshot(
            "keyup",
            KeyboardKeyRules.VkLeftMenu,
            "alt",
            ["ctrl"],
            (uint)HotkeyModifiers.Control,
            IsKeyDown: false,
            IsKeyUp: true,
            IsModifier: true,
            IsInjected: false,
            IsExtended: false));

        // Modifier releases always reach the foreground app; only the hold ends.
        Assert.False(releasedModifier);
        Assert.Collection(
            presenter.Requests,
            request => Assert.Equal("start", request.Text),
            request => Assert.Equal("stop", request.Text));
    }

    [Fact]
    public void HeldHotkeyCanAllowExtraModifiersAndDisableBlocking()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""
            hs.hotkey.bindHeld(["ctrl"], "A", () => hs.alert.show("start", "normal", 1), () => hs.alert.show("stop", "normal", 1), {
              allowExtraModifiers: true,
              blocking: false,
              includeInjected: true
            });
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.False(watch.Options.Blocking);
        Assert.True(watch.Options.IncludeInjected);
        var swallowed = watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            (uint)'A',
            "a",
            ["ctrl", "shift"],
            (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift),
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false));

        Assert.False(swallowed);
        Assert.Equal("start", Assert.Single(presenter.Requests).Text);
    }

    [Fact]
    public void ReloadDisposesPreviousHotkeyBindings()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload("""hs.hotkey.bind(["ctrl"], "A", () => hs.alert.show("old"));""");
        var oldRegistration = Assert.Single(hotkeys.Registrations);
        runtime.Reload("""hs.hotkey.bind(["ctrl"], "B", () => hs.alert.show("new"));""");

        Assert.True(oldRegistration.IsDisposed);
        Assert.Equal(2, hotkeys.Registrations.Count);
        Assert.False(hotkeys.Registrations[1].IsDisposed);
    }

    [Fact]
    public void ReloadDisposesPreviousHeldHotkeyBindings()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""hs.hotkey.bindHeld([], "F13", () => {}, () => {});""");
        var watch = Assert.Single(keyboardEvents.Watches).Registration;
        runtime.Reload("""console.log("new");""");

        Assert.True(watch.IsDisposed);
    }

    [Fact]
    public void HeldHotkeyRegistersMousePressAndReleaseCallbacks()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload("""
            hs.hotkey.bindHeld([], "mouse.back", event => {
              hs.alert.show(`${event.type}:${event.button}`, "normal", 1);
            }, event => {
              hs.alert.show(`${event.type}:${event.button}`, "normal", 1);
            }, { blocking: false });
            """);

        var registration = Assert.Single(hotkeys.HeldRegistrations);
        Assert.Equal(HotkeyMouseButton.XButton1, registration.Hotkey.MouseButton);
        Assert.False(registration.Blocking);

        registration.TriggerPressed();
        registration.TriggerReleased();

        Assert.Collection(
            presenter.Requests,
            request => Assert.Equal("mousedown:back", request.Text),
            request => Assert.Equal("mouseup:back", request.Text));
    }

    [Fact]
    public void HotkeyCallbackErrorsShowAlertInsteadOfEscaping()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload("""hs.hotkey.bind(["ctrl"], "E", () => { throw new Error("boom"); });""");
        hotkeys.TriggerOnlyRegistration();

        var request = Assert.Single(presenter.Requests);
        Assert.Equal(AlertKind.Error, request.Kind);
        Assert.Contains("Hotkey callback error", request.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardHeldHotkeyRegistersKeyFilterCoveringReleasePaths()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""hs.hotkey.bindHeld(["shift"], "F13", () => {}, () => {});""");

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.NotNull(watch.Options.KeyFilter);
        var filter = watch.Options.KeyFilter!;
        Assert.Contains(0x7Cu, filter); // F13 definition key

        // The release path can act on any modifier key-up, so the filter must cover every
        // VK that KeyboardKeyRules classifies as a modifier — not just the bound ones.
        for (var virtualKey = 0u; virtualKey < 256; virtualKey++)
        {
            if (KeyboardKeyRules.IsModifierVirtualKey(virtualKey))
            {
                Assert.Contains(virtualKey, filter);
            }
        }
    }

    [Fact]
    public void KeyboardHeldHotkeyEventMatchesJsonShape()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""
            hs.hotkey.bindHeld(["shift"], "F13", event => {
              const shape = [
                event.type,
                event.keyCode,
                event.key,
                event.modifiers.join("+"),
                event.modifierFlags,
                event.isKeyDown,
                event.isKeyUp,
                event.isModifier,
                event.isInjected,
                event.isExtended
              ].join(":");
              hs.alert.show(shape, "normal", 1);
            }, () => {});
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.True(watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            0x7C,
            "f13",
            ["shift"],
            (uint)HotkeyModifiers.Shift,
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false)));

        Assert.Equal(
            "keydown:124:f13:shift:4:true:false:false:false:false",
            Assert.Single(presenter.Requests).Text);
    }

    [Fact]
    public void MouseHeldHotkeyEventMatchesJsonShape()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload("""
            hs.hotkey.bindHeld([], "mouse.forward", event => {
              hs.alert.show(`${event.type}:${event.button}:${event.isDown}:${event.isUp}`, "normal", 1);
            }, event => {
              hs.alert.show(`${event.type}:${event.button}:${event.isDown}:${event.isUp}`, "normal", 1);
            });
            """);

        var registration = Assert.Single(hotkeys.HeldRegistrations);
        registration.TriggerPressed();

        Assert.Equal("mousedown:forward:true:false", Assert.Single(presenter.Requests).Text);
    }
}
