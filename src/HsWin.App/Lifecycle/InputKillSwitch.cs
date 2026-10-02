using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Lifecycle;

/// <summary>Stops everything HsWin may be injecting; each step is isolated so one failure never skips the rest.</summary>
internal interface IInputKillSwitch
{
    void StopRepeats(string context);

    void ReleaseHeldKeys(string context);
}

internal sealed class InputKillSwitch(
    IMouseInputService mouse,
    IKeyboardInputService keyboard,
    IRuntimeLogger logger) : IInputKillSwitch
{
    public void StopRepeats(string context)
    {
        Run(context, "mouse StopActiveRepeat", mouse.StopActiveRepeat);
        Run(context, "keyboard StopActiveRepeat", keyboard.StopActiveRepeat);
    }

    public void ReleaseHeldKeys(string context)
    {
        Run(context, "keyboard ReleaseHeldKeys", keyboard.ReleaseHeldKeys);
    }

    private void Run(string context, string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.Error($"{context}: {operation} failed.", exception);
        }
    }
}
