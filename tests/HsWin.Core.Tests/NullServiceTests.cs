using HsWin.Core.Keyboard;
using HsWin.Core.Mouse;
using HsWin.Core.Support;
using HsWin.Core.Timers;
using HsWin.Core.Windows;

namespace HsWin.Core.Tests;

/// <summary>
/// Null services stand in when a platform feature is unavailable (lint, tests). Watches and timers
/// are harmless no-ops that share one registration; they never throw and never call back.
/// </summary>
public sealed class NullServiceTests
{
    [Fact]
    public void WatchRegistrationsAreTheSharedNoOpDisposable()
    {
        var registrations = new IDisposable[]
        {
            NullKeyboardEventService.Instance.Watch(KeyboardEventWatchOptions.Default, _ => false),
            NullMouseEventService.Instance.WatchScroll(MouseScrollWatchOptions.Default, _ => { }),
            NullMouseButtonEventService.Instance.WatchPhysicalButtons(_ => { }),
            NullScriptTimerService.Instance.DoAfter(10, () => { }),
            NullScriptTimerService.Instance.DoEvery(10, () => { }),
            NullWindowService.Instance.WatchFocused(_ => { })
        };

        Assert.All(registrations, registration => Assert.Same(EmptyDisposable.Instance, registration));
        Assert.All(registrations, registration => registration.Dispose());
    }

    [Fact]
    public void KeyboardQueriesReportNothingPressed()
    {
        Assert.False(NullKeyboardEventService.Instance.IsKeyDown(0x41));
    }

    [Fact]
    public void WindowServiceReportsNoWindowsAndRefusesMoves()
    {
        Assert.Null(NullWindowService.Instance.GetFocusedWindow());
        Assert.Null(NullWindowService.Instance.GetWindow("any"));
    }
}
