using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;

namespace HsWin.Core.Magnifier;

/// <summary>Native held binding: no JavaScript callback or script lock on the input path.</summary>
public sealed class MagnifierHeldBinding : IDisposable
{
    private readonly IMagnifierSession _lens;
    private readonly HotkeyDefinition _key;
    private readonly IDisposable _watch;
    private readonly object _gate = new();
    private bool _active;
    private bool _swallowed;
    private bool _disposed;

    public MagnifierHeldBinding(IKeyboardEventService keyboard, HotkeyDefinition key, IMagnifierSession lens)
    {
        _key = key;
        _lens = lens;
        try
        {
            _watch = keyboard.Watch(new KeyboardEventWatchOptions(false, true,
                new HashSet<uint>(KeyboardKeyRules.AllModifierVirtualKeys) { key.VirtualKey }, Prepend: true), Handle);
        }
        catch
        {
            lens.Dispose();
            throw;
        }
    }

    private bool Handle(KeyboardEventSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed || snapshot.IsInjected)
            {
                return false;
            }

            var matches = (HotkeyModifiers)snapshot.ModifierFlags == _key.Modifiers;
            if (snapshot.IsKeyUp && snapshot.KeyCode == _key.VirtualKey)
            {
                if (_active) _lens.Hide();
                _active = false;
                var swallowed = _swallowed;
                _swallowed = false;
                return swallowed;
            }

            if (_active && snapshot.IsModifier && !matches)
            {
                _active = false;
                _lens.Hide();
                // Modifier releases still reach the foreground app.
                return false;
            }

            if (snapshot.IsKeyDown && snapshot.KeyCode == _key.VirtualKey)
            {
                if (_swallowed) return true; // Suppress autorepeat until a fresh press, even after modifier release.
                if (!matches) return false;
                _lens.Show();
                _active = _swallowed = true;
                return true;
            }

            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active = false;
            _lens.Hide();
        }

        _watch.Dispose();
        _lens.Dispose();
    }
}
