using HsWin.Core.Support;

namespace HsWin.Core.Timers;

public sealed class NullScriptTimerService : IScriptTimerService
{
    public static NullScriptTimerService Instance { get; } = new();

    private NullScriptTimerService()
    {
    }

    public IDisposable DoAfter(int delayMs, Action callback)
    {
        return EmptyDisposable.Instance;
    }

    public IDisposable DoEvery(int intervalMs, Action callback)
    {
        return EmptyDisposable.Instance;
    }
}
