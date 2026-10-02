using System.ComponentModel;
using System.Runtime.InteropServices;
using HsWin.App.Native;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;

namespace HsWin.App.Input;

/// <summary>
/// Posts native key-down/key-up messages directly to the focused window (the focused child of the
/// foreground window).
/// Useful for GLFW/game apps that ignore injected SendInput keyboard events.
/// </summary>
internal static partial class WindowMessageKeyboardInputSender
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;

    private static readonly InputTargetLog TargetLog = new("Keyboard");

    internal static void SendKeyDown(uint virtualKey, IRuntimeLogger? logger = null)
    {
        PostKey(virtualKey, keyUp: false, logger);
    }

    internal static void SendKeyUp(uint virtualKey, IRuntimeLogger? logger = null)
    {
        PostKey(virtualKey, keyUp: true, logger);
    }

    internal static void SendTap(
        uint virtualKey,
        IReadOnlyList<uint>? suppressedModifierVirtualKeys = null,
        IReadOnlyList<uint>? modifierVirtualKeys = null,
        IRuntimeLogger? logger = null)
    {
        // Suppressed physical modifiers must update global key state (SendInput), not just the target window.
        if (suppressedModifierVirtualKeys is { Count: > 0 })
        {
            foreach (var modifierVirtualKey in suppressedModifierVirtualKeys)
            {
                KeyboardInputSender.SendKeyUp(modifierVirtualKey, logger);
            }
        }

        if (modifierVirtualKeys is { Count: > 0 })
        {
            foreach (var modifierVirtualKey in modifierVirtualKeys)
            {
                PostKey(modifierVirtualKey, keyUp: false, logger);
            }
        }

        try
        {
            PostKey(virtualKey, keyUp: false, logger);
            PostKey(virtualKey, keyUp: true, logger);
        }
        finally
        {
            if (modifierVirtualKeys is { Count: > 0 })
            {
                foreach (var modifierVirtualKey in modifierVirtualKeys.Reverse())
                {
                    PostKey(modifierVirtualKey, keyUp: true, logger);
                }
            }
        }
    }

    private static void PostKey(uint virtualKey, bool keyUp, IRuntimeLogger? logger)
    {
        var target = ForegroundInputTarget.Resolve();
        if (target == 0)
        {
            throw new InvalidOperationException("Could not find a focused window for keyboard-message input.");
        }

        TargetLog.RecordIfChanged(target, logger);

        var scanCode = KeyboardScanCodes.Resolve(virtualKey);
        var isAlt = virtualKey is KeyboardKeyRules.VkMenu
            or KeyboardKeyRules.VkLeftMenu
            or KeyboardKeyRules.VkRightMenu;

        var message = (keyUp, isAlt) switch
        {
            (false, true) => WmSysKeyDown,
            (true, true) => WmSysKeyUp,
            (false, false) => WmKeyDown,
            (true, false) => WmKeyUp
        };

        // Real WM_KEY* messages use the generic modifier VK in wParam and encode left/right in
        // the scan-code/extended bits. GLFW follows that convention when translating modifiers.
        nuint wParam = ResolveMessageVirtualKey(virtualKey);
        var lParam = BuildKeyLParam(scanCode, keyUp);

        if (User32.PostMessage(target, message, wParam, lParam))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        logger?.Warning(
            $"PostMessage keyboard failed phase={(keyUp ? "up" : "down")} vk=0x{virtualKey:X2} " +
            $"hwnd=0x{target.ToInt64():X} win32=0x{error:X}.");
        throw new Win32Exception(error, $"Could not post keyboard input for virtual key 0x{virtualKey:X2}.");
    }

    internal static nint BuildKeyLParam(ScanCode scanCode, bool keyUp)
    {
        // bits 0-15: repeat count = 1
        // bits 16-23: scan code
        // bit 24: extended
        // bit 30: previous key state (1 if already down → set on keyup)
        // bit 31: transition state (1 = keyup)
        var lParam = 1 | ((scanCode.Code & 0xFF) << 16);
        if (scanCode.IsExtended)
        {
            lParam |= 1 << 24;
        }

        if (keyUp)
        {
            lParam |= 1 << 30;
            lParam |= 1 << 31;
        }

        return lParam;
    }

    internal static uint ResolveMessageVirtualKey(uint virtualKey)
    {
        return virtualKey switch
        {
            KeyboardKeyRules.VkLeftShift or KeyboardKeyRules.VkRightShift => KeyboardKeyRules.VkShift,
            KeyboardKeyRules.VkLeftControl or KeyboardKeyRules.VkRightControl => KeyboardKeyRules.VkControl,
            KeyboardKeyRules.VkLeftMenu or KeyboardKeyRules.VkRightMenu => KeyboardKeyRules.VkMenu,
            _ => virtualKey
        };
    }

    private static partial class User32
    {
        [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostMessage(nint windowHandle, uint message, nuint wParam, nint lParam);
    }
}
