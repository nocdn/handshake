using System.ComponentModel;
using System.Runtime.InteropServices;
using HsWin.Core.Logging;

namespace HsWin.App.Native;

internal delegate IntPtr LowLevelHookProcedure(int code, IntPtr wParam, IntPtr lParam);

/// <summary>The Win32 calls a low-level hook thread makes; faked in tests.</summary>
internal interface ILowLevelHookPlatform
{
    IntPtr SetWindowsHookEx(int idHook, LowLevelHookProcedure hookProcedure, IntPtr moduleHandle, uint threadId);

    bool UnhookWindowsHookEx(IntPtr hookHandle);

    IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

    int GetMessage(out NativeMessage message, IntPtr windowHandle, uint messageFilterMin, uint messageFilterMax);

    bool TranslateMessage(ref NativeMessage message);

    IntPtr DispatchMessage(ref NativeMessage message);

    bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);

    uint GetCurrentThreadId();

    IntPtr GetModuleHandle(string? moduleName);
}

/// <summary>
/// Owns one WH_KEYBOARD_LL or WH_MOUSE_LL hook on a dedicated message-pump thread, so physical
/// input is never delayed by the UI dispatcher. Not thread-safe: the owning service serializes
/// <see cref="EnsureInstalled"/> and <see cref="Uninstall"/> under its own lock.
/// </summary>
internal sealed class LowLevelHookThread
{
    public const int KeyboardHookId = 13;
    public const int MouseHookId = 14;

    private const int WmQuit = 0x0012;
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(5);

    private readonly int _hookId;
    private readonly string _hookName;
    private readonly string _threadName;
    private readonly LowLevelHookProcedure _procedure;
    private readonly ILowLevelHookPlatform _platform;
    private readonly IRuntimeLogger _logger;
    private IntPtr _hookHandle;
    private uint _threadId;
    private Exception? _installException;

    public LowLevelHookThread(
        int hookId,
        string threadName,
        LowLevelHookProcedure procedure,
        ILowLevelHookPlatform platform,
        IRuntimeLogger logger)
    {
        _hookId = hookId;
        _hookName = hookId == KeyboardHookId ? "WH_KEYBOARD_LL" : "WH_MOUSE_LL";
        _threadName = threadName;

        // The field keeps the delegate alive for as long as Windows can call it.
        _procedure = procedure;
        _platform = platform;
        _logger = logger;
    }

    public bool IsInstalled => Volatile.Read(ref _hookHandle) != IntPtr.Zero;

    public IntPtr CallNext(int code, IntPtr wParam, IntPtr lParam)
    {
        return _platform.CallNextHookEx(Volatile.Read(ref _hookHandle), code, wParam, lParam);
    }

    public void EnsureInstalled()
    {
        if (IsInstalled)
        {
            return;
        }

        _installException = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = _threadName
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(InstallTimeout))
        {
            // Stop a late-finishing install thread so it cannot leave an orphaned hook behind.
            Uninstall();
            var exception = new TimeoutException(
                $"Timed out after {InstallTimeout.TotalMilliseconds:F0}ms while installing {_hookName} hook.");
            _logger.Error($"{_hookName} hook installation timed out.", exception);
            throw exception;
        }

        if (_installException is not null)
        {
            throw _installException;
        }

        if (!IsInstalled)
        {
            throw new InvalidOperationException($"{_hookName} hook thread started without publishing a hook handle.");
        }
    }

    public void Uninstall()
    {
        var hookHandle = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
        if (hookHandle != IntPtr.Zero)
        {
            if (_platform.UnhookWindowsHookEx(hookHandle))
            {
                _logger.Info($"{_hookName} hook uninstalled.");
            }
            else
            {
                _logger.Error(
                    $"{_hookName} hook uninstall failed.",
                    new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not uninstall {_hookName} hook."));
            }
        }

        var threadId = Interlocked.Exchange(ref _threadId, 0);
        if (threadId != 0 && !_platform.PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero))
        {
            _logger.Error(
                $"{_hookName} hook thread stop signal failed.",
                new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not stop the {_hookName} hook thread."));
        }
    }

    private void Run(ManualResetEventSlim ready)
    {
        var threadId = _platform.GetCurrentThreadId();
        Volatile.Write(ref _threadId, threadId);

        var hookHandle = _platform.SetWindowsHookEx(_hookId, _procedure, _platform.GetModuleHandle(null), 0);
        if (hookHandle == IntPtr.Zero)
        {
            var exception = new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not install {_hookName} hook.");
            _installException = exception;
            Volatile.Write(ref _threadId, 0);
            _logger.Error($"{_hookName} hook installation failed.", exception);
            ready.Set();
            return;
        }

        Volatile.Write(ref _hookHandle, hookHandle);
        _logger.Info($"{_hookName} hook installed. Hook=0x{hookHandle.ToInt64():X} threadId={threadId}.");
        ready.Set();

        while (_platform.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            _platform.TranslateMessage(ref message);
            _platform.DispatchMessage(ref message);
        }

        _logger.Info($"{_hookName} hook thread exited threadId={threadId}.");
    }
}

internal partial class Win32LowLevelHookPlatform : ILowLevelHookPlatform
{
    public static Win32LowLevelHookPlatform Instance { get; } = new();

    protected Win32LowLevelHookPlatform()
    {
    }

    public IntPtr SetWindowsHookEx(int idHook, LowLevelHookProcedure hookProcedure, IntPtr moduleHandle, uint threadId) =>
        User32.SetWindowsHookEx(idHook, hookProcedure, moduleHandle, threadId);

    public bool UnhookWindowsHookEx(IntPtr hookHandle) => User32.UnhookWindowsHookEx(hookHandle);

    public IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam) =>
        User32.CallNextHookEx(hookHandle, code, wParam, lParam);

    public int GetMessage(out NativeMessage message, IntPtr windowHandle, uint messageFilterMin, uint messageFilterMax) =>
        User32.GetMessage(out message, windowHandle, messageFilterMin, messageFilterMax);

    public bool TranslateMessage(ref NativeMessage message) => User32.TranslateMessage(ref message);

    public IntPtr DispatchMessage(ref NativeMessage message) => User32.DispatchMessage(ref message);

    public bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam) =>
        User32.PostThreadMessage(threadId, message, wParam, lParam);

    public uint GetCurrentThreadId() => Kernel32.GetCurrentThreadId();

    public IntPtr GetModuleHandle(string? moduleName) => Kernel32.GetModuleHandle(moduleName);

    private static partial class User32
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProcedure hookProcedure, IntPtr moduleHandle, uint threadId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnhookWindowsHookEx(IntPtr hookHandle);

        [LibraryImport("user32.dll")]
        public static partial IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
        public static partial int GetMessage(out NativeMessage message, IntPtr windowHandle, uint messageFilterMin, uint messageFilterMax);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool TranslateMessage(ref NativeMessage message);

        [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
        public static partial IntPtr DispatchMessage(ref NativeMessage message);

        [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    }

    private static partial class Kernel32
    {
        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr GetModuleHandle(string? moduleName);

        [LibraryImport("kernel32.dll")]
        public static partial uint GetCurrentThreadId();
    }
}
