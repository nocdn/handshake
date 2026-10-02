using System.IO.Pipes;
using System.Text;
using HsWin.Core.Commands;

namespace HsWin.Core.Tests;

/// <summary>The one-line JSON command pipe between hspn / a newly launched instance and the running app.</summary>
public sealed class HsWinCommandPipeTests
{
    private readonly CapturingRuntimeLogger _logger = new();
    private readonly string _pipeName = $"HsWin.Core.Tests.{Guid.NewGuid():N}";

    private HsWinCommandServer StartServer(Func<HsWinCommandRequest, HsWinCommandResponse> handler)
    {
        var server = new HsWinCommandServer(_pipeName, handler, _logger);
        server.Start();
        return server;
    }

    [Fact]
    public void ServerCanAnswerSeveralRequestsInARow()
    {
        var seen = new List<string>();
        using var server = StartServer(request =>
        {
            seen.Add(request.Command);
            return HsWinCommandResponse.Ok($"ok {request.Command}");
        });
        var client = new HsWinCommandClient(_pipeName);

        var first = client.Send(new HsWinCommandRequest(HsWinCommandNames.ConfigReload), timeoutMs: 5000);
        var second = client.Send(new HsWinCommandRequest(HsWinCommandNames.AppQuit), timeoutMs: 5000);

        Assert.Equal("ok config.reload", first.Message);
        Assert.Equal("ok app.quit", second.Message);
        Assert.Equal([HsWinCommandNames.ConfigReload, HsWinCommandNames.AppQuit], seen);
    }

    [Fact]
    public void AHandlerFailureBecomesAnErrorResponseAndTheServerKeepsRunning()
    {
        var calls = 0;
        using var server = StartServer(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("handler exploded");
            }

            return HsWinCommandResponse.Ok("recovered");
        });
        var client = new HsWinCommandClient(_pipeName);

        var failed = client.Send(new HsWinCommandRequest("anything"), timeoutMs: 5000);
        var recovered = client.Send(new HsWinCommandRequest("anything"), timeoutMs: 5000);

        Assert.False(failed.Success);
        Assert.Contains("handler exploded", failed.Message, StringComparison.Ordinal);
        Assert.True(recovered.Success);
        Assert.Contains(_logger.Errors, error => error.Contains("command request failed", StringComparison.Ordinal));
    }

    [Fact]
    public void AGarbageRequestGetsAnErrorResponseInsteadOfHangingTheServer()
    {
        using var server = StartServer(_ => HsWinCommandResponse.Ok("never reached"));

        using (var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
        {
            pipe.Connect(5000);
            var bytes = Encoding.UTF8.GetBytes("this is not json\n");
            pipe.Write(bytes);
            pipe.Flush();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var reply = HsWinCommandProtocol.DeserializeResponse(reader.ReadLine()!);

            Assert.False(reply.Success);
        }

        var next = new HsWinCommandClient(_pipeName).Send(new HsWinCommandRequest("ping"), timeoutMs: 5000);
        Assert.Equal("never reached", next.Message);
    }

    [Fact]
    public void ClientReportsATimeoutWhenNoInstanceIsRunning()
    {
        var client = new HsWinCommandClient($"HsWin.Core.Tests.Missing.{Guid.NewGuid():N}");

        Assert.Throws<TimeoutException>(() => client.Send(new HsWinCommandRequest("ping"), timeoutMs: 100));
    }

    [Fact]
    public void ADisposedServerStopsAnsweringAndDisposeIsRepeatable()
    {
        var server = StartServer(_ => HsWinCommandResponse.Ok("hi"));
        Assert.True(new HsWinCommandClient(_pipeName).Send(new HsWinCommandRequest("ping"), timeoutMs: 5000).Success);

        server.Dispose();
        server.Dispose();

        Assert.Throws<TimeoutException>(() => new HsWinCommandClient(_pipeName).Send(new HsWinCommandRequest("ping"), timeoutMs: 200));
        Assert.Throws<ObjectDisposedException>(server.Start);
    }

    [Fact]
    public void BackoffDoublesFromTheInitialDelayAndStopsAtTheCap()
    {
        var backoff = HsWinCommandServer.NextBackoff(TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMilliseconds(100), backoff);

        for (var step = 0; step < 20; step++)
        {
            backoff = HsWinCommandServer.NextBackoff(backoff);
        }

        Assert.Equal(TimeSpan.FromSeconds(30), backoff);
    }

    [Fact]
    public void ThePipeNameIsScopedToTheUserAndSession()
    {
        var alice = HsWinCommandProtocol.CreatePipeName(@"PC\alice", sessionId: 1);
        var bob = HsWinCommandProtocol.CreatePipeName(@"PC\bob", sessionId: 1);
        var aliceElsewhere = HsWinCommandProtocol.CreatePipeName(@"PC\alice", sessionId: 2);

        Assert.NotEqual(alice, bob);
        Assert.NotEqual(alice, aliceElsewhere);
        Assert.DoesNotContain("alice", alice, StringComparison.OrdinalIgnoreCase);
    }
}
