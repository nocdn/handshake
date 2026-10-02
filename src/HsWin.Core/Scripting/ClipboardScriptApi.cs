using HsWin.Core.Clipboard;
using HsWin.Core.Logging;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public sealed class ClipboardScriptApi
{
    private readonly IClipboardService _clipboard;
    private readonly IRuntimeLogger _logger;
    private readonly IScriptCallbackScheduler _callbackScheduler;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal ClipboardScriptApi(
        IClipboardService clipboard,
        IRuntimeLogger logger,
        IScriptCallbackScheduler callbackScheduler,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _clipboard = clipboard;
        _logger = logger;
        _callbackScheduler = callbackScheduler;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public string GetText()
    {
        var text = _clipboard.GetText();
        _logger.Info($"Script hs.pasteboard.getContents() returned {text.Length} characters.");
        return text;
    }

    public bool SetText(object? text)
    {
        var clipboardText = ScriptArgumentReader.RequireText(text, "text");
        var result = _clipboard.SetText(clipboardText);
        _logger.Info($"Script hs.pasteboard.setContents() wrote {clipboardText.Length} characters result={result}.");
        return result;
    }

    public ScriptResourceHandle Watch(object? callback)
    {
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Clipboard watch callback must be a JavaScript function.", nameof(callback));
        }

        var relay = new ScriptCallbackRelay(scriptFunction, _callbackScheduler, _callbacks);
        var handle = relay.Attach(_clipboard.Watch(snapshot => relay.Post(ScriptJson.Serialize(snapshot))));
        _trackResource(handle);
        _logger.Info("Script hs.pasteboard.watch() registered.");
        return handle;
    }
}
