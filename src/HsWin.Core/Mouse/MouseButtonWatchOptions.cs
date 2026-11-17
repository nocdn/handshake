namespace HsWin.Core.Mouse;

/// <summary>Blocking watches swallow presses natively; callbacks always run off-hook.</summary>
public sealed record MouseButtonWatchOptions(bool IncludeInjected = false, bool Blocking = false);

public sealed record MouseButtonEventSnapshot(
    MouseButton Button, bool IsDown, bool IsInjected, uint ModifierFlags, int X, int Y);
