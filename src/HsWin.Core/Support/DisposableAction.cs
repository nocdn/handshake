namespace HsWin.Core.Support;

/// <summary>Runs an action exactly once on the first <see cref="Dispose"/>, from any thread.</summary>
public sealed class DisposableAction : IDisposable
{
    private Action? _dispose;

    public DisposableAction(Action dispose)
    {
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
