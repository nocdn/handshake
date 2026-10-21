using HsWin.Core.Input;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

internal sealed class CapturingMouseService : IMouseService
{
    private readonly MouseScreenSnapshot? _screen;

    public CapturingMouseService(MouseScreenSnapshot? screen)
    {
        _screen = screen;
    }

    public int GetCurrentScreenCallCount { get; private set; }

    public MouseScreenSnapshot? GetCurrentScreen()
    {
        GetCurrentScreenCallCount++;
        return _screen;
    }
}

internal sealed class CapturingMouseInputService : IMouseInputService
{
    public List<MouseButton> Clicks { get; } = [];

    public List<int> Scrolls { get; } = [];

    public List<CapturingMouseRepeat> Repeats { get; } = [];

    public int StopActiveRepeatCallCount { get; private set; }

    public void Click(MouseButton button)
    {
        Clicks.Add(button);
    }

    public void Scroll(int delta)
    {
        Scrolls.Add(delta);
    }

    public IRepeatSession Repeat(MouseButton button, MouseRepeatOptions options)
    {
        var registration = new CapturingRepeatSession(options.IntervalMs);
        Repeats.Add(new CapturingMouseRepeat(button, options, registration));
        return registration;
    }

    public void StopActiveRepeat()
    {
        StopActiveRepeatCallCount++;
    }
}

internal sealed class CapturingRepeatSession : IRepeatSession
{
    private readonly CapturingDisposable _disposable = new();

    public CapturingRepeatSession(int intervalMs)
    {
        IntervalMs = intervalMs;
    }

    public int IntervalMs { get; private set; }

    public bool IsDisposed => _disposable.IsDisposed;

    public void SetIntervalMs(int intervalMs)
    {
        IntervalMs = intervalMs;
    }

    public void Dispose()
    {
        _disposable.Dispose();
    }
}

internal sealed class CapturingMouseEventService : IMouseEventService
{
    public List<CapturingMouseScrollWatch> Watches { get; } = [];

    public IDisposable WatchScroll(MouseScrollWatchOptions options, Action<MouseScrollEventSnapshot> callback)
    {
        var registration = new CapturingDisposable();
        Watches.Add(new CapturingMouseScrollWatch(options, callback, registration));
        return registration;
    }
}

internal sealed record CapturingMouseScrollWatch(
    MouseScrollWatchOptions Options,
    Action<MouseScrollEventSnapshot> Callback,
    CapturingDisposable Registration);

internal sealed record CapturingMouseRepeat(
    MouseButton Button,
    MouseRepeatOptions Options,
    CapturingRepeatSession Registration);
