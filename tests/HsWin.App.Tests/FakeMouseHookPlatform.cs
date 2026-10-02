using System.Runtime.InteropServices;
using HsWin.App.Hotkeys;
using HsWin.App.Native;

namespace HsWin.App.Tests;

/// <summary>A scripted WH_MOUSE_LL platform: records installs and lets tests push hook messages.</summary>
internal sealed class FakeMouseHookPlatform : IMouseHookPlatform
{
    private readonly ManualResetEventSlim _quitSignal = new(false);
    private LowLevelHookProcedure? _hookProcedure;

    public int SetHookExCount { get; private set; }

    public int UnhookCount { get; private set; }

    public int QuitPostedCount { get; private set; }

    public uint HookThreadId { get; private set; }

    public ManualResetEventSlim InstallCompleted { get; } = new(false);

    public IntPtr SetWindowsHookEx(int idHook, LowLevelHookProcedure hookProcedure, IntPtr moduleHandle, uint threadId)
    {
        SetHookExCount++;
        _hookProcedure = hookProcedure;
        InstallCompleted.Set();
        return new IntPtr(0x1234);
    }

    public bool UnhookWindowsHookEx(IntPtr hookHandle)
    {
        UnhookCount++;
        return true;
    }

    public IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam) =>
        IntPtr.Zero;

    public short GetAsyncKeyState(int virtualKey) => 0;

    public int GetMessage(out NativeMessage message, IntPtr windowHandle, uint messageFilterMin, uint messageFilterMax)
    {
        _quitSignal.Wait(TimeSpan.FromSeconds(5));
        message = default;
        return 0;
    }

    public bool TranslateMessage(ref NativeMessage message) => true;

    public IntPtr DispatchMessage(ref NativeMessage message) => IntPtr.Zero;

    public bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam)
    {
        if (message == 0x0012)
        {
            QuitPostedCount++;
            _quitSignal.Set();
        }

        return true;
    }

    public uint GetCurrentThreadId()
    {
        HookThreadId = (uint)Environment.CurrentManagedThreadId;
        return HookThreadId;
    }

    public IntPtr GetModuleHandle(string? moduleName) => new IntPtr(1);

    public IntPtr InvokeHook(int message, uint mouseData, uint flags = 0, int x = 0, int y = 0)
    {
        if (_hookProcedure is null)
        {
            throw new InvalidOperationException("Hook procedure was not installed.");
        }

        var hookData = new MouseHookData
        {
            X = x,
            Y = y,
            MouseData = mouseData,
            Flags = flags
        };

        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<MouseHookData>());
        try
        {
            Marshal.StructureToPtr(hookData, pointer, false);
            return _hookProcedure(0, new IntPtr(message), pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>Delivers a wheel message at (5, 6) with the signed delta in the high word.</summary>
    public IntPtr InvokeWheel(int message, int delta) =>
        InvokeHook(message, unchecked((uint)((ushort)delta << 16)), x: 5, y: 6);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X;

        public int Y;

        public uint MouseData;

        public uint Flags;

        public uint Time;

        public UIntPtr ExtraInfo;
    }
}
