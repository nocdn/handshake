using HsWin.App.Native;

namespace HsWin.App.Windows;

internal enum WindowMoveStrategy
{
    SetFrame,
    SetRestoreBoundsAndRestore,
    RestoreMoveAndMaximize
}

internal static class WindowMoveStrategySelector
{
    public static WindowMoveStrategy Select(int showCommand) =>
        showCommand switch
        {
            ShowWindowCommand.ShowMaximized => WindowMoveStrategy.RestoreMoveAndMaximize,
            ShowWindowCommand.ShowMinimized => WindowMoveStrategy.SetRestoreBoundsAndRestore,
            _ => WindowMoveStrategy.SetFrame
        };
}
