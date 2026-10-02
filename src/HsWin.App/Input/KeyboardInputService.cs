using System.Diagnostics;
using HsWin.App.Keyboard;
using HsWin.App.Threading;
using HsWin.Core.Hotkeys;
using HsWin.Core.Input;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;

namespace HsWin.App.Input;

internal sealed class KeyboardInputService : IKeyboardInputService
{
    private readonly IRuntimeLogger _logger;
    private readonly IKeyboardInputSender _inputSender;
    private readonly IKeyboardEventService _keyboardEvents;
    private readonly IPreciseTimerFactory _timers;
    private readonly ActiveRepeatSlot<KeyboardRepeatHandle> _repeat;
    private readonly object _heldGate = new();
    private readonly HashSet<uint> _heldKeys = [];

    public KeyboardInputService(IRuntimeLogger logger, IKeyboardEventService keyboardEvents)
        : this(logger, NativeKeyboardInputSender.Instance, keyboardEvents, PreciseTimerScheduler.Shared)
    {
    }

    internal KeyboardInputService(
        IRuntimeLogger logger,
        IKeyboardInputSender inputSender,
        IKeyboardEventService? keyboardEvents = null,
        IPreciseTimerFactory? timers = null)
    {
        _logger = logger;
        _inputSender = inputSender;
        _keyboardEvents = keyboardEvents ?? NullKeyboardEventService.Instance;
        _timers = timers ?? PreciseTimerScheduler.Shared;
        _repeat = new ActiveRepeatSlot<KeyboardRepeatHandle>("keyboard", logger);
    }

    public void KeyDown(uint virtualKey)
    {
        TrackHeldKey(virtualKey);
        SendOrDefer(() => _inputSender.SendKeyDown(virtualKey, InputMethod.SendInput, _logger));
    }

    public void KeyUp(uint virtualKey)
    {
        UntrackHeldKey(virtualKey);
        SendOrDefer(() => _inputSender.SendKeyUp(virtualKey, InputMethod.SendInput, _logger));
    }

    public void ReleaseHeldKeys()
    {
        uint[] held;
        lock (_heldGate)
        {
            if (_heldKeys.Count == 0)
            {
                return;
            }

            held = [.. _heldKeys];
            _heldKeys.Clear();
        }

        _logger.Info($"Releasing {held.Length} script-held key(s).");
        SendOrDefer(() =>
        {
            foreach (var virtualKey in held)
            {
                _inputSender.SendKeyUp(virtualKey, InputMethod.SendInput, _logger);
            }
        });
    }

    public void Tap(uint virtualKey, KeyboardTapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var suppressedModifiers = GetCurrentlyDownModifiers(options.SuppressPhysicalModifiers);
        var modifiers = KeyboardKeyRules.GetModifierVirtualKeys(options.Modifiers);
        var inputMethod = options.InputMethod;
        SendOrDefer(() => _inputSender.SendTap(virtualKey, suppressedModifiers, modifiers, inputMethod, _logger));
    }

    public IRepeatSession Repeat(uint virtualKey, KeyboardRepeatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RepeatTiming.ValidateIntervalMs(options.IntervalMs, nameof(options));
        RepeatTiming.ValidateKeyDownMs(options.KeyDownMs, options.IntervalMs, nameof(options));

        return _repeat.Start(
            () => new KeyboardRepeatHandle(
                virtualKey,
                options,
                GetCurrentlyDownModifiers(options.SuppressPhysicalModifiers),
                KeyboardKeyRules.GetModifierVirtualKeys(options.Modifiers),
                _logger,
                _inputSender,
                _keyboardEvents,
                _timers,
                _repeat.Clear),
            repeat => repeat.Start());
    }

    public void StopActiveRepeat() => _repeat.StopActive();

    /// <summary>
    /// Input requested from inside a blocking keyboard hook dispatch (a remap) must wait until
    /// the hook returns; everything else is sent immediately.
    /// </summary>
    private static void SendOrDefer(Action send)
    {
        if (!KeyboardHookDispatchScope.TryDefer(send))
        {
            send();
        }
    }

    private void TrackHeldKey(uint virtualKey)
    {
        lock (_heldGate)
        {
            _heldKeys.Add(virtualKey);
        }
    }

    private void UntrackHeldKey(uint virtualKey)
    {
        lock (_heldGate)
        {
            _heldKeys.Remove(virtualKey);
        }
    }

    private IReadOnlyList<uint> GetCurrentlyDownModifiers(HotkeyModifiers modifiers)
    {
        // Release the *physical* L/R keys that are down, not only the generic VK_SHIFT/VK_CONTROL
        // codes. Games and the OS track left/right modifiers separately.
        var down = new List<uint>(4);

        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            AddPhysicalModifier(down, KeyboardKeyRules.VkLeftControl, KeyboardKeyRules.VkRightControl, KeyboardKeyRules.VkControl);
        }

        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            AddPhysicalModifier(down, KeyboardKeyRules.VkLeftShift, KeyboardKeyRules.VkRightShift, KeyboardKeyRules.VkShift);
        }

        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            AddPhysicalModifier(down, KeyboardKeyRules.VkLeftMenu, KeyboardKeyRules.VkRightMenu, KeyboardKeyRules.VkMenu);
        }

        if ((modifiers & HotkeyModifiers.Win) != 0)
        {
            AddIfDown(down, KeyboardKeyRules.VkLeftWin);
            AddIfDown(down, KeyboardKeyRules.VkRightWin);
        }

        return down;
    }

    private void AddPhysicalModifier(List<uint> keys, uint left, uint right, uint generic)
    {
        var before = keys.Count;
        AddIfDown(keys, left);
        AddIfDown(keys, right);
        if (keys.Count == before)
        {
            AddIfDown(keys, generic);
        }
    }

    private void AddIfDown(List<uint> keys, uint virtualKey)
    {
        if (_keyboardEvents.IsKeyDown(virtualKey))
        {
            keys.Add(virtualKey);
        }
    }

    /// <summary>
    /// One running repeat. Instantaneous repeats tap on a periodic timer; pulse repeats
    /// (<see cref="KeyboardRepeatOptions.KeyDownMs"/> &gt; 0) alternate a held phase and a released
    /// phase with one-shot timer deadlines.
    /// </summary>
    private sealed class KeyboardRepeatHandle : IRepeatSession
    {
        private const int ProgressLogEveryTicks = 250;

        private readonly uint _virtualKey;
        private readonly HotkeyModifiers _suppressPhysicalModifiers;
        private readonly InputMethod _inputMethod;
        private readonly int _keyDownMs;
        private readonly IReadOnlyList<uint> _suppressedModifiers;
        private readonly IReadOnlyList<uint> _modifiers;
        private readonly IRuntimeLogger _logger;
        private readonly IKeyboardInputSender _inputSender;
        private readonly IKeyboardEventService _keyboardEvents;
        private readonly Action<KeyboardRepeatHandle> _clearActiveRepeat;
        private readonly object _gate = new();
        private readonly Stopwatch _stopwatch = new();
        private readonly IPreciseTimer _timer;

        private int _intervalMs;
        private bool _disposed;
        private bool _repeatKeyIsDown;
        private long _tickCount;
        private long _lastLoggedTickCount;
        private IDisposable? _physicalModifierSuppression;

        public KeyboardRepeatHandle(
            uint virtualKey,
            KeyboardRepeatOptions options,
            IReadOnlyList<uint> suppressedModifiers,
            IReadOnlyList<uint> modifiers,
            IRuntimeLogger logger,
            IKeyboardInputSender inputSender,
            IKeyboardEventService keyboardEvents,
            IPreciseTimerFactory timers,
            Action<KeyboardRepeatHandle> clearActiveRepeat)
        {
            _virtualKey = virtualKey;
            _intervalMs = options.IntervalMs;
            _suppressPhysicalModifiers = options.SuppressPhysicalModifiers;
            _inputMethod = options.InputMethod;
            _keyDownMs = options.KeyDownMs;
            _suppressedModifiers = suppressedModifiers;
            _modifiers = modifiers;
            _logger = logger;
            _inputSender = inputSender;
            _keyboardEvents = keyboardEvents;
            _clearActiveRepeat = clearActiveRepeat;
            _timer = timers.Create(Tick);
        }

        public int IntervalMs
        {
            get
            {
                lock (_gate)
                {
                    return _intervalMs;
                }
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                _stopwatch.Start();
                RegisterPhysicalModifierSuppression();
                _logger.Info(
                    $"Keyboard repeat started vk=0x{_virtualKey:X2} intervalMs={_intervalMs} keyDownMs={_keyDownMs} " +
                    $"modifiers={_modifiers.Count} suppressModifiers=0x{(uint)_suppressPhysicalModifiers:X} " +
                    $"inputMethod={InputMethodParser.GetDisplayName(_inputMethod)}.");
                ReleaseSuppressedModifiers();
                if (_keyDownMs == 0)
                {
                    SendTap();
                    _timer.Change(_intervalMs, _intervalMs);
                }
                else
                {
                    PressRepeatKey();
                    _timer.Change(_keyDownMs, Timeout.Infinite);
                }

                _tickCount++;
            }
        }

        public void SetIntervalMs(int intervalMs)
        {
            RepeatTiming.ValidateIntervalMs(intervalMs, nameof(intervalMs));
            lock (_gate)
            {
                if (_disposed || _intervalMs == intervalMs)
                {
                    return;
                }

                if (intervalMs <= _keyDownMs)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(intervalMs),
                        $"Keyboard repeat interval must be greater than keyDownMs ({_keyDownMs}).");
                }

                _intervalMs = intervalMs;
                if (_keyDownMs == 0)
                {
                    _timer.Change(intervalMs, intervalMs);
                }
                else if (!_repeatKeyIsDown)
                {
                    _timer.Change(intervalMs - _keyDownMs, Timeout.Infinite);
                }

                _logger.Info($"Keyboard repeat interval changed vk=0x{_virtualKey:X2} intervalMs={intervalMs}.");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Dispose();
                ReleaseRepeatKeys();
                _physicalModifierSuppression?.Dispose();
                _physicalModifierSuppression = null;
                _stopwatch.Stop();
                LogStop();
                _clearActiveRepeat(this);
            }
        }

        private void Tick()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    if (_keyDownMs == 0)
                    {
                        SendTap();
                    }
                    else if (_repeatKeyIsDown)
                    {
                        ReleaseRepeatKey();
                        _timer.Change(_intervalMs - _keyDownMs, Timeout.Infinite);
                        return;
                    }
                    else
                    {
                        PressRepeatKey();
                        _timer.Change(_keyDownMs, Timeout.Infinite);
                    }

                    _tickCount++;
                    LogProgressIfNeeded();
                }
                catch (Exception exception)
                {
                    _logger.Error($"Keyboard repeat tick failed vk=0x{_virtualKey:X2} tick={_tickCount}.", exception);
                    Dispose();
                }
            }
        }

        private void SendTap()
        {
            _inputSender.SendTap(_virtualKey, modifierVirtualKeys: _modifiers, inputMethod: _inputMethod, logger: _logger);
        }

        private void PressRepeatKey()
        {
            foreach (var modifier in _modifiers)
            {
                _inputSender.SendKeyDown(modifier, _inputMethod, _logger);
            }

            _inputSender.SendKeyDown(_virtualKey, _inputMethod, _logger);
            _repeatKeyIsDown = true;
        }

        private void ReleaseRepeatKey()
        {
            _inputSender.SendKeyUp(_virtualKey, _inputMethod, _logger);
            for (var index = _modifiers.Count - 1; index >= 0; index--)
            {
                _inputSender.SendKeyUp(_modifiers[index], _inputMethod, _logger);
            }

            _repeatKeyIsDown = false;
        }

        private void ReleaseSuppressedModifiers()
        {
            foreach (var modifierVirtualKey in _suppressedModifiers)
            {
                // SendInput updates global key state; a matching window message also clears state
                // already observed by a game that ignores injected SendInput events.
                _inputSender.SendKeyUp(modifierVirtualKey, InputMethod.SendInput, _logger);
                if (_inputMethod == InputMethod.WindowMessage)
                {
                    _inputSender.SendKeyUp(modifierVirtualKey, InputMethod.WindowMessage, _logger);
                }
            }
        }

        private void ReleaseRepeatKeys()
        {
            try
            {
                if (_repeatKeyIsDown)
                {
                    ReleaseRepeatKey();
                }
                else
                {
                    // Belt and braces: a target that missed a key-up must not keep the key held.
                    _inputSender.SendKeyUp(_virtualKey, _inputMethod, _logger);
                }

                ReleaseSuppressedModifiers();
            }
            catch (Exception exception)
            {
                _logger.Warning($"Keyboard repeat cleanup failed vk=0x{_virtualKey:X2}. {exception.Message}");
            }
        }

        private void RegisterPhysicalModifierSuppression()
        {
            if (_suppressPhysicalModifiers == HotkeyModifiers.None)
            {
                return;
            }

            _physicalModifierSuppression = _keyboardEvents.Watch(
                new KeyboardEventWatchOptions(
                    IncludeInjected: false,
                    Blocking: true,
                    KeyFilter: KeyboardKeyRules.GetAllModifierVirtualKeys(_suppressPhysicalModifiers)),
                _ => true);
        }

        private void LogProgressIfNeeded()
        {
            if (_tickCount - _lastLoggedTickCount < ProgressLogEveryTicks)
            {
                return;
            }

            _lastLoggedTickCount = _tickCount;
            var elapsedMs = Math.Max(1, _stopwatch.Elapsed.TotalMilliseconds);
            _logger.Info(
                $"Keyboard repeat progress vk=0x{_virtualKey:X2} ticks={_tickCount} elapsedMs={elapsedMs:F0} " +
                $"effectiveIntervalMs={elapsedMs / _tickCount:F2}.");
        }

        private void LogStop()
        {
            var elapsedMs = Math.Max(1, _stopwatch.Elapsed.TotalMilliseconds);
            _logger.Info(
                $"Keyboard repeat stopped vk=0x{_virtualKey:X2} ticks={_tickCount} elapsedMs={elapsedMs:F0} " +
                $"requestedIntervalMs={_intervalMs} effectiveIntervalMs={elapsedMs / Math.Max(1, _tickCount):F2}.");
        }
    }
}
