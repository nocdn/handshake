using HsWin.Core.Scripting;
using HsWin.Core.Windows;

namespace HsWin.Core.Tests;

internal sealed class CapturingWindowService : IWindowService
{
    public WindowSnapshot? FocusedWindow { get; init; }

    public WindowMoveResult MoveResult { get; init; } =
        WindowMoveResult.NotMoved("0x0", "not-configured");

    public int GetFocusedWindowCallCount { get; private set; }

    public List<CapturingWindowMove> Moves { get; } = [];

    public List<CapturingWindowFocusWatch> FocusWatches { get; } = [];

    public WindowSnapshot? GetFocusedWindow()
    {
        GetFocusedWindowCallCount++;
        return FocusedWindow;
    }

    public WindowSnapshot? GetWindow(string id)
    {
        return string.Equals(FocusedWindow?.Id, id, StringComparison.OrdinalIgnoreCase)
            ? FocusedWindow
            : null;
    }

    public WindowMoveResult MoveToScreen(string id, WindowTargetScreen targetScreen, WindowMoveOptions options)
    {
        Moves.Add(new CapturingWindowMove(id, targetScreen, options));
        return MoveResult with { WindowId = id };
    }

    public IDisposable WatchFocused(Action<WindowSnapshot> callback)
    {
        var registration = new CapturingDisposable();
        FocusWatches.Add(new CapturingWindowFocusWatch(callback, registration));
        return registration;
    }
}

internal sealed record CapturingWindowMove(
    string Id,
    WindowTargetScreen TargetScreen,
    WindowMoveOptions Options);

internal sealed class CapturingWindowFocusWatch
{
    private readonly Action<WindowSnapshot> _callback;

    public CapturingWindowFocusWatch(Action<WindowSnapshot> callback, CapturingDisposable registration)
    {
        _callback = callback;
        Registration = registration;
    }

    public CapturingDisposable Registration { get; }

    public void Emit(WindowSnapshot snapshot)
    {
        _callback(snapshot);
    }
}
