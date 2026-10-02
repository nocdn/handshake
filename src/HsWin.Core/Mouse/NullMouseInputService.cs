using HsWin.Core.Input;

namespace HsWin.Core.Mouse;

public sealed class NullMouseInputService : IMouseInputService
{
    public static NullMouseInputService Instance { get; } = new();

    private NullMouseInputService()
    {
    }

    public void Click(MouseButton button)
    {
    }

    public IRepeatSession Repeat(MouseButton button, MouseRepeatOptions options)
    {
        return new NullRepeatSession(options.IntervalMs);
    }

    public void StopActiveRepeat()
    {
    }
}
