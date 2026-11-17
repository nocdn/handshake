using HsWin.Core.Geometry;

namespace HsWin.Core.Drawing;

public sealed class NullDrawingService : IDrawingService
{
    public static NullDrawingService Instance { get; } = new();
    public IDrawing CreateRectangle(PixelRectangle frame) => new Drawing();

    private sealed class Drawing : IDrawing
    {
        public bool IsVisible { get; private set; }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void SetFrame(PixelRectangle frame) { }
        public void SetFillColor(DrawingColor color) { }
        public void Dispose() => Hide();
    }
}
