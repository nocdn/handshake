namespace HsWin.Core.Input;

/// <summary>A repeat that sends nothing; used by null input services (tests, lint).</summary>
public sealed class NullRepeatSession(int intervalMs) : IRepeatSession
{
    public int IntervalMs { get; private set; } = intervalMs;

    public void SetIntervalMs(int intervalMs)
    {
        RepeatTiming.ValidateIntervalMs(intervalMs, nameof(intervalMs));
        IntervalMs = intervalMs;
    }

    public void Dispose()
    {
    }
}
