using HsWin.Core.Logging;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.App.Mouse;

internal sealed class MouseButtonWatchSubscription(MouseButtonWatchOptions options,
    Action<MouseButtonEventSnapshot> callback, Action<MouseButtonWatchSubscription> remove) : IDisposable
{
    internal const int MaxPending = 128;
    private readonly object _gate = new();
    private readonly Queue<MouseButtonEventSnapshot> _pending = new();
    private bool _scheduled;
    private bool _warned;
    private int _disposed;
    public MouseButtonWatchOptions Options { get; } = options;

    public void Enqueue(MouseButtonEventSnapshot input, IScriptCallbackScheduler scheduler, IRuntimeLogger logger)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (_pending.Count == MaxPending)
            {
                if (!_warned) logger.Warning("Mouse button watch callback backlog is full; dropping observations while preserving native swallowing.");
                _warned = true;
                return;
            }
            _pending.Enqueue(input);
            if (_scheduled) return;
            _scheduled = true;
        }

        Schedule(scheduler, logger);
    }

    private void Schedule(IScriptCallbackScheduler scheduler, IRuntimeLogger logger)
    {
        try { scheduler.Schedule(() => Deliver(scheduler, logger)); }
        catch (Exception exception)
        {
            lock (_gate) { _scheduled = false; _pending.Clear(); }
            logger.Error("Mouse button watch callback scheduling failed.", exception);
        }
    }

    private void Deliver(IScriptCallbackScheduler scheduler, IRuntimeLogger logger)
    {
        // Yield between batches so a continuously busy observer cannot monopolize the UI queue.
        for (var delivered = 0; delivered < 16; delivered++)
        {
            MouseButtonEventSnapshot input;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0 || !_pending.TryDequeue(out input!))
                {
                    _scheduled = false;
                    return;
                }
            }
            try { callback(input); }
            catch (Exception exception) { logger.Error("Mouse button watch callback failed.", exception); }
        }
        Schedule(scheduler, logger);
    }

    public void Deactivate()
    {
        Interlocked.Exchange(ref _disposed, 1);
        lock (_gate) _pending.Clear();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate) _pending.Clear();
        remove(this);
    }
}
