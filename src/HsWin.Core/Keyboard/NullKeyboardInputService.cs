using HsWin.Core.Input;

namespace HsWin.Core.Keyboard;

public sealed class NullKeyboardInputService : IKeyboardInputService
{
    public static NullKeyboardInputService Instance { get; } = new();

    private NullKeyboardInputService()
    {
    }

    public void KeyDown(uint virtualKey)
    {
    }

    public void KeyUp(uint virtualKey)
    {
    }

    public void Tap(uint virtualKey, KeyboardTapOptions options)
    {
    }

    public IRepeatSession Repeat(uint virtualKey, KeyboardRepeatOptions options)
    {
        return new NullRepeatSession(options.IntervalMs);
    }

    public void StopActiveRepeat()
    {
    }

    public void ReleaseHeldKeys()
    {
    }
}
