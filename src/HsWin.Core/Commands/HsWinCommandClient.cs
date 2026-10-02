using System.IO.Pipes;

namespace HsWin.Core.Commands;

public sealed class HsWinCommandClient
{
    /// <summary>How long to wait for a running instance to accept the connection.</summary>
    public const int DefaultTimeoutMs = 2000;

    /// <summary>How long to wait for the reply once connected; covers a slow config reload request.</summary>
    public const int DefaultResponseTimeoutMs = 15000;

    private readonly string _pipeName;

    public HsWinCommandClient()
        : this(HsWinCommandProtocol.PipeName)
    {
    }

    public HsWinCommandClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        _pipeName = pipeName;
    }

    /// <exception cref="TimeoutException">No instance accepted the connection in time (it is not running).</exception>
    /// <exception cref="IOException">The instance connected but did not answer properly in time.</exception>
    public HsWinCommandResponse Send(
        HsWinCommandRequest request,
        int timeoutMs = DefaultTimeoutMs,
        int responseTimeoutMs = DefaultResponseTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(responseTimeoutMs, 1);

        // CurrentUserOnly also verifies that the server is owned by this user, so a pipe squatted
        // by another account cannot receive the request.
        using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect(timeoutMs);

        using var timeout = new CancellationTokenSource(responseTimeoutMs);
        try
        {
            HsWinCommandLineIO.WriteLineAsync(pipe, HsWinCommandProtocol.SerializeRequest(request), timeout.Token)
                .GetAwaiter().GetResult();
            var responseJson = HsWinCommandLineIO.ReadLineAsync(pipe, timeout.Token).GetAwaiter().GetResult()
                ?? throw new IOException("The HsWin command server closed the connection before returning a response.");
            return HsWinCommandProtocol.DeserializeResponse(responseJson);
        }
        catch (OperationCanceledException exception)
        {
            throw new IOException($"HsWin accepted the request but did not respond within {responseTimeoutMs}ms.", exception);
        }
    }
}
