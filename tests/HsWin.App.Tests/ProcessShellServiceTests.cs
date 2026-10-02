using System.Diagnostics;
using HsWin.App.Shell;
using HsWin.Core.Shell;

namespace HsWin.App.Tests;

public sealed class ProcessShellServiceTests
{
    private const string SlowCommand = "ping -n 30 127.0.0.1 > nul & echo secret-shell-token";

    [Fact]
    public void ExecuteReturnsOutputAndExitCode()
    {
        var service = new ProcessShellService(new CapturingRuntimeLogger());

        var result = service.Execute("echo hello", ShellExecutionOptions.Default);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteNeverLogsTheRawCommandText()
    {
        var logger = new CapturingRuntimeLogger();
        var service = new ProcessShellService(logger);

        service.Execute("echo secret-shell-token", ShellExecutionOptions.Default);
        service.Execute(SlowCommand, new ShellExecutionOptions(null, 300));

        Assert.NotEmpty(logger.All);
        Assert.DoesNotContain(logger.All, message => message.Contains("secret-shell-token", StringComparison.Ordinal));
    }

    [Fact]
    public void TimedOutCommandIsKilledAndLoggedByDescription()
    {
        var logger = new CapturingRuntimeLogger();
        var service = new ProcessShellService(logger);
        var startedAt = Stopwatch.GetTimestamp();

        var result = service.Execute(SlowCommand, new ShellExecutionOptions(null, 300));

        Assert.True(result.TimedOut);
        Assert.False(result.Success);
        Assert.True(Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(10), "The command was not stopped at its timeout.");
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("length=", warning, StringComparison.Ordinal);
        Assert.Contains("sha256=", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellingStopsTheCommandAndReportsCancelled()
    {
        var service = new ProcessShellService(new CapturingRuntimeLogger());
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(300);
        var startedAt = Stopwatch.GetTimestamp();

        var result = service.Execute(SlowCommand, new ShellExecutionOptions(null, 60_000), cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.True(Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(10), "The command was not stopped on cancel.");
    }
}
