using HsWin.Core.Alerts;
using HsWin.Core.Logging;
using Microsoft.ClearScript;
using System.Diagnostics;

namespace HsWin.Core.Scripting;

internal sealed class ScriptCallbackInvoker
{
    /// <summary>
    /// When a low-level hook thread needs the script gate, wait at most this long then fail-open
    /// so physical input cannot stall behind a wedged UI callback forever.
    /// Tuned for light contention without dropping normal short hotkey work.
    /// </summary>
    internal static readonly TimeSpan HookPathGateTimeout = TimeSpan.FromMilliseconds(30);

    /// <summary>Only log routine start/complete when work exceeds this (reduces hot-path noise).</summary>
    private static readonly TimeSpan SlowCallbackLogThreshold = TimeSpan.FromMilliseconds(10);

    private readonly object _callbackGate = new();
    private readonly IAlertPresenter _alerts;
    private readonly IRuntimeLogger _logger;
    private int _closed;

    public ScriptCallbackInvoker(IAlertPresenter alerts, IRuntimeLogger logger)
    {
        _alerts = alerts;
        _logger = logger;
    }

    /// <summary>True once the owning config generation has ended; callbacks then never run.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>
    /// Ends this generation: no new callback starts, and the caller waits (bounded) for a callback
    /// already inside the engine to leave before the engine is torn down.
    /// </summary>
    /// <returns>false when a callback was still running after <paramref name="wait"/>.</returns>
    public bool Close(TimeSpan wait)
    {
        Volatile.Write(ref _closed, 1);
        var lockTaken = false;
        try
        {
            Monitor.TryEnter(_callbackGate, wait, ref lockTaken);
            return lockTaken;
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_callbackGate);
            }
        }
    }

    public void InvokeHotkeyCallback(ScriptObject scriptFunction)
    {
        var queuedAt = Stopwatch.GetTimestamp();
        lock (_callbackGate)
        {
            if (IsClosed)
            {
                return;
            }

            var startedAt = Stopwatch.GetTimestamp();
            var waitMs = Stopwatch.GetElapsedTime(queuedAt, startedAt).TotalMilliseconds;
            try
            {
                scriptFunction.Invoke(asConstructor: false);
                LogIfSlow("Script hotkey callback", waitMs, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, args: null);
            }
            catch (Exception exception)
            {
                _logger.Warning(
                    $"Script hotkey callback failed elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}. {exception.Message}");
                _alerts.Show(AlertRequest.Create($"Hotkey callback error: {exception.Message}", AlertKind.Error, 7000));
            }
        }
    }

    /// <summary>
    /// Invokes a script callback, waiting for the gate (UI / scheduled work). Prefer this off the hook path.
    /// </summary>
    public object? InvokeScriptCallback(ScriptObject scriptFunction, params object?[] args)
    {
        var queuedAt = Stopwatch.GetTimestamp();
        lock (_callbackGate)
        {
            return InvokeUnderGate(scriptFunction, args, queuedAt);
        }
    }

    /// <summary>
    /// Hook-path invoke: if the script gate is busy longer than <see cref="HookPathGateTimeout"/>,
    /// returns false without running JS so WH_*_LL handlers fail open.
    /// </summary>
    public object? InvokeScriptCallbackFailOpen(ScriptObject scriptFunction, params object?[] args)
    {
        _ = TryInvokeScriptCallbackFailOpen(scriptFunction, out var result, args);
        return result;
    }

    /// <summary>
    /// Hook-path invoke with explicit skip detection.
    /// Returns false if the gate was busy (callback not run); true if the callback ran (even if it threw/returned false).
    /// </summary>
    public bool TryInvokeScriptCallbackFailOpen(ScriptObject scriptFunction, out object? result, params object?[] args)
    {
        var queuedAt = Stopwatch.GetTimestamp();
        var lockTaken = false;
        try
        {
            Monitor.TryEnter(_callbackGate, HookPathGateTimeout, ref lockTaken);
            if (!lockTaken)
            {
                _logger.Warning(
                    $"Script callback skipped args={args.Length}: gate busy after {HookPathGateTimeout.TotalMilliseconds:F0}ms (fail-open on hook path).");
                result = false;
                return false;
            }

            result = InvokeUnderGate(scriptFunction, args, queuedAt);
            return true;
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_callbackGate);
            }
        }
    }

    private object? InvokeUnderGate(ScriptObject scriptFunction, object?[] args, long queuedAt)
    {
        if (IsClosed)
        {
            // A timer, hook or scheduled delivery from a config that has since been reloaded.
            return false;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var waitMs = Stopwatch.GetElapsedTime(queuedAt, startedAt).TotalMilliseconds;
        try
        {
            var invokeResult = scriptFunction.Invoke(asConstructor: false, args);
            LogIfSlow("Script callback", waitMs, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, args.Length);
            return invokeResult;
        }
        catch (Exception exception)
        {
            _logger.Warning(
                $"Script callback failed args={args.Length} elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}. {exception.Message}");
            _alerts.Show(AlertRequest.Create($"Callback error: {exception.Message}", AlertKind.Error, 7000));
            return false;
        }
    }

    private void LogIfSlow(string label, double waitMs, double elapsedMs, int? args)
    {
        if (waitMs < SlowCallbackLogThreshold.TotalMilliseconds
            && elapsedMs < SlowCallbackLogThreshold.TotalMilliseconds)
        {
            return;
        }

        var argsPart = args is null ? string.Empty : $" args={args}";
        _logger.Info($"{label} completed{argsPart} waitMs={waitMs:F3} elapsedMs={elapsedMs:F3}.");
    }
}
