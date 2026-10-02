using System.ComponentModel;
using System.Runtime.InteropServices;
using HsWin.App.Native;
using HsWin.App.Threading;
using HsWin.Core.Logging;
using HsWin.Core.Windows;

namespace HsWin.App.Windows;

internal sealed partial class NativeWindowService : IWindowService, IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutofcontext = 0x0000;
    private const uint WineventSkipownprocess = 0x0002;
    private const int ObjidWindow = 0;
    private const int ChildidSelf = 0;
    private const int GaRoot = 2;
    private const int GwOwner = 4;
    private const int GwlExstyle = -20;
    private const long WsExToolwindow = 0x00000080L;
    private const uint SwpNozorder = 0x0004;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNoownerzorder = 0x0200;
    private const uint MonitorDefaulttonearest = 0x00000002;
    private const int DwmwaCloaked = 14;

    private readonly object _gate = new();
    private readonly IRuntimeLogger _logger;
    private readonly DedicatedDispatcherThread _hookThread;
    private readonly WinEventProcedure _eventProcedure;
    private readonly Dictionary<long, FocusWatch> _focusWatches = [];
    private IntPtr _eventHook;
    private long _nextWatchId;
    private int _disposed;

    public NativeWindowService(IRuntimeLogger logger)
    {
        _logger = logger;
        _eventProcedure = HandleWinEvent;
        _hookThread = new DedicatedDispatcherThread("HsWin Window Events");
    }

    public WindowSnapshot? GetFocusedWindow()
    {
        var handle = User32.GetForegroundWindow();
        return TryCreateSnapshot(handle, out var snapshot) ? snapshot : null;
    }

    public WindowSnapshot? GetWindow(string id)
    {
        if (!WindowId.TryParse(id, out var handle))
        {
            return null;
        }

        return TryCreateSnapshot(handle, out var snapshot) ? snapshot : null;
    }

    public WindowMoveResult MoveToScreen(string id, WindowTargetScreen targetScreen, WindowMoveOptions options)
    {
        ArgumentNullException.ThrowIfNull(targetScreen);
        ArgumentNullException.ThrowIfNull(options);

        if (!WindowId.TryParse(id, out var handle) || !User32.IsWindow(handle))
        {
            return WindowMoveResult.NotMoved(id, "window-not-found");
        }

        handle = NormalizeHandle(handle);
        if (!TryGetWindowFrame(handle, out var visibleFrame))
        {
            return WindowMoveResult.NotMoved(id, "window-frame-unavailable");
        }

        if (!TryGetWindowPlacement(handle, out var placement))
        {
            return WindowMoveResult.NotMoved(id, "window-placement-unavailable");
        }

        var currentFrame = ShouldUseRestoreFrame(placement)
            ? ToSnapshot(placement.RcNormalPosition)
            : visibleFrame;
        if (currentFrame.Width <= 0 || currentFrame.Height <= 0)
        {
            currentFrame = visibleFrame;
        }

        var targetArea = options.UseWorkingArea ? targetScreen.WorkingArea : targetScreen.Bounds;
        if (WindowPlacementCalculator.ContainsWindowCenter(targetArea, visibleFrame))
        {
            return WindowMoveResult.AlreadyOnScreen(id, visibleFrame);
        }

        var sourceArea = TryGetWindowMonitorArea(handle, options.UseWorkingArea, out var monitorArea)
            ? monitorArea
            : currentFrame;
        var targetFrame = WindowPlacementCalculator.CalculateTargetFrame(
            currentFrame,
            sourceArea,
            targetArea,
            options);

        if (!TryMoveWindow(handle, targetFrame, placement))
        {
            return WindowMoveResult.NotMoved(id, "move-failed");
        }

        return WindowMoveResult.MovedTo(id, targetFrame);
    }

    public IDisposable WatchFocused(Action<WindowSnapshot> callback)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(callback);

        var watch = new FocusWatch(Interlocked.Increment(ref _nextWatchId), callback, RemoveWatch);
        lock (_gate)
        {
            _focusWatches[watch.Id] = watch;
            _logger.Info($"Window focus watch registered id={watch.Id} count={_focusWatches.Count}.");
        }

        try
        {
            SyncEventHook();
        }
        catch
        {
            watch.Dispose();
            throw;
        }

        return watch;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var watch in _focusWatches.Values)
            {
                watch.MarkDisposed();
            }

            _focusWatches.Clear();
        }

        try
        {
            SyncEventHook();
        }
        catch (TimeoutException exception)
        {
            _logger.Error("Foreground window event hook was not removed before shutdown.", exception);
        }

        _hookThread.Dispose();
    }

    private bool TryMoveWindow(IntPtr handle, WindowRectangleSnapshot targetFrame, WindowPlacement placement)
    {
        var targetRect = FromSnapshot(targetFrame);
        if (WindowMoveStrategySelector.Select(placement.ShowCmd) == WindowMoveStrategy.RestoreMoveAndMaximize)
        {
            placement.RcNormalPosition = targetRect;
            placement.ShowCmd = ShowWindowCommand.ShowNormal;
            if (!User32.SetWindowPlacement(handle, ref placement))
            {
                LogLastWin32Error("SetWindowPlacement failed while setting maximized restore bounds.");
                return false;
            }

            User32.ShowWindow(handle, ShowWindowCommand.Restore);
            if (!TrySetWindowFrame(handle, targetFrame))
            {
                return false;
            }

            User32.ShowWindow(handle, ShowWindowCommand.ShowMaximized);
            return true;
        }

        if (WindowMoveStrategySelector.Select(placement.ShowCmd) == WindowMoveStrategy.SetRestoreBoundsAndRestore)
        {
            placement.RcNormalPosition = targetRect;
            if (!User32.SetWindowPlacement(handle, ref placement))
            {
                LogLastWin32Error("SetWindowPlacement failed while moving window.");
                return false;
            }

            if (placement.ShowCmd == ShowWindowCommand.ShowMinimized)
            {
                User32.ShowWindow(handle, ShowWindowCommand.Restore);
            }

            return true;
        }

        return TrySetWindowFrame(handle, targetFrame);
    }

    private bool TrySetWindowFrame(IntPtr handle, WindowRectangleSnapshot targetFrame)
    {
        if (!User32.SetWindowPos(
            handle,
            IntPtr.Zero,
            targetFrame.X,
            targetFrame.Y,
            targetFrame.Width,
            targetFrame.Height,
            SwpNozorder | SwpNoactivate | SwpNoownerzorder))
        {
            LogLastWin32Error("SetWindowPos failed while moving window.");
            return false;
        }

        return true;
    }

    private static bool ShouldUseRestoreFrame(WindowPlacement placement) =>
        placement.ShowCmd is ShowWindowCommand.ShowMinimized or ShowWindowCommand.ShowMaximized;

    private void HandleWinEvent(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType != EventSystemForeground || objectId != ObjidWindow || childId != ChildidSelf)
        {
            return;
        }

        if (!TryCreateSnapshot(windowHandle, out var snapshot))
        {
            return;
        }

        FocusWatch[] watches;
        lock (_gate)
        {
            watches = [.. _focusWatches.Values];
        }

        foreach (var watch in watches)
        {
            watch.Dispatch(snapshot);
        }
    }

    private bool TryCreateSnapshot(IntPtr handle, out WindowSnapshot snapshot)
    {
        snapshot = default!;
        handle = NormalizeHandle(handle);

        if (handle == IntPtr.Zero
            || !User32.IsWindow(handle)
            || !User32.IsWindowVisible(handle)
            || IsToolWindow(handle)
            || IsCloaked(handle)
            || !TryGetWindowFrame(handle, out var frame))
        {
            return false;
        }

        var processId = NativeWindowInfo.GetProcessId(handle);
        snapshot = new WindowSnapshot(
            WindowId.Format(handle),
            NativeWindowInfo.GetTitle(handle),
            unchecked((int)processId),
            NativeWindowInfo.GetProcessName(processId),
            frame,
            User32.IsIconic(handle),
            User32.IsZoomed(handle),
            IsVisible: true);
        return true;
    }

    private static IntPtr NormalizeHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = User32.GetAncestor(handle, GaRoot);
        return root == IntPtr.Zero ? handle : root;
    }

    private static bool IsToolWindow(IntPtr handle)
    {
        if (User32.GetWindow(handle, GwOwner) != IntPtr.Zero)
        {
            return true;
        }

        return (User32.GetWindowLongPtr(handle, GwlExstyle).ToInt64() & WsExToolwindow) != 0;
    }

    private static bool IsCloaked(IntPtr handle)
    {
        var result = DwmApi.DwmGetWindowAttribute(
            handle,
            DwmwaCloaked,
            out var cloaked,
            Marshal.SizeOf<int>());
        return result == 0 && cloaked != 0;
    }

    private static bool TryGetWindowFrame(IntPtr handle, out WindowRectangleSnapshot frame)
    {
        frame = default!;
        if (!User32.GetWindowRect(handle, out var rect))
        {
            return false;
        }

        frame = ToSnapshot(rect);
        return frame.Width > 0 && frame.Height > 0;
    }

    private static bool TryGetWindowPlacement(IntPtr handle, out WindowPlacement placement)
    {
        placement = WindowPlacement.Create();
        return User32.GetWindowPlacement(handle, ref placement);
    }

    private static bool TryGetWindowMonitorArea(IntPtr handle, bool useWorkingArea, out WindowRectangleSnapshot area)
    {
        area = default!;
        var monitor = User32.MonitorFromWindow(handle, MonitorDefaulttonearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = NativeMonitorInfo.Create();
        if (!User32.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        area = ToSnapshot(useWorkingArea ? info.WorkArea : info.Monitor);
        return true;
    }

    /// <summary>
    /// Installs or removes the WinEvent hook to match the current watches. Never called under
    /// <see cref="_gate"/>: the hook thread takes that lock to dispatch focus changes, so holding
    /// it while waiting on the hook thread would deadlock against a focus change.
    /// </summary>
    private void SyncEventHook()
    {
        _hookThread.Invoke(
            () =>
            {
                bool wanted;
                lock (_gate)
                {
                    wanted = _focusWatches.Count > 0;
                }

                if (wanted)
                {
                    InstallEventHook();
                }
                else
                {
                    UninstallEventHook();
                }
            },
            "window focus hook update");
    }

    private void InstallEventHook()
    {
        if (_eventHook != IntPtr.Zero)
        {
            return;
        }

        _eventHook = User32.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _eventProcedure,
            0,
            0,
            WineventOutofcontext | WineventSkipownprocess);
        if (_eventHook == IntPtr.Zero)
        {
            var exception = new Win32Exception(Marshal.GetLastPInvokeError(), "Could not install foreground window event hook.");
            _logger.Error("Foreground window event hook installation failed.", exception);
            throw exception;
        }

        _logger.Info($"Foreground window event hook installed. Hook=0x{_eventHook.ToInt64():X}");
    }

    private void RemoveWatch(FocusWatch watch)
    {
        bool removed;
        lock (_gate)
        {
            removed = _focusWatches.Remove(watch.Id);
            if (removed)
            {
                _logger.Info($"Window focus watch disposed id={watch.Id} count={_focusWatches.Count}.");
            }
        }

        if (removed && Volatile.Read(ref _disposed) == 0)
        {
            SyncEventHook();
        }
    }

    private void UninstallEventHook()
    {
        if (_eventHook == IntPtr.Zero)
        {
            return;
        }

        if (User32.UnhookWinEvent(_eventHook))
        {
            _logger.Info("Foreground window event hook uninstalled.");
        }
        else
        {
            LogLastWin32Error("Foreground window event hook uninstall failed.");
        }

        _eventHook = IntPtr.Zero;
    }

    private void LogLastWin32Error(string message)
    {
        var exception = new Win32Exception(Marshal.GetLastPInvokeError(), message);
        _logger.Error(message, exception);
    }

    private sealed class FocusWatch : IDisposable
    {
        private readonly Action<WindowSnapshot> _callback;
        private readonly Action<FocusWatch> _dispose;
        private int _disposed;

        public FocusWatch(long id, Action<WindowSnapshot> callback, Action<FocusWatch> dispose)
        {
            Id = id;
            _callback = callback;
            _dispose = dispose;
        }

        public long Id { get; }

        public void Dispatch(WindowSnapshot snapshot)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _callback(snapshot);
            }
        }

        public void MarkDisposed()
        {
            Interlocked.Exchange(ref _disposed, 1);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _dispose(this);
            }
        }
    }

    private delegate void WinEventProcedure(
        IntPtr hookHandle,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    private static NativeRect FromSnapshot(WindowRectangleSnapshot snapshot) =>
        new(snapshot.X, snapshot.Y, snapshot.X + snapshot.Width, snapshot.Y + snapshot.Height);

    private static WindowRectangleSnapshot ToSnapshot(NativeRect rect) =>
        new(rect.Left, rect.Top, Math.Max(0, rect.Width), Math.Max(0, rect.Height));

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;

        public uint Flags;

        public int ShowCmd;

        public NativePoint MinPosition;

        public NativePoint MaxPosition;

        public NativeRect RcNormalPosition;

        public static WindowPlacement Create() =>
            new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
    }

    private static partial class User32
    {
        [LibraryImport("user32.dll")]
        public static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindow(IntPtr windowHandle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(IntPtr windowHandle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsIconic(IntPtr windowHandle);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsZoomed(IntPtr windowHandle);

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetAncestor(IntPtr windowHandle, uint flags);

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetWindow(IntPtr windowHandle, uint command);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static partial IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(IntPtr windowHandle, int command);

        [LibraryImport("user32.dll")]
        public static partial IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr moduleHandle,
            WinEventProcedure eventProcedure,
            uint processId,
            uint threadId,
            uint flags);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnhookWinEvent(IntPtr hookHandle);
    }

    private static partial class DwmApi
    {
        [LibraryImport("dwmapi.dll")]
        public static partial int DwmGetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            out int attributeValue,
            int attributeSize);
    }
}
