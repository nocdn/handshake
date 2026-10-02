using HsWin.Core.Scripting;
using HsWin.App.Mouse;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Tests;

public sealed class MouseScrollWatchDispatcherTests
{
    [Fact]
    public void NonBlockingWatcherSchedulesCallbackAndDoesNotSwallow()
    {
        var scheduler = new CapturingScheduler();
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new MouseScrollWatchDispatcher(logger, scheduler);
        var called = false;
        var subscription = CreateSubscription(blocking: false, _ =>
        {
            called = true;
        });

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.False(shouldSwallow);
        Assert.False(called);
        var scheduled = Assert.Single(scheduler.Callbacks);

        scheduled();

        Assert.True(called);
        Assert.Empty(logger.Warnings);
    }

    [Fact]
    public void PreventDefaultSwallowsNativelyWithoutRunningCallbackOnHookPath()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var called = false;
        var subscription = CreateSubscription(blocking: true, _ =>
        {
            called = true;
        });

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.True(shouldSwallow);
        Assert.False(called);
        var scheduled = Assert.Single(scheduler.Callbacks);

        scheduled();

        Assert.True(called);
    }

    [Fact]
    public void ABacklogBeyondTheLimitIsDroppedWithASingleWarning()
    {
        var scheduler = new CapturingScheduler();
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new MouseScrollWatchDispatcher(logger, scheduler);
        var delivered = 0;
        var subscription = CreateSubscription(blocking: false, _ => delivered++);

        for (var index = 0; index < MouseScrollWatchSubscription.MaxPendingCallbacks + 50; index++)
        {
            dispatcher.Dispatch(CreateSnapshot(), [subscription]);
        }

        Assert.Equal(MouseScrollWatchSubscription.MaxPendingCallbacks, scheduler.Callbacks.Count);
        Assert.Single(logger.Warnings);
        Assert.Contains("falling behind", logger.Warnings[0], StringComparison.Ordinal);

        foreach (var callback in scheduler.Callbacks.ToArray())
        {
            callback();
        }

        Assert.Equal(MouseScrollWatchSubscription.MaxPendingCallbacks, delivered);
    }

    [Fact]
    public void DeliveryResumesOnceTheBacklogDrains()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var delivered = 0;
        var subscription = CreateSubscription(blocking: false, _ => delivered++);
        for (var index = 0; index < MouseScrollWatchSubscription.MaxPendingCallbacks; index++)
        {
            dispatcher.Dispatch(CreateSnapshot(), [subscription]);
        }

        scheduler.Callbacks[0]();
        dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.Equal(MouseScrollWatchSubscription.MaxPendingCallbacks + 1, scheduler.Callbacks.Count);
    }

    [Fact]
    public void AFailedSchedulingReleasesItsSlot()
    {
        var scheduler = new CapturingScheduler { Failure = new InvalidOperationException("scheduler gone") };
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new MouseScrollWatchDispatcher(logger, scheduler);
        var subscription = CreateSubscription(blocking: false, _ => { });

        for (var index = 0; index < MouseScrollWatchSubscription.MaxPendingCallbacks * 2; index++)
        {
            dispatcher.Dispatch(CreateSnapshot(), [subscription]);
        }

        Assert.Empty(logger.Warnings);
        Assert.Equal(MouseScrollWatchSubscription.MaxPendingCallbacks * 2, logger.Errors.Count);
    }

    [Fact]
    public void SkipsAxesOutsideWatcherFilter()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var subscription = CreateSubscription(
            blocking: true,
            _ => { },
            axes: MouseScrollAxis.Horizontal);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(vertical: true), [subscription]);

        Assert.False(shouldSwallow);
        Assert.Empty(scheduler.Callbacks);
    }

    [Fact]
    public void SkipsInjectedEventsUnlessRequested()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var subscription = CreateSubscription(blocking: true, _ => { }, includeInjected: false);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(isInjected: true), [subscription]);

        Assert.False(shouldSwallow);
    }

    [Fact]
    public void MultiplePreventDefaultWatchersAllScheduleAndStillSwallowOnce()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var first = CreateSubscription(blocking: true, _ => { }, id: 1);
        var second = CreateSubscription(blocking: true, _ => { }, id: 2);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [first, second]);

        Assert.True(shouldSwallow);
        Assert.Equal(2, scheduler.Callbacks.Count);
    }

    [Fact]
    public void NonBlockingAndPreventDefaultTogetherStillSwallowAndScheduleBoth()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var nonBlocking = CreateSubscription(blocking: false, _ => { }, id: 1);
        var blocking = CreateSubscription(blocking: true, _ => { }, id: 2);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [nonBlocking, blocking]);

        Assert.True(shouldSwallow);
        Assert.Equal(2, scheduler.Callbacks.Count);
    }

    [Fact]
    public void IncludeInjectedDeliversInjectedEvents()
    {
        var scheduler = new CapturingScheduler();
        var dispatcher = new MouseScrollWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var called = false;
        var subscription = CreateSubscription(blocking: true, _ =>
        {
            called = true;
        }, includeInjected: true);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(isInjected: true), [subscription]);

        Assert.True(shouldSwallow);
        Assert.False(called);
        var scheduled = Assert.Single(scheduler.Callbacks);
        scheduled();
        Assert.True(called);
    }

    private static MouseScrollWatchSubscription CreateSubscription(
        bool blocking,
        Action<MouseScrollEventSnapshot> callback,
        MouseScrollAxis axes = MouseScrollAxis.Both,
        bool includeInjected = false,
        long id = 1)
    {
        return new MouseScrollWatchSubscription(
            id,
            new MouseScrollWatchOptions(includeInjected, blocking, axes),
            callback);
    }

    private static MouseScrollEventSnapshot CreateSnapshot(bool vertical = true, bool isInjected = false)
    {
        return new MouseScrollEventSnapshot(
            MouseScrollEventSnapshot.ScrollType,
            vertical ? MouseScrollEventSnapshot.VerticalAxis : MouseScrollEventSnapshot.HorizontalAxis,
            vertical ? MouseScrollEventSnapshot.DirectionUp : MouseScrollEventSnapshot.DirectionRight,
            Delta: 120,
            Notches: 1,
            IsVertical: vertical,
            IsHorizontal: !vertical,
            IsInjected: isInjected,
            Modifiers: [],
            ModifierFlags: 0,
            X: 1,
            Y: 2);
    }

    private sealed class CapturingScheduler : IScriptCallbackScheduler
    {
        public List<Action> Callbacks { get; } = [];

        public Exception? Failure { get; set; }

        public void Schedule(Action callback)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Callbacks.Add(callback);
        }
    }
}
