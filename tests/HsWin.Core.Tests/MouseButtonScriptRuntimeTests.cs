using HsWin.Core.Config;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class MouseButtonScriptRuntimeTests
{
    [Fact]
    public void WatchDefaultsAndAllEventFieldsReachJavaScript()
    {
        var events = new CapturingMouseEventService();
        var alerts = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new() { MouseEvents = events, Alerts = alerts });
        runtime.Reload("""
            hs.mouse.watchButtons(event => hs.alert.show(JSON.stringify(event)));
            """);
        var watch = Assert.Single(events.ButtonWatches);
        Assert.Equal(new(), watch.Options);
        watch.Callback(new(MouseButton.XButton2, true, true, 3, -100, 200));
        var text = Assert.Single(alerts.Requests).Text;
        Assert.Contains("\"type\":\"mousedown\"", text);
        Assert.Contains("\"button\":\"forward\"", text);
        Assert.Contains("\"isDown\":true", text);
        Assert.Contains("\"isUp\":false", text);
        Assert.Contains("\"isInjected\":true", text);
        Assert.Contains("\"modifierFlags\":3", text);
        Assert.Contains("\"modifiers\":[\"ctrl\",\"alt\"]", text);
        Assert.Contains("\"x\":-100", text);
        Assert.Contains("\"y\":200", text);
    }

    [Theory]
    [InlineData("{blocking:true}")]
    [InlineData("{swallow:true}")]
    [InlineData("{preventDefault:true}")]
    public void BlockingAliasesAndInjectedOptionReachService(string options)
    {
        var events = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new() { MouseEvents = events });
        runtime.Reload($"hs.mouse.watchButtons(() => {{}}, {{...{options}, includeInjected:true}});");
        Assert.Equal(new(true, true), Assert.Single(events.ButtonWatches).Options);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("failedReload")]
    [InlineData("emergencyStop")]
    [InlineData("dispose")]
    public void LifecycleStopsEveryMouseButtonWatch(string action)
    {
        var events = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new() { MouseEvents = events });
        runtime.Reload("hs.mouse.watchButtons(() => {}, {blocking:true});");
        switch (action)
        {
            case "reload": runtime.Reload(""); break;
            case "failedReload": Assert.ThrowsAny<Exception>(() => runtime.Reload("hs.mouse.watchButtons(() => {}); throw new Error('test');")); break;
            case "emergencyStop": runtime.EmergencyStop(); break;
            case "dispose": runtime.Dispose(); break;
        }
        Assert.All(events.ButtonWatches, watch => Assert.True(watch.Registration.IsDisposed));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("dispose")]
    [InlineData("delete")]
    public void HandleAliasesStopTheWatch(string alias)
    {
        var events = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new() { MouseEvents = events });
        runtime.Reload($"const watch = hs.mouse.watchButtons(() => {{}}); watch.{alias}(); watch.{alias}();");
        Assert.True(Assert.Single(events.ButtonWatches).Registration.IsDisposed);
    }

    [Theory]
    [InlineData("null", "{}")]
    [InlineData("42", "{}")]
    [InlineData("() => {}", "true")]
    [InlineData("() => {}", "{blocking:'yes'}")]
    public void InvalidCallbackOrOptionsNeverRegisterAWatch(string callback, string options)
    {
        var events = new CapturingMouseEventService();
        using var runtime = new ScriptRuntime(new() { MouseEvents = events });
        Assert.ThrowsAny<Exception>(() => runtime.Reload($"hs.mouse.watchButtons({callback}, {options});"));
        Assert.Empty(events.ButtonWatches);
    }

    [Fact]
    public void LintSupportsButtonWatchesWithoutHooks() =>
        Assert.True(new ConfigLinter().LintSource("hs.mouse.watchButtons(() => {}, {blocking:true}).stop();").Success);
}
