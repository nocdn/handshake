using HsWin.Core.Commands;
using HsWin.Core.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace HsWin.App;

/// <summary>
/// Makes room for a new instance. A running instance is first asked to quit over the command pipe,
/// so it releases held keys and repeats and leaves the apps it launched (and an open config editor)
/// alone. Only an instance that does not exit in time is killed, and never its process tree.
/// </summary>
internal static class PreviousInstanceCleaner
{
    private const int QuitConnectTimeoutMs = 1000;
    private const int QuitResponseTimeoutMs = 2000;
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan KillExitTimeout = TimeSpan.FromSeconds(5);

    public static void StopPreviousInstances(IRuntimeLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var startedAt = Stopwatch.GetTimestamp();
        var instances = FindPreviousInstances();
        if (instances.Count == 0)
        {
            logger.Info($"Previous instance cleanup found nothing elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");
            return;
        }

        try
        {
            var quitRequested = RequestQuitOverPipe(logger);
            var deadline = Stopwatch.GetTimestamp() + (long)(GracefulExitTimeout.TotalSeconds * Stopwatch.Frequency);
            var graceful = 0;
            var killed = 0;
            var failed = 0;
            foreach (var instance in instances)
            {
                var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
                if (quitRequested && WaitForExit(instance.Process, remaining))
                {
                    graceful++;
                }
                else if (Kill(instance, logger))
                {
                    killed++;
                }
                else
                {
                    failed++;
                }
            }

            logger.Info(
                $"Previous instance cleanup completed matched={instances.Count} quitRequested={quitRequested} graceful={graceful} " +
                $"killed={killed} failed={failed} elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");
        }
        finally
        {
            foreach (var instance in instances)
            {
                instance.Process.Dispose();
            }
        }
    }

    private static bool RequestQuitOverPipe(IRuntimeLogger logger)
    {
        try
        {
            var response = new HsWinCommandClient().Send(
                new HsWinCommandRequest(HsWinCommandNames.AppQuit),
                QuitConnectTimeoutMs,
                QuitResponseTimeoutMs);
            if (!response.Success)
            {
                // Builds that predate app.quit answer with "Unknown command".
                logger.Warning($"Previous instance declined the quit request: {response.Message}");
            }

            return response.Success;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.Warning($"Could not ask the previous instance to quit: {exception.Message}");
            return false;
        }
    }

    private static List<PreviousInstance> FindPreviousInstances()
    {
        using var currentProcess = Process.GetCurrentProcess();
        var currentProcessId = currentProcess.Id;
        var currentSessionId = TryGetSessionId(currentProcess);
        var currentExecutablePath = TryGetExecutablePath(currentProcess);
        var instances = new List<PreviousInstance>();

        // Only processes whose names can ever match are inspected; querying by name avoids walking
        // the whole process table (and its MainModule reads) on every login-start launch.
        foreach (var candidateName in PreviousInstanceProcessMatcher.CandidateProcessNames(currentExecutablePath))
        {
            foreach (var process in Process.GetProcessesByName(candidateName))
            {
                var processId = process.Id;
                var processName = TryGetProcessName(process);
                var sessionId = TryGetSessionId(process);
                var matches = PreviousInstanceProcessMatcher.ShouldTerminate(
                    processId,
                    currentProcessId,
                    processName,
                    sessionId,
                    currentSessionId,
                    candidateExecutablePath: null,
                    currentExecutablePath);
                if (!matches && PreviousInstanceProcessMatcher.ShouldReadExecutablePath(processName, currentExecutablePath))
                {
                    matches = PreviousInstanceProcessMatcher.ShouldTerminate(
                        processId,
                        currentProcessId,
                        processName,
                        sessionId,
                        currentSessionId,
                        TryGetExecutablePath(process),
                        currentExecutablePath);
                }

                if (matches)
                {
                    instances.Add(new PreviousInstance(process, processName, processId));
                }
                else
                {
                    process.Dispose();
                }
            }
        }

        return instances;
    }

    private static bool WaitForExit(Process process, TimeSpan timeout)
    {
        try
        {
            return process.HasExited || (timeout > TimeSpan.Zero && process.WaitForExit(timeout));
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static bool Kill(PreviousInstance instance, IRuntimeLogger logger)
    {
        try
        {
            if (instance.Process.HasExited)
            {
                return true;
            }

            logger.Warning($"Previous HsWin instance did not quit; stopping it processName='{instance.Name}' id={instance.Id}.");

            // Only the instance itself: apps it launched and editors it opened must survive.
            instance.Process.Kill(entireProcessTree: false);
            if (!instance.Process.WaitForExit(KillExitTimeout))
            {
                logger.Warning($"Previous HsWin instance did not exit within {KillExitTimeout.TotalMilliseconds:F0}ms id={instance.Id}.");
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            logger.Error($"Could not stop previous HsWin instance processName='{instance.Name}' id={instance.Id}.", exception);
            return false;
        }
    }

    private sealed record PreviousInstance(Process Process, string Name, int Id);

    private static string TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static int? TryGetSessionId(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}

internal static class PreviousInstanceProcessMatcher
{
    private static readonly string[] KnownProcessNames =
    [
        AppBranding.DisplayName,
        "HsWin.App"
    ];

    /// <summary>
    /// Process names the cleanup can ever act on: the known product names plus the current
    /// executable's base name (the path-equality branch is only reachable for that name).
    /// Callers enumerate candidates with Process.GetProcessesByName instead of scanning the
    /// full process table.
    /// </summary>
    public static IReadOnlyList<string> CandidateProcessNames(string? currentExecutablePath)
    {
        var names = new List<string>(KnownProcessNames);
        if (!string.IsNullOrWhiteSpace(currentExecutablePath))
        {
            var currentName = Path.GetFileNameWithoutExtension(currentExecutablePath);
            if (!names.Contains(currentName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(currentName);
            }
        }

        return names;
    }

    public static bool ShouldTerminate(
        int candidateProcessId,
        int currentProcessId,
        string candidateProcessName,
        int? candidateSessionId,
        int? currentSessionId,
        string? candidateExecutablePath,
        string? currentExecutablePath)
    {
        if (candidateProcessId == currentProcessId)
        {
            return false;
        }

        if (candidateSessionId.HasValue &&
            currentSessionId.HasValue &&
            candidateSessionId.Value != currentSessionId.Value)
        {
            return false;
        }

        if (KnownProcessNames.Any(name =>
            string.Equals(name, candidateProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return PathsEqual(candidateExecutablePath, currentExecutablePath);
    }

    public static bool ShouldReadExecutablePath(string candidateProcessName, string? currentExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(candidateProcessName) || string.IsNullOrWhiteSpace(currentExecutablePath))
        {
            return false;
        }

        var currentProcessName = Path.GetFileNameWithoutExtension(currentExecutablePath);
        return string.Equals(candidateProcessName, currentProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
