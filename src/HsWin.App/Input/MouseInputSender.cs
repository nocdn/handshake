using System.ComponentModel;
using HsWin.App.Native;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Input;

/// <summary>
/// Sends mouse button clicks and vertical wheel events with SendInput.
/// </summary>
internal static class MouseInputSender
{
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventXDown = 0x0080;
    private const uint MouseEventXUp = 0x0100;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;
    private const uint MouseEventWheel = 0x0800;

    internal static NativeInput BuildScrollInput(int delta)
    {
        return NativeInputSender.Mouse(MouseEventWheel, unchecked((uint)delta));
    }

    internal static void SendScroll(int delta, IRuntimeLogger? logger = null)
    {
        ReadOnlySpan<NativeInput> inputs = [BuildScrollInput(delta)];
        if (NativeInputSender.TrySend(inputs, out var sentCount, out var error))
        {
            return;
        }

        logger?.Warning($"SendInput sent {sentCount}/1 mouse wheel events delta={delta} win32=0x{error:X}.");
        throw new Win32Exception(error, "Could not send mouse wheel input.");
    }

    internal static void SendClick(MouseButton button, IRuntimeLogger? logger = null)
    {
        var (downFlags, upFlags, mouseData) = GetButtonInput(button);
        ReadOnlySpan<NativeInput> inputs =
        [
            NativeInputSender.Mouse(downFlags, mouseData),
            NativeInputSender.Mouse(upFlags, mouseData)
        ];

        if (NativeInputSender.TrySend(inputs, out var sentCount, out var error))
        {
            return;
        }

        var buttonName = MouseButtonParser.GetDisplayName(button);
        logger?.Warning($"SendInput sent {sentCount}/{inputs.Length} mouse events button={buttonName} win32=0x{error:X}.");
        throw new Win32Exception(error, $"Could not send {buttonName} mouse input.");
    }

    private static (uint DownFlags, uint UpFlags, uint MouseData) GetButtonInput(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => (MouseEventLeftDown, MouseEventLeftUp, 0),
            MouseButton.Right => (MouseEventRightDown, MouseEventRightUp, 0),
            MouseButton.Middle => (MouseEventMiddleDown, MouseEventMiddleUp, 0),
            MouseButton.XButton1 => (MouseEventXDown, MouseEventXUp, XButton1),
            MouseButton.XButton2 => (MouseEventXDown, MouseEventXUp, XButton2),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
    }
}
