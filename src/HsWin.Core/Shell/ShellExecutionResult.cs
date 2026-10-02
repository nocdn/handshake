namespace HsWin.Core.Shell;

public sealed record ShellExecutionResult(
    string Command,
    bool Success,
    int? ExitCode,
    string Output,
    string Error,
    bool TimedOut,
    bool Cancelled = false)
{
    public bool Status => Success;
}
