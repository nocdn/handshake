using System.Windows.Threading;
using HsWin.App.Threading;
using HsWin.Core.Timers;

namespace HsWin.App.Timers;

/// <summary>
/// Backs <c>hs.timer</c>. Deadlines come from the shared <see cref="PreciseTimerScheduler"/>
/// (about 1 ms accuracy instead of the ~15.6 ms DispatcherTimer tick); callbacks still run on the
/// dispatcher like every other script callback. Creating and stopping a timer never waits for the
/// dispatcher, so a wedged UI thread cannot stall a config reload.
/// </summary>
internal sealed class PreciseScriptTimerService : IScriptTimerService
{
    private readonly Dispatcher _dispatcher;
    private readonly IPreciseTimerFactory _timers;

    public PreciseScriptTimerService(Dispatcher dispatcher)
        : this(dispatcher, PreciseTimerScheduler.Shared)
    {
    }

    internal PreciseScriptTimerService(Dispatcher dispatcher, IPreciseTimerFactory timers)
    {
        _dispatcher = dispatcher;
        _timers = timers;
    }

    public IDisposable DoAfter(int delayMs, Action callback)
    {
        return Start(delayMs, callback, repeats: false);
    }

    public IDisposable DoEvery(int intervalMs, Action callback)
    {
        return Start(intervalMs, callback, repeats: true);
    }

    private TimerHandle Start(int intervalMs, Action callback, bool repeats)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (intervalMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Timer interval must be at least 1 millisecond.");
        }

        var handle = new TimerHandle(_dispatcher, callback, repeats);
        handle.Start(_timers, intervalMs);
        return handle;
    }

    private sealed class TimerHandle(Dispatcher dispatcher, Action callback, bool repeats) : IDisposable
    {
        private IPreciseTimer? _timer;
        private int _disposed;
        private int _tickPending;

        public void Start(IPreciseTimerFactory timers, int intervalMs)
        {
            _timer = timers.Create(OnSchedulerTick);
            _timer.Change(intervalMs, repeats ? intervalMs : Timeout.Infinite);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _timer?.Dispose();
            }
        }

        private void OnSchedulerTick()
        {
            if (!repeats)
            {
                // A one-shot timer is finished once it fires; release its scheduler slot now.
                _timer?.Dispose();
            }

            // Coalesce: while a tick is still queued behind a busy dispatcher, later ticks are
            // dropped rather than piling up and replaying in a burst.
            if (Interlocked.Exchange(ref _tickPending, 1) != 0)
            {
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Normal, OnDispatcherTick);
        }

        private void OnDispatcherTick()
        {
            Volatile.Write(ref _tickPending, 0);
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (!repeats)
            {
                Interlocked.Exchange(ref _disposed, 1);
            }

            callback();
        }
    }
}
