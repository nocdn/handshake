using HsWin.Core.Logging;

namespace HsWin.App.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void AcquireRunsPreviousInstanceCleanupWhenGuardIsFree()
    {
        var cleanupCount = 0;
        using var guard = SingleInstanceGuard.Acquire(
            new CapturingRuntimeLogger(),
            CreateMutexName(),
            _ => cleanupCount++,
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, cleanupCount);
    }

    [Fact]
    public void AcquireRunsCleanupAndWaitsWhenAnotherGuardIsActive()
    {
        var mutexName = CreateMutexName();
        using var holderReady = new ManualResetEventSlim();
        using var releaseHolder = new ManualResetEventSlim();
        using var holderReleased = new ManualResetEventSlim();
        Exception? holderException = null;

        var holder = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(initiallyOwned: false, mutexName);
                mutex.WaitOne();
                holderReady.Set();
                releaseHolder.Wait(TimeSpan.FromSeconds(5));
                mutex.ReleaseMutex();
                holderReleased.Set();
            }
            catch (Exception exception)
            {
                holderException = exception;
                holderReady.Set();
            }
        });
        holder.Start();

        try
        {
            Assert.True(holderReady.Wait(TimeSpan.FromSeconds(5)));

            var cleanupCount = 0;
            using var guard = SingleInstanceGuard.Acquire(
                new CapturingRuntimeLogger(),
                mutexName,
                _ =>
                {
                    cleanupCount++;
                    releaseHolder.Set();
                    Assert.True(holderReleased.Wait(TimeSpan.FromSeconds(5)));
                },
                TimeSpan.FromSeconds(5));

            Assert.Equal(1, cleanupCount);
            Assert.Null(holderException);
        }
        finally
        {
            releaseHolder.Set();
            holder.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static string CreateMutexName()
    {
        return $@"Local\HsWin.App.Tests.{Guid.NewGuid():N}";
    }
}
