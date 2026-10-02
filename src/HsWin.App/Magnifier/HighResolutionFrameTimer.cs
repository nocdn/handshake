using System.Diagnostics;
using HsWin.App.Threading;

namespace HsWin.App.Magnifier;

/// <summary>
/// The magnifier's frame clock. It keeps a dedicated thread rather than sharing
/// <see cref="PreciseTimerScheduler"/> because frame periods (4.17 ms at 240 fps) are not whole
/// milliseconds and must never queue behind other app timers.
/// </summary>
internal sealed class HighResolutionFrameTimer : IDisposable
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _changed = new(false);
    private readonly HighResolutionWaitableTimer _timer;
    private readonly Thread _thread;
    private readonly Action _tick;
    private readonly Action<Exception> _failure;
    private readonly long _period;
    private int _running;
    private int _disposed;

    internal HighResolutionFrameTimer(int fps, Action tick, Action<Exception> failure)
    {
        _tick = tick;
        _failure = failure;
        _period = Stopwatch.Frequency / fps;
        try
        {
            _timer = new HighResolutionWaitableTimer();
        }
        catch
        {
            _changed.Dispose();
            throw;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "HsWin Magnifier Frame Clock" };
        _thread.Start();
    }

    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            Volatile.Write(ref _running, 1);
            _changed.Set();
        }
    }

    internal void Stop()
    {
        lock (_gate)
        {
            if (_disposed != 0)
            {
                return;
            }

            Volatile.Write(ref _running, 0);
            _changed.Set();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed != 0)
            {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            _changed.Set();
        }

        if (_thread != Thread.CurrentThread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        // The worker owns handle disposal, including when shutdown takes longer than the join budget.
    }

    private void Run()
    {
        try
        {
            WaitHandle[] waits = [_changed, _timer];
            var deadline = Stopwatch.GetTimestamp() + _period;
            while (Volatile.Read(ref _disposed) == 0)
            {
                if (Volatile.Read(ref _running) == 0)
                {
                    _changed.WaitOne();
                    deadline = Stopwatch.GetTimestamp() + _period;
                    continue;
                }

                _timer.Arm(Math.Max(1, deadline - Stopwatch.GetTimestamp()));
                if (WaitHandle.WaitAny(waits) == 0)
                {
                    deadline = Stopwatch.GetTimestamp() + _period;
                    continue;
                }

                if (Volatile.Read(ref _running) != 0 && Volatile.Read(ref _disposed) == 0)
                {
                    _tick();
                }

                deadline = HighResolutionWaitableTimer.NextDeadline(deadline, Stopwatch.GetTimestamp(), _period);
            }
        }
        catch (Exception exception)
        {
            _failure(exception);
        }
        finally
        {
            lock (_gate)
            {
                Volatile.Write(ref _disposed, 1);
                _timer.Dispose();
                _changed.Dispose();
            }
        }
    }
}
