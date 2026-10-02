using System.Runtime.InteropServices;
using HsWin.App.Clipboard;

namespace HsWin.App.Tests;

public sealed class ClipboardRetryTests
{
    [Fact]
    public void RunReturnsImmediatelyWhenTheClipboardIsAvailable()
    {
        var sleeps = new List<int>();

        var result = ClipboardRetry.Run(() => 42, sleeps.Add);

        Assert.Equal(42, result);
        Assert.Empty(sleeps);
    }

    [Fact]
    public void RunRetriesWithGrowingBackoffUntilTheClipboardOpens()
    {
        var sleeps = new List<int>();
        var attempts = 0;

        var result = ClipboardRetry.Run(
            () => ++attempts < 3 ? throw new COMException("CLIPBRD_E_CANT_OPEN") : "ok",
            sleeps.Add);

        Assert.Equal("ok", result);
        Assert.Equal([20, 40], sleeps);
    }

    [Fact]
    public void RunGivesUpAfterTheMaximumAttemptsAndSurfacesTheError()
    {
        var sleeps = new List<int>();
        var attempts = 0;

        Assert.Throws<ExternalException>(() => ClipboardRetry.Run<int>(
            () =>
            {
                attempts++;
                throw new ExternalException("busy");
            },
            sleeps.Add));

        Assert.Equal(ClipboardRetry.MaxAttempts, attempts);
        Assert.Equal(ClipboardRetry.MaxAttempts - 1, sleeps.Count);
    }

    [Fact]
    public void RunDoesNotRetryUnrelatedErrors()
    {
        var attempts = 0;

        Assert.Throws<InvalidOperationException>(() => ClipboardRetry.Run<int>(
            () =>
            {
                attempts++;
                throw new InvalidOperationException();
            },
            _ => { }));

        Assert.Equal(1, attempts);
    }
}
