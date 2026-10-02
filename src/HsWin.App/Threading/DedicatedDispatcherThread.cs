using System.Windows.Threading;

namespace HsWin.App.Threading;

/// <summary>
/// A background STA thread running its own WPF dispatcher (and so a Win32 message loop). Used for
/// out-of-context WinEvent hooks, which deliver callbacks through the installing thread's message
/// queue: hosting them here keeps them off the UI dispatcher, so a busy UI cannot delay them and a
/// focus change can never deadlock against UI work.
/// </summary>
internal sealed class DedicatedDispatcherThread : IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly Thread _thread;
    private int _disposed;

    public DedicatedDispatcherThread(string name)
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = name
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
        Dispatcher = dispatcher!;
    }

    public Dispatcher Dispatcher { get; }

    /// <summary>Runs <paramref name="action"/> on this thread and waits, within the marshal budget.</summary>
    public void Invoke(Action action, string description)
    {
        ArgumentNullException.ThrowIfNull(action);
        Invoke(
            () =>
            {
                action();
                return true;
            },
            description);
    }

    /// <summary>Runs <paramref name="action"/> on this thread and returns its result, within the marshal budget.</summary>
    public T Invoke<T>(Func<T> action, string description)
    {
        ArgumentNullException.ThrowIfNull(action);
        return DispatcherGuard.InvokeWithTimeout(Dispatcher, action, description, DispatcherGuard.MarshalTimeout);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Dispatcher.InvokeShutdown();
        if (Thread.CurrentThread != _thread)
        {
            _thread.Join(ShutdownTimeout);
        }
    }
}
