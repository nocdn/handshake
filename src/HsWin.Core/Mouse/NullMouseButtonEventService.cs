using HsWin.Core.Support;

namespace HsWin.Core.Mouse;

public sealed class NullMouseButtonEventService : IMouseButtonEventService
{
    public static NullMouseButtonEventService Instance { get; } = new();

    private NullMouseButtonEventService()
    {
    }

    public IDisposable WatchPhysicalButtons(Action<PhysicalMouseButtonEvent> callback)
    {
        return EmptyDisposable.Instance;
    }
}
