using HsWin.Core.Hotkeys;
using HsWin.Core.Input;

namespace HsWin.Core.Keyboard;

/// <param name="IntervalMs">Time between repeated presses; see <see cref="RepeatTiming"/>.</param>
/// <param name="SuppressPhysicalModifiers">Physical modifiers released and blocked while repeating.</param>
/// <param name="InputMethod">How each press is delivered.</param>
/// <param name="KeyDownMs">Held phase of each press; 0 sends an instantaneous tap.</param>
/// <param name="Modifiers">Modifiers pressed around every repeated key press.</param>
public sealed record KeyboardRepeatOptions(
    int IntervalMs,
    HotkeyModifiers SuppressPhysicalModifiers,
    InputMethod InputMethod = InputMethod.SendInput,
    int KeyDownMs = 0,
    HotkeyModifiers Modifiers = HotkeyModifiers.None)
{
    public const int DefaultKeyDownMs = 0;

    public static KeyboardRepeatOptions Default { get; } =
        new(RepeatTiming.DefaultIntervalMs, HotkeyModifiers.None, InputMethod.SendInput, DefaultKeyDownMs);
}
