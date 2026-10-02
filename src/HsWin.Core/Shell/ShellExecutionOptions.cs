namespace HsWin.Core.Shell;

public sealed record ShellExecutionOptions(
    string? WorkingDirectory,
    int TimeoutMs)
{
    /// <summary>Background commands (<c>hs.task.run</c>) may run for a while.</summary>
    public const int DefaultTimeoutMs = 30000;

    /// <summary>
    /// <c>hs.execute</c> blocks the script thread (often the UI thread), so its default is short.
    /// </summary>
    public const int DefaultSynchronousTimeoutMs = 10000;

    public static ShellExecutionOptions Default { get; } = new(null, DefaultTimeoutMs);
}
