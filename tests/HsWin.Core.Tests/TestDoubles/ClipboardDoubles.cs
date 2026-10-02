using HsWin.Core.Clipboard;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

internal sealed class CapturingClipboardService : IClipboardService
{
    private long _sequence;

    public CapturingClipboardService(string text)
    {
        Text = text;
    }

    public string Text { get; private set; }

    public List<CapturingClipboardWatch> Watches { get; } = [];

    public string GetText()
    {
        return Text;
    }

    public bool SetText(string text)
    {
        Text = text;
        return true;
    }

    public IDisposable Watch(Action<ClipboardChangeSnapshot> callback)
    {
        var watch = new CapturingClipboardWatch(callback);
        Watches.Add(watch);
        return watch;
    }

    public void TriggerChange(string text, bool hasText = true)
    {
        Text = hasText ? text : string.Empty;
        var snapshot = new ClipboardChangeSnapshot(
            Interlocked.Increment(ref _sequence),
            Text,
            hasText);
        foreach (var watch in Watches.Where(watch => !watch.IsDisposed).ToArray())
        {
            watch.Trigger(snapshot);
        }
    }
}

internal sealed class CapturingClipboardWatch : IDisposable
{
    private readonly Action<ClipboardChangeSnapshot> _callback;

    public CapturingClipboardWatch(Action<ClipboardChangeSnapshot> callback)
    {
        _callback = callback;
    }

    public bool IsDisposed { get; private set; }

    public void Trigger(ClipboardChangeSnapshot snapshot)
    {
        _callback(snapshot);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
