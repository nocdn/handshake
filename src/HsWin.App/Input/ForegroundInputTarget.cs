using System.Runtime.InteropServices;
using HsWin.App.Native;
using HsWin.Core.Logging;

namespace HsWin.App.Input;

/// <summary>
/// Finds the window that should receive posted input: the keyboard-focused child of the
/// foreground window (an edit box, a game's render view), not the top-level frame.
/// </summary>
internal static partial class ForegroundInputTarget
{
    public static nint Resolve()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            return 0;
        }

        var threadId = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) && info.Focus != 0
            ? info.Focus
            : foreground;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public NativeRect CaretRect;
    }
}

/// <summary>
/// Logs the posted-input target once per change. Only the process is recorded: window titles can
/// contain document names, chat partners or other private text.
/// </summary>
internal sealed class InputTargetLog(string inputKind)
{
    private readonly object _gate = new();
    private nint _lastTarget;

    public void RecordIfChanged(nint target, IRuntimeLogger? logger)
    {
        if (logger is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_lastTarget == target)
            {
                return;
            }

            _lastTarget = target;
        }

        var processId = NativeWindowInfo.GetProcessId(target);
        logger.Info(
            $"{inputKind} window-message target hwnd=0x{target.ToInt64():X} processId={processId} " +
            $"processName='{NativeWindowInfo.GetProcessName(processId) ?? string.Empty}'.");
    }
}
