using HsWin.Core.Logging;
using HsWin.Core.Magnifier;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static HsWin.App.Magnifier.MagnifierNative;
using HsWin.App.Native;

namespace HsWin.App.Magnifier;

/// <summary>All HWND work and live capture runs on the magnifier dispatcher, never the WPF UI or hook thread.</summary>
internal sealed class NativeMagnifierSession : IMagnifierSession
{
    private readonly Dispatcher _dispatcher;
    private readonly IRuntimeLogger _logger;
    private readonly MagnifierOptions _options;
    private readonly HighResolutionFrameTimer _timer;
    private readonly MagnifierFrameDispatch _frames;
    private readonly Action<NativeMagnifierSession> _remove;
    private nint _host;
    private nint _lens;
    private int _visible;
    private int _disposed;
    private double _zoom;
    private NativeRect _monitor;
    private MagnifierGeometry? _geometry;
    private double _appliedZoom;
    private long _captureFrames;
    private IGpuMagnifierRenderer? _gpu;
    private readonly Func<nint, nint, nint, int, IGpuMagnifierRenderer> _rendererFactory;
    private nint _monitorHandle;
    private nint _gpuMonitor;
    private nint _captureWindow;
    private nint _gpuWindow;
    private readonly bool _preferGpu;
    private long _holdStarted;
    private long _holdCaptured;
    private long _holdPresented;
    private MagnifierGpuDiagnostics _holdDiagnostics;

    internal nint HostHandle => _host;
    internal nint LensHandle => _lens;
    internal long CaptureFrames => Interlocked.Read(ref _captureFrames);
    internal bool UsesGpu => Volatile.Read(ref _gpu) is not null;
    internal long AcquiredFrames => _gpu?.AcquiredFrames ?? 0;

    internal NativeMagnifierSession(Dispatcher dispatcher, IRuntimeLogger logger, MagnifierOptions options,
        Action<NativeMagnifierSession> remove, bool preferGpu, Func<nint, nint, nint, int, IGpuMagnifierRenderer> rendererFactory)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _options = options;
        _zoom = options.Zoom;
        _remove = remove;
        _rendererFactory = rendererFactory;
        _preferGpu = preferGpu;
        // Background-priority dispatcher operations are deferred by Win32 paint/input activity and
        // can throttle a high-refresh lens. Render runs promptly; visibility commands retain Send priority.
        _frames = new MagnifierFrameDispatch(action => dispatcher.BeginInvoke(DispatcherPriority.Render, action), Tick);
        _timer = new HighResolutionFrameTimer(options.Fps, _frames.RequestFrame,
            exception => Queue(() =>
            {
                Volatile.Write(ref _visible, 0);
                HideNative();
                _logger.Error("Magnifier frame clock failed; lens hidden.", exception);
            }));
        try
        {
            _host = CreateWindowEx(HostExtendedStyle | (preferGpu ? ExNoRedirectionBitmap : 0), "STATIC", "HsWin Magnifier", HostStyle,
                0, 0, options.Diameter, options.Diameter, 0, 0, 0, 0);
            Check(_host != 0, "create host window");
            Check(SetLayeredWindowAttributes(_host, 0, FullyOpaque, LayeredAlpha), "set host opacity");
            _lens = CreateWindowEx(0, "Magnifier", "", LensStyle, options.BorderWidth, options.BorderWidth,
                options.Diameter - options.BorderWidth * 2, options.Diameter - options.BorderWidth * 2, _host, 0, 0, 0);
            Check(_lens != 0, "create live magnifier control");
            Check(MagSetWindowFilterList(_lens, FilterModeExclude, 1, [_host]), "exclude overlay from capture");
            _monitor = SelectMonitor();
            if (preferGpu) TryGpuRenderer();
            if (_gpu is null) UseNativeRenderer();
            UpdateGeometry();
            CaptureFrame(); // Prewarm the image while hidden.
        }
        catch
        {
            DestroyNative();
            throw;
        }
    }

    public bool IsVisible => Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _visible) != 0;

    public void Show()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Volatile.Write(ref _visible, 1);
        Queue(() =>
        {
            if (!IsVisible) return; // A quick release must not flash a queued show.
            _monitor = SelectMonitor();
            if (_gpu is not null && (_gpuMonitor != _monitorHandle || _gpuWindow != _captureWindow))
            {
                _gpu.Dispose();
                _gpu = null;
                TryGpuRenderer();
                if (_gpu is null) UseNativeRenderer();
            }
            else if (_gpu is null && _preferGpu)
            {
                TryGpuRenderer(); // A display/fullscreen transition may have invalidated the previous capture.
                if (_gpu is null) UseNativeRenderer();
            }
            _geometry = null;
            try { _gpu?.SetActive(true); }
            catch (Exception exception)
            {
                _logger.Warning($"Magnifier capture startup interrupted; using native capture. {exception.Message}");
                UseNativeRenderer();
            }
            UpdateGeometry();
            CaptureFrame();
            ShowWindow(_host, ShowWindowCommand.ShowNoActivate);
            _holdStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            _holdCaptured = AcquiredFrames;
            _holdPresented = CaptureFrames;
            _holdDiagnostics = _gpu?.Diagnostics ?? default;
            _frames.Start();
            _timer.Start();
        });
    }

    public void Hide()
    {
        Volatile.Write(ref _visible, 0);
        Queue(HideNative);
    }

    public void SetZoom(double zoom)
    {
        MagnifierOptions.ValidateZoom(zoom);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Queue(() =>
        {
            _zoom = zoom;
            if (IsVisible) { UpdateGeometry(); CaptureFrame(); }
        });
    }

    private void Queue(Action action)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
        {
            if (_host == 0) return;
            try { action(); }
            catch (Exception exception)
            {
                Volatile.Write(ref _visible, 0);
                HideNative();
                _logger.Error("Magnifier update failed; lens hidden.", exception);
            }
        });
    }

    private NativeRect SelectMonitor()
    {
        nint monitor;
        if (_options.Screen == MagnifierScreen.Mouse)
        {
            Check(GetCursorPos(out var cursor), "read cursor position");
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        }
        else
        {
            monitor = _options.Screen == MagnifierScreen.Primary
                ? MonitorFromPoint(default, MonitorDefaultToPrimary)
                : MonitorFromWindow(GetForegroundWindow(), MonitorDefaultToPrimary);
        }

        var info = NativeMonitorInfo.Create();
        Check(GetMonitorInfo(monitor, ref info), "read screen bounds");
        _monitorHandle = monitor;
        var foreground = GetForegroundWindow();
        _captureWindow = _options.Screen == MagnifierScreen.Foreground
            && DwmGetWindowAttribute(foreground, DwmExtendedFrameBounds, out var bounds, Marshal.SizeOf<NativeRect>()) == 0
            && MagnifierCaptureTarget.IsFullscreen(bounds, info.Monitor) ? foreground : 0;
        return info.Monitor;
    }

    private void TryGpuRenderer()
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                throw new NotSupportedException("Desktop capture exclusion requires Windows 10 version 2004 or newer.");
            Check(SetWindowDisplayAffinity(_host, ExcludeFromCapture), "exclude GPU overlay from desktop capture");
            SetWindowLongPtr(_host, ExtendedStyleIndex, (nint)(HostExtendedStyle | ExNoRedirectionBitmap));
            _gpu = _rendererFactory(_host, _monitorHandle, _captureWindow, _options.Diameter);
            _gpu.SetFrameRequest(_frames.RequestFrame, _options.Fps);
            _gpuMonitor = _monitorHandle;
            _gpuWindow = _captureWindow;
            ShowWindow(_lens, ShowWindowCommand.Hide);
            _logger.Info($"Magnifier using GPU {_gpu.Backend}; diameter={_options.Diameter} targetFps={_options.Fps}.");
        }
        catch (Exception exception)
        {
            _logger.Warning($"Magnifier GPU capture unavailable; using Windows Magnification API. {exception.Message}");
        }
    }

    private void UseNativeRenderer()
    {
        _gpu?.Dispose();
        _gpu = null;
        SetWindowLongPtr(_host, ExtendedStyleIndex, (nint)HostExtendedStyle);
        Check(SetWindowDisplayAffinity(_host, DisplayAffinityNone), "restore native overlay capture policy");
        Check(SetLayeredWindowAttributes(_host, 0, FullyOpaque, LayeredAlpha), "set native lens opacity");
        ShowWindow(_lens, ShowWindowCommand.Show);
        _geometry = null;
        _appliedZoom = 0;
    }

    private void Tick()
    {
        if (!IsVisible) { HideNative(); return; }
        try { CaptureFrame(); }
        catch (Exception exception)
        {
            Volatile.Write(ref _visible, 0);
            HideNative();
            _logger.Error("Magnifier capture failed; lens hidden.", exception);
        }
    }

    private void UpdateGeometry()
    {
        var geometry = MagnifierGeometry.Calculate(_monitor, _options.Diameter, _options.BorderWidth, _zoom);
        if (_geometry != geometry)
        {
            var inner = geometry.Diameter - _options.BorderWidth * 2;
            Check(SetWindowPos(_host, InsertAfterTopmost, geometry.X, geometry.Y, geometry.Diameter, geometry.Diameter, PositionNoActivate), "position lens");
            Check(SetWindowPos(_lens, InsertAfterTop, _options.BorderWidth, _options.BorderWidth, inner, inner, PositionNoZOrder | PositionNoActivate), "size capture");
            SetCircle(_host, geometry.Diameter);
            SetCircle(_lens, inner);
            _geometry = geometry;
        }
        if (_gpu is not null)
        {
            try { _gpu.Configure(geometry, _monitor, _options.BorderWidth); }
            catch (Exception exception)
            {
                _logger.Warning($"Magnifier GPU configuration interrupted; switching to native capture. {exception.Message}");
                UseNativeRenderer();
                UpdateGeometry();
            }
            return;
        }
        if (_appliedZoom != _zoom)
        {
            var transform = new Transform((float)_zoom);
            Check(MagSetWindowTransform(_lens, ref transform), "set zoom");
            _appliedZoom = _zoom;
        }
        Check(MagSetWindowSource(_lens, geometry.Source), "set capture area");
    }

    private void CaptureFrame()
    {
        if (_geometry is null) return;
        if (_gpu is not null)
        {
            try
            {
                if (_gpu.Render()) Interlocked.Increment(ref _captureFrames);
                return;
            }
            catch (Exception exception)
            {
                _logger.Warning($"Magnifier GPU capture interrupted; switching to native capture. {exception.Message}");
                UseNativeRenderer();
                UpdateGeometry();
            }
        }
        Check(InvalidateRect(_lens, 0, false), "request lens repaint");
        Check(UpdateWindow(_lens), "repaint lens");
        Interlocked.Increment(ref _captureFrames);
    }

    private void HideNative()
    {
        _frames.Stop();
        _timer.Stop();
        if (_host != 0) ShowWindow(_host, ShowWindowCommand.Hide);
        if (_holdStarted != 0)
        {
            var seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - _holdStarted) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (seconds >= 0.5)
                _logger.Info($"Magnifier hold backend={_gpu?.Backend ?? "native"} durationSeconds={seconds:F2} capturedSourceFps={Math.Max(0, AcquiredFrames - _holdCaptured) / seconds:F1} submittedFps={(CaptureFrames - _holdPresented) / seconds:F1} targetFps={_options.Fps}.");
            if (seconds >= 0.5 && _gpu is not null)
                _logger.Info($"Magnifier pipeline {_gpu.Diagnostics.DescribeSince(_holdDiagnostics, seconds)}.");
            _holdStarted = 0;
        }
        _gpu?.SetActive(false); // Hide the HWND before potentially expensive capture-session cleanup.
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _visible, 0);
        if (_dispatcher.CheckAccess()) DestroyNative();
        else if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(DispatcherPriority.Send, DestroyNative);
    }

    internal void DestroyNative()
    {
        Volatile.Write(ref _disposed, 1);

        // Teardown must reach DestroyWindow and deregistration even when an earlier step throws
        // (GPU device loss), or the lens window and session entry would leak for the app's lifetime.
        try
        {
            HideNative();
            _timer.Dispose();
            _gpu?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.Error("Magnifier cleanup failed; continuing teardown.", exception);
        }
        finally
        {
            _gpu = null;
            if (_host != 0) DestroyWindow(_host); // Also destroys the child control.
            _host = _lens = 0;
            _remove(this);
        }
    }
}
