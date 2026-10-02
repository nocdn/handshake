using HsWin.Core.Alerts;
using HsWin.Core.Hotkeys;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class MouseScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesMouseCurrentScreen()
    {
        var presenter = new CapturingAlertPresenter();
        var mouse = new CapturingMouseService(
            new MouseScreenSnapshot(
                "display-2",
                "Right Monitor",
                IsPrimary: false,
                new MousePointSnapshot(1930, 50),
                new MouseRectangleSnapshot(1920, 0, 2560, 1440),
                new MouseRectangleSnapshot(1920, 0, 2560, 1400)));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Mouse = mouse
        });

        runtime.Reload("""
            const screen = hs.mouse.getCurrentScreen();
            hs.alert.show(
              `${screen.id}:${screen.name}:${screen.isPrimary}:${screen.mousePosition.x},${screen.mousePosition.y}:${screen.bounds.width}x${screen.bounds.height}`,
              "normal",
              1);
            """);

        Assert.Equal(1, mouse.GetCurrentScreenCallCount);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("display-2:Right Monitor:false:1930,50:2560x1440", request.Text);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ReloadExposesMousePrimaryScreenShortcut(bool isPrimary, string expectedText)
    {
        var presenter = new CapturingAlertPresenter();
        var mouse = new CapturingMouseService(
            new MouseScreenSnapshot(
                "display-1",
                "Primary Monitor",
                isPrimary,
                new MousePointSnapshot(10, 20),
                new MouseRectangleSnapshot(0, 0, 1920, 1080),
                new MouseRectangleSnapshot(0, 0, 1920, 1040)));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Mouse = mouse
        });

        runtime.Reload("""
            hs.alert.show(String(hs.mouse.isOnPrimaryScreen()), "normal", 1);
            """);

        Assert.Equal(1, mouse.GetCurrentScreenCallCount);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal(expectedText, request.Text);
    }

    [Fact]
    public void ReloadExposesMouseClickAndRepeat()
    {
        var mouseInput = new CapturingMouseInputService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            MouseInput = mouseInput
        });

        runtime.Reload("""
            hs.mouse.click("right");
            const repeat = hs.mouse.repeat("right", { intervalMs: 20 });
            repeat.setIntervalMs(15);
            hs.mouse.stopRepeat();
            repeat.stop();
            """);

        Assert.Equal([MouseButton.Right], mouseInput.Clicks);
        var repeatRegistration = Assert.Single(mouseInput.Repeats);
        Assert.Equal(MouseButton.Right, repeatRegistration.Button);
        Assert.Equal(20, repeatRegistration.Options.IntervalMs);
        Assert.Equal(15, repeatRegistration.Registration.IntervalMs);
        Assert.Equal(1, mouseInput.StopActiveRepeatCallCount);
        Assert.True(repeatRegistration.Registration.IsDisposed);
    }

    [Fact]
    public void ReloadExposesMouseScrollWatch()
    {
        var presenter = new CapturingAlertPresenter();
        var mouseEvents = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            MouseEvents = mouseEvents
        });

        runtime.Reload("""
            hs.mouse.watchScroll(event => {
              hs.alert.show(`${event.type}:${event.axis}:${event.direction}:${event.delta}`, "normal", 1);
            }, { preventDefault: true, axes: "vertical" });
            """);

        var watch = Assert.Single(mouseEvents.Watches);
        Assert.True(watch.Options.Blocking);
        Assert.Equal(MouseScrollAxis.Vertical, watch.Options.Axes);

        // The host swallows scroll natively while a preventDefault watcher is registered; the callback is notification-only.
        watch.Callback(new MouseScrollEventSnapshot(
            MouseScrollEventSnapshot.ScrollType,
            MouseScrollEventSnapshot.VerticalAxis,
            MouseScrollEventSnapshot.DirectionUp,
            Delta: 120,
            Notches: 1,
            IsVertical: true,
            IsHorizontal: false,
            IsInjected: false,
            Modifiers: [],
            ModifierFlags: 0,
            X: 10,
            Y: 20));

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("scroll:vertical:up:120", request.Text);
    }

    [Fact]
    public void ReloadDisposesPreviousMouseScrollWatchers()
    {
        var mouseEvents = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            MouseEvents = mouseEvents
        });

        runtime.Reload("""hs.mouse.watchScroll(() => false);""");
        var watch = Assert.Single(mouseEvents.Watches).Registration;
        runtime.Reload("""console.log("new");""");

        Assert.True(watch.IsDisposed);
    }

    [Fact]
    public void MouseScrollWatchEventMatchesJsonShape()
    {
        var presenter = new CapturingAlertPresenter();
        var mouseEvents = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            MouseEvents = mouseEvents
        });

        runtime.Reload("""
            hs.mouse.watchScroll(event => {
              const shape = [
                event.type,
                event.axis,
                event.direction,
                event.delta,
                event.notches,
                event.isVertical,
                event.isHorizontal,
                event.isInjected,
                event.modifiers.join("+"),
                event.modifierFlags,
                event.x,
                event.y,
                Array.isArray(event.modifiers)
              ].join(":");
              hs.alert.show(shape, "normal", 1);
            }, { preventDefault: true });
            """);

        var watch = Assert.Single(mouseEvents.Watches);
        watch.Callback(new MouseScrollEventSnapshot(
            MouseScrollEventSnapshot.ScrollType,
            MouseScrollEventSnapshot.VerticalAxis,
            MouseScrollEventSnapshot.DirectionDown,
            Delta: -240,
            Notches: -2,
            IsVertical: true,
            IsHorizontal: false,
            IsInjected: false,
            Modifiers: ["alt"],
            ModifierFlags: (uint)HotkeyModifiers.Alt,
            X: 11,
            Y: 22));

        Assert.Equal(
            "scroll:vertical:down:-240:-2:true:false:false:alt:1:11:22:true",
            Assert.Single(presenter.Requests).Text);
    }

    [Fact]
    public void MouseScrollWatchHorizontalEventMatchesJsonShape()
    {
        var presenter = new CapturingAlertPresenter();
        var mouseEvents = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            MouseEvents = mouseEvents
        });

        runtime.Reload("""
            hs.mouse.watchScroll(event => {
              hs.alert.show(`${event.axis}:${event.direction}:${event.isVertical}:${event.isHorizontal}`, "normal", 1);
            });
            """);

        var watch = Assert.Single(mouseEvents.Watches);
        watch.Callback(new MouseScrollEventSnapshot(
            MouseScrollEventSnapshot.ScrollType,
            MouseScrollEventSnapshot.HorizontalAxis,
            MouseScrollEventSnapshot.DirectionLeft,
            Delta: -120,
            Notches: -1,
            IsVertical: false,
            IsHorizontal: true,
            IsInjected: false,
            Modifiers: [],
            ModifierFlags: 0,
            X: 0,
            Y: 0));

        Assert.Equal("horizontal:left:false:true", Assert.Single(presenter.Requests).Text);
    }
}
