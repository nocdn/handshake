using System.Runtime.InteropServices;
using HsWin.App.Native;
using HsWin.App.Threading;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;

namespace HsWin.App.Keyboard;

/// <summary>
/// Global keyboard event source backed by WH_KEYBOARD_LL. Callback code must stay small because
/// Windows runs this hook before posting keyboard input to the target thread.
/// </summary>
internal sealed class NativeKeyboardEventService : IKeyboardEventService, IDisposable
{
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint LlkhfUp = 0x80;

    private readonly object _gate = new();
    private readonly IRuntimeLogger _logger;
    private readonly KeyboardWatchDispatcher _watchDispatcher;
    private readonly LowLevelHookThread _hook;
    private readonly SerializedInputQueue _deferredInput;
    private readonly KeyboardModifierTracker _modifierTracker = new();
    private readonly List<KeyboardWatchSubscription> _subscriptions = [];

    // Immutable snapshot of _subscriptions, rebuilt under _gate on every mutation and read
    // without allocation on the hook thread (which must not copy the list per keystroke).
    private KeyboardWatchSubscription[] _subscriptionSnapshot = [];

    // Physical modifier state published for lock-free readers (the mouse hook samples this
    // five times per event while holding its own gate; taking this service's lock there
    // nested cross-hook locking). Written only under _gate alongside the tracker.
    private volatile bool _modifiersTracked;
    private volatile HotkeyModifiers _publishedModifiers;

    private bool _disposed;
    private long _nextSubscriptionId;
    private Func<KeyboardEventSnapshot, bool>? _hostPriorityHandler;

    public NativeKeyboardEventService(IRuntimeLogger logger)
        : this(
            logger,
            new KeyboardWatchDispatcher(
                logger,
                new SynchronizationContextCallbackScheduler(SynchronizationContext.Current)))
    {
    }

    internal NativeKeyboardEventService(IRuntimeLogger logger, KeyboardWatchDispatcher watchDispatcher)
    {
        _logger = logger;
        _watchDispatcher = watchDispatcher;
        _hook = new LowLevelHookThread(LowLevelHookThread.KeyboardHookId, "HsWin Keyboard Hook", HookCallback, Win32LowLevelHookPlatform.Instance, logger);
        _deferredInput = new SerializedInputQueue(logger);
    }

    public IDisposable Watch(KeyboardEventWatchOptions options, Func<KeyboardEventSnapshot, bool> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            EnsureHookInstalled();

            var subscription = new KeyboardWatchSubscription(
                Interlocked.Increment(ref _nextSubscriptionId),
                options,
                callback,
                RemoveSubscription);
            if (options.Prepend)
            {
                _subscriptions.Insert(0, subscription);
            }
            else
            {
                _subscriptions.Add(subscription);
            }

            RebuildSubscriptionSnapshot();

            _logger.Info(
                $"Keyboard watch registered id={subscription.Id} includeInjected={options.IncludeInjected} blocking={options.Blocking} " +
                $"prepend={options.Prepend} keys={FormatKeyFilter(options.KeyFilter)} count={_subscriptions.Count}.");
            return subscription;
        }
    }

    /// <summary>
    /// Installs a host-only handler that runs on the keyboard hook path before any script watchers.
    /// Used for emergency stop so it cannot be ordered behind config remaps or blocked by script lock.
    /// Ensures the hook is installed even when no script watchers exist.
    /// </summary>
    public void SetHostPriorityHandler(Func<KeyboardEventSnapshot, bool>? handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            _hostPriorityHandler = handler;
            if (handler is not null)
            {
                EnsureHookInstalled();
            }
            else if (_subscriptions.Count == 0)
            {
                _modifierTracker.Reset();
                UninstallHook();
            }
        }

        _logger.Info(handler is null
            ? "Keyboard host priority handler cleared."
            : "Keyboard host priority handler registered.");
    }

    public bool IsKeyDown(uint virtualKey)
    {
        var modifier = KeyboardKeyRules.ModifierForVirtualKey(virtualKey);
        if (modifier != HotkeyModifiers.None)
        {
            // Capture the state first, then the flag: if the hook is being torn down between
            // the two reads, the captured state is still the last-published truth rather than
            // a torn true/None pair. SendInput key-up events intentionally alter
            // GetAsyncKeyState even though the user still physically holds the modifier; the
            // LL hook tracker ignores injected events, so it remains the source of truth while
            // our hook is installed. The published state is read without the service lock so
            // other hook threads never nest locks against this service.
            var publishedModifiers = _publishedModifiers;
            if (_modifiersTracked)
            {
                return IsTrackedModifierDown(virtualKey, publishedModifiers);
            }
        }

        return NativeKeyStateReader.IsDown(virtualKey);
    }

    internal static bool IsTrackedModifierDown(uint virtualKey, HotkeyModifiers pressedModifiers)
    {
        var modifier = KeyboardKeyRules.ModifierForVirtualKey(virtualKey);
        return modifier != HotkeyModifiers.None && (pressedModifiers & modifier) != 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var subscription in _subscriptions.ToArray())
            {
                subscription.MarkDisposed();
            }

            _subscriptions.Clear();
            RebuildSubscriptionSnapshot();
            _hostPriorityHandler = null;
            _modifierTracker.Reset();
            UninstallHook();
            _disposed = true;
        }

        // Outside the lock: draining runs remap key-ups, whose SendInput re-enters this hook.
        _deferredInput.Dispose();
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HcAction)
        {
            return _hook.CallNext(code, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var hookData = Marshal.PtrToStructure<KeyboardHookStruct>(lParam);
        var isKeyUp = IsKeyUpMessage(message, hookData.Flags);
        var isInjected = IsInjectedEvent(hookData);
        KeyboardWatchSubscription[] subscriptions;
        KeyboardEventSnapshot snapshot;
        Func<KeyboardEventSnapshot, bool>? hostPriorityHandler;

        lock (_gate)
        {
            if (!isInjected)
            {
                _modifierTracker.Apply(hookData.VkCode, isKeyUp);
                _publishedModifiers = _modifierTracker.Pressed;
            }

            hostPriorityHandler = _hostPriorityHandler;
            if (hostPriorityHandler is null && _subscriptions.Count == 0)
            {
                return _hook.CallNext(code, wParam, lParam);
            }

            snapshot = CreateSnapshot(hookData.VkCode, isKeyUp, isInjected);
            subscriptions = _subscriptionSnapshot;
        }

        // Host priority (emergency stop) always runs first, never behind script remaps/watchers.
        if (hostPriorityHandler is not null)
        {
            try
            {
                if (hostPriorityHandler(snapshot))
                {
                    return new IntPtr(1);
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Keyboard host priority handler failed.", exception);
            }
        }

        if (subscriptions.Length == 0)
        {
            return _hook.CallNext(code, wParam, lParam);
        }

        bool shouldSwallow;
        using (KeyboardHookDispatchScope.Enter(_deferredInput))
        {
            shouldSwallow = _watchDispatcher.Dispatch(snapshot, subscriptions);
        }

        return shouldSwallow
            ? new IntPtr(1)
            : _hook.CallNext(code, wParam, lParam);
    }

    private KeyboardEventSnapshot CreateSnapshot(uint virtualKey, bool isKeyUp, bool isInjected)
    {
        var pressedModifiers = _modifierTracker.Pressed;
        return new KeyboardEventSnapshot(
            Type: isKeyUp ? "keyup" : "keydown",
            KeyCode: virtualKey,
            Key: KeyboardKeyRules.GetDisplayName(virtualKey),
            Modifiers: KeyboardKeyRules.GetModifierNames(pressedModifiers),
            ModifierFlags: (uint)pressedModifiers,
            IsKeyDown: !isKeyUp,
            IsKeyUp: isKeyUp,
            IsModifier: KeyboardKeyRules.IsModifierVirtualKey(virtualKey),
            IsInjected: isInjected,
            IsExtended: KeyboardKeyRules.IsExtendedVirtualKey(virtualKey));
    }

    private void RemoveSubscription(KeyboardWatchSubscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.Remove(subscription))
            {
                return;
            }

            _logger.Info($"Keyboard watch disposed id={subscription.Id} count={_subscriptions.Count}.");
            RebuildSubscriptionSnapshot();
            if (_subscriptions.Count == 0 && _hostPriorityHandler is null)
            {
                _modifierTracker.Reset();
                UninstallHook();
            }
        }
    }

    private void EnsureHookInstalled()
    {
        if (_hook.IsInstalled)
        {
            return;
        }

        _hook.EnsureInstalled();

        // Publish tracked modifier state for lock-free IsKeyDown readers while the hook runs.
        _publishedModifiers = _modifierTracker.Pressed;
        _modifiersTracked = true;
    }

    private void RebuildSubscriptionSnapshot()
    {
        _subscriptionSnapshot = [.. _subscriptions];
    }

    private void UninstallHook()
    {
        // Lock-free modifier readers must fall back to GetAsyncKeyState once tracking stops.
        // The published modifiers stay at their last value: readers gate on the flag below,
        // and clearing them here could pair a stale flag with a cleared value (torn read).
        _modifiersTracked = false;

        _hook.Uninstall();
    }

    private static bool IsKeyUpMessage(int message, uint flags)
    {
        return message is WmKeyUp or WmSysKeyUp || (flags & LlkhfUp) != 0;
    }

    private static bool IsInjectedEvent(KeyboardHookStruct hookData)
    {
        return (hookData.Flags & LlkhfInjected) != 0
            || hookData.ExtraInfo == NativeInputSender.InjectedExtraInfo;
    }

    private static string FormatKeyFilter(IReadOnlySet<uint>? keyFilter)
    {
        return keyFilter is { Count: > 0 }
            ? string.Join(",", keyFilter.Select(key => $"0x{key:X2}"))
            : "<all>";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public uint VkCode;

        public uint ScanCode;

        public uint Flags;

        public uint Time;

        public UIntPtr ExtraInfo;
    }
}
