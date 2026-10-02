using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class ClipboardScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesPasteboardAndClipboardAliases()
    {
        var presenter = new CapturingAlertPresenter();
        var clipboard = new CapturingClipboardService("hello");
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Clipboard = clipboard
        });

        runtime.Reload("""
            const previous = hs.pasteboard.getContents();
            hs.clipboard.setContents(`${previous} world`);
            hs.alert.show(hs.pasteboard.getContents(), "normal", 1);
            """);

        Assert.Equal("hello world", clipboard.Text);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("hello world", request.Text);
    }

    [Fact]
    public void ReloadExposesPasteboardWatchAndDisposesOnReload()
    {
        var presenter = new CapturingAlertPresenter();
        var clipboard = new CapturingClipboardService("hello");
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Clipboard = clipboard
        });

        runtime.Reload("""
            hs.pasteboard.watch(event => {
              hs.alert.show(`${event.sequence}:${event.contents}:${event.hasText}`, "normal", 1);
            });
            """);
        var watch = Assert.Single(clipboard.Watches);
        clipboard.TriggerChange("npm install react");

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("1:npm install react:true", request.Text);

        runtime.Reload("""console.log("second load");""");

        Assert.True(watch.IsDisposed);
    }

    [Fact]
    public void PasteboardWatchTextReplacesChangedClipboardTextInPlace()
    {
        var clipboard = new CapturingClipboardService("initial");
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Clipboard = clipboard
        });

        runtime.Reload("""
            hs.pasteboard.watchText(text => text.replace(/\bnpm\b/g, "bun"));
            """);
        clipboard.TriggerChange("npm install react && npm test");

        Assert.Equal("bun install react && bun test", clipboard.Text);
    }

    [Fact]
    public void PasteboardReplaceTextReturnsReplacementResult()
    {
        var presenter = new CapturingAlertPresenter();
        var clipboard = new CapturingClipboardService("npm install react");
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Clipboard = clipboard
        });

        runtime.Reload("""
            const result = hs.pasteboard.replaceText(/\bnpm\b/g, "bun");
            hs.alert.show(`${result.changed}:${result.previous}:${result.current}`, "normal", 1);
            """);

        Assert.Equal("bun install react", clipboard.Text);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("true:npm install react:bun install react", request.Text);
    }
}
