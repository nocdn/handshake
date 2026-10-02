using HsWin.Core.Input;

namespace HsWin.Core.Keyboard;

public interface IKeyboardInputService
{
    void KeyDown(uint virtualKey);

    void KeyUp(uint virtualKey);

    void Tap(uint virtualKey, KeyboardTapOptions options);

    IRepeatSession Repeat(uint virtualKey, KeyboardRepeatOptions options);

    /// <summary>
    /// Stops the active global keyboard-repeat session, if any. Safe to call when idle.
    /// </summary>
    void StopActiveRepeat();

    /// <summary>
    /// Releases every key currently held via <see cref="KeyDown"/> that has not been
    /// matched by <see cref="KeyUp"/>. Used by emergency stop and config reload so a
    /// script-held key (for example hands-free auto-walk) can never stick. Safe to
    /// call when nothing is held.
    /// </summary>
    void ReleaseHeldKeys();
}
