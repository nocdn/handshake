using HsWin.Core.Input;
using HsWin.Core.Logging;

namespace HsWin.App.Input;

internal interface IKeyboardInputSender
{
    void SendKeyDown(
        uint virtualKey,
        InputMethod inputMethod = InputMethod.SendInput,
        IRuntimeLogger? logger = null);

    void SendKeyUp(
        uint virtualKey,
        InputMethod inputMethod = InputMethod.SendInput,
        IRuntimeLogger? logger = null);

    void SendTap(
        uint virtualKey,
        IReadOnlyList<uint>? suppressedModifierVirtualKeys = null,
        IReadOnlyList<uint>? modifierVirtualKeys = null,
        InputMethod inputMethod = InputMethod.SendInput,
        IRuntimeLogger? logger = null);
}

internal sealed class NativeKeyboardInputSender : IKeyboardInputSender
{
    public static NativeKeyboardInputSender Instance { get; } = new();

    private NativeKeyboardInputSender()
    {
    }

    public void SendKeyDown(
        uint virtualKey,
        InputMethod inputMethod = InputMethod.SendInput,
        IRuntimeLogger? logger = null)
    {
        if (inputMethod == InputMethod.WindowMessage)
        {
            WindowMessageKeyboardInputSender.SendKeyDown(virtualKey, logger);
            return;
        }

        KeyboardInputSender.SendKeyDown(virtualKey, logger);
    }

    public void SendKeyUp(
        uint virtualKey,
        InputMethod inputMethod = InputMethod.SendInput,
        IRuntimeLogger? logger = null)
    {
        if (inputMethod == InputMethod.WindowMessage)
        {
            WindowMessageKeyboardInputSender.SendKeyUp(virtualKey, logger);
            return;
        }

        KeyboardInputSender.SendKeyUp(virtualKey, logger);
    }

    public void SendTap(
        uint virtualKey,
        IReadOnlyList<uint>? suppressedModifierVirtualKeys = null,
        IReadOnlyList<uint>? modifierVirtualKeys = null,
        InputMethod inputMethod = InputMethod.SendInput,
        IRuntimeLogger? logger = null)
    {
        if (inputMethod == InputMethod.WindowMessage)
        {
            WindowMessageKeyboardInputSender.SendTap(
                virtualKey,
                suppressedModifierVirtualKeys,
                modifierVirtualKeys,
                logger);
            return;
        }

        KeyboardInputSender.SendTap(virtualKey, suppressedModifierVirtualKeys, modifierVirtualKeys, logger);
    }
}
