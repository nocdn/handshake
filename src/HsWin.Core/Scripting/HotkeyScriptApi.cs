using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using Microsoft.ClearScript;
using System.Globalization;

namespace HsWin.Core.Scripting;

public sealed class HotkeyScriptApi
{
    private readonly IHotkeyRegistrar _hotkeys;
    private readonly IKeyboardEventService _keyboardEvents;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal HotkeyScriptApi(
        IHotkeyRegistrar hotkeys,
        IKeyboardEventService keyboardEvents,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _hotkeys = hotkeys;
        _keyboardEvents = keyboardEvents;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public ScriptResourceHandle Bind(object? modifiers, object? key, object? pressedFn)
    {
        if (pressedFn is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Hotkey callback must be a JavaScript function.", nameof(pressedFn));
        }

        var definition = HotkeyParser.Parse(modifiers, key);
        var handle = new ScriptResourceHandle(_hotkeys.Register(definition, () => _callbacks.InvokeHotkeyCallback(scriptFunction)));
        _trackResource(handle);
        return handle;
    }

    public ScriptResourceHandle BindHeld(object? modifiers, object? key, object? pressedFn, object? releasedFn, object? options = null)
    {
        if (pressedFn is not ScriptObject pressedFunction)
        {
            throw new ArgumentException("Held hotkey pressed callback must be a JavaScript function.", nameof(pressedFn));
        }

        if (releasedFn is not ScriptObject releasedFunction)
        {
            throw new ArgumentException("Held hotkey released callback must be a JavaScript function.", nameof(releasedFn));
        }

        var parsedOptions = ParseHeldOptions(options);
        var definition = HotkeyParser.Parse(modifiers, key);
        if (definition.InputKind == HotkeyInputKind.MouseButton)
        {
            if (parsedOptions.Repeat)
            {
                throw new ArgumentException("Mouse held hotkeys do not support repeat/retrigger.", nameof(options));
            }

            var buttonName = GetMouseButtonName(definition);
            var mouseRegistration = _hotkeys.RegisterHeld(
                definition,
                () => _callbacks.InvokeScriptCallback(pressedFunction, "mousedown", buttonName, true, false),
                () => _callbacks.InvokeScriptCallback(releasedFunction, "mouseup", buttonName, false, true),
                parsedOptions.Blocking);
            var mouseHandle = new ScriptResourceHandle(mouseRegistration);
            _trackResource(mouseHandle);
            return mouseHandle;
        }

        if (KeyboardKeyRules.IsModifierVirtualKey(definition.VirtualKey))
        {
            throw new ArgumentException("Held hotkey key must be a non-modifier key.", nameof(key));
        }

        var handler = new HeldHotkeyHandler(definition, parsedOptions, pressedFunction, releasedFunction, _callbacks);
        var watch = _keyboardEvents.Watch(
            new KeyboardEventWatchOptions(
                parsedOptions.IncludeInjected,
                parsedOptions.Blocking,
                KeyFilter: CreateHeldHotkeyKeyFilter(definition)),
            handler.Handle);
        var handle = new ScriptResourceHandle(watch);
        _trackResource(handle);
        return handle;
    }

    /// <summary>
    /// Restricts the held-hotkey watch to keys its handler can act on (the hotkey key plus every
    /// modifier VK, since the release path also matches modifier key-ups after activation). The
    /// dispatcher then skips unrelated keystrokes before any callback work runs.
    /// </summary>
    private static HashSet<uint> CreateHeldHotkeyKeyFilter(HotkeyDefinition definition)
    {
        var keyFilter = new HashSet<uint>(KeyboardKeyRules.AllModifierVirtualKeys)
        {
            definition.VirtualKey
        };
        return keyFilter;
    }

    private static string GetMouseButtonName(HotkeyDefinition definition)
    {
        return definition.MouseButton switch
        {
            HotkeyMouseButton.Middle => "middle",
            HotkeyMouseButton.XButton1 => "back",
            HotkeyMouseButton.XButton2 => "forward",
            _ => throw new ArgumentException("Mouse held hotkey requires a supported mouse button.", nameof(definition))
        };
    }

    private static HotkeyHeldOptions ParseHeldOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return HotkeyHeldOptions.Default;
        }

        return new HotkeyHeldOptions(
            ScriptArgumentReader.OptionalBoolean(ScriptArgumentReader.GetPropertyValue(value, "includeInjected"), "includeInjected", HotkeyHeldOptions.Default.IncludeInjected),
            ScriptArgumentReader.OptionalBoolean(ScriptArgumentReader.GetPropertyValue(value, "blocking", "swallow"), "blocking", HotkeyHeldOptions.Default.Blocking),
            ScriptArgumentReader.OptionalBoolean(ScriptArgumentReader.GetPropertyValue(value, "allowExtraModifiers", "allowExtra"), "allowExtraModifiers", HotkeyHeldOptions.Default.AllowExtraModifiers),
            ScriptArgumentReader.OptionalBoolean(ScriptArgumentReader.GetPropertyValue(value, "repeat", "retrigger"), "repeat", HotkeyHeldOptions.Default.Repeat));
    }

    /// <summary>
    /// Press/release state for a keyboard held hotkey. Modifier events always pass through so the
    /// OS never misses a Ctrl/Alt/Shift/Win change, and the trigger key-up is swallowed exactly
    /// when its key-down was, so neither Windows nor the focused app sees an unmatched key.
    /// </summary>
    internal sealed class HeldHotkeyHandler
    {
        private readonly HotkeyDefinition _definition;
        private readonly HotkeyHeldOptions _options;
        private readonly ScriptObject _pressedFunction;
        private readonly ScriptObject _releasedFunction;
        private readonly ScriptCallbackInvoker _callbacks;
        private bool _active;
        private bool _triggerDownSwallowed;

        public HeldHotkeyHandler(
            HotkeyDefinition definition,
            HotkeyHeldOptions options,
            ScriptObject pressedFunction,
            ScriptObject releasedFunction,
            ScriptCallbackInvoker callbacks)
        {
            _definition = definition;
            _options = options;
            _pressedFunction = pressedFunction;
            _releasedFunction = releasedFunction;
            _callbacks = callbacks;
        }

        public bool Handle(KeyboardEventSnapshot snapshot)
        {
            if (snapshot.IsModifier)
            {
                if (_active && !ModifiersMatch(snapshot.ModifierFlags))
                {
                    Release(snapshot);
                }

                return false;
            }

            if (snapshot.KeyCode != _definition.VirtualKey)
            {
                return false;
            }

            if (snapshot.IsKeyUp)
            {
                if (_active)
                {
                    Release(snapshot);
                }

                var swallowKeyUp = _triggerDownSwallowed;
                _triggerDownSwallowed = false;
                return swallowKeyUp;
            }

            if (!snapshot.IsKeyDown || !ModifiersMatch(snapshot.ModifierFlags))
            {
                // A key-down that reaches the OS needs its key-up to reach it too.
                _triggerDownSwallowed = false;
                return false;
            }

            if ((!_active || _options.Repeat) && !Press(snapshot))
            {
                // Fail-open: the script gate was busy, so this key-down goes to the OS.
                _triggerDownSwallowed = false;
                return false;
            }

            _triggerDownSwallowed = _options.Blocking;
            return _options.Blocking;
        }

        private bool Press(KeyboardEventSnapshot snapshot)
        {
            if (!InvokeHeldCallback(_pressedFunction, snapshot))
            {
                return false;
            }

            _active = true;
            return true;
        }

        private void Release(KeyboardEventSnapshot snapshot)
        {
            // Clear state first: even a fail-open skip must not leave the hotkey stuck active.
            _active = false;
            _ = InvokeHeldCallback(_releasedFunction, snapshot);
        }

        /// <returns>false when a blocking fail-open skip occurred (script gate busy).</returns>
        private bool InvokeHeldCallback(ScriptObject function, KeyboardEventSnapshot snapshot)
        {
            var arguments = ScriptEventArguments.Keyboard(snapshot);
            if (_options.Blocking)
            {
                return _callbacks.TryInvokeScriptCallbackFailOpen(function, out _, arguments);
            }

            _callbacks.InvokeScriptCallback(function, arguments);
            return true;
        }

        private bool ModifiersMatch(uint modifierFlags)
        {
            var modifiers = (HotkeyModifiers)modifierFlags;
            return _options.AllowExtraModifiers
                ? (modifiers & _definition.Modifiers) == _definition.Modifiers
                : modifiers == _definition.Modifiers;
        }
    }
}
