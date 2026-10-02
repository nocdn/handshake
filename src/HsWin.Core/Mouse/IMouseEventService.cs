namespace HsWin.Core.Mouse;

public interface IMouseEventService
{
    IDisposable WatchScroll(MouseScrollWatchOptions options, Action<MouseScrollEventSnapshot> callback);
}
