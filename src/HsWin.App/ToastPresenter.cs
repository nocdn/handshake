using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using HsWin.App.Lifecycle;
using HsWin.App.Native;
using HsWin.App.Threading;
using HsWin.Core.Alerts;
using HsWin.Core.Logging;
using Point = System.Windows.Point;
using WpfApplication = System.Windows.Application;

namespace HsWin.App;

internal sealed class ToastPresenter : ICountingAlertPresenter, IDisposable
{
    private const double HiddenWindowCoordinate = -1_000_000;

    private readonly Dispatcher _dispatcher;
    private readonly Func<AlertStyle, IToastView> _createWindow;
    private readonly Action<IToastView> _positionWindow;
    private readonly ICursorFollowingToastController _cursorFollower;
    private readonly IRuntimeLogger _logger;
    private readonly Dictionary<AlertStyle, ToastChannel> _channels = [];
    private bool _disposed;

    /// <summary>
    /// How many content toasts have taken the standard channel. Lifecycle
    /// receipts such as "Config reloaded" capture this when a reload starts
    /// and yield when it moved: a newer user toast already showing must not
    /// be clobbered by older news. Hides (zero duration) and following pills
    /// use an independent channel, so neither moves this counter.
    /// Incremented on the dispatcher; safe to read from any thread.
    /// </summary>
    public long StandardContentShows => Volatile.Read(ref _standardContentShows);

    private long _standardContentShows;

    public ToastPresenter()
        : this(WpfApplication.Current.Dispatcher, NullRuntimeLogger.Instance)
    {
    }

    public ToastPresenter(IRuntimeLogger logger)
        : this(WpfApplication.Current.Dispatcher, logger)
    {
    }

    public ToastPresenter(Dispatcher dispatcher)
        : this(dispatcher, NullRuntimeLogger.Instance)
    {
    }

    private ToastPresenter(Dispatcher dispatcher, IRuntimeLogger logger)
        : this(
            dispatcher,
            static style => style is AlertStyle.Following
                ? new FollowingToastWindow()
                : new ToastWindow(),
            Position,
            new CursorFollowingToastController(),
            logger)
    {
    }

    internal ToastPresenter(
        Dispatcher dispatcher,
        Func<IToastView> createWindow,
        Action<IToastView> positionWindow)
        : this(
            dispatcher,
            _ => createWindow(),
            positionWindow,
            new CursorFollowingToastController(),
            NullRuntimeLogger.Instance)
    {
    }

    internal ToastPresenter(
        Dispatcher dispatcher,
        Func<AlertStyle, IToastView> createWindow,
        Action<IToastView> positionWindow,
        ICursorFollowingToastController cursorFollower)
        : this(dispatcher, createWindow, positionWindow, cursorFollower, NullRuntimeLogger.Instance)
    {
    }

    private ToastPresenter(
        Dispatcher dispatcher,
        Func<AlertStyle, IToastView> createWindow,
        Action<IToastView> positionWindow,
        ICursorFollowingToastController cursorFollower,
        IRuntimeLogger logger)
    {
        _dispatcher = dispatcher;
        _createWindow = createWindow;
        _positionWindow = positionWindow;
        _cursorFollower = cursorFollower;
        _logger = logger;
    }

    public void Show(AlertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_dispatcher.CheckAccess())
        {
            ShowOnDispatcher(request);
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                ShowOnDispatcher(request);
            }
        }, DispatcherPriority.Send);
    }

    public void Prewarm()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_dispatcher.CheckAccess())
        {
            PrewarmOnDispatcher();
            return;
        }

        _dispatcher.InvokeAsync(PrewarmOnDispatcher, DispatcherPriority.Send);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            DisposeOnDispatcher();
            return;
        }

        // Never block shutdown on a wedged UI thread; the windows die with the process anyway.
        DispatcherGuard.FireAndForget(_dispatcher, DisposeOnDispatcher, "toast teardown", _logger);
    }

    private void ShowOnDispatcher(AlertRequest request)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var channel = GetOrCreateChannel(request.Style);
        StopTimer(channel);

        if (request.DurationMs == 0)
        {
            HideChannel(channel);
            return;
        }

        if (request.Style is AlertStyle.Standard)
        {
            Interlocked.Increment(ref _standardContentShows);
        }

        var window = GetOrCreateWindow(channel);
        CancelExitAndReset(channel, window);

        var wasOnScreen = !IsHiddenOffscreen(window);
        if (!wasOnScreen)
        {
            MoveOffscreen(window);
        }

        var movedAt = Stopwatch.GetTimestamp();
        window.UpdateRequest(request);
        var updatedAt = Stopwatch.GetTimestamp();

        var wasVisible = window.IsVisible;
        if (!window.IsVisible)
        {
            window.Show();
        }
        var shownAt = Stopwatch.GetTimestamp();

        window.UpdateLayout();
        var layoutAt = Stopwatch.GetTimestamp();
        if (request.Style is AlertStyle.Following)
        {
            _cursorFollower.Start(window);
        }
        else
        {
            _positionWindow(window);
        }
        var positionedAt = Stopwatch.GetTimestamp();
        window.BeginEnterAnimation();
        StartTimer(channel, request.DurationMs);

        _logger.Info(
            // Toast text can hold clipboard contents or other private data, so only its length is logged.
            $"Toast show timing textLength={request.Text.Length} kind='{request.Kind}' icon='{request.EffectiveIcon}' style='{request.Style}' alreadyVisible={wasVisible} wasOnScreen={wasOnScreen} size={window.ActualWidth:F1}x{window.ActualHeight:F1} " +
            $"moveMs={ElapsedMs(startedAt, movedAt):F3} updateMs={ElapsedMs(movedAt, updatedAt):F3} " +
            $"showMs={ElapsedMs(updatedAt, shownAt):F3} layoutMs={ElapsedMs(shownAt, layoutAt):F3} " +
            $"positionMs={ElapsedMs(layoutAt, positionedAt):F3} totalMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");
    }

    private void DisposeOnDispatcher()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var channel in _channels.Values)
        {
            StopTimer(channel);
        }

        _cursorFollower.Dispose();
        foreach (var channel in _channels.Values)
        {
            channel.ExitGeneration++;
            if (channel.Window is not null)
            {
                channel.Window.CancelExitAnimation();
                channel.Window.Close();
            }

            channel.Dispose();
        }

        _channels.Clear();
        _disposed = true;
    }

    private void HideChannel(ToastChannel channel)
    {
        StopTimer(channel);
        var window = channel.Window;
        if (window is null)
        {
            return;
        }

        if (IsHiddenOffscreen(window))
        {
            CancelExitAndReset(channel, window);
            MoveOffscreen(window);
            StopFollowerIfNeeded(channel);
            return;
        }

        var generation = ++channel.ExitGeneration;
        StopFollowerIfNeeded(channel);
        window.BeginExitAnimation(() =>
        {
            if (_disposed || !ReferenceEquals(channel.Window, window) || generation != channel.ExitGeneration)
            {
                return;
            }

            MoveOffscreen(window);
            window.PrepareForShow();
        });
    }

    private static void CancelExitAndReset(ToastChannel channel, IToastView window)
    {
        channel.ExitGeneration++;
        window.CancelExitAnimation();
        window.PrepareForShow();
    }

    private static bool IsHiddenOffscreen(IToastView window) =>
        Math.Abs(window.Left - HiddenWindowCoordinate) < 0.5
        && Math.Abs(window.Top - HiddenWindowCoordinate) < 0.5;

    private static void StopTimer(ToastChannel channel)
    {
        channel.Timer.Stop();
    }

    private static void StartTimer(ToastChannel channel, int durationMs)
    {
        channel.Timer.Interval = TimeSpan.FromMilliseconds(durationMs);
        channel.Timer.Start();
    }

    private void PrewarmOnDispatcher()
    {
        if (_disposed
            || (_channels.TryGetValue(AlertStyle.Standard, out var existingChannel)
                && existingChannel.Window is not null))
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var channel = GetOrCreateChannel(AlertStyle.Standard);
        var window = GetOrCreateWindow(channel);
        var createdAt = Stopwatch.GetTimestamp();
        MoveOffscreen(window);
        window.UpdateRequest(AlertRequest.Create("Ready", AlertKind.Normal, 1));
        var updatedAt = Stopwatch.GetTimestamp();
        window.Show();
        var shownAt = Stopwatch.GetTimestamp();
        window.UpdateLayout();
        MoveOffscreen(window);
        var finishedAt = Stopwatch.GetTimestamp();

        _logger.Info(
            $"Toast prewarm timing createMs={ElapsedMs(startedAt, createdAt):F3} updateMs={ElapsedMs(createdAt, updatedAt):F3} " +
            $"showMs={ElapsedMs(updatedAt, shownAt):F3} layoutMs={ElapsedMs(shownAt, finishedAt):F3} totalMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");
    }

    private ToastChannel GetOrCreateChannel(AlertStyle style)
    {
        if (_channels.TryGetValue(style, out var channel))
        {
            return channel;
        }

        channel = new ToastChannel(
            style,
            _dispatcher,
            timerChannel =>
            {
                if (!_disposed)
                {
                    HideChannel(timerChannel);
                }
            });
        _channels.Add(style, channel);
        return channel;
    }

    private IToastView GetOrCreateWindow(ToastChannel channel)
    {
        return channel.Window ??= _createWindow(channel.Style);
    }

    private void StopFollowerIfNeeded(ToastChannel channel)
    {
        if (channel.Style is AlertStyle.Following)
        {
            _cursorFollower.Stop();
        }
    }

    private static void MoveOffscreen(IToastView window)
    {
        window.Left = HiddenWindowCoordinate;
        window.Top = HiddenWindowCoordinate;
    }

    private static void Position(IToastView window)
    {
        var cursor = Cursor.Position;
        var workingArea = Screen.FromPoint(cursor).WorkingArea;

        // The toast is parked off-screen until now, so its own DPI says nothing about the target
        // monitor; convert with the DPI of the monitor it is about to appear on.
        var scale = ScreenGeometry.GetScaleAt(cursor.X, cursor.Y);
        var left = workingArea.Left / scale;
        var width = workingArea.Width / scale;
        var bottom = workingArea.Bottom / scale;
        const double bottomPadding = 48;

        window.Left = left + ((width - window.ActualWidth) / 2);
        window.Top = bottom - window.ActualHeight - bottomPadding;
    }

    private static double ElapsedMs(long startTimestamp, long endTimestamp)
    {
        return Stopwatch.GetElapsedTime(startTimestamp, endTimestamp).TotalMilliseconds;
    }

    private sealed class ToastChannel : IDisposable
    {
        private readonly EventHandler _timerTick;

        public ToastChannel(
            AlertStyle style,
            Dispatcher dispatcher,
            Action<ToastChannel> onTimerTick)
        {
            Style = style;
            Timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            _timerTick = (_, _) => onTimerTick(this);
            Timer.Tick += _timerTick;
        }

        public AlertStyle Style { get; }

        public DispatcherTimer Timer { get; }

        public IToastView? Window { get; set; }

        public int ExitGeneration { get; set; }

        public void Dispose()
        {
            Timer.Stop();
            Timer.Tick -= _timerTick;
        }
    }
}
