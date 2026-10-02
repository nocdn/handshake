using HsWin.App.Threading;
using System.Diagnostics;
using System.Windows.Threading;

namespace HsWin.App.Tests;

/// <summary>
/// Regression tests for the wedged-dispatcher config-reload deadlock: gate-holding
/// threads must fail fast instead of parking in Dispatcher.Invoke forever.
/// </summary>
public sealed class DispatcherGuardTests
{
    [Fact]
    public void InvokeReturnsResultWhenDispatcherPumping()
    {
        using var dispatcher = new PumpingDispatcher();

        var result = DispatcherGuard.InvokeWithTimeout(
            dispatcher.Dispatcher,
            () => 42,
            "test marshal",
            TimeSpan.FromSeconds(5));

        Assert.Equal(42, result);
    }

    [Fact]
    public void InvokeRunsInlineOnDispatcherThread()
    {
        using var dispatcher = new PumpingDispatcher();

        var result = dispatcher.Dispatcher.Invoke(() =>
            DispatcherGuard.InvokeWithTimeout(
                dispatcher.Dispatcher,
                () => "inline",
                "test marshal",
                TimeSpan.FromSeconds(5)));

        Assert.Equal("inline", result);
    }

    [Fact]
    public void InvokeThrowsTimeoutAndAbandonsWhenDispatcherWedged()
    {
        using var dispatcher = new PumpingDispatcher();
        using var wedgeRelease = new ManualResetEventSlim(false);
        var actionRan = false;

        dispatcher.Wedge(wedgeRelease.WaitHandle);

        var marshal = new DispatcherMarshal();
        var startedAt = Stopwatch.GetTimestamp();
        var exception = Assert.Throws<TimeoutException>(() =>
            DispatcherGuard.InvokeWithTimeout<object?>(
                dispatcher.Dispatcher,
                () =>
                {
                    actionRan = true;
                    return null;
                },
                "wedged marshal",
                TimeSpan.FromMilliseconds(250),
                marshal));
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.Contains("wedged marshal", exception.Message);
        Assert.True(marshal.IsAbandoned);
        Assert.True(elapsed < TimeSpan.FromSeconds(10), $"Took too long: {elapsed}");

        // After recovery the abandoned operation must not run its action.
        wedgeRelease.Set();
        dispatcher.Pump();
        Assert.False(actionRan);
    }

    [Fact]
    public void FireAndForgetRunsActionWithoutWaiting()
    {
        using var dispatcher = new PumpingDispatcher();
        using var ran = new ManualResetEventSlim(false);

        DispatcherGuard.FireAndForget(dispatcher.Dispatcher, () => ran.Set(), "test teardown");

        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void FireAndForgetSurvivesShutdownDispatcher()
    {
        var dispatcher = new PumpingDispatcher();
        var underlying = dispatcher.Dispatcher;
        dispatcher.Dispose();

        // Must not throw for a dispatcher that is shutting down.
        DispatcherGuard.FireAndForget(underlying, () => { }, "torn-down dispatcher");
    }

    /// <summary>
    /// Own STA thread running a real WPF dispatcher message loop, so tests can wedge
    /// it and verify gate-holding callers fail fast instead of deadlocking.
    /// </summary>
    private sealed class PumpingDispatcher : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new(false);
        private bool _disposed;

        public PumpingDispatcher()
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

        /// <summary>
        /// Parks the dispatcher inside a blocking operation until
        /// <paramref name="release"/> is signaled. Waits until the wedge is active.
        /// </summary>
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

        /// <summary>
        /// Runs a no-op through the dispatcher, proving it is pumping again.
        /// </summary>
        public void Pump()
        {
            Dispatcher.Invoke(() => { }, DispatcherPriority.Send);
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
