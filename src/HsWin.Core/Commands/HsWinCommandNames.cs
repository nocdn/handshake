namespace HsWin.Core.Commands;

public static class HsWinCommandNames
{
    public const string ConfigReload = "config.reload";

    /// <summary>Asks a running instance to shut down cleanly; sent by a newly launched instance.</summary>
    public const string AppQuit = "app.quit";
}
