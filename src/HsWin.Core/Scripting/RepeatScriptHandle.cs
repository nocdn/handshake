using HsWin.Core.Input;

namespace HsWin.Core.Scripting;

/// <summary>The script handle returned by <c>hs.keyboard.repeat</c> and <c>hs.mouse.repeat</c>.</summary>
public sealed class RepeatScriptHandle : ScriptResourceHandle
{
    private readonly IRepeatSession _session;

    internal RepeatScriptHandle(IRepeatSession session)
        : base(session)
    {
        _session = session;
    }

    public int IntervalMs => _session.IntervalMs;

    public void SetIntervalMs(object? intervalMs)
    {
        _session.SetIntervalMs(RepeatTiming.ParseIntervalMs(intervalMs));
    }

    public int getIntervalMs() => IntervalMs;

    public void setIntervalMs(object? intervalMs) => SetIntervalMs(intervalMs);
}
