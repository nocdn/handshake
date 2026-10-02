namespace HsWin.Core.Scripting;

/// <summary>
/// The script-facing handle for a host resource. Every alias releases the resource; disposal is
/// idempotent and thread-safe because timers, hooks and scripts can all end a resource.
/// </summary>
public class ScriptResourceHandle : IDisposable
{
    private readonly IDisposable _inner;
    private int _disposed;

    public ScriptResourceHandle(IDisposable inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>Raised once, after the resource has been released.</summary>
    internal event Action<ScriptResourceHandle>? Disposed;

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Ends the resource. Resources with a graceful finish (recordings) override this.</summary>
    public virtual void Stop() => Dispose();

    public void stop() => Stop();

    public void Delete() => Dispose();

    public void delete() => Dispose();

    public void dispose() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            ReleaseResource();
        }
        finally
        {
            Disposed?.Invoke(this);
        }
    }

    /// <summary>Releases the wrapped resource. Derived handles extend this, not <see cref="Dispose"/>.</summary>
    protected virtual void ReleaseResource()
    {
        _inner.Dispose();
    }
}
