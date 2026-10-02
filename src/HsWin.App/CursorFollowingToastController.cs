using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using HsWin.App.Native;
using HsWin.App.Threading;

namespace HsWin.App;

internal interface ICursorFollowingToastController : IDisposable
{
    void Start(IToastView view);

    void Stop();
}

internal interface INativeCursorFollowingToastView
{
    nint NativeHandle { get; }
}

internal sealed partial class CursorFollowingToastController : ICursorFollowingToastController
{
    private static readonly TimeSpan NativeFrameInterval =
        TimeSpan.FromSeconds(1d / FollowingToastStyleMetrics.TargetFrameRate);

    private readonly IPreciseTimerFactory _timers;
    private IToastView? _fallbackView;
    private IPreciseTimer? _nativeTimer;
    private Point _smoothedCursor;
    private long _lastTimestamp;
    private long _generation;
    private bool _subscribed;

    public CursorFollowingToastController()
        : this(PreciseTimerScheduler.Shared)
    {
    }

    internal CursorFollowingToastController(IPreciseTimerFactory timers)
    {
        _timers = timers;
    }

    internal bool IsUsingNativeLoop => _nativeTimer is not null;

    public void Start(IToastView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Stop();

        if (view is INativeCursorFollowingToastView nativeView
            && nativeView.NativeHandle != nint.Zero
            && TryGetCursor(out var cursor)
            && TryCaptureNativeSnapshot(nativeView.NativeHandle, cursor, out var snapshot))
        {
            StartNative(nativeView.NativeHandle, snapshot);
            return;
        }

        StartCompositionFallback(view);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _nativeTimer, null)?.Dispose();

        if (_subscribed)
        {
            CompositionTarget.Rendering -= Rendering;
            _subscribed = false;
        }

        _fallbackView = null;
        _lastTimestamp = 0;
    }

    public void Dispose() => Stop();

    private void StartNative(nint handle, NativeSnapshot snapshot)
    {
        var generation = Interlocked.Increment(ref _generation);
        var state = new NativeLoopState(
            handle,
            snapshot,
            Stopwatch.GetTimestamp(),
            generation);
        PositionNative(state, snapshot);
        // The precise scheduler keeps the advertised frame rate; a pool timer rounds to ~15.6 ms.
        var frameMs = Math.Max(1, (int)Math.Round(NativeFrameInterval.TotalMilliseconds));
        var timer = _timers.Create(() => NativeTick(state));
        _nativeTimer = timer;
        timer.Change(frameMs, frameMs);
    }

    private void NativeTick(NativeLoopState state)
    {
        if (state.Generation != Volatile.Read(ref _generation)
            || Interlocked.Exchange(ref state.TickActive, 1) != 0)
        {
            return;
        }

        try
        {
            if (!TryGetCursor(out var cursor))
            {
                return;
            }

            var snapshot = state.Snapshot with { Cursor = cursor };
            state.FramesSinceMetricsRefresh++;
            if (!snapshot.MonitorBounds.Contains(cursor)
                || state.FramesSinceMetricsRefresh >= FollowingToastStyleMetrics.TargetFrameRate / 4)
            {
                if (!TryCaptureNativeSnapshot(state.Handle, cursor, out snapshot))
                {
                    return;
                }

                state.FramesSinceMetricsRefresh = 0;
            }

            state.Snapshot = snapshot;

            var timestamp = Stopwatch.GetTimestamp();
            var elapsed = state.LastTimestamp == 0
                ? NativeFrameInterval
                : Stopwatch.GetElapsedTime(state.LastTimestamp, timestamp);
            state.LastTimestamp = timestamp;
            state.SmoothedCursor = FollowingToastMotion.SmoothCursor(
                state.SmoothedCursor,
                snapshot.Cursor,
                elapsed);
            PositionNative(state, snapshot);
        }
        finally
        {
            Volatile.Write(ref state.TickActive, 0);
        }
    }

    private void PositionNative(NativeLoopState state, NativeSnapshot snapshot)
    {
        var shadowInset = FollowingToastStyleMetrics.ShadowInset * snapshot.Scale;
        var pillSize = new Size(
            Math.Max(0, snapshot.WindowSize.Width - (shadowInset * 2)),
            Math.Max(0, snapshot.WindowSize.Height - (shadowInset * 2)));
        var pillOrigin = FollowingToastMotion.PlacePill(
            state.SmoothedCursor,
            pillSize,
            snapshot.ScreenBounds,
            snapshot.Scale);

        if (state.Generation != Volatile.Read(ref _generation))
        {
            return;
        }

        var left = (int)Math.Round(pillOrigin.X - shadowInset);
        var top = (int)Math.Round(pillOrigin.Y - shadowInset);
        if (state.LastWindowLeft == left && state.LastWindowTop == top)
        {
            return;
        }

        state.LastWindowLeft = left;
        state.LastWindowTop = top;

        NativeMethods.SetWindowPos(
            state.Handle,
            nint.Zero,
            left,
            top,
            0,
            0,
            NativeMethods.DoNotSize
                | NativeMethods.DoNotActivate
                | NativeMethods.DoNotChangeZOrder
                | NativeMethods.DoNotChangeOwnerZOrder);
    }

    private void StartCompositionFallback(IToastView view)
    {
        _fallbackView = view;
        var snapshot = CaptureFallbackSnapshot();
        _smoothedCursor = snapshot.Cursor;
        _lastTimestamp = Stopwatch.GetTimestamp();
        PositionFallback(view, snapshot.ScreenBounds);
        CompositionTarget.Rendering += Rendering;
        _subscribed = true;
    }

    private void Rendering(object? sender, EventArgs e)
    {
        var view = _fallbackView;
        if (view is null)
        {
            Stop();
            return;
        }

        var timestamp = Stopwatch.GetTimestamp();
        var elapsed = _lastTimestamp == 0
            ? NativeFrameInterval
            : Stopwatch.GetElapsedTime(_lastTimestamp, timestamp);
        _lastTimestamp = timestamp;

        var snapshot = CaptureFallbackSnapshot();
        _smoothedCursor = FollowingToastMotion.SmoothCursor(
            _smoothedCursor,
            snapshot.Cursor,
            elapsed);
        PositionFallback(view, snapshot.ScreenBounds);
    }

    private void PositionFallback(IToastView view, Rect screenBounds)
    {
        var shadowInset = FollowingToastStyleMetrics.ShadowInset;
        var pillSize = new Size(
            Math.Max(0, view.ActualWidth - (shadowInset * 2)),
            Math.Max(0, view.ActualHeight - (shadowInset * 2)));
        var pillOrigin = FollowingToastMotion.PlacePill(_smoothedCursor, pillSize, screenBounds);
        view.Left = pillOrigin.X - shadowInset;
        view.Top = pillOrigin.Y - shadowInset;
    }

    private static CursorSnapshot CaptureFallbackSnapshot()
    {
        var cursor = Cursor.Position;
        var workingArea = Screen.FromPoint(cursor).WorkingArea;

        // Convert with the target monitor's DPI; the toast may still sit on another monitor.
        var scale = ScreenGeometry.GetScaleAt(cursor.X, cursor.Y);
        return new CursorSnapshot(
            new Point(cursor.X / scale, cursor.Y / scale),
            new Rect(
                new Point(workingArea.Left / scale, workingArea.Top / scale),
                new Point(workingArea.Right / scale, workingArea.Bottom / scale)));
    }

    private static bool TryGetCursor(out Point cursor)
    {
        cursor = default;
        if (!ScreenGeometry.TryGetCursor(out var nativeCursor))
        {
            return false;
        }

        cursor = new Point(nativeCursor.X, nativeCursor.Y);
        return true;
    }

    private static bool TryCaptureNativeSnapshot(
        nint handle,
        Point cursor,
        out NativeSnapshot snapshot)
    {
        snapshot = default;
        if (!ScreenGeometry.TryGetWindowRect(handle, out var windowRect))
        {
            return false;
        }

        var monitor = ScreenGeometry.MonitorFromPoint(new NativePoint
        {
            X = (int)Math.Round(cursor.X),
            Y = (int)Math.Round(cursor.Y)
        });
        if (!ScreenGeometry.TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            return false;
        }

        // The toast is placed on the cursor's monitor, so that monitor's DPI decides its metrics,
        // not the DPI of whichever monitor the window happens to be on now.
        snapshot = new NativeSnapshot(
            cursor,
            new Rect(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top, monitorInfo.WorkArea.Width, monitorInfo.WorkArea.Height),
            new Rect(monitorInfo.Monitor.Left, monitorInfo.Monitor.Top, monitorInfo.Monitor.Width, monitorInfo.Monitor.Height),
            new Size(windowRect.Width, windowRect.Height),
            ScreenGeometry.GetScale(monitor));
        return true;
    }

    private readonly record struct CursorSnapshot(Point Cursor, Rect ScreenBounds);

    private readonly record struct NativeSnapshot(
        Point Cursor,
        Rect ScreenBounds,
        Rect MonitorBounds,
        Size WindowSize,
        double Scale);

    private sealed class NativeLoopState(
        nint handle,
        NativeSnapshot snapshot,
        long lastTimestamp,
        long generation)
    {
        public nint Handle { get; } = handle;

        public long Generation { get; } = generation;

        public long LastTimestamp { get; set; } = lastTimestamp;

        public Point SmoothedCursor { get; set; } = snapshot.Cursor;

        public NativeSnapshot Snapshot { get; set; } = snapshot;

        public int FramesSinceMetricsRefresh { get; set; }

        public int? LastWindowLeft { get; set; }

        public int? LastWindowTop { get; set; }

        public int TickActive;
    }

    private static partial class NativeMethods
    {
        internal const uint DoNotSize = 0x0001;
        internal const uint DoNotChangeZOrder = 0x0004;
        internal const uint DoNotActivate = 0x0010;
        internal const uint DoNotChangeOwnerZOrder = 0x0200;

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWindowPos(
            nint windowHandle,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
