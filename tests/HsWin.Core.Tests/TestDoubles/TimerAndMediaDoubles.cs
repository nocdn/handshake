using HsWin.Core.Media;
using HsWin.Core.Scripting;
using HsWin.Core.Timers;

namespace HsWin.Core.Tests;

internal sealed class CapturingScriptTimerService : IScriptTimerService
{
    public List<CapturingTimer> Timers { get; } = [];

    public IDisposable DoAfter(int delayMs, Action callback)
    {
        return AddTimer(delayMs, callback);
    }

    public IDisposable DoEvery(int intervalMs, Action callback)
    {
        return AddTimer(intervalMs, callback);
    }

    private CapturingTimer AddTimer(int intervalMs, Action callback)
    {
        var timer = new CapturingTimer(intervalMs, callback);
        Timers.Add(timer);
        return timer;
    }
}

internal sealed class CapturingTimer : IDisposable
{
    private readonly Action _callback;

    public CapturingTimer(int intervalMs, Action callback)
    {
        IntervalMs = intervalMs;
        _callback = callback;
    }

    public int IntervalMs { get; }

    public bool IsDisposed { get; private set; }

    public void Trigger()
    {
        _callback();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

internal sealed class CapturingMediaController : IMediaController
{
    public List<string> Actions { get; } = [];

    public MediaCommandResult PlayPauseResult { get; init; } =
        new("playPause", true, "played", "paused", "playing", "mediaSession");

    public MediaCommandResult PlayPause()
    {
        Actions.Add("playPause");
        return PlayPauseResult;
    }

    public MediaCommandResult PreviousTrack()
    {
        Actions.Add("previousTrack");
        return MediaCommandResult.Sent("previousTrack", "test");
    }

    public MediaCommandResult NextTrack()
    {
        Actions.Add("nextTrack");
        return MediaCommandResult.Sent("nextTrack", "test");
    }
}
