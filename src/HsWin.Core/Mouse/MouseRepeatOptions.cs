using HsWin.Core.Input;

namespace HsWin.Core.Mouse;

public sealed record MouseRepeatOptions(
    int IntervalMs,
    InputMethod InputMethod = InputMethod.SendInput)
{
    public static MouseRepeatOptions Default { get; } = new(RepeatTiming.DefaultIntervalMs, InputMethod.SendInput);
}
