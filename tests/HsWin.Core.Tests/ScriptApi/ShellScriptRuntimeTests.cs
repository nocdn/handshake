using HsWin.Core.Alerts;
using HsWin.Core.Scripting;
using HsWin.Core.Shell;

namespace HsWin.Core.Tests;

public sealed class ShellScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesExecuteCommand()
    {
        var presenter = new CapturingAlertPresenter();
        var shell = new CapturingShellService
        {
            ExecuteResult = new ShellExecutionResult("echo hello", true, 0, "hello\r\n", string.Empty, TimedOut: false)
        };
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Shell = shell
        });

        runtime.Reload("""
            const result = hs.execute("echo hello", { cwd: "C:\\Temp", timeoutMs: 1234 });
            hs.alert.show(`${result.success}:${result.status}:${result.exitCode}:${result.output.trim()}`, "normal", 1);
            """);

        var execution = Assert.Single(shell.Executions);
        Assert.Equal("echo hello", execution.Command);
        Assert.Equal(@"C:\Temp", execution.Options.WorkingDirectory);
        Assert.Equal(1234, execution.Options.TimeoutMs);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("true:true:0:hello", request.Text);
    }

    [Fact]
    public void ReloadExposesTaskRunForBackgroundCommands()
    {
        var presenter = new CapturingAlertPresenter();
        var shell = new CapturingShellService
        {
            ExecuteResult = new ShellExecutionResult("echo hello", true, 0, "hello\r\n", string.Empty, TimedOut: false)
        };
        var callbacks = new QueuedScriptCallbackScheduler();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Shell = shell,
            CallbackScheduler = callbacks
        });

        runtime.Reload("""
            hs.alert.show("Working", { type: "normal", loading: true, durationMs: 60000 });
            hs.task.run("echo hello", { cwd: "C:\\Temp", timeoutMs: 1234 }, result => {
              hs.alert.show(`${result.success}:${result.exitCode}:${result.output.trim()}`, { type: "success", durationMs: 2500 });
            });
            """);

        var startAlert = Assert.Single(presenter.Requests);
        Assert.Equal("Working", startAlert.Text);
        Assert.Equal(AlertIcon.Loader, startAlert.EffectiveIcon);

        Assert.True(callbacks.WaitForCallback(TimeSpan.FromSeconds(5)));
        callbacks.RunNext();

        var execution = Assert.Single(shell.Executions);
        Assert.Equal("echo hello", execution.Command);
        Assert.Equal(@"C:\Temp", execution.Options.WorkingDirectory);
        Assert.Equal(1234, execution.Options.TimeoutMs);
        Assert.Collection(
            presenter.Requests,
            request => Assert.Equal("Working", request.Text),
            request =>
            {
                Assert.Equal("true:0:hello", request.Text);
                Assert.Equal(AlertKind.Success, request.Kind);
                Assert.Equal(AlertIcon.Dot, request.EffectiveIcon);
            });
    }

    [Fact]
    public void ReloadExecuteLogsCommandDescriptionWithoutRawCommandText()
    {
        var logger = new CapturingRuntimeLogger();
        var shell = new CapturingShellService
        {
            ExecuteResult = new ShellExecutionResult("ignored", true, 0, string.Empty, string.Empty, TimedOut: false)
        };
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Logger = logger,
            Shell = shell
        });

        runtime.Reload("""
            hs.execute("curl --header Authorization: Bearer fake-token-value");
            """);

        var log = Assert.Single(logger.Infos, message => message.Contains("hs.execute()", StringComparison.Ordinal));
        Assert.DoesNotContain("fake-token-value", log);
        Assert.DoesNotContain("curl --header", log);
        Assert.Contains("length=", log);
        Assert.Contains("sha256=", log);
    }

    [Fact]
    public void ReloadDisposesOutstandingTaskRunCallbacks()
    {
        var presenter = new CapturingAlertPresenter();
        var shell = new CapturingShellService
        {
            ExecuteResult = new ShellExecutionResult("echo old", true, 0, "old\r\n", string.Empty, TimedOut: false)
        };
        var callbacks = new QueuedScriptCallbackScheduler();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Shell = shell,
            CallbackScheduler = callbacks
        });

        runtime.Reload("""
            hs.task.run("echo old", result => {
              hs.alert.show("Old callback should not run");
            });
            """);

        Assert.True(callbacks.WaitForCallback(TimeSpan.FromSeconds(5)));
        runtime.Reload("""console.log("new config");""");
        callbacks.RunNext();

        Assert.Empty(presenter.Requests);
    }
}
