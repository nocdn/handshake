namespace HsWin.Core.Magnifier;

public interface IMagnifierService
{
    IMagnifierSession Create(MagnifierOptions options);
}

public interface IMagnifierSession : IDisposable
{
    bool IsVisible { get; }

    // These operations must enqueue native work without waiting on a UI thread.
    void Show();
    void Hide();
    void SetZoom(double zoom);
}
