using System.Diagnostics;
using System.Windows.Threading;
using HsWin.App.Timers;

namespace HsWin.App.Tests;

/// <summary>
/// hs.timer runs on the shared precise scheduler but its callbacks run on the dispatcher. Creating
/// and stopping a timer must never wait for a wedged dispatcher (that deadlocked config reload),
/// and a stopped timer must never call back into a disposed script engine.
/// </summary>
public sealed class PreciseScriptTimerServiceTests
{
    [Fact]
    public void DoEverySchedulesAPeriodicTimerAndTicksOnTheDispatcherThread()
    {
        using var dispatcher = new TestDispatcher();
        var timers = new ManualPreciseTimerFactory();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, timers);
        var tickThreads = new List<int>();

        using var handle = service.DoEvery(25, () => tickThreads.Add(Environment.CurrentManagedThreadId));
        timers.Timer.Fire();
        dispatcher.Pump();

        Assert.Equal((25, 25), timers.Timer.LastSchedule);
        Assert.Equal([dispatcher.ThreadId], tickThreads);
    }

    [Fact]
    public void DoAfterSchedulesAOneShotTimerThatFiresOnceAndReleasesItsSchedulerSlot()
    {
        using var dispatcher = new TestDispatcher();
        var timers = new ManualPreciseTimerFactory();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, timers);
        var ticks = 0;

        using var handle = service.DoAfter(40, () => ticks++);
        Assert.Equal((40, System.Threading.Timeout.Infinite), timers.Timer.LastSchedule);

        timers.Timer.Fire();
        dispatcher.Pump();
        timers.Timer.Fire();
        dispatcher.Pump();

        Assert.Equal(1, ticks);
        Assert.True(timers.Timer.IsDisposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TimersRejectIntervalsBelowOneMillisecond(int intervalMs)
    {
        using var dispatcher = new TestDispatcher();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, new ManualPreciseTimerFactory());

        Assert.Throws<ArgumentOutOfRangeException>(() => service.DoEvery(intervalMs, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.DoAfter(intervalMs, () => { }));
    }

    [Fact]
    public void DisposedTimerStopsTicking()
    {
        using var dispatcher = new TestDispatcher();
        var timers = new ManualPreciseTimerFactory();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, timers);
        var ticks = 0;
        var handle = service.DoEvery(25, () => ticks++);

        handle.Dispose();
        timers.Timer.Fire();
        dispatcher.Pump();

        Assert.Equal(0, ticks);
        Assert.True(timers.Timer.IsDisposed);
    }

    [Fact]
    public void CreatingAndDisposingReturnPromptlyWhenTheDispatcherIsWedged()
    {
        using var dispatcher = new TestDispatcher();
        var timers = new ManualPreciseTimerFactory();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, timers);
        using var release = new ManualResetEventSlim(false);
        dispatcher.Wedge(release.WaitHandle);

        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            var handle = service.DoEvery(25, () => { });
            handle.Dispose();

            Assert.True(Stopwatch.GetElapsedTime(startedAt) < TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void TicksQueuedBehindABusyDispatcherAreCoalescedNotReplayed()
    {
        using var dispatcher = new TestDispatcher();
        var timers = new ManualPreciseTimerFactory();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, timers);
        var ticks = 0;
        using var handle = service.DoEvery(10, () => ticks++);
        using var release = new ManualResetEventSlim(false);
        dispatcher.Wedge(release.WaitHandle);

        for (var tick = 0; tick < 20; tick++)
        {
            timers.Timer.Fire();
        }

        release.Set();
        dispatcher.Pump();

        Assert.Equal(1, ticks);
    }

    [Fact]
    public void TimerDisposedWhileATickIsQueuedNeverRunsItsCallback()
    {
        using var dispatcher = new TestDispatcher();
        var timers = new ManualPreciseTimerFactory();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher, timers);
        var ticks = 0;
        var handle = service.DoEvery(10, () => ticks++);
        using var release = new ManualResetEventSlim(false);
        dispatcher.Wedge(release.WaitHandle);

        timers.Timer.Fire();
        handle.Dispose();
        release.Set();
        dispatcher.Pump();

        Assert.Equal(0, ticks);
    }

    [Fact]
    public void ARealSchedulerTicksPeriodicallyUntilDisposed()
    {
        using var dispatcher = new TestDispatcher();
        var service = new PreciseScriptTimerService(dispatcher.Dispatcher);
        var ticks = 0;

        var handle = service.DoEvery(10, () => Interlocked.Increment(ref ticks));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref ticks) >= 5, TimeSpan.FromSeconds(5)), "Timer did not tick repeatedly.");

        handle.Dispose();
        dispatcher.Pump();
        var countAfterDispose = Volatile.Read(ref ticks);
        Thread.Sleep(100);

        Assert.Equal(countAfterDispose, Volatile.Read(ref ticks));
    }

    private sealed class TestDispatcher : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new(false);
        private bool _disposed;

        public TestDispatcher()
        {
            Dispatcher = null!;
            _thread = new Thread(() =>
            {
                Dispatcher = Dispatcher.CurrentDispatcher;
                _ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)), "Dispatcher thread did not start.");
        }

        public Dispatcher Dispatcher { get; private set; }

        public int ThreadId => _thread.ManagedThreadId;

        public void Wedge(WaitHandle release)
        {
            using var entered = new ManualResetEventSlim(false);
            _ = Dispatcher.InvokeAsync(() =>
            {
                entered.Set();
                release.WaitOne();
            });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "Dispatcher did not wedge.");
        }

        /// <summary>Waits until everything queued at normal priority or above has run.</summary>
        public void Pump()
        {
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
            catch
            {
                // Already shutting down; joining below still applies.
            }

            _thread.Join(TimeSpan.FromSeconds(10));
            _ready.Dispose();
        }
    }
}
