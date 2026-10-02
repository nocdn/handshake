using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Magnifier;
using HsWin.Core.Mouse;

namespace HsWin.Core.Scripting;

public sealed class MagnifierScriptApi
{
    private readonly IMagnifierService _service;
    private readonly IKeyboardEventService _keyboard;
    private readonly IMouseButtonEventService _mouse;
    private readonly Action<IDisposable> _track;

    internal MagnifierScriptApi(IMagnifierService service, IKeyboardEventService keyboard, IMouseButtonEventService mouse, Action<IDisposable> track)
    {
        _service = service;
        _keyboard = keyboard;
        _mouse = mouse;
        _track = track;
    }

    public MagnifierScriptHandle Create(object? options = null)
    {
        var handle = new MagnifierScriptHandle(_service.Create(MagnifierScriptOptionsParser.Parse(options)));
        _track(handle);
        return handle;
    }

    public ScriptResourceHandle BindHeld(object? modifiers, object? key, object? options = null)
    {
        var definition = HotkeyParser.Parse(modifiers, key);
        if (definition.InputKind == HotkeyInputKind.Keyboard && KeyboardKeyRules.IsModifierVirtualKey(definition.VirtualKey))
        {
            throw new ArgumentException("Magnifier held key must be a non-modifier key or supported mouse button.");
        }

        var lens = _service.Create(MagnifierScriptOptionsParser.Parse(options));
        // The binding takes ownership of the lens and disposes it itself if registration fails.
        var handle = new ScriptResourceHandle(definition.InputKind == HotkeyInputKind.MouseButton
            ? new MagnifierMouseHeldBinding(_mouse, definition, lens)
            : new MagnifierHeldBinding(_keyboard, definition, lens));
        _track(handle);
        return handle;
    }
}

/// <summary>The script handle returned by <c>hs.magnifier.create</c>; disposing it destroys the lens.</summary>
public sealed class MagnifierScriptHandle : ScriptResourceHandle
{
    private readonly IMagnifierSession _session;

    internal MagnifierScriptHandle(IMagnifierSession session)
        : base(session)
    {
        _session = session;
    }

    public bool isVisible => !IsDisposed && _session.IsVisible;

    public void show()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        _session.Show();
    }

    public void hide()
    {
        if (!IsDisposed)
        {
            _session.Hide();
        }
    }

    public void setZoom(object? zoom)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var value = ScriptArgumentReader.RequireDouble(zoom, "zoom", "a number");
        MagnifierOptions.ValidateZoom(value);
        _session.SetZoom(value);
    }
}
