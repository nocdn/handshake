namespace HsWin.App.Magnifier;

/// <summary>A stopped Windows capture session and its pool must never be reused.</summary>
internal sealed class CaptureActivation<T>(Func<T> create) : IDisposable where T : class, IDisposable
{
    private T? _current;
    private bool _disposed;
    internal T? Current => Volatile.Read(ref _current);
    internal void SetActive(bool active)
    {
        if (active)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is null) Volatile.Write(ref _current, create());
        }
        else
        {
            // Clear state before cleanup, including when a driver rejects session shutdown.
            Interlocked.Exchange(ref _current, null)?.Dispose();
        }
    }
    public void Dispose()
    {
        _disposed = true;
        SetActive(false);
    }
}
