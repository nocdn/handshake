using HsWin.App.Mouse;
using HsWin.App.Native;
using HsWin.App.Threading;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HsWin.App.Hotkeys;

/// <summary>
/// Single WH_MOUSE_LL host for mouse-button hotkeys and scroll watches. One dedicated
/// message-pump thread owns the hook so physical input is not delayed by the UI dispatcher.
/// </summary>
internal sealed class NativeMouseHotkeyHook : IDisposable
{
    private const int HcAction = 0;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmMouseHWheel = 0x020E;
    private const int XButton1 = 0x0001;
    private const int XButton2 = 0x0002;
    private const uint LlmhfInjected = 0x00000001;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const short KeyPressedMask = unchecked((short)0x8000);

    private readonly object _gate = new();
    private readonly IRuntimeLogger _logger;
    private readonly IMouseHookPlatform _platform;
    private readonly LowLevelHookThread _hook;
    private readonly IKeyboardEventService? _keyboardEvents;
    private readonly SynchronizationContext? _callbackContext;
    private readonly MouseScrollWatchDispatcher _scrollDispatcher;
    private readonly Dictionary<int, RegistrationState> _registrations = [];
    private readonly Dictionary<(HotkeyMouseButton Button, HotkeyModifiers Modifiers), RegistrationState> _registrationsByHotkey = [];
    private readonly Dictionary<HotkeyMouseButton, RegistrationState> _activeRegistrations = [];
    private readonly List<MouseScrollWatchSubscription> _scrollSubscriptions = [];
    private readonly List<ButtonObserver> _buttonObservers = [];
    private ButtonObserver[] _buttonObserverSnapshot = [];

    // Immutable snapshot of _scrollSubscriptions, rebuilt under _gate on every mutation and read
    // without allocation on the hook thread (wheel events can fire thousands of times per second).
    private MouseScrollWatchSubscription[] _scrollSubscriptionSnapshot = [];

    private int _nextId = 1;
    private long _nextScrollSubscriptionId;
    private bool _disposed;

    public NativeMouseHotkeyHook(IRuntimeLogger logger)
        : this(logger, Win32MouseHookPlatform.Instance, SynchronizationContext.Current)
    {
    }

    internal NativeMouseHotkeyHook(
        IRuntimeLogger logger,
        IMouseHookPlatform platform,
        SynchronizationContext? callbackContext,
        MouseScrollWatchDispatcher? scrollDispatcher = null,
        IKeyboardEventService? keyboardEvents = null)
    {
        _logger = logger;
        _platform = platform;
        _keyboardEvents = keyboardEvents;
        _callbackContext = callbackContext;
        _scrollDispatcher = scrollDispatcher
            ?? new MouseScrollWatchDispatcher(
                logger,
                new SynchronizationContextCallbackScheduler(callbackContext));
        _hook = new LowLevelHookThread(LowLevelHookThread.MouseHookId, "HsWin Mouse Hook", HookCallback, platform, logger);
    }

    public IDisposable Register(HotkeyDefinition hotkey, Action pressed)
    {
        return RegisterInternal(hotkey, pressed, released: null, blocking: true);
    }

    public IDisposable RegisterHeld(HotkeyDefinition hotkey, Action pressed, Action released, bool blocking)
    {
        return RegisterInternal(hotkey, pressed, released, blocking);
    }

    public IDisposable WatchScroll(MouseScrollWatchOptions options, Action<MouseScrollEventSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hook.EnsureInstalled();

            var subscription = new MouseScrollWatchSubscription(
                Interlocked.Increment(ref _nextScrollSubscriptionId),
                options,
                callback,
                RemoveScrollSubscription);
            if (options.Prepend)
            {
                _scrollSubscriptions.Insert(0, subscription);
            }
            else
            {
                _scrollSubscriptions.Add(subscription);
            }

            RebuildScrollSubscriptionSnapshot();

            _logger.Info(
                $"Mouse scroll watch registered id={subscription.Id} includeInjected={options.IncludeInjected} " +
                $"blocking={options.Blocking} prepend={options.Prepend} axes={FormatAxes(options.Axes)} " +
                $"scrollCount={_scrollSubscriptions.Count} hotkeyCount={_registrations.Count}.");
            return subscription;
        }
    }

    public IDisposable WatchPhysicalButtons(Action<PhysicalMouseButtonEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _hook.EnsureInstalled();
            var observer = new ButtonObserver(this, callback);
            _buttonObservers.Add(observer);
            Volatile.Write(ref _buttonObserverSnapshot, [.. _buttonObservers]);
            return observer;
        }
    }

    private IDisposable RegisterInternal(
        HotkeyDefinition hotkey,
        Action pressed,
        Action? released,
        bool blocking)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(pressed);
        if (released is null && !blocking)
        {
            throw new ArgumentException("A non-blocking mouse hotkey must provide a release callback.", nameof(released));
        }

        if (hotkey.InputKind != HotkeyInputKind.MouseButton || hotkey.MouseButton is null)
        {
            throw new ArgumentException("Mouse hotkeys require a mouse-button definition.", nameof(hotkey));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registrationsByHotkey.ContainsKey((hotkey.MouseButton!.Value, hotkey.Modifiers)))
            {
                throw new InvalidOperationException($"Mouse hotkey {hotkey} is already registered.");
            }

            _hook.EnsureInstalled();

            var id = _nextId++;
            var registration = new RegistrationState(id, hotkey, pressed, released, blocking);
            _registrations[id] = registration;
            _registrationsByHotkey[(hotkey.MouseButton.Value, hotkey.Modifiers)] = registration;
            _logger.Info(
                $"Mouse hotkey registered id={id} modifiers=0x{(uint)hotkey.Modifiers:X} button={hotkey.MouseButton} " +
                $"held={released is not null} blocking={blocking}.");
            return new MouseHotkeyRegistration(this, id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var subscription in _scrollSubscriptions.ToArray())
            {
                subscription.MarkDisposed();
            }

            _scrollSubscriptions.Clear();
            foreach (var observer in _buttonObservers) observer.Deactivate();
            _buttonObservers.Clear();
            Volatile.Write(ref _buttonObserverSnapshot, []);
            RebuildScrollSubscriptionSnapshot();
            _registrations.Clear();
            _registrationsByHotkey.Clear();
            _activeRegistrations.Clear(); // explicit: no stale button-up after dispose
            _hook.Uninstall();
            _disposed = true;
        }
    }

    internal static bool TryGetMouseButtonEvent(int message, uint mouseData, out MouseButtonEvent mouseButtonEvent)
    {
        mouseButtonEvent = default;

        if (message == WmMButtonDown)
        {
            mouseButtonEvent = new MouseButtonEvent(HotkeyMouseButton.Middle, IsDown: true);
            return true;
        }

        if (message == WmMButtonUp)
        {
            mouseButtonEvent = new MouseButtonEvent(HotkeyMouseButton.Middle, IsDown: false);
            return true;
        }

        if (message is WmXButtonDown or WmXButtonUp)
        {
            var xButton = (int)((mouseData >> 16) & 0xFFFF);
            var isDown = message == WmXButtonDown;
            if (xButton == XButton1)
            {
                mouseButtonEvent = new MouseButtonEvent(HotkeyMouseButton.XButton1, isDown);
                return true;
            }

            if (xButton == XButton2)
            {
                mouseButtonEvent = new MouseButtonEvent(HotkeyMouseButton.XButton2, isDown);
                return true;
            }
        }

        return false;
    }

    internal static bool TryCreateScrollEvent(
        int message,
        uint mouseData,
        uint flags,
        int x,
        int y,
        HotkeyModifiers pressedModifiers,
        out MouseScrollEventSnapshot snapshot)
    {
        snapshot = default!;

        var isVertical = message == WmMouseWheel;
        var isHorizontal = message == WmMouseHWheel;
        if (!isVertical && !isHorizontal)
        {
            return false;
        }

        // High-order word of mouseData is a signed wheel delta (WHEEL_DELTA units).
        var delta = unchecked((short)((mouseData >> 16) & 0xFFFF));
        if (delta == 0)
        {
            return false;
        }

        var axis = isVertical
            ? MouseScrollEventSnapshot.VerticalAxis
            : MouseScrollEventSnapshot.HorizontalAxis;
        var direction = isVertical
            ? (delta > 0 ? MouseScrollEventSnapshot.DirectionUp : MouseScrollEventSnapshot.DirectionDown)
            : (delta > 0 ? MouseScrollEventSnapshot.DirectionRight : MouseScrollEventSnapshot.DirectionLeft);
        var isInjected = (flags & LlmhfInjected) != 0;
        var notches = delta / (double)MouseScrollEventSnapshot.WheelDelta;

        snapshot = new MouseScrollEventSnapshot(
            Type: MouseScrollEventSnapshot.ScrollType,
            Axis: axis,
            Direction: direction,
            Delta: delta,
            Notches: notches,
            IsVertical: isVertical,
            IsHorizontal: isHorizontal,
            IsInjected: isInjected,
            Modifiers: KeyboardKeyRules.GetModifierNames(pressedModifiers),
            ModifierFlags: (uint)pressedModifiers,
            X: x,
            Y: y);
        return true;
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HcAction)
        {
            return _hook.CallNext(code, wParam, lParam);
        }

        var message = wParam.ToInt32();

        // Avoid marshaling MSLLHOOKSTRUCT for moves and other high-frequency messages.
        if (message is WmMouseWheel or WmMouseHWheel)
        {
            return HandleScrollMessage(code, wParam, lParam, message);
        }

        if (message is WmMButtonDown or WmMButtonUp or WmXButtonDown or WmXButtonUp)
        {
            var hookData = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            if (TryGetMouseButtonEvent(message, hookData.MouseData, out var mouseButtonEvent))
            {
                if ((hookData.Flags & LlmhfInjected) == 0)
                {
                    var input = new PhysicalMouseButtonEvent(mouseButtonEvent.Button, mouseButtonEvent.IsDown, ReadPressedModifiers());
                    foreach (var observer in Volatile.Read(ref _buttonObserverSnapshot))
                    {
                        try { observer.Deliver(input); }
                        catch (Exception exception) { _logger.Error("Physical mouse observer failed.", exception); }
                    }
                }
                if (TryHandleMouseButtonEvent(mouseButtonEvent)) return new IntPtr(1);
            }
        }

        return _hook.CallNext(code, wParam, lParam);
    }

    private IntPtr HandleScrollMessage(int code, IntPtr wParam, IntPtr lParam, int message)
    {
        MouseScrollWatchSubscription[] subscriptions;
        MouseScrollEventSnapshot snapshot;
        lock (_gate)
        {
            if (_scrollSubscriptions.Count == 0)
            {
                return _hook.CallNext(code, wParam, lParam);
            }

            var hookData = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            if (!TryCreateScrollEvent(
                    message,
                    hookData.MouseData,
                    hookData.Flags,
                    hookData.Point.X,
                    hookData.Point.Y,
                    ReadPressedModifiers(),
                    out snapshot))
            {
                return _hook.CallNext(code, wParam, lParam);
            }

            subscriptions = _scrollSubscriptionSnapshot;
        }

        var shouldSwallow = _scrollDispatcher.Dispatch(snapshot, subscriptions);
        return shouldSwallow
            ? new IntPtr(1)
            : _hook.CallNext(code, wParam, lParam);
    }

    private bool TryHandleMouseButtonEvent(MouseButtonEvent mouseButtonEvent)
    {
        if (!mouseButtonEvent.IsDown)
        {
            RegistrationState? activeRegistration;
            lock (_gate)
            {
                if (!_activeRegistrations.Remove(mouseButtonEvent.Button, out activeRegistration))
                {
                    return false;
                }
            }

            if (activeRegistration.Released is not null)
            {
                DispatchCallback(activeRegistration.Released);
            }

            return activeRegistration.Blocking;
        }

        var pressedModifiers = ReadPressedModifiers();
        RegistrationState? match;
        lock (_gate)
        {
            // Registrations are unique per (button, modifiers), so a keyed lookup replaces the
            // former O(n) scan with its closure on the hook thread.
            if (!_registrationsByHotkey.TryGetValue((mouseButtonEvent.Button, pressedModifiers), out var registration))
            {
                return false;
            }

            match = registration;
            _activeRegistrations[mouseButtonEvent.Button] = match;
        }

        DispatchCallback(match.Pressed);
        return match.Blocking;
    }

    private void DispatchCallback(Action callback)
    {
        if (_callbackContext is not null)
        {
            _callbackContext.Post(_ =>
            {
                try
                {
                    callback();
                }
                catch (Exception exception)
                {
                    _logger.Error("Mouse hotkey callback failed.", exception);
                }
            }, null);
            return;
        }

        callback();
    }

    private void Unregister(int id)
    {
        lock (_gate)
        {
            if (!_registrations.Remove(id, out var removed))
            {
                return;
            }

            if (removed.Hotkey.MouseButton is { } removedButton)
            {
                _registrationsByHotkey.Remove((removedButton, removed.Hotkey.Modifiers));
            }

            // Drop held-state for this registration so a later button-up cannot fire a stale release
            // callback or keep swallowing after dispose/reload/emergency stop.
            if (removed.Hotkey.MouseButton is { } button
                && _activeRegistrations.TryGetValue(button, out var active)
                && active.Id == id)
            {
                _activeRegistrations.Remove(button);
            }

            _logger.Info($"Mouse hotkey unregistered id={id}.");
            MaybeUninstallHookUnlocked();
        }
    }

    private void RemoveScrollSubscription(MouseScrollWatchSubscription subscription)
    {
        lock (_gate)
        {
            if (!_scrollSubscriptions.Remove(subscription))
            {
                return;
            }

            RebuildScrollSubscriptionSnapshot();
            _logger.Info($"Mouse scroll watch disposed id={subscription.Id} count={_scrollSubscriptions.Count}.");
            MaybeUninstallHookUnlocked();
        }
    }

    private void RebuildScrollSubscriptionSnapshot()
    {
        _scrollSubscriptionSnapshot = [.. _scrollSubscriptions];
    }

    private void MaybeUninstallHookUnlocked()
    {
        if (_registrations.Count == 0 && _scrollSubscriptions.Count == 0 && _buttonObservers.Count == 0)
        {
            _activeRegistrations.Clear();
            _hook.Uninstall();
        }
    }

    private HotkeyModifiers ReadPressedModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (IsKeyPressed(VkControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsKeyPressed(VkMenu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsKeyPressed(VkShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsKeyPressed(VkLWin) || IsKeyPressed(VkRWin))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private bool IsKeyPressed(int virtualKey)
    {
        if (_keyboardEvents is not null)
        {
            return _keyboardEvents.IsKeyDown(unchecked((uint)virtualKey));
        }

        return (_platform.GetAsyncKeyState(virtualKey) & KeyPressedMask) != 0;
    }

    private static string FormatAxes(MouseScrollAxis axes)
    {
        return axes switch
        {
            MouseScrollAxis.Vertical => "vertical",
            MouseScrollAxis.Horizontal => "horizontal",
            MouseScrollAxis.Both => "both",
            _ => axes.ToString().ToLowerInvariant()
        };
    }

    internal readonly record struct MouseButtonEvent(HotkeyMouseButton Button, bool IsDown);

    private sealed class ButtonObserver(NativeMouseHotkeyHook owner, Action<PhysicalMouseButtonEvent> callback) : IDisposable
    {
        private Action<PhysicalMouseButtonEvent>? _callback = callback;
        internal void Deliver(PhysicalMouseButtonEvent input) => Volatile.Read(ref _callback)?.Invoke(input);
        internal void Deactivate() => Interlocked.Exchange(ref _callback, null);
        public void Dispose()
        {
            Deactivate();
            lock (owner._gate)
            {
                owner._buttonObservers.Remove(this);
                Volatile.Write(ref owner._buttonObserverSnapshot, [.. owner._buttonObservers]);
                owner.MaybeUninstallHookUnlocked();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public NativePoint Point;

        public uint MouseData;

        public uint Flags;

        public uint Time;

        public UIntPtr ExtraInfo;
    }

    private sealed record RegistrationState(
        int Id,
        HotkeyDefinition Hotkey,
        Action Pressed,
        Action? Released,
        bool Blocking);

    private sealed class MouseHotkeyRegistration : IDisposable
    {
        private readonly NativeMouseHotkeyHook _owner;
        private readonly int _id;
        private bool _disposed;

        public MouseHotkeyRegistration(NativeMouseHotkeyHook owner, int id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner.Unregister(_id);
            _disposed = true;
        }
    }
}
