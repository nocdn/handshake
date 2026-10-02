using HsWin.App.Magnifier;

namespace HsWin.App.Tests;

public sealed class LatestCaptureFrameConsumerTests
{
    [Fact]
    public void WindowsCaptureDeliversDifferentSurfacesEvenWithIdenticalTimestamps()
    {
        var images = new List<int>();
        var frames = new[] { new TimedFrame(12345, 10), new TimedFrame(12345, 20), new TimedFrame(12345, 30) };
        foreach (var frame in frames)
        {
            var pending = true;
            Assert.True(LatestCaptureFrameConsumer.ConsumeSurface(() =>
            {
                if (!pending) return null;
                pending = false;
                return frame;
            }, captured => captured.Surface, surface => { images.Add(surface.Pixel); return true; }));
            Assert.Equal(1, frame.Surface.Disposals);
            Assert.Equal(1, frame.Disposals);
        }
        Assert.Equal(new[] { 10, 20, 30 }, images);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurfaceOpeningAndCopyFailuresReturnCaptureBuffers(bool failOpening)
    {
        var frame = new TimedFrame(12345, 10);
        var pending = true;
        Assert.Throws<InvalidOperationException>(() => LatestCaptureFrameConsumer.ConsumeSurface(() =>
        {
            if (!pending) return null;
            pending = false;
            return frame;
        }, captured => failOpening ? throw new InvalidOperationException() : captured.Surface,
            _ => throw new InvalidOperationException()));
        Assert.Equal(1, frame.Disposals);
        Assert.Equal(failOpening ? 0 : 1, frame.Surface.Disposals);
    }

    [Fact]
    public void NewestFrameIsConsumedAndAllBuffersAreReturnedBeforeNextTick()
    {
        var first = new Frame();
        var second = new Frame();
        var queue = new Queue<Frame>([first, second]);
        Assert.True(LatestCaptureFrameConsumer.Consume(() => queue.TryDequeue(out var frame) ? frame : null, frame =>
        {
            Assert.Same(second, frame);
            Assert.Equal(1, first.Disposals);
            Assert.Equal(0, second.Disposals);
            return true;
        }));
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Fact]
    public void NoFrameDoesNotInvokeConsumer()
    {
        Assert.False(LatestCaptureFrameConsumer.Consume<Frame>(() => null, _ => throw new InvalidOperationException()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcquisitionAndCopyFailuresAlwaysReturnBorrowedBuffer(bool duringAcquire)
    {
        var frame = new Frame();
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => LatestCaptureFrameConsumer.Consume(() =>
        {
            if (calls++ == 0) return frame;
            if (duringAcquire) throw new InvalidOperationException();
            return null;
        }, _ => throw new InvalidOperationException()));
        Assert.Equal(1, frame.Disposals);
    }

    [Fact]
    public void UnchangedFrameIsReturnedEvenWhenConsumerDeclinesToPresent()
    {
        var frame = new Frame();
        var pending = true;
        Assert.False(LatestCaptureFrameConsumer.Consume(() =>
        {
            if (!pending) return null;
            pending = false;
            return frame;
        }, _ => false));
        Assert.Equal(1, frame.Disposals);
    }

    private sealed class Frame : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private sealed class TimedFrame(long timestamp, int pixel) : IDisposable
    {
        public long Timestamp { get; } = timestamp;
        public Surface Surface { get; } = new(pixel);
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private sealed class Surface(int pixel) : IDisposable
    {
        public int Pixel { get; } = pixel;
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
