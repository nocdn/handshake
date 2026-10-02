using System.ComponentModel;
using System.Reflection;
using HsWin.Core.Config;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class ScriptErrorFormatterTests
{
    [Fact]
    public void FormatUnwrapsTargetInvocationExceptions()
    {
        var inner = new Win32Exception("Could not register hotkey Alt, Control+0x52.");

        var message = ScriptErrorFormatter.Format(new TargetInvocationException(inner));

        Assert.Equal("Could not register hotkey Alt, Control+0x52.", message);
    }

    [Fact]
    public void FormatStripsTheClearScriptErrorPrefix()
    {
        var message = ScriptErrorFormatter.Format(new InvalidOperationException("Error: Could not register hotkey."));

        Assert.Equal("Could not register hotkey.", message);
    }

    [Fact]
    public void FormatAppendsTheUserConfigLocationForHostErrorsRaisedThroughHsWrappers()
    {
        var exception = CaptureReloadFailure("""
            const first = 1;
            hs.hotkey.bind(["hyper"], "A", () => {});
            """);

        var message = ScriptErrorFormatter.Format(exception);

        Assert.StartsWith("Unsupported hotkey modifier", message, StringComparison.Ordinal);
        Assert.Matches(@"\(config\.js:2:\d+\)$", message);
    }

    [Fact]
    public void FindConfigLocationSkipsBootstrapFrames()
    {
        var exception = CaptureReloadFailure("""


            hs.timer.doAfter(0, () => {});
            """);

        var location = ScriptErrorFormatter.FindConfigLocation(exception);

        Assert.NotNull(location);
        Assert.Equal("config.js", location.File);
        Assert.Equal(3, location.Line);
    }

    [Fact]
    public void FormatReportsPlainJavaScriptErrorsWithTheirLocation()
    {
        var exception = CaptureReloadFailure("""
            const value = 1;
            missingFunction();
            """);

        var message = ScriptErrorFormatter.Format(exception);

        Assert.Contains("missingFunction is not defined", message, StringComparison.Ordinal);
        Assert.Contains("(config.js:2:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMessageOmitsTheLocation()
    {
        var exception = CaptureReloadFailure("""missingFunction();""");

        var message = ScriptErrorFormatter.GetMessage(exception);

        Assert.DoesNotContain("config.js", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigLocationReturnsNullForHostOnlyExceptions()
    {
        Assert.Null(ScriptErrorFormatter.FindConfigLocation(new InvalidOperationException("boom")));
    }

    private static Exception CaptureReloadFailure(string source)
    {
        using var runtime = new ScriptRuntime(ConfigLintRuntimeServices.Create(TimeSpan.FromSeconds(5)));
        return Assert.ThrowsAny<Exception>(() => runtime.Reload(source));
    }
}
