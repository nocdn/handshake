using System.Runtime.InteropServices;
using HsWin.Core.Keyboard;

namespace HsWin.App.Native;

/// <summary>A hardware scan code; <see cref="IsExtended"/> means the key sends the <c>E0</c> prefix.</summary>
internal readonly record struct ScanCode(ushort Code, bool IsExtended);

/// <summary>
/// Maps virtual keys to the scan codes physical keyboards send, shared by the <c>SendInput</c> and
/// window-message senders.
/// </summary>
internal static partial class KeyboardScanCodes
{
    private const uint MapVkToVsc = 0;
    private const uint MapVkToVscEx = 4;
    private const uint ExtendedPrefix = 0xE0;
    private const uint PausePrefix = 0xE1;

    public static ScanCode Resolve(uint virtualKey)
    {
        // Left/right modifiers are fixed by hardware, and MapVirtualKey is unreliable for the
        // sided VKs on some layouts.
        return virtualKey switch
        {
            KeyboardKeyRules.VkShift or KeyboardKeyRules.VkLeftShift => new ScanCode(0x2A, false),
            KeyboardKeyRules.VkRightShift => new ScanCode(0x36, false),
            KeyboardKeyRules.VkControl or KeyboardKeyRules.VkLeftControl => new ScanCode(0x1D, false),
            KeyboardKeyRules.VkRightControl => new ScanCode(0x1D, true),
            KeyboardKeyRules.VkMenu or KeyboardKeyRules.VkLeftMenu => new ScanCode(0x38, false),
            KeyboardKeyRules.VkRightMenu => new ScanCode(0x38, true),
            _ => FromMappedValue(virtualKey, MapVirtualKey(virtualKey))
        };
    }

    /// <summary>
    /// Interprets a <c>MAPVK_VK_TO_VSC_EX</c> result: the high byte is the <c>E0</c>/<c>E1</c>
    /// prefix, the low byte the scan code. Returns code 0 when the key must be sent by virtual key.
    /// </summary>
    internal static ScanCode FromMappedValue(uint virtualKey, uint mapped)
    {
        var prefix = (mapped >> 8) & 0xFF;

        // Pause (E1 1D 45) has no single scan code; its low byte alone would be Right Ctrl.
        if (prefix == PausePrefix)
        {
            return new ScanCode(0, false);
        }

        var isExtended = prefix == ExtendedPrefix || KeyboardKeyRules.IsExtendedVirtualKey(virtualKey);
        return new ScanCode((ushort)(mapped & 0xFF), isExtended);
    }

    private static uint MapVirtualKey(uint virtualKey)
    {
        var mapped = MapVirtualKeyNative(virtualKey, MapVkToVscEx);
        return mapped != 0 ? mapped : MapVirtualKeyNative(virtualKey, MapVkToVsc);
    }

    // Windows exports MapVirtualKeyA/W, not an undecorated MapVirtualKey entry point.
    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKeyNative(uint code, uint mapType);
}
