namespace HsWin.App.Magnifier;

/// <summary>Consume the newest pool frame and return every buffer before the next capture tick.</summary>
internal static class LatestCaptureFrameConsumer
{
    internal static bool ConsumeSurface<TFrame, TSurface>(Func<TFrame?> acquire,
        Func<TFrame, TSurface> openSurface, Func<TSurface, bool> consume)
        where TFrame : class, IDisposable where TSurface : IDisposable
    {
        return Consume(acquire, frame =>
        {
            using var surface = openSurface(frame);
            return consume(surface);
        });
    }

    internal static bool Consume<T>(Func<T?> acquire, Func<T, bool> consume) where T : class, IDisposable
    {
        T? frame = null;
        try
        {
            frame = acquire();
            if (frame is null) return false;
            while (acquire() is { } newer)
            {
                var previous = frame;
                frame = newer;
                previous.Dispose();
            }
            return consume(frame);
        }
        finally { frame?.Dispose(); }
    }
}
