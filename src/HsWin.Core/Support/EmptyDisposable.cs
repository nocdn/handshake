namespace HsWin.Core.Support;

/// <summary>The shared no-op registration returned by null and lint services.</summary>
public sealed class EmptyDisposable : IDisposable
{
    public static EmptyDisposable Instance { get; } = new();

    private EmptyDisposable()
    {
    }

    public void Dispose()
    {
    }
}
