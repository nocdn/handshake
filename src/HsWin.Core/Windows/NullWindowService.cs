using HsWin.Core.Support;

namespace HsWin.Core.Windows;

public sealed class NullWindowService : IWindowService
{
    public static NullWindowService Instance { get; } = new();

    private NullWindowService()
    {
    }

    public WindowSnapshot? GetFocusedWindow() => null;

    public WindowSnapshot? GetWindow(string id) => null;

    public WindowMoveResult MoveToScreen(string id, WindowTargetScreen targetScreen, WindowMoveOptions options) =>
        WindowMoveResult.NotMoved(id, "window-service-unavailable");

    public IDisposable WatchFocused(Action<WindowSnapshot> callback) => EmptyDisposable.Instance;
}
