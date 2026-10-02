using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HsWin.App.Native;

/// <summary>Read-only queries about a top-level window and its owning process.</summary>
internal static partial class NativeWindowInfo
{
    public static string GetTitle(nint windowHandle)
    {
        var length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowText(windowHandle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    public static uint GetProcessId(nint windowHandle)
    {
        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        return processId;
    }

    /// <summary>The process name, or null when the process has exited or cannot be opened.</summary>
    public static string? GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(nint windowHandle);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(nint windowHandle, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}

/// <summary><c>ShowWindow</c> / <c>WINDOWPLACEMENT.showCmd</c> values.</summary>
internal static class ShowWindowCommand
{
    public const int Hide = 0;
    public const int ShowNormal = 1;
    public const int ShowMinimized = 2;
    public const int ShowMaximized = 3;
    public const int ShowNoActivate = 4;
    public const int Show = 5;
    public const int Restore = 9;
}
