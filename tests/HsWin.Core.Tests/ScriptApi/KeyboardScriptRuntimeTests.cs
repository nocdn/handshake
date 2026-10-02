using HsWin.Core.Hotkeys;
using HsWin.Core.Input;
using HsWin.Core.Keyboard;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class KeyboardScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesKeyboardWatchAndTap()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var keyboardEvents = new CapturingKeyboardEventService();
        var keyboardInput = new CapturingKeyboardInputService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            KeyboardEvents = keyboardEvents,
            KeyboardInput = keyboardInput
        });

        runtime.Reload("""
            hs.keyboard.watch(event => {
              if (event.type === "keydown" && event.key === "w") {
                hs.keyboard.tap(event.keyCode, { suppressPhysicalModifiers: ["alt", "shift"] });
                return true;
              }

              return false;
            }, { blocking: true });
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.False(watch.Options.IncludeInjected);
        var swallowed = watch.Callback(
            new KeyboardEventSnapshot(
                "keydown",
                (uint)'W',
                "w",
                ["alt", "shift"],
                (uint)(HotkeyModifiers.Alt | HotkeyModifiers.Shift),
                IsKeyDown: true,
                IsKeyUp: false,
                IsModifier: false,
                IsInjected: false,
                IsExtended: false));

        Assert.True(swallowed);
        var tap = Assert.Single(keyboardInput.Taps);
        Assert.Equal((uint)'W', tap.VirtualKey);
        Assert.Equal(HotkeyModifiers.Alt | HotkeyModifiers.Shift, tap.Options.SuppressPhysicalModifiers);
    }

    [Fact]
    public void ReloadExposesKeyboardRemapWithoutScriptCallbackOnHookPath()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        var keyboardInput = new CapturingKeyboardInputService();
        var logger = new CapturingRuntimeLogger();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardEvents = keyboardEvents,
            KeyboardInput = keyboardInput,
            Logger = logger
        });

        runtime.Reload("""hs.keyboard.remap("pageup", "end");""");

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.True(watch.Options.Blocking);
        Assert.False(watch.Options.IncludeInjected);
        Assert.True(watch.Options.Prepend);
        Assert.NotNull(watch.Options.KeyFilter);
        Assert.Contains(0x21u, watch.Options.KeyFilter);

        var ignored = watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            0x22,
            "pagedown",
            [],
            0,
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: true));
        Assert.False(ignored);
        Assert.Empty(keyboardInput.Taps);

        var swallowedDown = watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            0x21,
            "pageup",
            [],
            0,
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: true));
        var swallowedUp = watch.Callback(new KeyboardEventSnapshot(
            "keyup",
            0x21,
            "pageup",
            [],
            0,
            IsKeyDown: false,
            IsKeyUp: true,
            IsModifier: false,
            IsInjected: false,
            IsExtended: true));

        Assert.True(swallowedDown);
        Assert.True(swallowedUp);
        Assert.Empty(keyboardInput.Taps);
        Assert.Equal([0x23u], keyboardInput.KeyDowns);
        Assert.Equal([0x23u], keyboardInput.KeyUps);
        // The registration log states the mapping; per-keystroke remap logging was removed
        // from the WH_KEYBOARD_LL path deliberately.
        Assert.Contains(logger.Infos, info => info.Contains("hs.keyboard.remap('pageup', 'end')", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.All, line => line.Contains("keydown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemappedKeyStaysHeldWhileTheSourceKeyIsHeld()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        var keyboardInput = new CapturingKeyboardInputService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardEvents = keyboardEvents,
            KeyboardInput = keyboardInput
        });
        runtime.Reload("""hs.keyboard.remap("a", "left");""");
        var watch = Assert.Single(keyboardEvents.Watches);

        // Auto-repeat sends the source key-down repeatedly while it stays held.
        for (var repeat = 0; repeat < 3; repeat++)
        {
            Assert.True(watch.Callback(CreateRemapEvent("keydown", (uint)'A', isDown: true)));
        }

        Assert.Empty(keyboardInput.KeyUps);
        Assert.Equal([0x25u, 0x25u, 0x25u], keyboardInput.KeyDowns);

        Assert.True(watch.Callback(CreateRemapEvent("keyup", (uint)'A', isDown: false)));
        Assert.Equal([0x25u], keyboardInput.KeyUps);
    }

    [Fact]
    public void SourceKeyUpPassesThroughWhenItsDownPredatedTheRemap()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        var keyboardInput = new CapturingKeyboardInputService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardEvents = keyboardEvents,
            KeyboardInput = keyboardInput
        });
        runtime.Reload("""hs.keyboard.remap("a", "left");""");
        var watch = Assert.Single(keyboardEvents.Watches);

        var swallowed = watch.Callback(CreateRemapEvent("keyup", (uint)'A', isDown: false));

        Assert.False(swallowed);
        Assert.Empty(keyboardInput.KeyUps);
    }

    [Fact]
    public void RemovingARemapWhileTheSourceIsHeldReleasesTheTarget()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        var keyboardInput = new CapturingKeyboardInputService();
        var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardEvents = keyboardEvents,
            KeyboardInput = keyboardInput
        });
        runtime.Reload("""hs.keyboard.remap("a", "left");""");
        var watch = Assert.Single(keyboardEvents.Watches);
        watch.Callback(CreateRemapEvent("keydown", (uint)'A', isDown: true));

        runtime.Dispose();

        Assert.True(watch.Registration.IsDisposed);
        Assert.Equal([0x25u], keyboardInput.KeyUps);
    }

    private static KeyboardEventSnapshot CreateRemapEvent(string type, uint virtualKey, bool isDown)
    {
        return new KeyboardEventSnapshot(
            type,
            virtualKey,
            KeyboardKeyRules.GetDisplayName(virtualKey),
            [],
            0,
            IsKeyDown: isDown,
            IsKeyUp: !isDown,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false);
    }

    [Fact]
    public void ReloadExposesKeyboardTapModifiers()
    {
        var keyboardInput = new CapturingKeyboardInputService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            KeyboardInput = keyboardInput
        });

        runtime.Reload("""hs.keyboard.tap("right", { modifiers: ["win", "shift"] });""");

        var tap = Assert.Single(keyboardInput.Taps);
        Assert.Equal(0x27u, tap.VirtualKey);
        Assert.Equal(HotkeyModifiers.Win | HotkeyModifiers.Shift, tap.Options.Modifiers);
    }

    [Fact]
    public void KeyboardWatchDefaultsToNonBlockingAndCannotSwallow()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        var logger = new CapturingRuntimeLogger();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents,
            Logger = logger
        });

        runtime.Reload("""
            hs.keyboard.watch(event => {
              hs.alert.show(event.key, "normal", 1);
              return true;
            });
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.False(watch.Options.Blocking);
        var swallowed = watch.Callback(
            new KeyboardEventSnapshot(
                "keydown",
                (uint)'A',
                "a",
                [],
                0,
                IsKeyDown: true,
                IsKeyUp: false,
                IsModifier: false,
                IsInjected: false,
                IsExtended: false));

        Assert.False(swallowed);
        Assert.Equal("a", Assert.Single(presenter.Requests).Text);
        Assert.Contains(logger.Warnings, warning => warning.Contains("Non-blocking hs.keyboard.watch", StringComparison.Ordinal));
    }

    [Fact]
    public void KeyboardWatchBlockingOptionCanSwallow()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""
            hs.keyboard.watch(() => true, { blocking: true });
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.True(watch.Options.Blocking);
        var swallowed = watch.Callback(
            new KeyboardEventSnapshot(
                "keydown",
                (uint)'A',
                "a",
                [],
                0,
                IsKeyDown: true,
                IsKeyUp: false,
                IsModifier: false,
                IsInjected: false,
                IsExtended: false));

        Assert.True(swallowed);
    }

    [Fact]
    public void KeyboardWatchParsesIncludeInjectedAndBlockingTogether()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = new CapturingAlertPresenter(),
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""hs.keyboard.watch(() => false, { includeInjected: true, blocking: true });""");

        var options = Assert.Single(keyboardEvents.Watches).Options;
        Assert.True(options.IncludeInjected);
        Assert.True(options.Blocking);
    }

    [Fact]
    public void KeyboardWatchParsesKeyFiltersFromJavaScript()
    {
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = new CapturingAlertPresenter(),
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""hs.keyboard.watch(() => false, { blocking: true, keys: ["pageup", "pagedown", 0xC0] });""");

        var options = Assert.Single(keyboardEvents.Watches).Options;
        Assert.True(options.Blocking);
        Assert.NotNull(options.KeyFilter);
        Assert.Contains(0x21u, options.KeyFilter);
        Assert.Contains(0x22u, options.KeyFilter);
        Assert.Contains(0xC0u, options.KeyFilter);
    }

    [Fact]
    public void ReloadExposesKeyboardRepeatPulse()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardInput = new CapturingKeyboardInputService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardInput = keyboardInput
        });

        runtime.Reload("""
            hs.keyboard.repeatPulse("w", {
              intervalMs: 120,
              keyDownMs: 60,
              inputMethod: "windowMessage",
              suppressPhysicalModifiers: ["alt", "shift"]
            });
            """);

        var repeat = Assert.Single(keyboardInput.Repeats);
        Assert.Equal((uint)'W', repeat.VirtualKey);
        Assert.Equal(120, repeat.Options.IntervalMs);
        Assert.Equal(60, repeat.Options.KeyDownMs);
        Assert.Equal(InputMethod.WindowMessage, repeat.Options.InputMethod);
        Assert.Equal(HotkeyModifiers.Alt | HotkeyModifiers.Shift, repeat.Options.SuppressPhysicalModifiers);
    }

    [Fact]
    public void KeyboardRepeatRejectsInstantaneousModifierAndLogsWarning()
    {
        var logger = new CapturingRuntimeLogger();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = new CapturingAlertPresenter(),
            KeyboardInput = new CapturingKeyboardInputService(),
            Logger = logger
        });

        var exception = Assert.ThrowsAny<Exception>(() =>
            runtime.Reload("""hs.keyboard.repeat("shift", { intervalMs: 120 });"""));

        Assert.Contains("repeatPulse", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardRepeatPulseRequiresPositiveKeyDownDuration()
    {
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = new CapturingAlertPresenter(),
            KeyboardInput = new CapturingKeyboardInputService()
        });

        var exception = Assert.ThrowsAny<Exception>(() =>
            runtime.Reload("""hs.keyboard.repeatPulse("w", { intervalMs: 120 });"""));

        Assert.Contains("keyDownMs", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardRepeatDirectsHeldDurationsToRepeatPulse()
    {
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = new CapturingAlertPresenter(),
            KeyboardInput = new CapturingKeyboardInputService()
        });

        var exception = Assert.ThrowsAny<Exception>(() =>
            runtime.Reload("""hs.keyboard.repeat("w", { intervalMs: 120, keyDownMs: 60 });"""));

        Assert.Contains("repeatPulse", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReloadDisposesKeyboardWatchesAndTimers()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var keyboardEvents = new CapturingKeyboardEventService();
        var timers = new CapturingScriptTimerService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            KeyboardEvents = keyboardEvents,
            Timers = timers
        });

        runtime.Reload("""
            hs.keyboard.watch(() => false);
            hs.timer.doEvery(25, () => {});
            """);
        var watch = Assert.Single(keyboardEvents.Watches).Registration;
        var timer = Assert.Single(timers.Timers);
        runtime.Reload("""console.log("second");""");

        Assert.True(watch.IsDisposed);
        Assert.True(timer.IsDisposed);
    }

    [Fact]
    public void KeyboardWatchEventMatchesJsonShapeWithNativeModifierArray()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardEvents = new CapturingKeyboardEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardEvents = keyboardEvents
        });

        runtime.Reload("""
            hs.keyboard.watch(event => {
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
                event.isExtended,
                Array.isArray(event.modifiers)
              ].join(":");
              hs.alert.show(shape, "normal", 1);
              return false;
            }, { blocking: true });
            """);

        var watch = Assert.Single(keyboardEvents.Watches);
        Assert.False(watch.Callback(new KeyboardEventSnapshot(
            "keydown",
            0x42,
            "b",
            ["ctrl", "shift"],
            (uint)(HotkeyModifiers.Control | HotkeyModifiers.Shift),
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: false,
            IsExtended: false)));

        // Field order, modifier order (ctrl before shift), flag values, and the native-array
        // modifier list must match the object JSON.parse used to produce.
        Assert.Equal(
            "keydown:66:b:ctrl+shift:6:true:false:false:false:false:true",
            Assert.Single(presenter.Requests).Text);
    }
}
