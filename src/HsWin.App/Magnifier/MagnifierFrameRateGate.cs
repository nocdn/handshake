using System.Diagnostics;

namespace HsWin.App.Magnifier;

/// <summary>A two-frame token budget absorbs capture jitter without reducing a 240 Hz source to alternate frames.</summary>
internal sealed class MagnifierFrameRateGate(int fps, long frequency)
{
    internal MagnifierFrameRateGate(int fps) : this(fps, Stopwatch.Frequency) { }
    private readonly long _period = Math.Max(1, frequency / fps);
    private long _deadline;
    internal bool IsDue(long now) => now >= _deadline - _period;
    internal void Presented(long now) => _deadline = Math.Max(_deadline, now) + _period;
    internal void Reset() => _deadline = 0;
}
