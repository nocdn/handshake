using HsWin.Core.Hotkeys;
using HsWin.Core.Mouse;

namespace HsWin.Core.Magnifier;

/// <summary>A mouse hold bypasses script/UI callbacks while preserving existing mouse actions.</summary>
public sealed class MagnifierMouseHeldBinding : IDisposable
{
    private readonly object _gate = new();
    private readonly IMagnifierSession _lens;
    private readonly HotkeyDefinition _definition;
    private readonly IDisposable _watch;
    private bool _active;
    private bool _disposed;

    public MagnifierMouseHeldBinding(IMouseButtonEventService mouse, HotkeyDefinition definition, IMagnifierSession lens)
    {
        _lens = lens;
        _definition = definition;
        try { _watch = mouse.WatchPhysicalButtons(Handle); }
        catch { lens.Dispose(); throw; }
    }

    private void Handle(PhysicalMouseButtonEvent input)
    {
        lock (_gate)
        {
            if (_disposed || input.Button != _definition.MouseButton) return;
            if (!input.IsDown)
            {
                if (_active) _lens.Hide();
                _active = false;
            }
            else if (!_active && input.Modifiers == _definition.Modifiers)
            {
                _active = true;
                _lens.Show();
            }
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
