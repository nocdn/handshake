using HsWin.Core.Keyboard;

namespace HsWin.Core.Scripting;

/// <summary>
/// Positional callback arguments shared with <c>bootstrap.js</c>, which rebuilds the event
/// object from them. Keep the order in sync with <c>buildKeyboardEvent</c> there.
/// </summary>
internal static class ScriptEventArguments
{
    public static object?[] Keyboard(KeyboardEventSnapshot snapshot) =>
    [
        snapshot.Type,
        snapshot.KeyCode,
        snapshot.Key,
        snapshot.ModifierFlags,
        snapshot.IsKeyDown,
        snapshot.IsKeyUp,
        snapshot.IsModifier,
        snapshot.IsInjected,
        snapshot.IsExtended
    ];
}
