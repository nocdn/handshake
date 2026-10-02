using HsWin.Core.Logging;

namespace HsWin.App;

/// <summary>Prunes old log files off the startup path so launching never waits on disk cleanup.</summary>
internal static class LogRetentionTask
{
    public static Task Start(IRuntimeLogger logger, params string[] directories)
    {
        return Task.Run(() =>
        {
            foreach (var directory in directories)
            {
                try
                {
                    var deleted = LogRetention.Prune(directory);
                    if (deleted > 0)
                    {
                        logger.Info($"Removed {deleted} old log file(s) from {directory}.");
                    }
                }
                catch (Exception exception)
                {
                    logger.Error($"Could not prune old logs in {directory}.", exception);
                }
            }
        });
    }
}
