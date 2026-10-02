using HsWin.App.Threading;

namespace HsWin.App.Tests;

public sealed class PreciseTimerSchedulerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void OneShotTimerFiresOnceAfterItsDueTime()
    {
        using var scheduler = new PreciseTimerScheduler(new CapturingRuntimeLogger());
        var ticks = 0;
        using var timer = scheduler.Create(() => Interlocked.Increment(ref ticks));

        timer.Change(20, Timeout.Infinite);

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref ticks) == 1, Patience));
        Thread.Sleep(100);
        Assert.Equal(1, Volatile.Read(ref ticks));
    }

    [Fact]
    public void PeriodicTimerKeepsTickingAtItsPeriod()
    {
        using var scheduler = new PreciseTimerScheduler(new CapturingRuntimeLogger());
        var ticks = 0;
        using var timer = scheduler.Create(() => Interlocked.Increment(ref ticks));

        timer.Change(0, 10);

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref ticks) >= 10, Patience));
    }

    [Fact]
    public void DisablingATimerStopsFurtherTicks()
    {
        using var scheduler = new PreciseTimerScheduler(new CapturingRuntimeLogger());
        var ticks = 0;
        using var timer = scheduler.Create(() => Interlocked.Increment(ref ticks));
        timer.Change(0, 5);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref ticks) >= 2, Patience));

        timer.Change(Timeout.Infinite, Timeout.Infinite);
        Thread.Sleep(30);
        var countAfterStop = Volatile.Read(ref ticks);
        Thread.Sleep(100);

        Assert.Equal(countAfterStop, Volatile.Read(ref ticks));
    }

    [Fact]
    public void DisposedTimerNeverTicksAgain()
    {
        using var scheduler = new PreciseTimerScheduler(new CapturingRuntimeLogger());
        var ticks = 0;
        var timer = scheduler.Create(() => Interlocked.Increment(ref ticks));
        timer.Change(0, 5);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref ticks) >= 1, Patience));

        timer.Dispose();
        timer.Dispose();
        Thread.Sleep(30);
        var countAfterDispose = Volatile.Read(ref ticks);
        Thread.Sleep(100);

        Assert.Equal(countAfterDispose, Volatile.Read(ref ticks));
    }

    [Fact]
    public void ATickThatThrowsIsLoggedAndOtherTimersKeepRunning()
    {
        var logger = new CapturingRuntimeLogger();
        using var scheduler = new PreciseTimerScheduler(logger);
        var healthyTicks = 0;
        using var failing = scheduler.Create(() => throw new InvalidOperationException("boom"));
        using var healthy = scheduler.Create(() => Interlocked.Increment(ref healthyTicks));

        failing.Change(0, Timeout.Infinite);
        healthy.Change(10, 10);

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref healthyTicks) >= 3, Patience));
        Assert.Contains(logger.Errors, error => error.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public void ManyTimersShareOneThread()
    {
        using var scheduler = new PreciseTimerScheduler(new CapturingRuntimeLogger());
        var threadIds = new System.Collections.Concurrent.ConcurrentBag<int>();
        var timers = Enumerable.Range(0, 5)
            .Select(_ => scheduler.Create(() => threadIds.Add(Environment.CurrentManagedThreadId)))
            .ToList();

        foreach (var timer in timers)
        {
            timer.Change(0, Timeout.Infinite);
        }

        Assert.True(SpinWait.SpinUntil(() => threadIds.Count == 5, Patience));
        Assert.Single(threadIds.Distinct());
        timers.ForEach(timer => timer.Dispose());
    }
}
