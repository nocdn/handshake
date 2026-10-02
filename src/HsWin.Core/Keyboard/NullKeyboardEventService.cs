using HsWin.Core.Support;

namespace HsWin.Core.Keyboard;

public sealed class NullKeyboardEventService : IKeyboardEventService
{
    public static NullKeyboardEventService Instance { get; } = new();

    private NullKeyboardEventService()
    {
    }

    public IDisposable Watch(KeyboardEventWatchOptions options, Func<KeyboardEventSnapshot, bool> callback)
    {
        return EmptyDisposable.Instance;
    }

    public bool IsKeyDown(uint virtualKey)
    {
        return false;
    }
}
