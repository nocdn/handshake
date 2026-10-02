using System.Diagnostics;

namespace HsWin.App.Magnifier;

internal readonly record struct MagnifierGpuDiagnostics(long Notifications, long Requests, long RateLimited,
    long QueueSignalMisses, long PresentRejected, long CaptureCalls, long CaptureTicks, long PresentCalls, long PresentTicks)
{
    internal string DescribeSince(MagnifierGpuDiagnostics previous, double seconds)
    {
        var captures = CaptureCalls - previous.CaptureCalls;
        var presents = PresentCalls - previous.PresentCalls;
        var captureMs = captures > 0 ? (CaptureTicks - previous.CaptureTicks) * 1000.0 / Stopwatch.Frequency / captures : 0;
        var presentMs = presents > 0 ? (PresentTicks - previous.PresentTicks) * 1000.0 / Stopwatch.Frequency / presents : 0;
        return FormattableString.Invariant($"captureEventsFps={(Notifications - previous.Notifications) / seconds:F1} renderRequestsFps={(Requests - previous.Requests) / seconds:F1} rateLimited={RateLimited - previous.RateLimited} queueSignalMisses={QueueSignalMisses - previous.QueueSignalMisses} presentRejected={PresentRejected - previous.PresentRejected} captureAverageMs={captureMs:F3} presentAverageMs={presentMs:F3}");
    }
}
