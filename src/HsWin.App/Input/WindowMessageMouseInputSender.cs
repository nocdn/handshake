using System.ComponentModel;
using System.Runtime.InteropServices;
using HsWin.App.Native;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Input;

/// <summary>
/// Posts native mouse-button messages directly to the focused window, at the cursor's position in
/// that window's client area. This is useful for GLFW applications that do not consume injected
/// SendInput events.
/// </summary>
internal static partial class WindowMessageMouseInputSender
{
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmXButtonUp = 0x020C;
    private const uint MkLButton = 0x0001;
    private const uint MkRButton = 0x0002;
    private const uint MkMButton = 0x0010;
    private const uint MkXButton1 = 0x0020;
    private const uint MkXButton2 = 0x0040;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;

    private static readonly InputTargetLog TargetLog = new("Mouse");

    internal static void SendClick(MouseButton button, IRuntimeLogger? logger = null)
    {
        var target = ForegroundInputTarget.Resolve();
        if (target == 0)
        {
            throw new InvalidOperationException("Could not find a focused window for mouse-message input.");
        }

        TargetLog.RecordIfChanged(target, logger);

        var messages = GetButtonMessages(button);
        var position = BuildPointLParam(GetCursorInClient(target));
        PostMessageOrThrow(target, messages.Down, messages.DownWParam, position, button, "down", logger);
        PostMessageOrThrow(target, messages.Up, messages.UpWParam, position, button, "up", logger);
    }

    /// <summary>
    /// The messages for a click. Button-down carries the button's <c>MK_*</c> state flag (the
    /// button is now held); button-up does not. X buttons also name the button in the high word.
    /// </summary>
    internal static (uint Down, nuint DownWParam, uint Up, nuint UpWParam) GetButtonMessages(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => (WmLButtonDown, MkLButton, WmLButtonUp, 0),
            MouseButton.Right => (WmRButtonDown, MkRButton, WmRButtonUp, 0),
            MouseButton.Middle => (WmMButtonDown, MkMButton, WmMButtonUp, 0),
            MouseButton.XButton1 => (WmXButtonDown, MkXButton1 | (XButton1 << 16), WmXButtonUp, XButton1 << 16),
            MouseButton.XButton2 => (WmXButtonDown, MkXButton2 | (XButton2 << 16), WmXButtonUp, XButton2 << 16),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Unsupported mouse button.")
        };
    }

    /// <summary><c>MAKELPARAM(x, y)</c> with signed 16-bit coordinates, as real mouse messages carry.</summary>
    internal static nint BuildPointLParam(NativePoint clientPoint)
    {
        return (nint)(uint)((ushort)(short)clientPoint.X | ((ushort)(short)clientPoint.Y << 16));
    }

    private static NativePoint GetCursorInClient(nint target)
    {
        if (!ScreenGeometry.TryGetCursor(out var cursor))
        {
            return default;
        }

        return User32.ScreenToClient(target, ref cursor) ? cursor : default;
    }

    private static void PostMessageOrThrow(
        nint target,
        uint message,
        nuint wParam,
        nint lParam,
        MouseButton button,
        string phase,
        IRuntimeLogger? logger)
    {
        if (User32.PostMessage(target, message, wParam, lParam))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        logger?.Warning(
            $"PostMessage failed phase={phase} button={MouseButtonParser.GetDisplayName(button)} " +
            $"hwnd=0x{target.ToInt64():X} win32=0x{error:X}.");
        throw new Win32Exception(error, "Could not post mouse input to the focused window.");
    }

    private static partial class User32
    {
        [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(nint windowHandle, uint message, nuint wParam, nint lParam);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ScreenToClient(nint windowHandle, ref NativePoint point);
    }
}
