using System.ComponentModel;
using System.Diagnostics;
using HsWin.Core.Logging;

namespace HsWin.App.Threading;

/// <summary>A timer whose ticks run on a <see cref="PreciseTimerScheduler"/> thread.</summary>
internal interface IPreciseTimer : IDisposable
{
    /// <summary>
    /// Schedules the first tick after <paramref name="dueTimeMs"/> and then every
    /// <paramref name="periodMs"/>; <see cref="Timeout.Infinite"/> disables either part,
    /// matching <see cref="System.Threading.Timer.Change(int, int)"/>.
    /// </summary>
    void Change(int dueTimeMs, int periodMs);
}

internal interface IPreciseTimerFactory
{
    IPreciseTimer Create(Action tick);
}

/// <summary>
/// One dedicated thread that runs every millisecond-scale timer in the app (hs.timer, keyboard
/// and mouse repeats, the cursor-following toast) from a high-resolution waitable timer.
/// Thread-pool and dispatcher timers round to the ~15.6 ms system tick, which turned a 10 ms
/// repeat into 15.6 ms; this keeps 1 ms accuracy without busy waiting or raising the
/// system-wide timer resolution. Periodic deadlines advance from the previous deadline, so
/// rate does not drift, and missed periods are skipped rather than replayed in a burst.
/// Ticks must stay short: hand longer work to another thread or the dispatcher.
/// </summary>
internal sealed class PreciseTimerScheduler : IPreciseTimerFactory, IDisposable
{
    private static readonly ForwardingRuntimeLogger SharedLogger = new();

    private static readonly Lazy<PreciseTimerScheduler> SharedInstance =
        new(() => new PreciseTimerScheduler(SharedLogger), LazyThreadSafetyMode.ExecutionAndPublication);


    private readonly object _gate = new();
    private readonly List<ScheduledTimer> _timers = [];
    private readonly AutoResetEvent _changed = new(false);
    private readonly HighResolutionWaitableTimer _waitableTimer = new();
    private readonly Thread _thread;
    private readonly IRuntimeLogger _logger;
    private bool _disposed;

    public PreciseTimerScheduler(IRuntimeLogger logger)
    {
        _logger = logger;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "HsWin Precise Timers",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    /// <summary>The process-wide scheduler used by app services.</summary>
    public static PreciseTimerScheduler Shared => SharedInstance.Value;

    /// <summary>Routes the shared scheduler's tick failures to the runtime log; call at startup.</summary>
    public static void UseLogger(IRuntimeLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        SharedLogger.Target = logger;
    }

    public IPreciseTimer Create(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        return new ScheduledTimer(this, tick);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timers.Clear();
        }

        _changed.Set();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private static long ToStopwatchTicks(int milliseconds) =>
        Math.Max(1, milliseconds * Stopwatch.Frequency / 1000);

    private void Schedule(ScheduledTimer timer, int dueTimeMs, int periodMs)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            timer.Period = periodMs == Timeout.Infinite ? 0 : ToStopwatchTicks(periodMs);
            if (dueTimeMs == Timeout.Infinite)
            {
                _timers.Remove(timer);
            }
            else
            {
                timer.Deadline = Stopwatch.GetTimestamp() + (dueTimeMs == 0 ? 0 : ToStopwatchTicks(dueTimeMs));
                if (!_timers.Contains(timer))
                {
                    _timers.Add(timer);
                }
            }
        }

        _changed.Set();
    }

    private void Remove(ScheduledTimer timer)
    {
        lock (_gate)
        {
            if (!_timers.Remove(timer))
            {
                return;
            }
        }

        _changed.Set();
    }

    private void Run()
    {
        WaitHandle[] waits = [_changed, _waitableTimer];
        var due = new List<ScheduledTimer>();
        while (true)
        {
            long? earliest = null;
            lock (_gate)
            {
                if (_disposed)
                {
                    break;
                }

                foreach (var timer in _timers)
                {
                    if (earliest is null || timer.Deadline < earliest)
                    {
                        earliest = timer.Deadline;
                    }
                }
            }

            if (earliest is null)
            {
                _changed.WaitOne();
                continue;
            }

            var remaining = earliest.Value - Stopwatch.GetTimestamp();
            if (remaining > 0)
            {
                try
                {
                    _waitableTimer.Arm(remaining);
                }
                catch (Win32Exception exception)
                {
                    _logger.Error("Precise timer could not be armed; falling back to a coarse wait.", exception);
                    _changed.WaitOne(Stopwatch.GetElapsedTime(0, remaining));
                    continue;
                }

                if (WaitHandle.WaitAny(waits) == 0)
                {
                    continue; // The timer set changed; recompute the earliest deadline.
                }
            }

            CollectDue(due);
            foreach (var timer in due)
            {
                timer.Fire(_logger);
            }

            due.Clear();
        }

        _waitableTimer.Dispose();
        _changed.Dispose();
    }

    private void CollectDue(List<ScheduledTimer> due)
    {
        var now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            for (var index = _timers.Count - 1; index >= 0; index--)
            {
                var timer = _timers[index];
                if (timer.Deadline > now)
                {
                    continue;
                }

                due.Add(timer);
                if (timer.Period > 0)
                {
                    timer.Deadline = HighResolutionWaitableTimer.NextDeadline(timer.Deadline, now, timer.Period);
                }
                else
                {
                    _timers.RemoveAt(index);
                }
            }
        }
    }

    private sealed class ScheduledTimer(PreciseTimerScheduler scheduler, Action tick) : IPreciseTimer
    {
        private int _disposed;

        // Guarded by the scheduler gate.
        public long Deadline { get; set; }

        public long Period { get; set; }

        public void Change(int dueTimeMs, int periodMs)
        {
            if (dueTimeMs < Timeout.Infinite || periodMs < Timeout.Infinite)
            {
                throw new ArgumentOutOfRangeException(nameof(dueTimeMs), "Timer times must be non-negative or Timeout.Infinite.");
            }

            if (Volatile.Read(ref _disposed) == 0)
            {
                scheduler.Schedule(this, dueTimeMs, periodMs);
            }
        }

        public void Fire(IRuntimeLogger logger)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                tick();
            }
            catch (Exception exception)
            {
                // One faulty tick must not stop every other timer in the app.
                logger.Error("Precise timer tick failed.", exception);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                scheduler.Remove(this);
            }
        }
    }

    /// <summary>Lets the shared scheduler exist before the app's logger is configured.</summary>
    private sealed class ForwardingRuntimeLogger : IRuntimeLogger
    {
        private IRuntimeLogger _target = NullRuntimeLogger.Instance;

        public IRuntimeLogger Target
        {
            get => Volatile.Read(ref _target);
            set => Volatile.Write(ref _target, value);
        }

        public void Info(string message) => Target.Info(message);

        public void Warning(string message) => Target.Warning(message);

        public void Error(string message, Exception exception) => Target.Error(message, exception);
    }
}
