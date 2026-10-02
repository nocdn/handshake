namespace HsWin.App.Magnifier;

/// <summary>At most one capture may be queued or running. Slow frames are dropped, never accumulated.</summary>
internal sealed class MagnifierFrameDispatch
{
    private readonly Action<Action> _schedule;
    private readonly Action _capture;
    private readonly Action _capturePendingFrame;
    private int _running;
    private int _pending;

    internal MagnifierFrameDispatch(Action<Action> schedule, Action capture)
    {
        _schedule = schedule;
        _capture = capture;
        _capturePendingFrame = CapturePendingFrame;
    }

    internal void Start() => Volatile.Write(ref _running, 1);
    internal void Stop() => Volatile.Write(ref _running, 0);

    internal void RequestFrame()
    {
        if (Volatile.Read(ref _running) == 0 || Interlocked.CompareExchange(ref _pending, 1, 0) != 0) return;
        try
        {
            _schedule(_capturePendingFrame);
        }
        catch
        {
            Volatile.Write(ref _pending, 0);
            throw;
        }
    }

    private void CapturePendingFrame()
    {
        try
        {
            if (Volatile.Read(ref _running) != 0) _capture();
        }
        finally
        {
            Volatile.Write(ref _pending, 0);
        }
    }
}
