using HsWin.App.Hotkeys;
using HsWin.Core.Hotkeys;
using HsWin.Core.Logging;

namespace HsWin.App.Tests;

public sealed class NativeHotkeyServiceTests
{
    [Fact]
    public void RegisterUsesThreadInvokerWhenCalledOffOwnerThread()
    {
        var invoker = new CapturingThreadInvoker { HasAccess = false };
        var platform = new CapturingHotkeyPlatform();
        using var service = new NativeHotkeyService(NullRuntimeLogger.Instance, invoker, platform);

        using var registration = service.Register(
            HotkeyDefinition.CreateKeyboard(HotkeyModifiers.None, 0xC0),
            () => { });

        Assert.Equal(1, invoker.FuncInvokeCount);
        var registered = Assert.Single(platform.RegisteredHotkeys);
        Assert.NotEqual(IntPtr.Zero, registered.WindowHandle);
        Assert.Equal(0x4000u, registered.Modifiers);
        Assert.Equal(0xC0u, registered.VirtualKey);
    }

    [Fact]
    public void RegistrationDisposeUsesThreadInvokerWhenCalledOffOwnerThread()
    {
        var invoker = new CapturingThreadInvoker { HasAccess = true };
        var platform = new CapturingHotkeyPlatform();
        using var service = new NativeHotkeyService(NullRuntimeLogger.Instance, invoker, platform);
        var registration = service.Register(
            HotkeyDefinition.CreateKeyboard(HotkeyModifiers.None, 0xC0),
            () => { });

        invoker.HasAccess = false;
        registration.Dispose();

        Assert.Equal(1, invoker.BeginInvokeCount);
        var unregistered = Assert.Single(platform.UnregisteredHotkeys);
        Assert.Equal(1, unregistered.Id);
    }

    [Fact]
    public void RegistrationDisposeReturnsWithoutWaitingForOwnerThread()
    {
        var invoker = new CapturingThreadInvoker { HasAccess = true };
        var platform = new CapturingHotkeyPlatform();
        using var service = new NativeHotkeyService(NullRuntimeLogger.Instance, invoker, platform);
        var registration = service.Register(
            HotkeyDefinition.CreateKeyboard(HotkeyModifiers.None, 0xC0),
            () => { });

        // The invoker defers teardown indefinitely (wedged owner thread): dispose
        // must still return and the script callback must already be gone.
        invoker.HasAccess = false;
        invoker.DeferBeginInvoke = true;
        registration.Dispose();

        Assert.Equal(1, invoker.BeginInvokeCount);
        Assert.Empty(platform.UnregisteredHotkeys);
    }

    [Fact]
    public void ReloadsReuseFreedIdsInsteadOfExhaustingTheRange()
    {
        const int idRangeSize = 0xBFFF;
        var invoker = new CapturingThreadInvoker { HasAccess = true };
        var platform = new CapturingHotkeyPlatform();
        using var service = new NativeHotkeyService(NullRuntimeLogger.Instance, invoker, platform);
        var hotkey = HotkeyDefinition.CreateKeyboard(HotkeyModifiers.None, 0xC0);

        for (var registration = 0; registration < idRangeSize + 3; registration++)
        {
            service.Register(hotkey, () => { }).Dispose();
        }

        Assert.Equal([1, 2, 3], platform.RegisteredHotkeys.Skip(idRangeSize).Select(item => item.Id));
    }

    [Fact]
    public void IdsStillRegisteredWithWindowsAreSkipped()
    {
        const int idRangeSize = 0xBFFF;
        var invoker = new CapturingThreadInvoker { HasAccess = true };
        var platform = new CapturingHotkeyPlatform();
        using var service = new NativeHotkeyService(NullRuntimeLogger.Instance, invoker, platform);
        var hotkey = HotkeyDefinition.CreateKeyboard(HotkeyModifiers.None, 0xC0);
        using var kept = service.Register(hotkey, () => { });

        // ID 2's native unregister is still queued on a busy owner thread.
        var pending = service.Register(hotkey, () => { });
        invoker.HasAccess = false;
        invoker.DeferBeginInvoke = true;
        pending.Dispose();
        invoker.HasAccess = true;
        invoker.DeferBeginInvoke = false;

        for (var registration = 2; registration < idRangeSize; registration++)
        {
            service.Register(hotkey, () => { }).Dispose();
        }

        using var wrapped = service.Register(hotkey, () => { });

        Assert.Equal(3, platform.RegisteredHotkeys[^1].Id);
    }

    [Fact]
    public void HotkeyPressesAreNotLogged()
    {
        var logger = new CapturingRuntimeLogger();
        var invoker = new CapturingThreadInvoker { HasAccess = true };
        using var service = new NativeHotkeyService(logger, invoker, new CapturingHotkeyPlatform());
        var presses = 0;
        using var registration = service.Register(HotkeyDefinition.CreateKeyboard(HotkeyModifiers.None, 0xC0), () => presses++);
        var linesAfterRegistration = logger.All.Count;

        service.DispatchHotkey(1);
        service.DispatchHotkey(1);

        Assert.Equal(2, presses);
        Assert.Equal(linesAfterRegistration, logger.All.Count);
    }

    [Fact]
    public void RegistrationFailureMessageOnlyReportsAlreadyInUseForHotkeyCollision()
    {
        var hotkey = HotkeyDefinition.CreateKeyboard(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'R');

        Assert.Equal(
            "Hotkey already in use: Alt, Control+R.",
            NativeHotkeyService.CreateRegistrationFailureMessage(1409, hotkey));
        Assert.Equal(
            "Could not register hotkey Alt, Control+R.",
            NativeHotkeyService.CreateRegistrationFailureMessage(1408, hotkey));
    }

    [Fact]
    public void DisposingTheServiceLeavesAnInjectedMouseHookRunning()
    {
        var mousePlatform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, mousePlatform, callbackContext: null);
        using var buttons = hook.WatchPhysicalButtons(_ => { });
        Assert.True(mousePlatform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        var service = new NativeHotkeyService(
            NullRuntimeLogger.Instance,
            new CapturingThreadInvoker { HasAccess = true },
            new CapturingHotkeyPlatform(),
            hook);

        service.Dispose();

        Assert.Equal(0, mousePlatform.UnhookCount);
        Assert.Equal(0, mousePlatform.QuitPostedCount);
    }

    [Fact]
    public void MouseButtonHotkeysRegisterOnTheInjectedMouseHook()
    {
        var mousePlatform = new FakeMouseHookPlatform();
        using var hook = new NativeMouseHotkeyHook(NullRuntimeLogger.Instance, mousePlatform, callbackContext: null);
        using var service = new NativeHotkeyService(
            NullRuntimeLogger.Instance,
            new CapturingThreadInvoker { HasAccess = true },
            new CapturingHotkeyPlatform(),
            hook);
        var presses = 0;

        using var registration = service.Register(
            HotkeyDefinition.CreateMouseButton(HotkeyModifiers.None, HotkeyMouseButton.XButton1),
            () => presses++);

        Assert.True(mousePlatform.InstallCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, mousePlatform.SetHookExCount);
    }

    [Fact]
    public void ModifierFlagsPassedToWindowsIncludeNoRepeatWithoutLeakingIntoTheDefinition()
    {
        var platform = new CapturingHotkeyPlatform();
        using var service = new NativeHotkeyService(
            NullRuntimeLogger.Instance,
            new CapturingThreadInvoker { HasAccess = true },
            platform);
        var definition = HotkeyDefinition.CreateKeyboard(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x52);

        using var registration = service.Register(definition, () => { });

        var registered = Assert.Single(platform.RegisteredHotkeys);
        Assert.Equal(0x4003u, registered.Modifiers);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, definition.Modifiers);
    }

    private sealed class CapturingThreadInvoker : NativeHotkeyService.IHotkeyThreadInvoker
    {
        public bool HasAccess { get; set; }

        public bool DeferBeginInvoke { get; set; }

        public int FuncInvokeCount { get; private set; }

        public int ActionInvokeCount { get; private set; }

        public int BeginInvokeCount { get; private set; }

        public bool CheckAccess()
        {
            return HasAccess;
        }

        public T Invoke<T>(Func<T> callback)
        {
            FuncInvokeCount++;
            return callback();
        }

        public void Invoke(Action callback)
        {
            ActionInvokeCount++;
            callback();
        }

        public void BeginInvoke(Action callback)
        {
            BeginInvokeCount++;
            if (!DeferBeginInvoke)
            {
                callback();
            }
        }
    }

    private sealed class CapturingHotkeyPlatform : NativeHotkeyService.IHotkeyPlatform
    {
        public List<RegisteredHotkey> RegisteredHotkeys { get; } = [];

        public List<UnregisteredHotkey> UnregisteredHotkeys { get; } = [];

        public bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int errorCode)
        {
            RegisteredHotkeys.Add(new RegisteredHotkey(windowHandle, id, modifiers, virtualKey));
            errorCode = 0;
            return true;
        }

        public bool UnregisterHotKey(IntPtr windowHandle, int id, out int errorCode)
        {
            UnregisteredHotkeys.Add(new UnregisteredHotkey(windowHandle, id));
            errorCode = 0;
            return true;
        }
    }

    private sealed record RegisteredHotkey(IntPtr WindowHandle, int Id, uint Modifiers, uint VirtualKey);

    private sealed record UnregisteredHotkey(IntPtr WindowHandle, int Id);
}
