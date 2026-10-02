using HsWin.App.Keyboard;
using HsWin.App.Native;

namespace HsWin.App.Hotkeys;

/// <summary>The low-level hook calls plus the key-state fallback the mouse hook uses.</summary>
internal interface IMouseHookPlatform : ILowLevelHookPlatform
{
    short GetAsyncKeyState(int virtualKey);
}

internal sealed class Win32MouseHookPlatform : Win32LowLevelHookPlatform, IMouseHookPlatform
{
    public static new Win32MouseHookPlatform Instance { get; } = new();

    private Win32MouseHookPlatform()
    {
    }

    public short GetAsyncKeyState(int virtualKey) => NativeKeyStateReader.GetAsyncState(virtualKey);
}
