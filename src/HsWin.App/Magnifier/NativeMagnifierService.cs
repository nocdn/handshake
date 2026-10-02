using System.Windows.Threading;
using HsWin.App.Threading;
using HsWin.Core.Logging;
using HsWin.Core.Magnifier;
using static HsWin.App.Magnifier.MagnifierNative;

namespace HsWin.App.Magnifier;

internal sealed class NativeMagnifierService(IRuntimeLogger logger, bool preferGpu = true,
    Func<nint, nint, nint, int, IGpuMagnifierRenderer>? rendererFactory = null) : IMagnifierService, IDisposable
{
    private static readonly TimeSpan CreateTimeout = TimeSpan.FromSeconds(5);

    // _gate guards the thread and the disposed flag; it is never held while waiting on the
    // magnifier thread. _sessionsGate guards _sessions, which sessions leave from their own thread.
    private readonly object _gate = new();
    private readonly object _sessionsGate = new();
    private readonly HashSet<NativeMagnifierSession> _sessions = [];
    private Dispatcher? _dispatcher;
    private Thread? _thread;
    private bool _disposed;

    public IMagnifierSession Create(MagnifierOptions options)
    {
        Dispatcher dispatcher;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureThread();
            dispatcher = _dispatcher!;
        }

        // A timed-out request is abandoned so it cannot create a lens nobody holds if the thread recovers.
        var marshal = new DispatcherMarshal();
        return DispatcherGuard.InvokeWithTimeout<IMagnifierSession>(
            dispatcher,
            () =>
            {
                var session = new NativeMagnifierSession(dispatcher, logger, options, RemoveSession, preferGpu,
                    rendererFactory ?? ((host, monitor, window, diameter) => new GpuMagnifierRenderer(host, monitor, window, diameter)));
                lock (_sessionsGate)
                {
                    _sessions.Add(session);
                }

                return session;
            },
            "magnifier creation",
            CreateTimeout,
            marshal);
    }

    private void RemoveSession(NativeMagnifierSession session)
    {
        lock (_sessionsGate)
        {
            _sessions.Remove(session);
        }
    }

    private void EnsureThread()
    {
        if (_dispatcher is not null) return;
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            var initialized = false;
            try
            {
                SetThreadDpiAwarenessContext(PerMonitorAwareV2); // Per-monitor V2: native capture always uses physical coordinates.
                Check(MagInitialize(), "initialize Windows Magnification API");
                initialized = true;
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
                logger.Error("Magnifier thread failed.", exception);
            }
            finally
            {
                NativeMagnifierSession[] remaining;
                lock (_sessionsGate)
                {
                    remaining = [.. _sessions];
                }

                foreach (var lens in remaining) lens.DestroyNative();
                if (initialized) MagUninitialize();
            }
        }) { IsBackground = true, Name = "HsWin Magnifier" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        }

        if (_thread is not null && _thread != Thread.CurrentThread) _thread.Join(TimeSpan.FromSeconds(5));
    }
}
