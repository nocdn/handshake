using System.IO.Pipes;
using HsWin.Core.Logging;

namespace HsWin.Core.Commands;

/// <summary>
/// Answers one-line JSON commands (from <c>hspn</c> or a newly launched instance) on this user's
/// named pipe. Each connection gets a bounded time and message size, and repeated failures back
/// off so a pipe owned elsewhere cannot spin the loop.
/// </summary>
public sealed class HsWinCommandServer : IDisposable
{
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(30);

    private readonly string _pipeName;
    private readonly Func<HsWinCommandRequest, HsWinCommandResponse> _handleRequest;
    private readonly IRuntimeLogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private Task? _runTask;
    private int _started;
    private int _disposed;

    public HsWinCommandServer(
        Func<HsWinCommandRequest, HsWinCommandResponse> handleRequest,
        IRuntimeLogger logger)
        : this(HsWinCommandProtocol.PipeName, handleRequest, logger)
    {
    }

    public HsWinCommandServer(
        string pipeName,
        Func<HsWinCommandRequest, HsWinCommandResponse> handleRequest,
        IRuntimeLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(handleRequest);
        ArgumentNullException.ThrowIfNull(logger);

        _pipeName = pipeName;
        _handleRequest = handleRequest;
        _logger = logger;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _runTask = Task.Run(() => RunAsync(_stop.Token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        try
        {
            _runTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(static item => item is OperationCanceledException))
        {
        }

        _stop.Dispose();
    }

    internal static TimeSpan NextBackoff(TimeSpan current) =>
        current == TimeSpan.Zero ? InitialBackoff : TimeSpan.FromTicks(Math.Min(MaximumBackoff.Ticks, current.Ticks * 2));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                backoff = TimeSpan.Zero;
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                backoff = NextBackoff(backoff);
                _logger.Error($"HsWin command server connection failed; retrying in {backoff.TotalMilliseconds:F0}ms.", exception);
                try
                {
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var requestJson = await HsWinCommandLineIO.ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
            if (requestJson is null)
            {
                return;
            }

            HsWinCommandResponse response;
            try
            {
                var request = HsWinCommandProtocol.DeserializeRequest(requestJson);
                response = _handleRequest(request);
            }
            catch (Exception exception)
            {
                _logger.Error("HsWin command request failed.", exception);
                response = HsWinCommandResponse.Error(exception.Message);
            }

            await HsWinCommandLineIO.WriteLineAsync(stream, HsWinCommandProtocol.SerializeResponse(response), timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warning($"HsWin command client did not complete its request within {RequestTimeout.TotalSeconds:F0}s.");
        }
    }
}
