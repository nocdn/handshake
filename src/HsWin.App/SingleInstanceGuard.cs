using HsWin.Core.Logging;
using System.Diagnostics;

namespace HsWin.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string MutexName = @"Local\HsWin.SingleInstance.5E65F5D3-AC46-43D2-B31C-B98F8757C640";
    private static readonly TimeSpan WaitAfterCleanup = TimeSpan.FromSeconds(8);

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstanceGuard Acquire(IRuntimeLogger logger)
    {
        return Acquire(logger, MutexName, PreviousInstanceCleaner.StopPreviousInstances, WaitAfterCleanup);
    }

    internal static SingleInstanceGuard Acquire(
        IRuntimeLogger logger,
        string mutexName,
        Action<IRuntimeLogger> terminatePreviousInstances,
        TimeSpan waitAfterCleanup)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentNullException.ThrowIfNull(terminatePreviousInstances);

        var startedAt = Stopwatch.GetTimestamp();
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        var cleaned = false;

        if (!TryAcquire(mutex, TimeSpan.Zero, logger))
        {
            logger.Warning("Another HsWin single-instance guard is active; asking previous instances to quit before startup continues.");
            terminatePreviousInstances(logger);
            cleaned = true;

            if (!TryAcquire(mutex, waitAfterCleanup, logger))
            {
                mutex.Dispose();
                throw new InvalidOperationException(
                    $"{AppBranding.DisplayName} could not start because another instance is still running.");
            }
        }

        logger.Info($"Single instance guard acquired elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");

        if (!cleaned)
        {
            var cleanupStartedAt = Stopwatch.GetTimestamp();
            terminatePreviousInstances(logger);
            logger.Info($"Single instance post-acquire cleanup elapsedMs={Stopwatch.GetElapsedTime(cleanupStartedAt).TotalMilliseconds:F3}.");
        }

        return new SingleInstanceGuard(mutex);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
        _disposed = true;
    }

    private static bool TryAcquire(Mutex mutex, TimeSpan timeout, IRuntimeLogger logger)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            logger.Warning("Single instance guard was abandoned by a previous process; startup will continue.");
            return true;
        }
    }
}
