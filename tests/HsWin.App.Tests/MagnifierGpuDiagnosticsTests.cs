using System.Diagnostics;
using HsWin.App.Magnifier;

namespace HsWin.App.Tests;

public sealed class MagnifierGpuDiagnosticsTests
{
    [Fact]
    public void HoldReportUsesCounterDeltasAndSeparatesQueueSignalsFromRejectedPresents()
    {
        var previous = new MagnifierGpuDiagnostics(10, 20, 3, 4, 1, 2, 100, 2, 200);
        var current = new MagnifierGpuDiagnostics(490, 980, 8, 11, 3, 6,
            100 + Stopwatch.Frequency / 250, 4, 200 + Stopwatch.Frequency / 1000);
        Assert.Equal("captureEventsFps=240.0 renderRequestsFps=480.0 rateLimited=5 queueSignalMisses=7 presentRejected=2 captureAverageMs=1.000 presentAverageMs=0.500",
            current.DescribeSince(previous, 2));
    }

    [Fact]
    public void HoldWithoutCaptureOrPresentationHasFiniteZeroAverages()
    {
        var diagnostics = new MagnifierGpuDiagnostics(10, 20, 3, 4, 1, 2, 100, 2, 200);
        Assert.Equal("captureEventsFps=0.0 renderRequestsFps=0.0 rateLimited=0 queueSignalMisses=0 presentRejected=0 captureAverageMs=0.000 presentAverageMs=0.000",
            diagnostics.DescribeSince(diagnostics, 2));
    }
}
