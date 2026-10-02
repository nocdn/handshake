using System.Runtime.InteropServices;

namespace HsWin.App.Native;

/// <summary>Win32 <c>POINT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;

    public int Y;
}

/// <summary>Win32 <c>RECT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect(int left, int top, int right, int bottom)
{
    public int Left = left;

    public int Top = top;

    public int Right = right;

    public int Bottom = bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}

/// <summary>Win32 <c>MONITORINFO</c>; create with <see cref="Create"/> so the size field is set.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMonitorInfo
{
    public uint Size;

    public NativeRect Monitor;

    public NativeRect WorkArea;

    public uint Flags;

    public static NativeMonitorInfo Create() =>
        new() { Size = (uint)Marshal.SizeOf<NativeMonitorInfo>() };
}

/// <summary>Win32 <c>MSG</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public IntPtr Hwnd;

    public uint Message;

    public IntPtr WParam;

    public IntPtr LParam;

    public uint Time;

    public NativePoint Point;
}
