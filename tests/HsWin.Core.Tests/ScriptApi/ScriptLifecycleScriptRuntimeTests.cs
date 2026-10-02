using HsWin.Core.Config;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class ScriptLifecycleScriptRuntimeTests
{
    [Fact]
    public void ReloadCreatesFreshJavaScriptEngine()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("globalThis.previousValue = 42;");
        runtime.Reload("""hs.alert.show(String(globalThis.previousValue), { type: "normal", durationMs: 1 });""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("undefined", request.Text);
    }

    [Fact]
    public void ReloadAcceptsDefaultConfig()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys
        });

        runtime.Reload(ConfigFileService.DefaultConfig);

        Assert.NotEmpty(hotkeys.Registrations);
    }

    [Fact]
    public void ReloadAcceptsScriptRuntimeServices()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Services");""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Services", request.Text);
    }

    [Fact]
    public void EmergencyStopDisposesResourcesAndClearsEngine()
    {
        var hotkeys = new CapturingHotkeyRegistrar();
        var timers = new CapturingScriptTimerService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Hotkeys = hotkeys,
            Timers = timers
        });

        runtime.Reload("""
            hs.hotkey.bind(["ctrl"], "F24", () => {});
            hs.timer.doEvery(1000, () => {});
            """);

        Assert.True(runtime.HasActiveEngine);
        Assert.NotEmpty(hotkeys.Registrations);
        Assert.NotEmpty(timers.Timers);

        runtime.EmergencyStop();

        Assert.False(runtime.HasActiveEngine);
        Assert.All(hotkeys.Registrations, registration => Assert.True(registration.IsDisposed));
        Assert.All(timers.Timers, timer => Assert.True(timer.IsDisposed));

        // Runtime remains reloadable after emergency stop.
        runtime.Reload("""hs.hotkey.bind(["ctrl"], "F23", () => {});""");
        Assert.True(runtime.HasActiveEngine);
    }
}
