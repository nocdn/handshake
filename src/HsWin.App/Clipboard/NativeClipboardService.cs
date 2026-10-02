using HsWin.Core.Clipboard;
using HsWin.Core.Logging;
using System.Windows.Threading;

namespace HsWin.App.Clipboard;

internal sealed class NativeClipboardService : IClipboardService, IDisposable
{
    private readonly IClipboardTextStore _textStore;
    private readonly IClipboardChangeNotifier _changeNotifier;
    private long _sequence;
    private bool _disposed;

    public NativeClipboardService(Dispatcher dispatcher, IRuntimeLogger logger)
        : this(
            new WpfClipboardTextStore(logger),
            new NativeClipboardChangeNotifier(dispatcher, logger))
    {
    }

    internal NativeClipboardService(
        IClipboardTextStore textStore,
        IClipboardChangeNotifier changeNotifier)
    {
        _textStore = textStore;
        _changeNotifier = changeNotifier;
    }

    public string GetText()
    {
        return _textStore.Read().Text;
    }

    public bool SetText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _textStore.Write(text);
    }

    public IDisposable Watch(Action<ClipboardChangeSnapshot> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);

        return _changeNotifier.Watch(() =>
        {
            var contents = _textStore.Read();
            var snapshot = new ClipboardChangeSnapshot(
                Interlocked.Increment(ref _sequence),
                contents.Text,
                contents.HasText);
            callback(snapshot);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _changeNotifier.Dispose();
        _textStore.Dispose();
        _disposed = true;
    }
}
