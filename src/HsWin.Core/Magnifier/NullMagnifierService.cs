namespace HsWin.Core.Magnifier;

public sealed class NullMagnifierService : IMagnifierService
{
    public static NullMagnifierService Instance { get; } = new();

    public IMagnifierSession Create(MagnifierOptions options) => new Session();

    private sealed class Session : IMagnifierSession
    {
        public bool IsVisible { get; private set; }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void SetZoom(double zoom) { }
        public void Dispose() => Hide();
    }
}
