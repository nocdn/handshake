using System.Windows.Threading;
using HsWin.App.Threading;
using HsWin.Core.Drawing;
using HsWin.Core.Geometry;
using HsWin.Core.Logging;

namespace HsWin.App.Drawing;

/// <summary>Drawings have their own message pump, independent of UI work and input hooks.</summary>
internal sealed class NativeDrawingService(IRuntimeLogger logger) : IDrawingService, IDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<RectangleDrawing> _drawings = [];
    private readonly object _drawingsGate = new();
    private Dispatcher? _dispatcher;
    private Thread? _thread;
    private bool _disposed;

    public IDrawing CreateRectangle(PixelRectangle frame)
    {
        Dispatcher dispatcher;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureThread();
            dispatcher = _dispatcher!;
        }

        return DispatcherGuard.InvokeWithTimeout<IDrawing>(dispatcher, () =>
        {
            // Disposal may have won the race while this operation was queued.
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var drawing = new RectangleDrawing(dispatcher, logger, frame, Remove);
                lock (_drawingsGate) _drawings.Add(drawing);
                return drawing;
            }
        }, "drawing creation", TimeSpan.FromSeconds(5), new DispatcherMarshal());
    }

    private void Remove(RectangleDrawing drawing)
    {
        lock (_drawingsGate) _drawings.Remove(drawing);
    }

    private void EnsureThread()
    {
        if (_dispatcher is not null) return;
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                DrawingNative.SetThreadDpiAwarenessContext(DrawingNative.PerMonitorAwareV2);
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
                logger.Error("Drawing thread failed.", exception);
            }
            finally
            {
                RectangleDrawing[] remaining;
                lock (_drawingsGate) remaining = [.. _drawings];
                foreach (var drawing in remaining) drawing.DestroyNative();
            }
        }) { IsBackground = true, Name = "HsWin Drawing" };
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
