using HsWin.App.Native;
using System.Runtime.InteropServices;
using HsWin.App.Hotkeys;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Tests;

public sealed class NativeMouseHotkeyHookTests
{
    [Fact]
    public void TryGetMouseButtonEventDecodesMiddleButtonDownAndUp()
    {
        Assert.True(NativeMouseHotkeyHook.TryGetMouseButtonEvent(0x0207, 0, out var down));
        Assert.Equal(HotkeyMouseButton.Middle, down.Button);
        Assert.True(down.IsDown);

        Assert.True(NativeMouseHotkeyHook.TryGetMouseButtonEvent(0x0208, 0, out var up));
        Assert.Equal(HotkeyMouseButton.Middle, up.Button);
        Assert.False(up.IsDown);
    }

    [Theory]
    [InlineData(0x0001u, HotkeyMouseButton.XButton1)]
    [InlineData(0x0002u, HotkeyMouseButton.XButton2)]
    public void TryGetMouseButtonEventDecodesXButtonDown(uint xButton, HotkeyMouseButton expectedButton)
    {
        var mouseData = xButton << 16;

        Assert.True(NativeMouseHotkeyHook.TryGetMouseButtonEvent(0x020B, mouseData, out var buttonEvent));
        Assert.Equal(expectedButton, buttonEvent.Button);
        Assert.True(buttonEvent.IsDown);
    }

    [Fact]
    public void TryGetMouseButtonEventRejectsUnknownXButton()
    {
        var mouseData = 0x0003u << 16;

        Assert.False(NativeMouseHotkeyHook.TryGetMouseButtonEvent(0x020B, mouseData, out _));
    }

    [Fact]
    public void RegisterInstallsHookOnDedicatedThread()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null);

        using var registration = hook.Register(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.Control, HotkeyMouseButton.XButton1),
            () => { });

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, platform.SetHookExCount);
        Assert.NotEqual(0u, platform.HookThreadId);
    }

    [Fact]
    public void LastRegistrationDisposeUnhooksAndStopsHookThread()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null);

        var registration = hook.Register(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.Control, HotkeyMouseButton.XButton1),
            () => { });

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        registration.Dispose();

        Assert.Equal(1, platform.UnhookCount);
        Assert.Equal(1, platform.QuitPostedCount);
    }

    [Fact]
    public void DispatchCallbackUsesCapturedSynchronizationContext()
    {
        var platform = new FakeMouseHookPlatform();
        var scheduler = new CapturingSynchronizationContext();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, scheduler);

        using var registration = hook.Register(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1),
            () => { });

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        platform.InvokeHook(0x020B, 0x0001u << 16);

        Assert.Equal(1, scheduler.PostCount);
    }

    [Fact]
    public void HeldRegistrationDispatchesPressAndReleaseCallbacks()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null);
        var pressed = 0;
        var released = 0;

        using var registration = hook.RegisterHeld(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1),
            () => pressed++,
            () => released++,
            blocking: true);

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new IntPtr(1), platform.InvokeHook(0x020B, 0x0001u << 16));
        Assert.Equal(new IntPtr(1), platform.InvokeHook(0x020C, 0x0001u << 16));
        Assert.Equal(1, pressed);
        Assert.Equal(1, released);
    }

    [Fact]
    public void NonBlockingHeldRegistrationPassesMouseEventsThrough()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null);
        var pressed = 0;
        var released = 0;

        using var registration = hook.RegisterHeld(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1),
            () => pressed++,
            () => released++,
            blocking: false);

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(IntPtr.Zero, platform.InvokeHook(0x020B, 0x0001u << 16));
        Assert.Equal(IntPtr.Zero, platform.InvokeHook(0x020C, 0x0001u << 16));
        Assert.Equal(1, pressed);
        Assert.Equal(1, released);
    }

    [Fact]
    public void ModifierMatchingUsesPhysicalKeyboardEventService()
    {
        var platform = new FakeMouseHookPlatform();
        var keyboardEvents = new StubKeyboardEventService(KeyboardKeyRules.VkControl);
        using var hook = new NativeMouseHotkeyHook(
            NullRuntimeLogger.Instance,
            platform,
            callbackContext: null,
            keyboardEvents: keyboardEvents);
        var pressed = 0;

        using var registration = hook.Register(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.Control, HotkeyMouseButton.XButton1),
            () => pressed++);

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new IntPtr(1), platform.InvokeHook(0x020B, 0x0001u << 16));
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void DisposeWhileHeldDoesNotFireStaleReleaseOnLaterButtonUp()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null);
        var pressed = 0;
        var released = 0;

        var registration = hook.RegisterHeld(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1),
            () => pressed++,
            () => released++,
            blocking: true);

        Assert.True(platform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(new IntPtr(1), platform.InvokeHook(0x020B, 0x0001u << 16));
        Assert.Equal(1, pressed);

        registration.Dispose();

        // Button-up after dispose must not invoke the disposed held release callback.
        Assert.Equal(IntPtr.Zero, platform.InvokeHook(0x020C, 0x0001u << 16));
        Assert.Equal(0, released);
    }

    [Fact]
    public void PhysicalObserversBypassUiContextAndCoexistWithHeldHotkeys()
    {
        var platform = new FakeMouseHookPlatform();
        var context = new CapturingSynchronizationContext();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, context);
        var events = new List<PhysicalMouseButtonEvent>();
        var pressed = 0;
        using var hotkey = hook.RegisterHeld(HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1),
            () => pressed++, () => { }, blocking: false);
        var observer = hook.WatchPhysicalButtons(events.Add);
        Assert.Equal(nint.Zero, platform.InvokeHook(0x020B, 1u << 16));
        Assert.Equal(1, context.PostCount); // Only the old hotkey uses UI dispatch.
        Assert.Equal(1, pressed);
        Assert.Equal(new PhysicalMouseButtonEvent(HotkeyMouseButton.XButton1, true, HotkeyModifiers.None), Assert.Single(events));
        hotkey.Dispose();
        Assert.Equal(0, platform.UnhookCount); // Native observer still owns the shared hook.
        platform.InvokeHook(0x020C, 1u << 16);
        Assert.False(events[1].IsDown);
        observer.Dispose();
        Assert.Equal(1, platform.UnhookCount);
        platform.InvokeHook(0x020B, 1u << 16);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void PhysicalObserversIgnoreInjectedInputAndUsePhysicalModifiers()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null,
            keyboardEvents: new StubKeyboardEventService(KeyboardKeyRules.VkControl));
        var events = new List<PhysicalMouseButtonEvent>();
        using var observer = hook.WatchPhysicalButtons(events.Add);
        Assert.Equal(nint.Zero, platform.InvokeHook(0x020B, 1u << 16, flags: 1));
        Assert.Empty(events);
        platform.InvokeHook(0x020B, 1u << 16);
        Assert.Equal(HotkeyModifiers.Control, Assert.Single(events).Modifiers);
        hook.Dispose();
        platform.InvokeHook(0x020C, 1u << 16);
        Assert.Single(events);
    }

    [Fact]
    public void FailedObserverDoesNotInterruptOtherObserversOrMouseDelivery()
    {
        var platform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, platform, callbackContext: null);
        using var failed = hook.WatchPhysicalButtons(_ => throw new InvalidOperationException("failed observer"));
        var observed = 0;
        using var healthy = hook.WatchPhysicalButtons(_ => observed++);
        Assert.Equal(nint.Zero, platform.InvokeHook(0x020B, 1u << 16));
        Assert.Equal(1, observed);
    }

    private sealed class StubKeyboardEventService(params uint[] downKeys) : IKeyboardEventService
    {
        private readonly HashSet<uint> _downKeys = [.. downKeys];

        public IDisposable Watch(
            KeyboardEventWatchOptions options,
            Func<KeyboardEventSnapshot, bool> callback) =>
            NullDisposable.Instance;

        public bool IsKeyDown(uint virtualKey) => _downKeys.Contains(virtualKey);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            d(state);
        }
    }
}
