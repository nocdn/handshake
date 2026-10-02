using HsWin.Core.Logging;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.App.Mouse;

/// <summary>
/// Dispatches mouse-scroll watch subscriptions.
/// <para>
/// Critical safety rule: script callbacks never run on the low-level mouse hook thread.
/// When <see cref="MouseScrollWatchOptions.Blocking"/> / preventDefault is enabled, every matching
/// event is swallowed natively on the hook path and the script callback is scheduled off-hook, so
/// (unlike keyboard watches) the callback cannot decide per event. High-resolution wheels send
/// events far faster than a script round trip, and keeping JavaScript off this hook keeps physical
/// input responsive and avoids script-lock deadlocks with hotkey release.
/// </para>
/// </summary>
internal sealed class MouseScrollWatchDispatcher
{
    private readonly IRuntimeLogger _logger;
    private readonly IScriptCallbackScheduler _scheduler;

    public MouseScrollWatchDispatcher(
        IRuntimeLogger logger,
        IScriptCallbackScheduler scheduler)
    {
        _logger = logger;
        _scheduler = scheduler;
    }

    public bool Dispatch(
        MouseScrollEventSnapshot snapshot,
        IReadOnlyList<MouseScrollWatchSubscription> subscriptions)
    {
        var shouldSwallow = false;

        foreach (var subscription in subscriptions)
        {
            if (ShouldSkip(subscription, snapshot))
            {
                continue;
            }

            if (subscription.Options.Blocking)
            {
                // Native swallow only — never invoke JavaScript on the hook thread.
                // Avoid per-notch Info logging: high-resolution wheels would flood the runtime log.
                shouldSwallow = true;
            }

            ScheduleCallback(subscription, snapshot);
        }

        return shouldSwallow;
    }

    private static bool ShouldSkip(MouseScrollWatchSubscription subscription, MouseScrollEventSnapshot snapshot)
    {
        if (subscription.IsDisposed)
        {
            return true;
        }

        if (snapshot.IsInjected && !subscription.Options.IncludeInjected)
        {
            return true;
        }

        var axis = snapshot.IsVertical ? MouseScrollAxis.Vertical : MouseScrollAxis.Horizontal;
        return !subscription.Options.IncludesAxis(axis);
    }

    private void ScheduleCallback(MouseScrollWatchSubscription subscription, MouseScrollEventSnapshot snapshot)
    {
        // High-resolution wheels outrun a script round trip; a slow callback must not let the queue
        // (and its memory) grow without bound, so events past the backlog limit are dropped.
        if (!subscription.TryReserveDelivery())
        {
            if (subscription.TryMarkBacklogWarned())
            {
                _logger.Warning(
                    $"Mouse scroll watch callback is falling behind id={subscription.Id}; dropping scroll events " +
                    $"while more than {MouseScrollWatchSubscription.MaxPendingCallbacks} are queued.");
            }

            return;
        }

        try
        {
            _scheduler.Schedule(() => InvokeCallback(subscription, snapshot));
        }
        catch (Exception exception)
        {
            subscription.ReleaseDelivery();
            _logger.Error($"Mouse scroll watch callback scheduling failed id={subscription.Id}.", exception);
        }
    }

    private void InvokeCallback(MouseScrollWatchSubscription subscription, MouseScrollEventSnapshot snapshot)
    {
        try
        {
            if (subscription.IsDisposed)
            {
                return;
            }

            subscription.Callback(snapshot);
        }
        catch (Exception exception)
        {
            _logger.Error($"Mouse scroll watch callback failed id={subscription.Id}.", exception);
        }
        finally
        {
            subscription.ReleaseDelivery();
        }
    }
}

internal sealed class MouseScrollWatchSubscription : IDisposable
{
    /// <summary>Callbacks that may be queued for one watch before further scroll events are dropped.</summary>
    internal const int MaxPendingCallbacks = 256;

    private readonly Action<MouseScrollWatchSubscription>? _dispose;
    private int _disposed;
    private int _pending;
    private int _backlogWarned;

    public MouseScrollWatchSubscription(
        long id,
        MouseScrollWatchOptions options,
        Action<MouseScrollEventSnapshot> callback,
        Action<MouseScrollWatchSubscription>? dispose = null)
    {
        Id = id;
        Options = options;
        Callback = callback;
        _dispose = dispose;
    }

    public long Id { get; }

    public MouseScrollWatchOptions Options { get; }

    public Action<MouseScrollEventSnapshot> Callback { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _dispose?.Invoke(this);
    }

    /// <summary>Claims a queue slot; false when the watch already has <see cref="MaxPendingCallbacks"/> queued.</summary>
    public bool TryReserveDelivery()
    {
        if (Interlocked.Increment(ref _pending) <= MaxPendingCallbacks)
        {
            return true;
        }

        Interlocked.Decrement(ref _pending);
        return false;
    }

    public void ReleaseDelivery() => Interlocked.Decrement(ref _pending);

    /// <summary>True once per watch, so a sustained backlog logs a single warning.</summary>
    public bool TryMarkBacklogWarned() => Interlocked.Exchange(ref _backlogWarned, 1) == 0;

    public void MarkDisposed()
    {
        Interlocked.Exchange(ref _disposed, 1);
    }
}
