using HsWin.App.Magnifier;

namespace HsWin.App.Tests;

public sealed class CaptureActivationTests
{
    [Fact]
    public void EveryNewHoldCreatesANewSessionAndPool()
    {
        var created = new List<Session>();
        using var activation = new CaptureActivation<Session>(() => { var session = new Session(); created.Add(session); return session; });
        Assert.Null(activation.Current);
        activation.SetActive(false);
        Assert.Empty(created);
        activation.SetActive(true);
        activation.SetActive(true);
        Assert.Single(created);
        Assert.Same(created[0], activation.Current);
        activation.SetActive(false);
        Assert.Null(activation.Current);
        Assert.Equal(1, created[0].Disposals);
        activation.SetActive(true);
        Assert.Equal(2, created.Count);
        Assert.Same(created[1], activation.Current);
        activation.Dispose();
        activation.Dispose();
        Assert.Equal(1, created[1].Disposals);
        Assert.Throws<ObjectDisposedException>(() => activation.SetActive(true));
    }

    [Fact]
    public void FailedStartupCanBeRetriedWithoutReusingAnInvalidSession()
    {
        var attempts = 0;
        var healthy = new Session();
        using var activation = new CaptureActivation<Session>(() => ++attempts == 1
            ? throw new InvalidOperationException("capture startup failed") : healthy);
        Assert.Throws<InvalidOperationException>(() => activation.SetActive(true));
        Assert.Null(activation.Current);
        activation.SetActive(true);
        Assert.Same(healthy, activation.Current);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void FailedShutdownStillClearsThePoolBeforeTheNextHold()
    {
        var broken = new Session { FailDispose = true };
        var healthy = new Session();
        var count = 0;
        using var activation = new CaptureActivation<Session>(() => ++count == 1 ? broken : healthy);
        activation.SetActive(true);
        Assert.Throws<InvalidOperationException>(() => activation.SetActive(false));
        Assert.Null(activation.Current);
        activation.SetActive(true);
        Assert.Same(healthy, activation.Current);
    }

    private sealed class Session : IDisposable
    {
        public int Disposals { get; private set; }
        public bool FailDispose { get; init; }
        public void Dispose() { Disposals++; if (FailDispose) throw new InvalidOperationException("capture shutdown failed"); }
    }
}
