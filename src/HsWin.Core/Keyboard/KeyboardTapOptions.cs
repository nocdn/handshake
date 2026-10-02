using HsWin.Core.Hotkeys;
using HsWin.Core.Input;

namespace HsWin.Core.Keyboard;

public sealed record KeyboardTapOptions(
    HotkeyModifiers SuppressPhysicalModifiers,
    HotkeyModifiers Modifiers,
    InputMethod InputMethod = InputMethod.SendInput)
{
    public static KeyboardTapOptions Default { get; } =
        new(HotkeyModifiers.None, HotkeyModifiers.None, InputMethod.SendInput);
}
