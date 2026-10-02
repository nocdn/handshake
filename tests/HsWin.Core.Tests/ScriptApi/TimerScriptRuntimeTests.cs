using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class TimerScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesTimerDoAfter()
    {
        var presenter = new CapturingAlertPresenter();
        var timers = new CapturingScriptTimerService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Timers = timers
        });

        runtime.Reload("""hs.timer.doAfter(10, () => hs.alert.show("timer"));""");
        Assert.Single(timers.Timers).Trigger();

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("timer", request.Text);
    }
}
