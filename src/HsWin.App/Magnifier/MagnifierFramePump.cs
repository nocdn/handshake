using System.Diagnostics;

namespace HsWin.App.Magnifier;

/// <summary>Drain capture independently of presentation, retaining only the newest pending image.</summary>
internal sealed class MagnifierFramePump(int fps, long frequency)
{
    internal MagnifierFramePump(int fps) : this(fps, Stopwatch.Frequency) { }
    private readonly MagnifierFrameRateGate _rate = new(fps, frequency);
    private bool _pendingImage;
    internal long Requests { get; private set; }
    internal long RateLimited { get; private set; }

    internal void ResetClock() => _rate.Reset();
    internal void SetPendingImage(bool pending) => _pendingImage = pending;

    internal bool Render(long now, Func<bool> captureLatest, Func<bool> presentLatest)
    {
        Requests++;
        // Always drain the capture pool, including while presentation is rate limited.
        _pendingImage |= captureLatest();
        if (!_rate.IsDue(now)) { RateLimited++; return false; }
        if (!_pendingImage || !presentLatest()) return false;
        _pendingImage = false;
        _rate.Presented(now);
        return true;
    }
}
