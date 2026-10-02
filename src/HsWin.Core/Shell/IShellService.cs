namespace HsWin.Core.Shell;

public interface IShellService
{
    /// <summary>
    /// Runs a command and waits for it. Cancelling <paramref name="cancellationToken"/> kills the
    /// command's process tree and returns a result with <see cref="ShellExecutionResult.Cancelled"/>.
    /// </summary>
    ShellExecutionResult Execute(string command, ShellExecutionOptions options, CancellationToken cancellationToken = default);

    LaunchResult Launch(string target, LaunchOptions options);
}
