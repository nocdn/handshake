using System.Windows.Forms;
using System.Windows.Threading;
using HsWin.App.Threading;
using HsWin.Core.Drawing;
using HsWin.Core.Geometry;
using HsWin.Core.Logging;

namespace HsWin.App.Drawing;

/// <summary>A solid rectangle owned by the drawing thread. It never activates or receives input.</summary>
internal sealed class RectangleDrawing : IDrawing
{
    private readonly Dispatcher _dispatcher;
    private readonly IRuntimeLogger _logger;
    private readonly Action<RectangleDrawing> _remove;
    private readonly RectangleWindow _window;
    private DrawingColor _color = new();
    private int _disposed;
    private int _visible;

    internal RectangleDrawing(Dispatcher dispatcher, IRuntimeLogger logger, PixelRectangle frame, Action<RectangleDrawing> remove)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _remove = remove;
        _window = new RectangleWindow();
        try
        {
            SetFrame(frame);
            DrawingNative.SetAlpha(_window.Handle, 1);
        }
        catch
        {
            _window.Dispose();
            throw;
        }
    }

    internal nint WindowHandle => _window.IsDisposed ? 0 : _window.Handle;
    public bool IsVisible => Volatile.Read(ref _visible) != 0 && Volatile.Read(ref _disposed) == 0;

    public void Show() => Invoke(() =>
    {
        _window.Show();
        DrawingNative.SetAlpha(_window.Handle, _color.Alpha);
        _window.Refresh();
        Volatile.Write(ref _visible, 1);
    });

    public void Hide()
    {
        if (Volatile.Read(ref _disposed) == 0)
            Invoke(() => { _window.Hide(); Volatile.Write(ref _visible, 0); });
    }

    public void SetFrame(PixelRectangle frame) => Invoke(() =>
        _window.Bounds = new System.Drawing.Rectangle(frame.X, frame.Y, frame.Width, frame.Height));

    public void SetFillColor(DrawingColor color) => Invoke(() =>
    {
        _window.BackColor = System.Drawing.Color.FromArgb(
            (int)Math.Round(color.Red * 255), (int)Math.Round(color.Green * 255), (int)Math.Round(color.Blue * 255));
        DrawingNative.SetAlpha(_window.Handle, color.Alpha);
        _color = color;
    });

    private void Invoke(Action action)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        DispatcherGuard.InvokeWithTimeout(_dispatcher, () =>
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            action();
            return true;
        }, "drawing update", TimeSpan.FromSeconds(5), new DispatcherMarshal());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _visible, 0);
        DispatcherGuard.FireAndForget(_dispatcher, DestroyNative, "drawing cleanup", _logger);
    }

    internal void DestroyNative()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Volatile.Write(ref _visible, 0);
        try { _window.Dispose(); }
        finally { _remove(this); }
    }

    private sealed class RectangleWindow : Form
    {
        internal RectangleWindow()
        {
            Text = "HsWin Drawing";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = System.Drawing.Color.Black;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= DrawingNative.ExtendedStyle;
                return parameters;
            }
        }
    }
}
