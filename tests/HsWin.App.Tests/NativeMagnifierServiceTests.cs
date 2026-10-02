using System.Runtime.InteropServices;
using HsWin.App.Magnifier;
using HsWin.Core.Logging;
using HsWin.Core.Magnifier;
using HsWin.App.Native;

namespace HsWin.App.Tests;

public sealed class NativeMagnifierServiceTests
{
    [Fact]
    public void NativeLensCreatesShowsChangesZoomHidesAndDestroysItsWindows()
    {
        using var service = new NativeMagnifierService(NullRuntimeLogger.Instance, preferGpu: false);
        using var session = (NativeMagnifierSession)service.Create(new MagnifierOptions(Screen: MagnifierScreen.Primary));
        var host = session.HostHandle;
        var lens = session.LensHandle;
        Assert.NotEqual(0, host);
        Assert.NotEqual(0, lens);
        Assert.False(IsWindowVisible(host));
        session.Show();
        Assert.True(SpinWait.SpinUntil(() => IsWindowVisible(host), TimeSpan.FromSeconds(5)));
        Assert.True(MagGetWindowTransform(lens, out var transform));
        Assert.Equal(2, transform.M00);
        Assert.True(MagGetWindowSource(lens, out var source));
        Assert.Equal(357, source.Right - source.Left);
        Assert.Equal(357, source.Bottom - source.Top);
        session.SetZoom(4);
        Assert.True(SpinWait.SpinUntil(() => MagGetWindowTransform(lens, out var current) && current.M00 == 4,
            TimeSpan.FromSeconds(5)));
        session.Hide();
        Assert.True(SpinWait.SpinUntil(() => !IsWindowVisible(host), TimeSpan.FromSeconds(5)));
        session.Dispose();
        Assert.True(SpinWait.SpinUntil(() => !IsWindow(host) && !IsWindow(lens), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisposingServiceDestroysEvenUndisposedLenses()
    {
        var service = new NativeMagnifierService(NullRuntimeLogger.Instance, preferGpu: false);
        var session = (NativeMagnifierSession)service.Create(new());
        var host = session.HostHandle;
        service.Dispose();
        Assert.False(IsWindow(host));
        Assert.False(session.IsVisible);
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => service.Create(new()));
    }

    [Fact]
    public void UnavailableGpuCaptureFallsBackToNativeRenderer()
    {
        using var service = new NativeMagnifierService(NullRuntimeLogger.Instance,
            rendererFactory: (_, _, _, _) => throw new InvalidOperationException("GPU unavailable"));
        using var lens = (NativeMagnifierSession)service.Create(new());
        Assert.False(lens.UsesGpu);
        lens.Show();
        Assert.True(SpinWait.SpinUntil(() => IsWindowVisible(lens.HostHandle), TimeSpan.FromSeconds(5)));
        Assert.True(MagGetWindowTransform(lens.LensHandle, out var transform));
        Assert.Equal(2, transform.M00);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GpuFailureOnShowOrZoomKeepsLensUsableWithNativeCapture(bool duringZoom)
    {
        // Older Windows versions intentionally select native capture before creating a GPU renderer.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        var renderer = new TestRenderer();
        using var service = new NativeMagnifierService(NullRuntimeLogger.Instance,
            rendererFactory: (_, _, _, _) => renderer);
        using var lens = (NativeMagnifierSession)service.Create(new());
        Assert.True(lens.UsesGpu);
        if (duringZoom)
        {
            lens.Show();
            Assert.True(SpinWait.SpinUntil(() => IsWindowVisible(lens.HostHandle), TimeSpan.FromSeconds(5)));
            renderer.FailConfigure = true;
            lens.SetZoom(3);
        }
        else
        {
            renderer.FailCapture = true;
            lens.Show();
        }
        Assert.True(SpinWait.SpinUntil(() => !lens.UsesGpu, TimeSpan.FromSeconds(5)));
        Assert.True(lens.IsVisible);
        Assert.True(SpinWait.SpinUntil(() => MagGetWindowTransform(lens.LensHandle, out var transform)
            && transform.M00 == (duringZoom ? 3 : 2), TimeSpan.FromSeconds(5)));
        Assert.Equal(1, renderer.Disposals);
    }

    [Fact]
    public void GpuCaptureRunsOnlyDuringHoldAndHidesBeforeCaptureCleanup()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        var renderer = new TestRenderer();
        using var service = new NativeMagnifierService(NullRuntimeLogger.Instance,
            rendererFactory: (host, _, _, _) => { renderer.Host = host; return renderer; });
        using var lens = (NativeMagnifierSession)service.Create(new(Fps: 240));
        Assert.Equal(240, renderer.FrameRate);
        Assert.False(renderer.Active);
        Assert.NotNull(renderer.FrameRequest);
        lens.Show();
        Assert.True(SpinWait.SpinUntil(() => renderer.Active && IsWindowVisible(lens.HostHandle), TimeSpan.FromSeconds(5)));
        var frames = lens.CaptureFrames;
        renderer.FrameRequest!();
        Assert.True(SpinWait.SpinUntil(() => lens.CaptureFrames > frames, TimeSpan.FromSeconds(5)));
        lens.Hide();
        Assert.True(SpinWait.SpinUntil(() => !renderer.Active, TimeSpan.FromSeconds(5)));
        Assert.False(renderer.VisibleDuringStop);
        Assert.False(IsWindowVisible(lens.HostHandle));
        lens.Show();
        Assert.True(SpinWait.SpinUntil(() => renderer.Active && IsWindowVisible(lens.HostHandle), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void CaptureStartupFailureFallsBackAndNextHoldRetriesGpu()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        var first = new TestRenderer { FailStart = true };
        var recovered = new TestRenderer();
        var attempts = 0;
        using var service = new NativeMagnifierService(NullRuntimeLogger.Instance,
            rendererFactory: (_, _, _, _) => ++attempts == 1 ? first : recovered);
        using var lens = (NativeMagnifierSession)service.Create(new());
        lens.Show();
        Assert.True(SpinWait.SpinUntil(() => !lens.UsesGpu && IsWindowVisible(lens.HostHandle), TimeSpan.FromSeconds(5)));
        Assert.True(lens.IsVisible);
        Assert.Equal(1, first.Disposals);
        lens.Hide();
        Assert.True(SpinWait.SpinUntil(() => !IsWindowVisible(lens.HostHandle), TimeSpan.FromSeconds(5)));
        lens.Show();
        Assert.True(SpinWait.SpinUntil(() => lens.UsesGpu && recovered.Active, TimeSpan.FromSeconds(5)));
        Assert.Equal(2, attempts);
    }

    private sealed class TestRenderer : IGpuMagnifierRenderer
    {
        public volatile bool FailCapture;
        public volatile bool FailConfigure;
        public volatile bool FailStart;
        public volatile bool Active;
        public volatile bool VisibleDuringStop;
        public int FrameRate;
        public nint Host;
        public Action? FrameRequest;
        public int Disposals;
        public long AcquiredFrames => 0;
        public void SetFrameRequest(Action request, int fps) { FrameRequest = request; FrameRate = fps; }
        public void SetActive(bool active)
        {
            if (active && FailStart) throw new InvalidOperationException("Capture session unavailable");
            if (!active) VisibleDuringStop = IsWindowVisible(Host);
            Active = active;
        }
        public void Configure(MagnifierGeometry geometry, NativeRect monitor, int border)
        {
            if (FailConfigure) throw new InvalidOperationException("GPU lost during resize");
        }
        public bool Render()
        {
            if (FailCapture) throw new InvalidOperationException("GPU lost during capture");
            return true;
        }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MagGetWindowTransform(nint window, out MagnifierNative.Transform transform);
    [DllImport("Magnification.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MagGetWindowSource(nint window, out NativeRect source);
}
