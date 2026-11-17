using HsWin.App.Hotkeys;
using HsWin.App.Mouse;
using HsWin.Core.Hotkeys;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.App.Tests;

public sealed class MouseButtonWatchTests
{
    [Theory]
    [InlineData(0x0201, 0u, MouseButton.Left, true)]
    [InlineData(0x0202, 0u, MouseButton.Left, false)]
    [InlineData(0x0204, 0u, MouseButton.Right, true)]
    [InlineData(0x0205, 0u, MouseButton.Right, false)]
    [InlineData(0x0207, 0u, MouseButton.Middle, true)]
    [InlineData(0x0208, 0u, MouseButton.Middle, false)]
    [InlineData(0x020B, 0x10000u, MouseButton.XButton1, true)]
    [InlineData(0x020C, 0x10000u, MouseButton.XButton1, false)]
    [InlineData(0x020B, 0x20000u, MouseButton.XButton2, true)]
    [InlineData(0x020C, 0x20000u, MouseButton.XButton2, false)]
    public void DecoderIncludesAllFiveButtonsAndCoordinates(int message, uint data, MouseButton button, bool down)
    {
        Assert.True(MouseButtonEventDecoder.TryDecode(message, data, 1, -20, 30, 3, out var input));
        Assert.Equal(new(button, down, true, 3, -20, 30), input);
    }

    [Theory]
    [InlineData(0x0200, 0u)]
    [InlineData(0x020A, 0x780000u)]
    [InlineData(0x020B, 0x30000u)]
    public void DecoderIgnoresMovementWheelAndUnknownButtons(int message, uint data) =>
        Assert.False(MouseButtonEventDecoder.TryDecode(message, data, 0, 0, 0, 0, out _));

    [Theory]
    [InlineData(0x0201, 0x0202, 0u)]
    [InlineData(0x0204, 0x0205, 0u)]
    [InlineData(0x0207, 0x0208, 0u)]
    [InlineData(0x020B, 0x020C, 0x10000u)]
    [InlineData(0x020B, 0x020C, 0x20000u)]
    public void StoppingFromPressCallbackStillSwallowsReleaseAndThenUnhooks(int down, int up, uint data)
    {
        var platform = new FakeMouseHookPlatform();
        var scheduler = new QueueScheduler();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, null, buttonScheduler: scheduler);
        IDisposable? watch = null;
        var callbacks = 0;
        watch = hook.WatchButtons(new(Blocking: true), input => { callbacks++; watch!.Dispose(); });
        Assert.Equal((nint)1, platform.InvokeHook(down, data));
        Assert.Equal(0, callbacks); // Decision made natively, without waiting for JavaScript.
        scheduler.Run();
        Assert.Equal(1, callbacks);
        Assert.Equal(0, platform.UnhookCount);
        Assert.Equal((nint)1, platform.InvokeHook(up, data));
        Assert.Equal(1, platform.UnhookCount);
        Assert.Equal(1, callbacks);
    }

    [Fact]
    public void WatchEnabledDuringDragPreservesOriginalRelease()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        Assert.False(dispatcher.Dispatch(Input(true)));
        using var watch = dispatcher.Watch(new(Blocking: true), _ => { });
        Assert.False(dispatcher.Dispatch(Input(false)));
        Assert.True(dispatcher.Dispatch(Input(true)));
        Assert.True(dispatcher.Dispatch(Input(false)));
    }

    [Fact]
    public void UnmatchedReleasePassesThrough()
    {
        var dispatcher = Create(new QueueScheduler());
        using var watch = dispatcher.Watch(new(Blocking: true), _ => { });
        Assert.False(dispatcher.Dispatch(Input(false)));
    }

    [Fact]
    public void InjectedAndPhysicalPressesHaveIndependentState()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = new List<MouseButtonEventSnapshot>();
        using var watch = dispatcher.Watch(new(Blocking: true), seen.Add);
        Assert.False(dispatcher.Dispatch(Input(true, injected: true)));
        Assert.True(dispatcher.Dispatch(Input(true)));
        Assert.False(dispatcher.Dispatch(Input(false, injected: true)));
        Assert.True(dispatcher.Dispatch(Input(false)));
        scheduler.Run();
        Assert.Equal(2, seen.Count);
        Assert.All(seen, input => Assert.False(input.IsInjected));
    }

    [Fact]
    public void IncludeInjectedCanSwallowSyntheticClicks()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = new List<MouseButtonEventSnapshot>();
        using var watch = dispatcher.Watch(new(IncludeInjected: true, Blocking: true), seen.Add);
        Assert.True(dispatcher.Dispatch(Input(true, injected: true)));
        Assert.True(dispatcher.Dispatch(Input(false, injected: true)));
        scheduler.Run();
        Assert.Equal(2, seen.Count);
        Assert.All(seen, input => Assert.True(input.IsInjected));
    }

    [Fact]
    public void NonBlockingObserversPreserveDeliveryAndEventOrder()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = new List<MouseButtonEventSnapshot>();
        using var watch = dispatcher.Watch(new(), seen.Add);
        Assert.False(dispatcher.Dispatch(Input(true)));
        Assert.False(dispatcher.Dispatch(Input(false)));
        Assert.Empty(seen);
        Assert.Single(scheduler.Pending);
        scheduler.Run();
        Assert.Collection(seen, input => Assert.True(input.IsDown), input => Assert.False(input.IsDown));
    }

    [Fact]
    public void BlockingWatcherPrecedesMouseHotkeysAndPhysicalObserversOnSharedHook()
    {
        var platform = new FakeMouseHookPlatform();
        var scheduler = new QueueScheduler();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, null, buttonScheduler: scheduler);
        var hotkeys = 0;
        var observed = 0;
        using var hotkey = hook.Register(HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1), () => hotkeys++);
        using var observer = hook.WatchPhysicalButtons(_ => observed++);
        using var watch = hook.WatchButtons(new(Blocking: true), _ => { });
        Assert.Equal(1, platform.SetHookExCount);
        Assert.Equal((nint)1, platform.InvokeHook(0x020B, 0x10000));
        Assert.Equal((nint)1, platform.InvokeHook(0x020C, 0x10000));
        Assert.Equal(0, hotkeys);
        Assert.Equal(0, observed);
    }

    [Fact]
    public void PassedHotkeyPressGetsItsReleaseWhenWatchIsEnabledMidPress()
    {
        var platform = new FakeMouseHookPlatform();
        var scheduler = new QueueScheduler();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, null, buttonScheduler: scheduler);
        var releases = 0;
        using var hotkey = hook.RegisterHeld(HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1), () => { }, () => releases++, blocking: false);
        Assert.Equal((nint)0, platform.InvokeHook(0x020B, 0x10000));
        using var watch = hook.WatchButtons(new(Blocking: true), _ => { });
        Assert.Equal((nint)0, platform.InvokeHook(0x020C, 0x10000));
        Assert.Equal(1, releases);
    }

    [Fact]
    public void DisposingCancelsQueuedCallbacksButOtherWatchersContinue()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var cancelled = 0;
        var live = 0;
        var first = dispatcher.Watch(new(), _ => cancelled++);
        using var second = dispatcher.Watch(new(), _ => live++);
        dispatcher.Dispatch(Input(true));
        first.Dispose(); first.Dispose();
        scheduler.Run();
        Assert.Equal(0, cancelled);
        Assert.Equal(1, live);
    }

    [Fact]
    public void CallbackFailureDoesNotInterruptOtherEvents()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = 0;
        using var watch = dispatcher.Watch(new(), _ => { seen++; throw new InvalidOperationException("test"); });
        dispatcher.Dispatch(Input(true)); dispatcher.Dispatch(Input(false));
        scheduler.Run();
        Assert.Equal(2, seen);
    }

    [Fact]
    public void BacklogIsBoundedWithoutChangingNativeSwallowing()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = 0;
        using var watch = dispatcher.Watch(new(Blocking: true), _ => seen++);
        for (var index = 0; index < 1000; index++) Assert.True(dispatcher.Dispatch(Input(index % 2 == 0)));
        Assert.Single(scheduler.Pending);
        scheduler.Run();
        Assert.Equal(MouseButtonWatchSubscription.MaxPending, seen);
    }

    [Fact]
    public void BusyObserversYieldBetweenCallbackBatches()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = 0;
        using var watch = dispatcher.Watch(new(), _ => seen++);
        for (var index = 0; index < 40; index++) dispatcher.Dispatch(Input(index % 2 == 0));
        var firstBatch = Assert.Single(scheduler.Pending);
        scheduler.Pending.Clear();
        firstBatch();
        Assert.Equal(16, seen);
        Assert.Single(scheduler.Pending);
        scheduler.Run();
        Assert.Equal(40, seen);
    }

    [Fact]
    public void ClearCancelsCallbacksAndAllPendingReleaseState()
    {
        var scheduler = new QueueScheduler();
        var dispatcher = Create(scheduler);
        var seen = 0;
        using var watch = dispatcher.Watch(new(Blocking: true), _ => seen++);
        dispatcher.Dispatch(Input(true));
        dispatcher.Clear();
        scheduler.Run();
        Assert.False(dispatcher.NeedsHook);
        Assert.False(dispatcher.Dispatch(Input(false)));
        Assert.Equal(0, seen);
    }

    [Fact]
    public void SchedulingFailureDoesNotLoseNativeSwallowingOrFutureDelivery()
    {
        var scheduler = new QueueScheduler { Fail = true };
        var dispatcher = Create(scheduler);
        var seen = 0;
        using var watch = dispatcher.Watch(new(Blocking: true), _ => seen++);
        Assert.True(dispatcher.Dispatch(Input(true)));
        scheduler.Fail = false;
        Assert.True(dispatcher.Dispatch(Input(false)));
        scheduler.Run();
        Assert.Equal(1, seen);
    }

    private static MouseButtonEventSnapshot Input(bool down, bool injected = false) => new(MouseButton.Left, down, injected, 0, 10, 20);
    private static MouseButtonWatchDispatcher Create(QueueScheduler scheduler) => new(NullRuntimeLogger.Instance, scheduler, () => { });

    private sealed class QueueScheduler : IScriptCallbackScheduler
    {
        public List<Action> Pending { get; } = [];
        public bool Fail { get; set; }
        public void Schedule(Action callback)
        {
            if (Fail) throw new InvalidOperationException("test");
            Pending.Add(callback);
        }
        public void Run()
        {
            while (Pending.Count != 0) { var callback = Pending[0]; Pending.RemoveAt(0); callback(); }
        }
    }
}
