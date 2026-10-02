using HsWin.Core.Support;

namespace HsWin.Core.Mouse;

public sealed class NullMouseEventService : IMouseEventService
{
    public static NullMouseEventService Instance { get; } = new();

    private NullMouseEventService()
    {
    }

    public IDisposable WatchScroll(MouseScrollWatchOptions options, Action<MouseScrollEventSnapshot> callback)
    {
        return EmptyDisposable.Instance;
    }
}
