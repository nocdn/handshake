namespace HsWin.Core.Mouse;

public interface IMouseEventService
{
    IDisposable WatchButtons(MouseButtonWatchOptions options, Action<MouseButtonEventSnapshot> callback);

    IDisposable WatchScroll(MouseScrollWatchOptions options, Action<MouseScrollEventSnapshot> callback);
}
