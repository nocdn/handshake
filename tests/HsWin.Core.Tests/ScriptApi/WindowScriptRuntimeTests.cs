using HsWin.Core.Hotkeys;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;
using HsWin.Core.Windows;

namespace HsWin.Core.Tests;

public sealed class WindowScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesFocusedWindowSnapshot()
    {
        var presenter = new CapturingAlertPresenter();
        var windows = new CapturingWindowService
        {
            FocusedWindow = new WindowSnapshot(
                "0x1234",
                "Notes",
                42,
                "notepad",
                new WindowRectangleSnapshot(10, 20, 800, 600),
                IsMinimized: false,
                IsMaximized: false,
                IsVisible: true)
        };
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Windows = windows
        });

        runtime.Reload("""
            const win = hs.window.focusedWindow();
            hs.alert.show(`${win.id}:${win.title}:${win.processId}:${win.processName}:${win.frame.width}x${win.frame.height}`, "normal", 1);
            """);

        Assert.Equal(1, windows.GetFocusedWindowCallCount);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("0x1234:Notes:42:notepad:800x600", request.Text);
    }

    [Fact]
    public void WindowObjectMovesToMouseScreen()
    {
        var presenter = new CapturingAlertPresenter();
        var windows = new CapturingWindowService
        {
            FocusedWindow = new WindowSnapshot(
                "0x1234",
                "Notes",
                42,
                "notepad",
                new WindowRectangleSnapshot(10, 20, 800, 600),
                IsMinimized: false,
                IsMaximized: false,
                IsVisible: true),
            MoveResult = WindowMoveResult.MovedTo(
                "0x1234",
                new WindowRectangleSnapshot(1920, 0, 800, 600))
        };
        var mouse = new CapturingMouseService(
            new MouseScreenSnapshot(
                "display-2",
                "Right Monitor",
                IsPrimary: false,
                new MousePointSnapshot(1930, 50),
                new MouseRectangleSnapshot(1920, 0, 2560, 1440),
                new MouseRectangleSnapshot(1920, 0, 2560, 1400)));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Mouse = mouse,
            Windows = windows
        });

        runtime.Reload("""
            const result = hs.window.focusedWindow().moveToMouseScreen({ preserveSize: true });
            hs.alert.show(`${result.success}:${result.moved}:${result.frame.x},${result.frame.y}`, "normal", 1);
            """);

        var move = Assert.Single(windows.Moves);
        Assert.Equal("0x1234", move.Id);
        Assert.Equal("display-2", move.TargetScreen.Id);
        Assert.True(move.Options.PreserveSize);
        Assert.True(move.Options.UseWorkingArea);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("true:true:1920,0", request.Text);
    }

    [Fact]
    public void WindowObjectCanSendNativeMonitorMoveToMouseScreen()
    {
        var presenter = new CapturingAlertPresenter();
        var keyboardInput = new CapturingKeyboardInputService();
        var windows = new CapturingWindowService
        {
            FocusedWindow = new WindowSnapshot(
                "0x1234",
                "Notes",
                42,
                "notepad",
                new WindowRectangleSnapshot(10, 20, 800, 600),
                IsMinimized: false,
                IsMaximized: true,
                IsVisible: true)
        };
        var mouse = new CapturingMouseService(
            new MouseScreenSnapshot(
                "display-2",
                "Right Monitor",
                IsPrimary: false,
                new MousePointSnapshot(1930, 50),
                new MouseRectangleSnapshot(1920, 0, 2560, 1440),
                new MouseRectangleSnapshot(1920, 0, 2560, 1400)));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            KeyboardInput = keyboardInput,
            Mouse = mouse,
            Windows = windows
        });

        runtime.Reload("""
            const result = hs.window.focusedWindow().moveToMouseScreenNative();
            hs.alert.show(`${result.success}:${result.moved}:${result.reason}:${result.direction}`, "normal", 1);
            """);

        var tap = Assert.Single(keyboardInput.Taps);
        Assert.Equal(0x27u, tap.VirtualKey);
        Assert.Equal(HotkeyModifiers.Win | HotkeyModifiers.Shift, tap.Options.Modifiers);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("true:true:sent-native-monitor-move:right", request.Text);
    }

    [Fact]
    public void WindowFocusWatchSchedulesCallbackAndDisposesOnReload()
    {
        var presenter = new CapturingAlertPresenter();
        var callbacks = new QueuedScriptCallbackScheduler();
        var windows = new CapturingWindowService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Windows = windows,
            CallbackScheduler = callbacks
        });

        runtime.Reload("""
            hs.window.watchFocused(win => {
              hs.alert.show(`${win.id}:${win.title}`, "normal", 1);
            });
            """);

        var watch = Assert.Single(windows.FocusWatches);
        watch.Emit(new WindowSnapshot(
            "0x9876",
            "Browser",
            100,
            "browser",
            new WindowRectangleSnapshot(0, 0, 1200, 800),
            IsMinimized: false,
            IsMaximized: true,
            IsVisible: true));
        callbacks.RunNext();

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("0x9876:Browser", request.Text);

        runtime.Reload("""console.log("new config");""");

        Assert.True(watch.Registration.IsDisposed);
    }
}
