using System.Windows.Threading;

namespace HsWin.App.Threading;

/// <summary>The UI thread as the lifecycle coordinators see it; fakeable without a WPF dispatcher.</summary>
internal interface IUiThread
{
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> to run on the UI thread, even when already on it.</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> on the UI thread; throws <see cref="TimeoutException"/> if it never responds.</summary>
    T Invoke<T>(Func<T> action, string description, TimeSpan timeout);
}

internal sealed class DispatcherUiThread(Dispatcher dispatcher) : IUiThread
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = dispatcher.BeginInvoke(action);
    }

    public T Invoke<T>(Func<T> action, string description, TimeSpan timeout) =>
        DispatcherGuard.InvokeWithTimeout(dispatcher, action, description, timeout);
}
