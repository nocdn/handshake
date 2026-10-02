using System.Windows.Threading;
using HsWin.Core.Logging;

namespace HsWin.App.Threading;

/// <summary>
/// Abandon flag for a single marshaled dispatcher operation. When a bounded marshal
/// times out, the flag is set so the operation becomes a no-op if the UI thread
/// recovers and runs it late, instead of applying stale side effects (a timer that
/// starts ticking into a disposed engine, a hotkey for an abandoned reload, ...).
/// </summary>
internal sealed class DispatcherMarshal
{
    private int _abandoned;

    public bool IsAbandoned => Volatile.Read(ref _abandoned) != 0;

    public void Abandon() => Interlocked.Exchange(ref _abandoned, 1);
}

/// <summary>
/// Marshals work to the WPF dispatcher without hanging forever when the UI thread
/// is wedged (open tray-menu modal loop, blocked compositor, ...).
///
/// Background threads that hold the script reload gate must never block
/// indefinitely on the dispatcher: that combination deadlocks config reload,
/// wedges the tray, and leaves emergency stop unable to finish teardown.
/// </summary>
internal static class DispatcherGuard
{
    /// <summary>
    /// How long a gate-holding thread waits for the UI thread before abandoning
    /// the marshal and failing fast. Normal dispatch takes milliseconds; this only
    /// fires when the dispatcher is truly wedged.
    /// </summary>
    public static readonly TimeSpan MarshalTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Shorter budget for best-effort reads (reload toast baseline) where giving up
    /// simply skips an optimization instead of failing the operation.
    /// </summary>
    public static readonly TimeSpan BaselineTimeout = TimeSpan.FromSeconds(5);

    public static T InvokeWithTimeout<T>(
        Dispatcher dispatcher,
        Func<T> action,
        string description,
        TimeSpan timeout,
        DispatcherMarshal? marshal = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (dispatcher.CheckAccess())
        {
            if (marshal?.IsAbandoned == true)
            {
                return default!;
            }

            return action();
        }

        var operation = dispatcher.InvokeAsync(() =>
        {
            if (marshal?.IsAbandoned == true)
            {
                return default!;
            }

            return action();
        }, DispatcherPriority.Normal);

        if (!operation.Task.Wait(timeout))
        {
            marshal?.Abandon();
            throw new TimeoutException(
                $"UI thread did not respond within {timeout.TotalSeconds:F0}s for {description}. " +
                "The request was abandoned: dismiss any open tray menu and retry, or restart the app.");
        }

        return operation.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Best-effort teardown marshaling: applies synchronous state first (callers must
    /// do that themselves), then queues native teardown without waiting. Never throws
    /// for a wedged or shutting-down dispatcher.
    /// </summary>
    public static void FireAndForget(
        Dispatcher dispatcher,
        Action action,
        string description,
        IRuntimeLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (dispatcher.CheckAccess())
        {
            RunGuarded(action, description, logger);
            return;
        }

        try
        {
            _ = dispatcher.BeginInvoke(() => RunGuarded(action, description, logger));
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is TaskCanceledException)
        {
            logger?.Warning($"Dispatcher unavailable; skipped {description}. {exception.Message}");
        }
    }

    private static void RunGuarded(Action action, string description, IRuntimeLogger? logger)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger?.Error($"Dispatcher background teardown failed for {description}.", exception);
        }
    }
}
