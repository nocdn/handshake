using HsWin.Core.Alerts;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

namespace HsWin.App.Tests;

public sealed class ToastPresenterTests
{
    [Fact]
    public void ShowDoesNotCreateWindowForZeroDurationRequest()
    {
        var createdCount = 0;
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () =>
            {
                createdCount++;
                return new FakeToastView();
            },
            _ => { });

        presenter.Show(AlertRequest.Create("Hidden", AlertKind.Normal, 0));

        Assert.Equal(0, createdCount);
    }

    [Fact]
    public void ShowReusesWarmWindowWithoutHiding()
    {
        var windows = new List<FakeToastView>();
        var positionCount = 0;
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () =>
            {
                var window = new FakeToastView();
                windows.Add(window);
                return window;
            },
            _ => positionCount++);

        var first = AlertRequest.Create("First", AlertKind.Normal, 1000);
        var second = AlertRequest.Create("Second", AlertKind.Success, 1000);

        presenter.Show(first);
        presenter.Show(AlertRequest.Create("Hide", AlertKind.Normal, 0));
        presenter.Show(second);

        var window = Assert.Single(windows);
        Assert.Equal(1, window.ShowCount);
        Assert.Equal(0, window.HideCount);
        Assert.Equal(0, window.CloseCount);
        Assert.True(window.IsVisible);
        Assert.Equal(2, positionCount);
        Assert.Collection(
            window.Requests,
            request => Assert.Equal(first, request),
            request => Assert.Equal(second, request));
    }

    [Fact]
    public void PrewarmShowsWindowOffscreenAndNextToastReusesIt()
    {
        FakeToastView? window = null;
        var positionCount = 0;
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () => window = new FakeToastView(),
            _ => positionCount++);

        presenter.Prewarm();

        Assert.NotNull(window);
        Assert.Equal(1, window.ShowCount);
        Assert.True(window.IsVisible);
        Assert.True(window.Left < -1000);
        Assert.True(window.Top < -1000);
        Assert.Equal(0, positionCount);

        presenter.Show(AlertRequest.Create("Visible", AlertKind.Success, 1000));

        Assert.Equal(1, window.ShowCount);
        Assert.Equal(1, positionCount);
        Assert.Equal(2, window.Requests.Count);
    }

    [Fact]
    public void ShowUpdatesVisibleWindowWithoutRecreatingIt()
    {
        var windows = new List<FakeToastView>();
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () =>
            {
                var window = new FakeToastView();
                windows.Add(window);
                return window;
            },
            _ => { });

        presenter.Show(AlertRequest.Create("First", AlertKind.Normal, 1000));
        presenter.Show(AlertRequest.Create("Second", AlertKind.Success, 1000));

        var window = Assert.Single(windows);
        Assert.Equal(1, window.ShowCount);
        Assert.Equal(0, window.HideCount);
        Assert.Equal(0, window.CloseCount);
        Assert.Equal(2, window.Requests.Count);
    }

    [Fact]
    public void HideAnimatesToastPositionedOnNegativeMonitor()
    {
        FakeToastView? window = null;
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () => window = new FakeToastView(),
            view =>
            {
                view.Left = -1600;
                view.Top = 900;
            });

        presenter.Show(AlertRequest.Create("Visible", AlertKind.Normal, 1000));
        presenter.Show(AlertRequest.Create("Hide", AlertKind.Normal, 0));

        Assert.NotNull(window);
        Assert.Equal(1, window.BeginExitAnimationCount);
    }

    [Fact]
    public void DisposeClosesCachedWindow()
    {
        FakeToastView? window = null;
        var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () => window = new FakeToastView(),
            _ => { });

        presenter.Show(AlertRequest.Create("Visible", AlertKind.Normal, 1000));
        presenter.Dispose();

        Assert.NotNull(window);
        Assert.Equal(1, window.CloseCount);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public void FollowingStyleUsesDedicatedWindowAndCursorFollower()
    {
        var windows = new Dictionary<AlertStyle, FakeToastView>();
        var follower = new FakeCursorFollowingToastController();
        var positionCount = 0;
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            style => windows[style] = new FakeToastView(),
            _ => positionCount++,
            follower);

        presenter.Show(AlertRequest.Create(
            "Testing",
            AlertKind.Success,
            6000,
            style: AlertStyle.Following));

        var window = Assert.IsType<FakeToastView>(windows[AlertStyle.Following]);
        Assert.Same(window, follower.ActiveView);
        Assert.Equal(1, follower.StartCount);
        Assert.Equal(0, positionCount);
        Assert.Equal(1, window.BeginEnterAnimationCount);
        Assert.Equal(AlertIcon.None, Assert.Single(window.Requests).EffectiveIcon);
    }

    [Fact]
    public void StandardAndFollowingStylesRemainVisibleIndependentlyAndReuseTheirWindows()
    {
        var windows = new Dictionary<AlertStyle, FakeToastView>();
        var follower = new FakeCursorFollowingToastController();
        var positionCount = 0;
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            style => windows[style] = new FakeToastView(),
            _ => positionCount++,
            follower);

        presenter.Show(AlertRequest.Create("Follow", durationMs: 1000, style: AlertStyle.Following));
        presenter.Show(AlertRequest.Create("Standard", durationMs: 1000));
        presenter.Show(AlertRequest.Create("Follow again", durationMs: 1000, style: AlertStyle.Following));

        Assert.Equal(2, windows.Count);
        Assert.Equal(2, follower.StartCount);
        Assert.Equal(0, follower.StopCount);
        Assert.Equal(1, positionCount);
        Assert.True(windows[AlertStyle.Standard].IsVisible);
        Assert.True(windows[AlertStyle.Following].IsVisible);
        Assert.True(windows[AlertStyle.Following].Left > -1000);
        Assert.Same(windows[AlertStyle.Following], follower.ActiveView);
    }

    [Fact]
    public void HidingStandardStyleDoesNotDismissFollowingStyle()
    {
        var windows = new Dictionary<AlertStyle, FakeToastView>();
        var follower = new FakeCursorFollowingToastController();
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            style => windows[style] = new FakeToastView(),
            _ => { },
            follower);

        presenter.Show(AlertRequest.Create("Follow", durationMs: 6000, style: AlertStyle.Following));
        presenter.Show(AlertRequest.Create("Standard", durationMs: 1000));
        presenter.Show(AlertRequest.Create("Hide standard", durationMs: 0));

        Assert.True(windows[AlertStyle.Following].IsVisible);
        Assert.Equal(0, windows[AlertStyle.Following].BeginExitAnimationCount);
        Assert.Equal(1, windows[AlertStyle.Standard].BeginExitAnimationCount);
        Assert.Same(windows[AlertStyle.Following], follower.ActiveView);
        Assert.Equal(0, follower.StopCount);
    }

    [Fact]
    public void HidingFollowingStyleStopsTrackingAsFadeOutBegins()
    {
        var windows = new Dictionary<AlertStyle, FakeToastView>();
        var follower = new FakeCursorFollowingToastController();
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            style => windows[style] = new FakeToastView(),
            _ => { },
            follower);

        presenter.Show(AlertRequest.Create("Follow", durationMs: 6000, style: AlertStyle.Following));
        presenter.Show(AlertRequest.Create("Hide follow", durationMs: 0, style: AlertStyle.Following));

        Assert.Equal(1, windows[AlertStyle.Following].BeginExitAnimationCount);
        Assert.Equal(1, follower.StopCount);
        Assert.Null(follower.ActiveView);
    }

    [Fact]
    public void StandardAndFollowingStylesUseIndependentDismissalTimers()
    {
        RunOnStaThread(() =>
        {
            var windows = new Dictionary<AlertStyle, FakeToastView>();
            var follower = new FakeCursorFollowingToastController();
            using var presenter = new ToastPresenter(
                Dispatcher.CurrentDispatcher,
                style => windows[style] = new FakeToastView(),
                _ => { },
                follower);

            presenter.Show(AlertRequest.Create("Follow", durationMs: 140, style: AlertStyle.Following));
            presenter.Show(AlertRequest.Create("Standard", durationMs: 20));
            PumpFor(TimeSpan.FromMilliseconds(70));

            Assert.Equal(1, windows[AlertStyle.Standard].BeginExitAnimationCount);
            Assert.Equal(0, windows[AlertStyle.Following].BeginExitAnimationCount);

            PumpFor(TimeSpan.FromMilliseconds(110));

            Assert.Equal(1, windows[AlertStyle.Following].BeginExitAnimationCount);
        });
    }

    [Fact]
    public void StandardContentShowsStartsAtZero()
    {
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () => new FakeToastView(),
            _ => { });

        Assert.Equal(0, presenter.StandardContentShows);
    }

    [Fact]
    public void StandardContentShowsCountsContentToasts()
    {
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () => new FakeToastView(),
            _ => { });

        presenter.Show(AlertRequest.Create("First", AlertKind.Normal, 1000));
        Assert.Equal(1, presenter.StandardContentShows);

        presenter.Show(AlertRequest.Create("Second", AlertKind.Success, 1000));
        Assert.Equal(2, presenter.StandardContentShows);
    }

    [Fact]
    public void StandardContentShowsIgnoresHides()
    {
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            () => new FakeToastView(),
            _ => { });

        presenter.Show(AlertRequest.Create("Visible", AlertKind.Normal, 1000));
        presenter.Show(AlertRequest.Create("Hide", AlertKind.Normal, 0));

        Assert.Equal(1, presenter.StandardContentShows);
    }

    [Fact]
    public void StandardContentShowsIgnoresFollowingStyle()
    {
        var follower = new FakeCursorFollowingToastController();
        using var presenter = new ToastPresenter(
            Dispatcher.CurrentDispatcher,
            _ => new FakeToastView(),
            _ => { },
            follower);

        presenter.Show(AlertRequest.Create("Follow", durationMs: 6000, style: AlertStyle.Following));

        Assert.Equal(0, presenter.StandardContentShows);
    }

    private sealed class FakeToastView : IToastView
    {
        public List<AlertRequest> Requests { get; } = [];

        public int CloseCount { get; private set; }

        public int HideCount { get; private set; }

        public int ShowCount { get; private set; }

        public double ActualHeight { get; set; } = 40;

        public double ActualWidth { get; set; } = 120;

        public bool IsVisible { get; private set; }

        public double Left { get; set; }

        public double Top { get; set; }

        public Visual PlacementVisual => throw new NotSupportedException();

        public int BeginEnterAnimationCount { get; private set; }

        public void BeginEnterAnimation()
        {
            BeginEnterAnimationCount++;
        }

        public void Close()
        {
            IsVisible = false;
            CloseCount++;
        }

        public void Hide()
        {
            IsVisible = false;
            HideCount++;
        }

        public void Show()
        {
            IsVisible = true;
            ShowCount++;
        }

        public void UpdateLayout()
        {
        }

        public void UpdateRequest(AlertRequest request)
        {
            Requests.Add(request);
        }

        public int BeginExitAnimationCount { get; private set; }

        public void BeginExitAnimation(Action onComplete)
        {
            BeginExitAnimationCount++;
            onComplete();
        }

        public void CancelExitAnimation()
        {
        }

        public void PrepareForShow()
        {
        }
    }

    private sealed class FakeCursorFollowingToastController : ICursorFollowingToastController
    {
        public IToastView? ActiveView { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start(IToastView view)
        {
            ActiveView = view;
            StartCount++;
        }

        public void Stop()
        {
            ActiveView = null;
            StopCount++;
        }

        public void Dispose() => Stop();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                thrown = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (thrown is not null)
        {
            throw thrown;
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
