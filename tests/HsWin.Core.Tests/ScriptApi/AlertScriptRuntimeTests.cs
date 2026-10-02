using HsWin.Core.Alerts;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class AlertScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesHsAlertShowWithDefaults()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Loaded");""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Loaded", request.Text);
        Assert.Equal(AlertKind.Success, request.Kind);
        Assert.Equal(2000, request.DurationMs);
    }

    [Fact]
    public void ReloadSupportsAlertObjectOptions()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Plain", { type: "normal", durationMs: 1250 });""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Plain", request.Text);
        Assert.Equal(AlertKind.Normal, request.Kind);
        Assert.Equal(1250, request.DurationMs);
    }

    [Fact]
    public void ReloadSupportsAlertLoaderOption()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Working", { type: "normal", loading: true, durationMs: 60000 });""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Working", request.Text);
        Assert.Equal(AlertKind.Normal, request.Kind);
        Assert.Equal(AlertIcon.Loader, request.EffectiveIcon);
        Assert.Equal(60000, request.DurationMs);
    }

    [Fact]
    public void ReloadSupportsCursorFollowingAlertStyle()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Testing", { style: "following", durationMs: 6000 });""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Testing", request.Text);
        Assert.Equal(AlertStyle.Following, request.Style);
        Assert.Equal(AlertIcon.None, request.EffectiveIcon);
        Assert.Equal(6000, request.DurationMs);
    }

    [Fact]
    public void ReloadSupportsAlertKindAndDurationArguments()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""hs.alert.show("Boom", "error", 4500);""");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Boom", request.Text);
        Assert.Equal(AlertKind.Error, request.Kind);
        Assert.Equal(4500, request.DurationMs);
    }

    [Fact]
    public void ReloadExposesOperationToastHandle()
    {
        var presenter = new CapturingAlertPresenter();
        var timers = new CapturingScriptTimerService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Timers = timers
        });

        runtime.Reload("""
            const toast = hs.alert.operation("Recording");
            toast.loading("Uploading");
            toast.loading("Transcribing");
            toast.success("Copied");
            """);

        Assert.Single(timers.Timers);
        Assert.Collection(
            presenter.Requests,
            request =>
            {
                Assert.StartsWith("Recording ", request.Text, StringComparison.Ordinal);
                Assert.Equal(AlertIcon.Loader, request.EffectiveIcon);
            },
            request =>
            {
                Assert.Equal("Uploading", request.Text);
                Assert.Equal(AlertIcon.Loader, request.EffectiveIcon);
            },
            request =>
            {
                Assert.Equal("Transcribing", request.Text);
                Assert.Equal(AlertIcon.Loader, request.EffectiveIcon);
            },
            request =>
            {
                Assert.Equal("Copied", request.Text);
                Assert.Equal(AlertKind.Success, request.Kind);
                Assert.Equal(AlertIcon.Dot, request.EffectiveIcon);
            });
        Assert.True(Assert.Single(timers.Timers).IsDisposed);
    }

    [Fact]
    public void OperationToastHideTargetsItsFollowingStyleChannel()
    {
        var presenter = new CapturingAlertPresenter();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter
        });

        runtime.Reload("""
            const toast = hs.alert.operation("Testing", { style: "following", elapsed: false });
            toast.hide();
            """);

        Assert.Collection(
            presenter.Requests,
            request =>
            {
                Assert.Equal(AlertStyle.Following, request.Style);
                Assert.Equal(60_000, request.DurationMs);
            },
            request =>
            {
                Assert.Equal(AlertStyle.Following, request.Style);
                Assert.Equal(0, request.DurationMs);
            });
    }
}
