using HsWin.Core.Logging;
using HsWin.Core.Timers;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public sealed class TimerScriptApi
{
    private readonly IScriptTimerService _timers;
    private readonly IRuntimeLogger _logger;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal TimerScriptApi(
        IScriptTimerService timers,
        IRuntimeLogger logger,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _timers = timers;
        _logger = logger;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public ScriptResourceHandle DoAfter(object? delayMs, object? callback)
    {
        var delay = ConvertTimerInterval(delayMs, nameof(delayMs));
        var scriptFunction = RequireCallback(callback);

        // A one-shot timer releases its handle once it has fired, so it stops being tracked.
        ScriptResourceHandle? handle = null;
        var fired = 0;
        var timer = _timers.DoAfter(delay, () =>
        {
            try
            {
                _callbacks.InvokeScriptCallback(scriptFunction);
            }
            finally
            {
                Volatile.Write(ref fired, 1);
                Volatile.Read(ref handle)?.Dispose();
            }
        });

        Volatile.Write(ref handle, new ScriptResourceHandle(timer));
        Track(handle, $"doAfter {delay}ms");
        if (Volatile.Read(ref fired) != 0)
        {
            handle.Dispose();
        }

        return handle;
    }

    public ScriptResourceHandle DoEvery(object? intervalMs, object? callback)
    {
        var interval = ConvertTimerInterval(intervalMs, nameof(intervalMs));
        var scriptFunction = RequireCallback(callback);
        var handle = new ScriptResourceHandle(_timers.DoEvery(interval, () => _callbacks.InvokeScriptCallback(scriptFunction)));
        Track(handle, $"doEvery {interval}ms");
        return handle;
    }

    private static ScriptObject RequireCallback(object? callback)
    {
        return callback as ScriptObject
            ?? throw new ArgumentException("Timer callback must be a JavaScript function.", nameof(callback));
    }

    private void Track(ScriptResourceHandle handle, string description)
    {
        _trackResource(handle);
        _logger.Info($"Script hs.timer.{description} created.");
    }

    private static int ConvertTimerInterval(object? value, string argumentName)
    {
        var interval = ScriptArgumentReader.RequireInt32(value, argumentName, "a number of milliseconds");
        if (interval < 1)
        {
            throw new ArgumentOutOfRangeException(argumentName, "Timer interval must be at least 1 millisecond.");
        }

        return interval;
    }
}
