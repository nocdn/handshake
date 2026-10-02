using HsWin.Core.Scripting;
using HsWin.App.Keyboard;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;

namespace HsWin.App.Tests;

public sealed class KeyboardWatchDispatcherTests
{
    [Fact]
    public void NonBlockingWatcherSchedulesCallbackAndDoesNotSwallowImmediately()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new KeyboardWatchDispatcher(logger, scheduler);
        var called = false;
        var subscription = CreateSubscription(blocking: false, _ =>
        {
            called = true;
            return true;
        });

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.False(shouldSwallow);
        Assert.False(called);
        var scheduled = Assert.Single(scheduler.Callbacks);

        scheduled();

        Assert.True(called);
        var warning = Assert.Single(logger.Warnings);
        Assert.Contains("not blocking", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void NonBlockingWatcherReturningTrueWarnsOncePerWatchNotPerKey()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new KeyboardWatchDispatcher(logger, scheduler);
        var subscription = CreateSubscription(blocking: false, _ => true);

        for (var key = 0; key < 25; key++)
        {
            dispatcher.Dispatch(CreateSnapshot(), [subscription]);
        }

        scheduler.Callbacks.ForEach(callback => callback());

        Assert.Equal(25, scheduler.Callbacks.Count);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void BlockingWatcherRunsInlineAndCanSwallow()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var called = false;
        var subscription = CreateSubscription(blocking: true, _ =>
        {
            called = true;
            return true;
        });

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.True(shouldSwallow);
        Assert.True(called);
        Assert.Empty(scheduler.Callbacks);
    }

    [Fact]
    public void BlockingWatcherReturningFalseDoesNotSwallow()
    {
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), new CapturingKeyboardScheduler());
        var subscription = CreateSubscription(blocking: true, _ => false);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.False(shouldSwallow);
    }

    [Fact]
    public void MixedWatchersCanScheduleAndSwallow()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var observed = false;
        var nonBlocking = CreateSubscription(blocking: false, _ =>
        {
            observed = true;
            return false;
        });
        var blocking = CreateSubscription(blocking: true, _ => true, id: 2);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [nonBlocking, blocking]);

        Assert.True(shouldSwallow);
        Assert.False(observed);
        Assert.Single(scheduler.Callbacks)();
        Assert.True(observed);
    }

    [Fact]
    public void SlowBlockingWatcherStillReturnsSwallowResultButLogsWarning()
    {
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new KeyboardWatchDispatcher(logger, new CapturingKeyboardScheduler());
        var subscription = CreateSubscription(blocking: true, _ =>
        {
            Thread.Sleep(20);
            return true;
        });

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.True(shouldSwallow);
        Assert.Contains(logger.Warnings, warning => warning.Contains("slow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BlockingWatcherSwallowStopsLaterWatchers()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var laterCalled = false;
        var first = CreateSubscription(blocking: true, _ => true);
        var later = CreateSubscription(blocking: true, _ =>
        {
            laterCalled = true;
            return false;
        }, id: 2);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [first, later]);

        Assert.True(shouldSwallow);
        Assert.False(laterCalled);
        Assert.Empty(scheduler.Callbacks);
    }

    [Fact]
    public void KeyFilterSkipsUnmatchedEvents()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var called = false;
        var subscription = new KeyboardWatchSubscription(
            1,
            new KeyboardEventWatchOptions(IncludeInjected: false, Blocking: false, KeyFilter: new HashSet<uint> { 0x21 }),
            _ =>
            {
                called = true;
                return false;
            });

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.False(shouldSwallow);
        Assert.False(called);
        Assert.Empty(scheduler.Callbacks);
    }

    [Fact]
    public void NonBlockingWatcherSkipsInjectedEventsByDefault()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var subscription = CreateSubscription(blocking: false, _ => throw new InvalidOperationException("Should not run."));

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(isInjected: true), [subscription]);

        Assert.False(shouldSwallow);
        Assert.Empty(scheduler.Callbacks);
    }

    [Fact]
    public void NonBlockingWatcherCanIncludeInjectedEvents()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var subscription = new KeyboardWatchSubscription(
            1,
            new KeyboardEventWatchOptions(IncludeInjected: true, Blocking: false),
            _ => false);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(isInjected: true), [subscription]);

        Assert.False(shouldSwallow);
        Assert.Single(scheduler.Callbacks);
    }

    [Fact]
    public void ScheduledNonBlockingCallbackSkipsDisposedSubscription()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var dispatcher = new KeyboardWatchDispatcher(new CapturingRuntimeLogger(), scheduler);
        var called = false;
        var subscription = CreateSubscription(blocking: false, _ =>
        {
            called = true;
            return false;
        });

        dispatcher.Dispatch(CreateSnapshot(), [subscription]);
        subscription.Dispose();
        Assert.Single(scheduler.Callbacks)();

        Assert.False(called);
    }

    [Fact]
    public void BlockingCallbackExceptionIsLoggedAndDoesNotSwallow()
    {
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new KeyboardWatchDispatcher(logger, new CapturingKeyboardScheduler());
        var subscription = CreateSubscription(blocking: true, _ => throw new InvalidOperationException("boom"));

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.False(shouldSwallow);
        Assert.Contains(logger.Errors, error => error.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public void NonBlockingCallbackExceptionIsLoggedWhenScheduledCallbackRuns()
    {
        var scheduler = new CapturingKeyboardScheduler();
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new KeyboardWatchDispatcher(logger, scheduler);
        var subscription = CreateSubscription(blocking: false, _ => throw new InvalidOperationException("boom"));

        dispatcher.Dispatch(CreateSnapshot(), [subscription]);
        Assert.Single(scheduler.Callbacks)();

        Assert.Contains(logger.Errors, error => error.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public void SchedulerExceptionIsLoggedAndDoesNotSwallow()
    {
        var logger = new CapturingRuntimeLogger();
        var dispatcher = new KeyboardWatchDispatcher(logger, new ThrowingKeyboardScheduler());
        var subscription = CreateSubscription(blocking: false, _ => false);

        var shouldSwallow = dispatcher.Dispatch(CreateSnapshot(), [subscription]);

        Assert.False(shouldSwallow);
        Assert.Contains(logger.Errors, error => error.Contains("scheduling", StringComparison.Ordinal));
    }

    private static KeyboardWatchSubscription CreateSubscription(
        bool blocking,
        Func<KeyboardEventSnapshot, bool> callback,
        long id = 1)
    {
        return new KeyboardWatchSubscription(
            id,
            new KeyboardEventWatchOptions(IncludeInjected: false, Blocking: blocking),
            callback);
    }

    private static KeyboardEventSnapshot CreateSnapshot(bool isInjected = false)
    {
        return new KeyboardEventSnapshot(
            "keydown",
            (uint)'A',
            "a",
            [],
            0,
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: false,
            IsInjected: isInjected,
            IsExtended: false);
    }

    private sealed class CapturingKeyboardScheduler : IScriptCallbackScheduler
    {
        public List<Action> Callbacks { get; } = [];

        public void Schedule(Action callback)
        {
            Callbacks.Add(callback);
        }
    }

    private sealed class ThrowingKeyboardScheduler : IScriptCallbackScheduler
    {
        public void Schedule(Action callback)
        {
            throw new InvalidOperationException("scheduling failed");
        }
    }
}
