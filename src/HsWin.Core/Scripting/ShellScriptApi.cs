using System.Diagnostics;
using System.Globalization;
using HsWin.Core.Logging;
using HsWin.Core.Shell;

namespace HsWin.Core.Scripting;

public sealed class ShellScriptApi
{
    /// <summary>Synchronous commands slower than this get a console hint to use hs.task.run.</summary>
    internal static readonly TimeSpan SlowSynchronousCommandThreshold = TimeSpan.FromSeconds(1);

    private readonly IShellService _shell;
    private readonly IScriptConsoleLogger _console;
    private readonly IRuntimeLogger _logger;

    internal ShellScriptApi(IShellService shell, IScriptConsoleLogger console, IRuntimeLogger logger)
    {
        _shell = shell;
        _console = console;
        _logger = logger;
    }

    public string ExecuteCommandJson(object? command, object? options = null)
    {
        var normalizedCommand = ScriptArgumentReader.RequireNonWhiteSpaceString(command, "command");
        var parsedOptions = ShellScriptOptionsParser.ParseExecutionOptions(options, ShellExecutionOptions.DefaultSynchronousTimeoutMs);
        var startedAt = Stopwatch.GetTimestamp();
        var result = _shell.Execute(normalizedCommand, parsedOptions);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        _logger.Info(
            $"Script hs.execute() command={LogSanitizer.DescribeCommand(normalizedCommand)} success={result.Success} " +
            $"exitCode={result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "null"} timedOut={result.TimedOut} elapsedMs={elapsed.TotalMilliseconds:F0}.");
        if (elapsed >= SlowSynchronousCommandThreshold)
        {
            // hs.execute holds the script thread (usually the UI thread): toasts, timers and
            // hotkeys wait for it. Tell the config author how to avoid that.
            _console.Write(
                "warn",
                $"hs.execute() blocked scripts for {elapsed.TotalMilliseconds:F0}ms. Use hs.task.run(command, options, callback) for commands that take longer than a moment.");
        }

        return ScriptJson.Serialize(result);
    }
}
