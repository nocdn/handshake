using HsWin.Core.Alerts;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class ScrittoScriptRuntimeTests
{
    [Fact]
    public void ReloadPassesScrittoRollOptionsThrough()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Count 50", { type: "normal", durationMs: 1500, scritto: true, trend: 1, rollDurationMs: 300, bounce: true });""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Count 50", request.Text);
        Assert.True(request.AnimateRoll);
        Assert.Equal(1, request.Trend);
        Assert.Equal(300, request.RollDurationMs);
        Assert.True(request.Bounce);
    }

    [Fact]
    public void ReloadAcceptsRollAliasAndNestedTransitionDuration()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Count 51", { roll: true, transition: { duration: 300 } });""");

        var request = Assert.Single(presenter.Requests);
        Assert.True(request.AnimateRoll);
        Assert.Equal(300, request.RollDurationMs);
    }

    [Fact]
    public void ReloadRejectsUnknownTrend()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        var exception = Assert.ThrowsAny<Exception>(() =>
            runtime.Reload("""hs.alert.show("Count 52", { scritto: true, trend: 2 });"""));

        Assert.Contains("trend", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CapturingAlertPresenter : IAlertPresenter
    {
        public List<AlertRequest> Requests { get; } = [];

        public void Show(AlertRequest request)
        {
            Requests.Add(request);
        }
    }
}
