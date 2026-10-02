using HsWin.App.Native;
using HsWin.App.Windows;

namespace HsWin.App.Tests;

public sealed class WindowMoveStrategySelectorTests
{
    [Fact]
    public void SelectUsesRestoreMoveAndMaximizeForMaximizedWindows()
    {
        Assert.Equal(
            WindowMoveStrategy.RestoreMoveAndMaximize,
            WindowMoveStrategySelector.Select(ShowWindowCommand.ShowMaximized));
    }

    [Fact]
    public void SelectUsesRestoreBoundsAndRestoreForMinimizedWindows()
    {
        Assert.Equal(
            WindowMoveStrategy.SetRestoreBoundsAndRestore,
            WindowMoveStrategySelector.Select(ShowWindowCommand.ShowMinimized));
    }

    [Fact]
    public void SelectUsesSetFrameForNormalWindows()
    {
        Assert.Equal(WindowMoveStrategy.SetFrame, WindowMoveStrategySelector.Select(1));
    }
}
