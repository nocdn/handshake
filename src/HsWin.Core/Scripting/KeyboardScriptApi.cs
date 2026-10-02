using HsWin.Core.Hotkeys;
using HsWin.Core.Input;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public sealed class KeyboardScriptApi
{
    private readonly IKeyboardEventService _keyboardEvents;
    private readonly IKeyboardInputService _keyboardInput;
    private readonly IRuntimeLogger _logger;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal KeyboardScriptApi(
        IKeyboardEventService keyboardEvents,
        IKeyboardInputService keyboardInput,
        IRuntimeLogger logger,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _keyboardEvents = keyboardEvents;
        _keyboardInput = keyboardInput;
        _logger = logger;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public ScriptResourceHandle Watch(object? callback, object? options = null)
    {
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Keyboard watch callback must be a JavaScript function.", nameof(callback));
        }

        var parsedOptions = KeyboardScriptOptionsParser.ParseWatchOptions(options);
        var warnedAboutNonBlockingSwallow = 0;
        var registration = _keyboardEvents.Watch(
            parsedOptions,
            keyboardEvent =>
            {
                // Primitive fields marshal into V8 far cheaper than a serialized JSON string;
                // bootstrap.js rebuilds the frozen event object from them.
                var args = ScriptEventArguments.Keyboard(keyboardEvent);
                if (parsedOptions.Blocking)
                {
                    // Blocking watchers run on WH_KEYBOARD_LL — fail-open if the script gate is busy
                    // so a wedged UI callback cannot freeze the physical keyboard.
                    return _callbacks.TryInvokeScriptCallbackFailOpen(scriptFunction, out var blockingResult, args)
                        && ScriptArgumentReader.IsTruthy(blockingResult);
                }

                var result = _callbacks.InvokeScriptCallback(scriptFunction, args);
                if (ScriptArgumentReader.IsTruthy(result) && Interlocked.Exchange(ref warnedAboutNonBlockingSwallow, 1) == 0)
                {
                    // Once per watch: this fires at typing rate.
                    _logger.Warning("Non-blocking hs.keyboard.watch() callback returned true; use { blocking: true } when a watcher must swallow keyboard input.");
                }

                return false;
            });

        var handle = new ScriptResourceHandle(registration);
        _trackResource(handle);
        _logger.Info(
            $"Script hs.keyboard.watch() registered includeInjected={parsedOptions.IncludeInjected} blocking={parsedOptions.Blocking} " +
            $"prepend={parsedOptions.Prepend} keys={FormatKeyFilter(parsedOptions.KeyFilter)}.");
        return handle;
    }

    /// <summary>
    /// Makes <paramref name="sourceKey"/> act as <paramref name="targetKey"/>: the target goes down
    /// while the source is held (auto-repeat included) and up when it is released.
    /// </summary>
    public ScriptResourceHandle Remap(object? sourceKey, object? targetKey)
    {
        var sourceVirtualKey = HotkeyParser.ParseVirtualKey(sourceKey);
        var targetVirtualKey = HotkeyParser.ParseVirtualKey(targetKey);
        var remap = new KeyRemap(sourceVirtualKey, targetVirtualKey, _keyboardInput);
        remap.Registration = _keyboardEvents.Watch(
            new KeyboardEventWatchOptions(
                IncludeInjected: false,
                Blocking: true,
                KeyFilter: new HashSet<uint> { sourceVirtualKey },
                Prepend: true),
            remap.Handle);

        var handle = new ScriptResourceHandle(remap);
        _trackResource(handle);
        _logger.Info(
            $"Script hs.keyboard.remap('{KeyboardKeyRules.GetDisplayName(sourceVirtualKey)}', '{KeyboardKeyRules.GetDisplayName(targetVirtualKey)}') " +
            $"registered sourceVk=0x{sourceVirtualKey:X2} targetVk=0x{targetVirtualKey:X2}.");
        return handle;
    }

    private static string FormatKeyFilter(IReadOnlySet<uint>? keyFilter)
    {
        return keyFilter is { Count: > 0 }
            ? string.Join(",", keyFilter.Select(key => $"0x{key:X2}"))
            : "<all>";
    }

    public void Tap(object? key, object? options = null)
    {
        var virtualKey = HotkeyParser.ParseVirtualKey(key);
        var parsedOptions = KeyboardScriptOptionsParser.ParseTapOptions(options);
        _keyboardInput.Tap(virtualKey, parsedOptions);
    }

    public RepeatScriptHandle Repeat(object? key, object? options = null)
    {
        return StartRepeat(key, options, pulse: false);
    }

    public RepeatScriptHandle RepeatPulse(object? key, object? options = null)
    {
        return StartRepeat(key, options, pulse: true);
    }

    private RepeatScriptHandle StartRepeat(object? key, object? options, bool pulse)
    {
        var virtualKey = HotkeyParser.ParseVirtualKey(key);
        var parsedOptions = KeyboardScriptOptionsParser.ParseRepeatOptions(options);
        KeyboardRepeatRules.Validate(virtualKey, parsedOptions, pulse);
        var displayName = KeyboardKeyRules.GetDisplayName(virtualKey);

        var handle = new RepeatScriptHandle(_keyboardInput.Repeat(virtualKey, parsedOptions));
        _trackResource(handle);
        var methodName = pulse ? "repeatPulse" : "repeat";
        _logger.Info(
            $"Script hs.keyboard.{methodName}('{displayName}') " +
            $"intervalMs={parsedOptions.IntervalMs} " +
            $"keyDownMs={parsedOptions.KeyDownMs} " +
            $"modifiers=0x{(uint)parsedOptions.Modifiers:X} " +
            $"suppressModifiers=0x{(uint)parsedOptions.SuppressPhysicalModifiers:X} " +
            $"inputMethod={InputMethodParser.GetDisplayName(parsedOptions.InputMethod)}.");
        return handle;
    }

    public void StopRepeat()
    {
        _keyboardInput.StopActiveRepeat();
        _logger.Info("Script hs.keyboard.stopRepeat() requested.");
    }

    public void KeyDown(object? key)
    {
        var virtualKey = HotkeyParser.ParseVirtualKey(key);
        _keyboardInput.KeyDown(virtualKey);
        _logger.Info($"Script hs.keyboard.keyDown('{KeyboardKeyRules.GetDisplayName(virtualKey)}') requested.");
    }

    public void KeyUp(object? key)
    {
        var virtualKey = HotkeyParser.ParseVirtualKey(key);
        _keyboardInput.KeyUp(virtualKey);
        _logger.Info($"Script hs.keyboard.keyUp('{KeyboardKeyRules.GetDisplayName(virtualKey)}') requested.");
    }

    public bool IsDown(object? key)
    {
        var virtualKey = HotkeyParser.ParseVirtualKey(key);
        return _keyboardEvents.IsKeyDown(virtualKey);
    }

    /// <summary>
    /// One remap's state. Remap matches fire at typing rate on the WH_KEYBOARD_LL thread, so
    /// nothing here logs per keystroke.
    /// </summary>
    private sealed class KeyRemap(uint sourceVirtualKey, uint targetVirtualKey, IKeyboardInputService keyboardInput) : IDisposable
    {
        private int _targetDown;

        public IDisposable? Registration { get; set; }

        public bool Handle(KeyboardEventSnapshot keyboardEvent)
        {
            if (keyboardEvent.KeyCode != sourceVirtualKey)
            {
                return false;
            }

            if (keyboardEvent.IsKeyDown)
            {
                Volatile.Write(ref _targetDown, 1);
                keyboardInput.KeyDown(targetVirtualKey);
                return true;
            }

            // A source key that went down before the remap existed must release normally.
            if (Interlocked.Exchange(ref _targetDown, 0) == 0)
            {
                return false;
            }

            keyboardInput.KeyUp(targetVirtualKey);
            return true;
        }

        public void Dispose()
        {
            Registration?.Dispose();
            if (Interlocked.Exchange(ref _targetDown, 0) != 0)
            {
                // Removing the remap while the source is held must not leave the target stuck.
                keyboardInput.KeyUp(targetVirtualKey);
            }
        }
    }
}
