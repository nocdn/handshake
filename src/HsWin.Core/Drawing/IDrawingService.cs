using HsWin.Core.Geometry;

namespace HsWin.Core.Drawing;

public interface IDrawingService
{
    IDrawing CreateRectangle(PixelRectangle frame);
}

public interface IDrawing : IDisposable
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void SetFrame(PixelRectangle frame);
    void SetFillColor(DrawingColor color);
}

/// <summary>RGBA channels in the inclusive range 0–1.</summary>
public sealed record DrawingColor(double Red = 0, double Green = 0, double Blue = 0, double Alpha = 1);
