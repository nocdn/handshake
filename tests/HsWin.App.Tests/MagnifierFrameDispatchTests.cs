using HsWin.App.Threading;
using HsWin.App.Magnifier;

namespace HsWin.App.Tests;

public sealed class MagnifierFrameDispatchTests
{
    [Fact]
    public void SlowDispatcherKeepsOnlyOneOutstandingFrame()
    {
        var queue = new Queue<Action>();
        var captures = 0;
        var frames = new MagnifierFrameDispatch(queue.Enqueue, () => captures++);
        frames.Start();
        for (var i = 0; i < 1000; i++) frames.RequestFrame();
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.Equal(1, captures);
        frames.RequestFrame();
        Assert.Single(queue);
    }

    [Fact]
    public void CaptureInProgressDoesNotAccumulateMoreFrames()
    {
        var queue = new Queue<Action>();
        MagnifierFrameDispatch? frames = null;
        frames = new MagnifierFrameDispatch(queue.Enqueue, () =>
        {
            for (var i = 0; i < 1000; i++) frames!.RequestFrame();
        });
        frames.Start();
        frames.RequestFrame();
        queue.Dequeue()();
        Assert.Empty(queue);
        frames.RequestFrame();
        Assert.Single(queue);
    }

    [Fact]
    public void HiddenLensDropsQueuedFrameAndStopsSchedulingUntilRestarted()
    {
        var queue = new Queue<Action>();
        var captures = 0;
        var frames = new MagnifierFrameDispatch(queue.Enqueue, () => captures++);
        frames.RequestFrame();
        Assert.Empty(queue);
        frames.Start();
        frames.RequestFrame();
        frames.Stop();
        queue.Dequeue()();
        frames.RequestFrame();
        Assert.Equal(0, captures);
        Assert.Empty(queue);
        frames.Start();
        frames.RequestFrame();
        queue.Dequeue()();
        Assert.Equal(1, captures);
    }

    [Fact]
    public void CaptureFailureDoesNotLeaveSchedulingStuck()
    {
        var queue = new Queue<Action>();
        var frames = new MagnifierFrameDispatch(queue.Enqueue, () => throw new InvalidOperationException());
        frames.Start();
        frames.RequestFrame();
        Assert.Throws<InvalidOperationException>(queue.Dequeue());
        frames.RequestFrame();
        Assert.Single(queue);
    }

    [Fact]
    public void DispatchFailureDoesNotLeaveSchedulingStuck()
    {
        var queue = new Queue<Action>();
        var failed = false;
        var frames = new MagnifierFrameDispatch(action =>
        {
            if (!failed) { failed = true; throw new InvalidOperationException(); }
            queue.Enqueue(action);
        }, () => { });
        frames.Start();
        Assert.Throws<InvalidOperationException>(frames.RequestFrame);
        frames.RequestFrame();
        Assert.Single(queue);
    }

    [Theory]
    [InlineData(100, 100, 10, 110)]
    [InlineData(100, 105, 10, 110)]
    [InlineData(100, 110, 10, 120)]
    [InlineData(100, 145, 10, 150)]
    public void FrameDeadlinesSkipMissedFramesWithoutDriftingOrBursting(long previous, long now, long period, long expected)
    {
        Assert.Equal(expected, HighResolutionWaitableTimer.NextDeadline(previous, now, period));
    }

    [Fact]
    public void HighResolutionClockCanStartStopResumeAndDispose()
    {
        using var observed = new AutoResetEvent(false);
        Exception? failure = null;
        using var timer = new HighResolutionFrameTimer(240, () => observed.Set(), error =>
        {
            failure = error;
            observed.Set();
        });
        timer.Start();
        Assert.True(observed.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        timer.Stop();
        timer.Start();
        Assert.True(observed.WaitOne(TimeSpan.FromSeconds(5)));
        timer.Dispose();
        timer.Dispose();
        Assert.Null(failure);
        Assert.Throws<ObjectDisposedException>(timer.Start);
    }
}
