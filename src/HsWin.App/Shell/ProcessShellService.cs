using HsWin.Core.Logging;
using HsWin.Core.Shell;
using System.Diagnostics;

namespace HsWin.App.Shell;

internal sealed class ProcessShellService : IShellService
{
    private readonly IRuntimeLogger _logger;

    public ProcessShellService(IRuntimeLogger logger)
    {
        _logger = logger;
    }

    public ShellExecutionResult Execute(string command, ShellExecutionOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = CreateShellStartInfo(command, options)
            };

            if (!process.Start())
            {
                return new ShellExecutionResult(command, false, null, string.Empty, "Process did not start.", TimedOut: false);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var outcome = WaitForExit(process, options.TimeoutMs, cancellationToken);
            if (outcome != WaitOutcome.Exited)
            {
                // cmd.exe is our own child here, so its whole tree is ours to end.
                KillProcessTree(process);
                var partialOutput = outputTask.GetAwaiter().GetResult();
                var partialError = errorTask.GetAwaiter().GetResult();
                var timedOut = outcome == WaitOutcome.TimedOut;
                _logger.Warning(timedOut
                    ? $"Command timed out command={LogSanitizer.DescribeCommand(command)} timeoutMs={options.TimeoutMs}."
                    : $"Command cancelled command={LogSanitizer.DescribeCommand(command)}.");
                return new ShellExecutionResult(command, false, null, partialOutput, partialError, TimedOut: timedOut, Cancelled: !timedOut);
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            var success = process.ExitCode == 0;
            return new ShellExecutionResult(command, success, process.ExitCode, output, error, TimedOut: false);
        }
        catch (Exception exception)
        {
            _logger.Error($"Command failed command={LogSanitizer.DescribeCommand(command)}.", exception);
            return new ShellExecutionResult(command, false, null, string.Empty, exception.Message, TimedOut: false);
        }
    }

    public LaunchResult Launch(string target, LaunchOptions options)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                WorkingDirectory = options.WorkingDirectory ?? string.Empty,
                Arguments = options.Arguments ?? string.Empty
            };

            using var process = Process.Start(startInfo);
            return new LaunchResult(target, Success: true, process?.Id, Error: null);
        }
        catch (Exception exception)
        {
            _logger.Error($"Launch failed target='{LogSanitizer.DescribeTarget(target)}'.", exception);
            return new LaunchResult(target, Success: false, ProcessId: null, exception.Message);
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string command, ShellExecutionOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = options.WorkingDirectory ?? string.Empty
        };

        startInfo.ArgumentList.Add("/S");
        startInfo.ArgumentList.Add("/C");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static WaitOutcome WaitForExit(Process process, int timeoutMs, CancellationToken cancellationToken)
    {
        // A task source rather than a wait handle: killing the process after a timeout or cancel
        // still raises Exited on a thread-pool thread, and that must not touch a disposed handle.
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => exited.TrySetResult();
        if (process.HasExited)
        {
            return WaitOutcome.Exited;
        }

        var cancelled = false;
        var signalled = false;
        try
        {
            signalled = exited.Task.Wait(timeoutMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (signalled || process.HasExited)
        {
            // Let the redirected streams finish before the caller reads them.
            process.WaitForExit();
            return WaitOutcome.Exited;
        }

        return cancelled ? WaitOutcome.Cancelled : WaitOutcome.TimedOut;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited on its own in the meantime.
        }
    }

    private enum WaitOutcome
    {
        Exited,
        TimedOut,
        Cancelled
    }
}
