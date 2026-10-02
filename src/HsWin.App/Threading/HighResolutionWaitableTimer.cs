using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HsWin.App.Threading;

/// <summary>
/// A Win32 waitable timer armed with relative <see cref="Stopwatch"/> deadlines. High-resolution
/// timers give about 1 ms accuracy without busy waiting or raising the system-wide timer
/// resolution; Windows builds without them (before 10 1803) fall back to a standard timer.
/// </summary>
internal sealed class HighResolutionWaitableTimer : WaitHandle
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint SynchronizeAndModifyState = 0x00100002;

    public HighResolutionWaitableTimer()
    {
        SafeWaitHandle = CreateHandle();
    }

    /// <summary>The next periodic deadline after <paramref name="now"/>, skipping missed periods.</summary>
    public static long NextDeadline(long previous, long now, long period) =>
        previous + ((Math.Max(0, now - previous) / period) + 1) * period;

    /// <summary>Arms the timer to signal after <paramref name="stopwatchTicks"/> (at least 100 ns).</summary>
    public void Arm(long stopwatchTicks)
    {
        // Negative due times are relative, in 100 ns units.
        var dueTime = -Math.Max(1, (long)(stopwatchTicks * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
        if (!SetWaitableTimer(SafeWaitHandle, ref dueTime, 0, 0, 0, false))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not arm the high-resolution timer.");
        }
    }

    private static SafeWaitHandle CreateHandle()
    {
        var handle = CreateWaitableTimerEx(0, null, CreateWaitableTimerHighResolution, SynchronizeAndModifyState);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        handle.Dispose();
        handle = CreateWaitableTimerEx(0, null, 0, SynchronizeAndModifyState);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Could not create a waitable timer.");
        }

        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(
        SafeWaitHandle timer,
        ref long dueTime,
        int period,
        nint completionRoutine,
        nint completionArgument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);
}
