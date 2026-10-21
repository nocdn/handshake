using HsWin.Core.Input;

namespace HsWin.Core.Mouse;

public interface IMouseInputService
{
    void Click(MouseButton button);

    /// <summary>Sends vertical wheel input; positive delta scrolls up, 120 units per notch.</summary>
    void Scroll(int delta);

    IRepeatSession Repeat(MouseButton button, MouseRepeatOptions options);

    /// <summary>
    /// Stops the active global mouse-repeat session, if any. Safe to call when idle.
    /// Use this as a belt-and-suspenders release path so a raced handle cannot keep clicking.
    /// </summary>
    void StopActiveRepeat();
}
