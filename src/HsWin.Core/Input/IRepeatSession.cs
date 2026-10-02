namespace HsWin.Core.Input;

/// <summary>
/// A running keyboard or mouse repeat loop that can change rate without tear-down races.
/// </summary>
public interface IRepeatSession : IDisposable
{
    int IntervalMs { get; }

    void SetIntervalMs(int intervalMs);
}
