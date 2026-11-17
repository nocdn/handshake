using HsWin.Core.Drawing;

namespace HsWin.Core.Scripting;

public sealed class DrawingScriptApi(IDrawingService service, Action<IDisposable> track)
{
    public DrawingScriptHandle Rectangle(object? frame)
    {
        var handle = new DrawingScriptHandle(service.CreateRectangle(DrawingScriptArguments.Frame(frame)));
        track(handle);
        return handle;
    }
}

public sealed class DrawingScriptHandle(IDrawing drawing) : ScriptResourceHandle(drawing)
{
    public bool isVisible => !IsDisposed && drawing.IsVisible;

    public DrawingScriptHandle show()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        drawing.Show();
        return this;
    }

    public DrawingScriptHandle hide()
    {
        if (!IsDisposed) drawing.Hide();
        return this;
    }

    public DrawingScriptHandle setFrame(object? frame)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        drawing.SetFrame(DrawingScriptArguments.Frame(frame));
        return this;
    }

    public DrawingScriptHandle setFillColor(object? color)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        drawing.SetFillColor(DrawingScriptArguments.Color(color));
        return this;
    }
}
