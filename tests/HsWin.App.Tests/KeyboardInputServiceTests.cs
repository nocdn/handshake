using HsWin.App.Input;
using HsWin.App.Keyboard;
using HsWin.Core.Hotkeys;
using HsWin.Core.Input;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using System.Runtime.ExceptionServices;

namespace HsWin.App.Tests;

public sealed class KeyboardInputServiceTests
{
    [Fact]
    public void TapOutsideKeyboardHookDispatchSendsImmediately()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        service.Tap(0x23, KeyboardTapOptions.Default);

        Assert.Equal("tap:0x23:sendInput", Assert.Single(sender.Actions));
    }

    [Fact]
    public void TapInsideKeyboardHookDispatchDefersUntilHookReturns()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        using var queue = new SerializedInputQueue(NullRuntimeLogger.Instance);

        using (KeyboardHookDispatchScope.Enter(queue))
        {
            service.Tap(0x23, KeyboardTapOptions.Default);

            Assert.Empty(sender.Actions);
        }

        Assert.True(sender.WaitForAction(TimeSpan.FromSeconds(2)));
        Assert.Equal("tap:0x23:sendInput", Assert.Single(sender.Actions));
    }

    [Fact]
    public void DeferredInputNeverLogsWhatWasTyped()
    {
        var logger = new CapturingRuntimeLogger();
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(logger, sender);
        using var queue = new SerializedInputQueue(logger);

        using (KeyboardHookDispatchScope.Enter(queue))
        {
            service.Tap(0x41, KeyboardTapOptions.Default);
        }

        Assert.True(sender.WaitForAction(TimeSpan.FromSeconds(2)));
        Assert.Empty(logger.All);
    }

    [Fact]
    public void DeferredInputFromSeparateKeystrokesRunsInKeystrokeOrder()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        using var queue = new SerializedInputQueue(NullRuntimeLogger.Instance);

        for (uint key = 0x30; key < 0x30 + 50; key++)
        {
            using (KeyboardHookDispatchScope.Enter(queue))
            {
                service.KeyDown(key);
                service.KeyUp(key);
            }
        }

        Assert.True(sender.WaitForActionCount(100, TimeSpan.FromSeconds(5)));
        var expected = Enumerable.Range(0x30, 50)
            .SelectMany(key => new[] { $"down:0x{key:X2}:sendInput", $"up:0x{key:X2}:sendInput" });
        Assert.Equal(expected, sender.Actions);
    }

    [Fact]
    public void ReleaseHeldKeysInsideKeyboardHookDispatchIsDeferredToo()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        using var queue = new SerializedInputQueue(NullRuntimeLogger.Instance);
        service.KeyDown(0x57);
        var sentBefore = sender.Actions.Count;

        using (KeyboardHookDispatchScope.Enter(queue))
        {
            service.ReleaseHeldKeys();

            Assert.Equal(sentBefore, sender.Actions.Count);
        }

        Assert.True(sender.WaitForActionCount(sentBefore + 1, TimeSpan.FromSeconds(2)));
        Assert.Equal("up:0x57:sendInput", sender.Actions[^1]);
    }

    [Fact]
    public void TapAfterKeyboardHookDispatchDisposedSendsImmediately()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        using var queue = new SerializedInputQueue(NullRuntimeLogger.Instance);

        using (KeyboardHookDispatchScope.Enter(queue))
        {
        }

        service.Tap(0x24, KeyboardTapOptions.Default);

        Assert.Equal("tap:0x24:sendInput", Assert.Single(sender.Actions));
    }

    [Fact]
    public void TapFromAnotherThreadDuringKeyboardHookDispatchSendsImmediately()
    {
        // The dispatch scope is local to the hook thread by design: only input sent
        // synchronously by the hook dispatch itself (remaps) is deferred. Script callbacks
        // scheduled onto other threads send inline.
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        using var queue = new SerializedInputQueue(NullRuntimeLogger.Instance);
        var scopeThreadId = Environment.CurrentManagedThreadId;
        var workerThreadId = scopeThreadId;
        ExceptionDispatchInfo? workerFailure = null;

        using (KeyboardHookDispatchScope.Enter(queue))
        {
            var worker = new Thread(() =>
            {
                try
                {
                    workerThreadId = Environment.CurrentManagedThreadId;
                    service.Tap(0x24, KeyboardTapOptions.Default);
                }
                catch (Exception exception)
                {
                    workerFailure = ExceptionDispatchInfo.Capture(exception);
                }
            })
            {
                IsBackground = true,
                Name = "Keyboard input cross-thread test"
            };
            worker.Start();

            Assert.True(worker.Join(TimeSpan.FromSeconds(2)), "Dedicated keyboard-input thread did not finish.");
            workerFailure?.Throw();

            Assert.NotEqual(scopeThreadId, workerThreadId);
            Assert.Equal("tap:0x24:sendInput", Assert.Single(sender.Actions));
        }
    }

    [Fact]
    public void ReleaseHeldKeysSendsKeyUpForTrackedHolds()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        service.KeyDown(0x57);
        service.KeyDown(0x41);
        service.ReleaseHeldKeys();

        Assert.Equal(
            [
                "down:0x57:sendInput",
                "down:0x41:sendInput",
                "up:0x57:sendInput",
                "up:0x41:sendInput"
            ],
            sender.Actions);

        service.ReleaseHeldKeys();

        Assert.Equal(4, sender.Actions.Count);
    }

    [Fact]
    public void KeyUpUntracksSoReleaseSkipsIt()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        service.KeyDown(0x57);
        service.KeyUp(0x57);
        service.ReleaseHeldKeys();

        Assert.Equal(
            [
                "down:0x57:sendInput",
                "up:0x57:sendInput"
            ],
            sender.Actions);
    }

    [Fact]
    public void TapDoesNotLeaveATrackedHold()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        service.Tap(0x57, KeyboardTapOptions.Default);
        service.ReleaseHeldKeys();

        var action = Assert.Single(sender.Actions);
        Assert.Equal("tap:0x57:sendInput", action);
    }

    [Fact]
    public void ReleaseHeldKeysWhenIdleIsNoOp()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        service.ReleaseHeldKeys();

        Assert.Empty(sender.Actions);
    }

    [Fact]
    public void RepeatPassesTheRequestedInputMethodToTheSender()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        using var repeat = service.Repeat(
            0xA0,
            new KeyboardRepeatOptions(10, HotkeyModifiers.None, InputMethod.WindowMessage));

        Assert.True(sender.WaitForActionCount(2, TimeSpan.FromSeconds(2)));
        repeat.Dispose();

        Assert.All(
            sender.Actions.Where(action => action.StartsWith("tap:", StringComparison.Ordinal)),
            action => Assert.EndsWith(":windowMessage", action, StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatWithKeyDownDurationSendsObservableDownAndUpPhases()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        using var repeat = service.Repeat(
            KeyboardKeyRules.VkShift,
            new KeyboardRepeatOptions(
                IntervalMs: 80,
                SuppressPhysicalModifiers: HotkeyModifiers.None,
                InputMethod: InputMethod.WindowMessage,
                KeyDownMs: 30));

        Assert.True(sender.WaitForActionCount(3, TimeSpan.FromSeconds(2)));

        Assert.Equal(
            [
                "down:0x10:windowMessage",
                "up:0x10:windowMessage",
                "down:0x10:windowMessage"
            ],
            sender.Actions.Take(3));
    }

    [Fact]
    public async Task RepeatPulseIsObservableToFiftyMillisecondStateSampler()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        var sampledStates = new List<bool>();

        using var repeat = service.Repeat(
            KeyboardKeyRules.VkShift,
            new KeyboardRepeatOptions(
                IntervalMs: 120,
                SuppressPhysicalModifiers: HotkeyModifiers.None,
                InputMethod: InputMethod.SendInput,
                KeyDownMs: 60));

        for (var sample = 0; sample < 8; sample++)
        {
            await Task.Delay(50);
            sampledStates.Add(sender.IsDown(KeyboardKeyRules.VkShift));
        }

        Assert.Contains(true, sampledStates);
        Assert.Contains(false, sampledStates);
    }

    [Fact]
    public void RepeatTapsWithTheRequestedModifiersHeld()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);

        using var repeat = service.Repeat(
            0x41,
            new KeyboardRepeatOptions(IntervalMs: 10, SuppressPhysicalModifiers: HotkeyModifiers.None, Modifiers: HotkeyModifiers.Control));

        Assert.True(sender.WaitForActionCount(1, TimeSpan.FromSeconds(2)));
        repeat.Dispose();

        Assert.Equal([KeyboardKeyRules.VkControl], sender.TapModifiers[0]);
    }

    [Fact]
    public void RepeatRejectsIntervalNotLongerThanKeyDownDuration()
    {
        var service = new KeyboardInputService(
            NullRuntimeLogger.Instance,
            new CapturingKeyboardInputSender());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Repeat(
                KeyboardKeyRules.VkShift,
                new KeyboardRepeatOptions(
                    IntervalMs: 60,
                    SuppressPhysicalModifiers: HotkeyModifiers.None,
                    KeyDownMs: 60)));
    }

    [Fact]
    public void RepeatRegistersNativeFallbackForSuppressedPhysicalModifiers()
    {
        var sender = new CapturingKeyboardInputSender();
        var keyboardEvents = new CapturingKeyboardEventService();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender, keyboardEvents);

        using var repeat = service.Repeat(
            KeyboardKeyRules.VkShift,
            new KeyboardRepeatOptions(
                IntervalMs: 100,
                SuppressPhysicalModifiers: HotkeyModifiers.Control | HotkeyModifiers.Shift));

        Assert.NotNull(keyboardEvents.Options);
        Assert.True(keyboardEvents.Options.Blocking);
        Assert.Contains(KeyboardKeyRules.VkLeftControl, keyboardEvents.Options.KeyFilter!);
        Assert.Contains(KeyboardKeyRules.VkRightShift, keyboardEvents.Options.KeyFilter!);
        Assert.True(keyboardEvents.Invoke(CreateKeyDownSnapshot(KeyboardKeyRules.VkLeftControl)));

        repeat.Dispose();

        Assert.True(keyboardEvents.RegistrationDisposed);
    }

    [Theory]
    [InlineData(KeyboardKeyRules.VkLeftShift, KeyboardKeyRules.VkShift)]
    [InlineData(KeyboardKeyRules.VkRightShift, KeyboardKeyRules.VkShift)]
    [InlineData(KeyboardKeyRules.VkLeftControl, KeyboardKeyRules.VkControl)]
    [InlineData(KeyboardKeyRules.VkRightMenu, KeyboardKeyRules.VkMenu)]
    [InlineData(0x41u, 0x41u)]
    public void WindowMessagesUseGenericModifierVirtualKeys(uint input, uint expected)
    {
        Assert.Equal(expected, WindowMessageKeyboardInputSender.ResolveMessageVirtualKey(input));
    }

    private static KeyboardEventSnapshot CreateKeyDownSnapshot(uint virtualKey)
    {
        return new KeyboardEventSnapshot(
            Type: "keydown",
            KeyCode: virtualKey,
            Key: KeyboardKeyRules.GetDisplayName(virtualKey),
            Modifiers: [],
            ModifierFlags: 0,
            IsKeyDown: true,
            IsKeyUp: false,
            IsModifier: KeyboardKeyRules.IsModifierVirtualKey(virtualKey),
            IsInjected: false,
            IsExtended: KeyboardKeyRules.IsExtendedVirtualKey(virtualKey));
    }

    private sealed class CapturingKeyboardInputSender : IKeyboardInputSender
    {
        private readonly ManualResetEventSlim _actionSent = new();
        private readonly object _gate = new();
        private readonly List<string> _actions = [];
        private readonly HashSet<uint> _downKeys = [];
        private readonly List<IReadOnlyList<uint>> _tapModifiers = [];

        public IReadOnlyList<IReadOnlyList<uint>> TapModifiers
        {
            get
            {
                lock (_gate)
                {
                    return [.. _tapModifiers];
                }
            }
        }

        public IReadOnlyList<string> Actions
        {
            get
            {
                lock (_gate)
                {
                    return [.. _actions];
                }
            }
        }

        public void SendKeyDown(
            uint virtualKey,
            InputMethod inputMethod = InputMethod.SendInput,
            IRuntimeLogger? logger = null)
        {
            Add(
                $"down:0x{virtualKey:X2}:{InputMethodParser.GetDisplayName(inputMethod)}",
                virtualKey,
                isDown: true);
        }

        public void SendKeyUp(
            uint virtualKey,
            InputMethod inputMethod = InputMethod.SendInput,
            IRuntimeLogger? logger = null)
        {
            Add(
                $"up:0x{virtualKey:X2}:{InputMethodParser.GetDisplayName(inputMethod)}",
                virtualKey,
                isDown: false);
        }

        public void SendTap(
            uint virtualKey,
            IReadOnlyList<uint>? suppressedModifierVirtualKeys = null,
            IReadOnlyList<uint>? modifierVirtualKeys = null,
            InputMethod inputMethod = InputMethod.SendInput,
            IRuntimeLogger? logger = null)
        {
            lock (_gate)
            {
                _tapModifiers.Add(modifierVirtualKeys ?? []);
            }

            Add($"tap:0x{virtualKey:X2}:{InputMethodParser.GetDisplayName(inputMethod)}");
        }

        public bool WaitForAction(TimeSpan timeout)
        {
            return _actionSent.Wait(timeout);
        }

        public bool WaitForActionCount(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (_actions.Count >= count)
                    {
                        return true;
                    }
                }

                _actionSent.Wait(TimeSpan.FromMilliseconds(25));
                _actionSent.Reset();
            }

            lock (_gate)
            {
                return _actions.Count >= count;
            }
        }

        public bool IsDown(uint virtualKey)
        {
            lock (_gate)
            {
                return _downKeys.Contains(virtualKey);
            }
        }

        private void Add(string action, uint? virtualKey = null, bool? isDown = null)
        {
            lock (_gate)
            {
                _actions.Add(action);
                if (virtualKey is not null && isDown is not null)
                {
                    if (isDown.Value)
                    {
                        _downKeys.Add(virtualKey.Value);
                    }
                    else
                    {
                        _downKeys.Remove(virtualKey.Value);
                    }
                }
            }

            _actionSent.Set();
        }
    }

    private sealed class CapturingKeyboardEventService : IKeyboardEventService
    {
        private Func<KeyboardEventSnapshot, bool>? _callback;

        public KeyboardEventWatchOptions? Options { get; private set; }

        public bool RegistrationDisposed { get; private set; }

        public IDisposable Watch(
            KeyboardEventWatchOptions options,
            Func<KeyboardEventSnapshot, bool> callback)
        {
            Options = options;
            _callback = callback;
            return new CallbackDisposable(() => RegistrationDisposed = true);
        }

        public bool IsKeyDown(uint virtualKey) => false;

        public bool Invoke(KeyboardEventSnapshot snapshot)
        {
            return Assert.IsType<Func<KeyboardEventSnapshot, bool>>(_callback)(snapshot);
        }
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }

    [Fact]
    public void NestedKeyboardHookDispatchScopesRestoreCorrectly()
    {
        var sender = new CapturingKeyboardInputSender();
        var service = new KeyboardInputService(NullRuntimeLogger.Instance, sender);
        using var queue = new SerializedInputQueue(NullRuntimeLogger.Instance);

        using (KeyboardHookDispatchScope.Enter(queue))
        {
            using (KeyboardHookDispatchScope.Enter(queue))
            {
                service.Tap(0x23, KeyboardTapOptions.Default);
                Assert.Empty(sender.Actions);
            }

            // Disposing the inner scope hands its deferred tap to the worker, which may run it at
            // any moment; wait for exactly that one so the next assertion is deterministic.
            Assert.True(sender.WaitForActionCount(1, TimeSpan.FromSeconds(2)));

            // Inner scope disposed: the outer scope must still capture deferrals.
            service.Tap(0x24, KeyboardTapOptions.Default);
            Assert.Equal("tap:0x23:sendInput", Assert.Single(sender.Actions));
        }

        Assert.True(sender.WaitForActionCount(2, TimeSpan.FromSeconds(2)));
        Assert.Equal(["tap:0x23:sendInput", "tap:0x24:sendInput"], sender.Actions);
    }
}
