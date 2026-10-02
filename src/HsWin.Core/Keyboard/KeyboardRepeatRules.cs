namespace HsWin.Core.Keyboard;

/// <summary>
/// Which repeat shapes are allowed. An instantaneous repeat taps (edge-triggered); a pulse holds the
/// key for <see cref="KeyboardRepeatOptions.KeyDownMs"/> so applications that sample key state see it.
/// </summary>
public static class KeyboardRepeatRules
{
    /// <exception cref="ArgumentException">The options do not fit <c>repeat</c> or <c>repeatPulse</c>.</exception>
    public static void Validate(uint virtualKey, KeyboardRepeatOptions options, bool pulse)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (pulse)
        {
            if (options.KeyDownMs <= 0)
            {
                throw new ArgumentException(
                    "hs.keyboard.repeatPulse() requires keyDownMs (or holdMs/pressDurationMs) greater than 0.",
                    nameof(options));
            }

            return;
        }

        if (options.KeyDownMs > 0)
        {
            throw new ArgumentException(
                "Use hs.keyboard.repeatPulse() for repeats with a held key-down phase.",
                nameof(options));
        }

        if (KeyboardKeyRules.IsModifierVirtualKey(virtualKey))
        {
            throw new ArgumentException(
                "Modifier repeats require hs.keyboard.repeatPulse() with keyDownMs greater than 0.",
                nameof(virtualKey));
        }
    }
}
