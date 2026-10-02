namespace HsWin.Core.Logging;

/// <summary>
/// Keeps the log directories from growing forever: a new log file is started on every launch and
/// every config reload, so they accumulate quickly.
/// </summary>
public static class LogRetention
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(14);

    public const int DefaultMinimumKept = 20;

    /// <summary>
    /// Deletes <c>*.log</c> files older than <paramref name="maxAge"/>, but always keeps the
    /// <paramref name="minimumKept"/> most recent so a quiet install still has history to look at.
    /// Files that cannot be deleted (open in an editor, or in use) are left alone. Returns the
    /// number of files deleted.
    /// </summary>
    public static int Prune(string directory, DateTimeOffset now, TimeSpan maxAge, int minimumKept)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumKept);

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = now.UtcDateTime - maxAge;
        var deleted = 0;
        var candidates = new DirectoryInfo(directory)
            .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(minimumKept)
            .Where(file => file.LastWriteTimeUtc < cutoff);

        foreach (var file in candidates)
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use right now; the next launch will try again.
            }
        }

        return deleted;
    }

    public static int Prune(string directory) =>
        Prune(directory, DateTimeOffset.Now, DefaultMaxAge, DefaultMinimumKept);
}
