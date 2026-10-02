namespace HsWin.Core.Shell;

public sealed class NullShellService : IShellService
{
    public static NullShellService Instance { get; } = new();

    private NullShellService()
    {
    }

    public ShellExecutionResult Execute(string command, ShellExecutionOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Shell execution is not available in this runtime.");
    }

    public LaunchResult Launch(string target, LaunchOptions options)
    {
        throw new NotSupportedException("Application launching is not available in this runtime.");
    }
}
