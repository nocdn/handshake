using HsWin.Core.Hotkeys;

namespace HsWin.Core.Mouse;

public readonly record struct PhysicalMouseButtonEvent(HotkeyMouseButton Button, bool IsDown, HotkeyModifiers Modifiers);

/// <summary>Observers run on the input thread and preserve normal mouse delivery.</summary>
public interface IMouseButtonEventService
{
    IDisposable WatchPhysicalButtons(Action<PhysicalMouseButtonEvent> callback);
}
