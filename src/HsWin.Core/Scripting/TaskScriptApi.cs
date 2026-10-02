using System.Diagnostics;
using System.Globalization;
using HsWin.Core.Logging;
using HsWin.Core.Shell;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public sealed class TaskScriptApi
{
    private readonly IShellService _shell;
    private readonly IRuntimeLogger _logger;
    private readonly IScriptCallbackScheduler _callbackScheduler;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal TaskScriptApi(
        IShellService shell,
        IRuntimeLogger logger,
        IScriptCallbackScheduler callbackScheduler,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _shell = shell;
        _logger = logger;
        _callbackScheduler = callbackScheduler;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    /// <summary>
    /// Runs a command on a background thread and reports its result. Stopping the handle (or
    /// reloading the config) kills the command's process tree and suppresses the callback.
    /// </summary>
    public ScriptResourceHandle Run(object? command, object? options, object? callback)
    {
        var normalizedCommand = ScriptArgumentReader.RequireNonWhiteSpaceString(command, "command");
        var parsedOptions = ShellScriptOptionsParser.ParseExecutionOptions(options);
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Task callback must be a JavaScript function.", nameof(callback));
        }

        var relay = new ScriptCallbackRelay(scriptFunction, _callbackScheduler, _callbacks);
        var cancellation = new CancellationTokenSource();
        var handle = relay.Attach(new Support.DisposableAction(() => CancelQuietly(cancellation)));
        _trackResource(handle);
        _logger.Info($"Script hs.task.run() started command={LogSanitizer.DescribeCommand(normalizedCommand)} timeoutMs={parsedOptions.TimeoutMs}.");
        _ = Task.Run(() => RunCommand(normalizedCommand, parsedOptions, relay, cancellation));
        return handle;
    }

    private static void CancelQuietly(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The command already finished and released its token.
        }
    }

    private void RunCommand(string command, ShellExecutionOptions options, ScriptCallbackRelay relay, CancellationTokenSource cancellation)
    {
        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            var result = _shell.Execute(command, options, cancellation.Token);
            _logger.Info(
                $"Script hs.task.run() command completed success={result.Success} exitCode={result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "null"} " +
                $"timedOut={result.TimedOut} cancelled={result.Cancelled} elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");

            if (result.Cancelled || relay.IsCancelled)
            {
                relay.Complete();
                return;
            }

            relay.PostFinal(ScriptJson.Serialize(result));
        }
        catch (Exception exception)
        {
            _logger.Error("Script hs.task.run() command failed unexpectedly.", exception);
            relay.Complete();
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
