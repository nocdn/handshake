using System.Runtime.InteropServices;
using HsWin.App.Threading;
using HsWin.Core.Logging;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace HsWin.App.Clipboard;

/// <summary>
/// Reads and writes clipboard text on a dedicated STA thread. Another process can hold the
/// clipboard open for a moment, so operations retry with backoff; doing that here rather than on
/// the UI dispatcher keeps those waits from freezing toasts and tray menus, and the bounded
/// marshal keeps a script from hanging if the clipboard thread is ever stuck.
/// </summary>
internal sealed class WpfClipboardTextStore : IClipboardTextStore
{
    private readonly DedicatedDispatcherThread _thread = new("HsWin clipboard");
    private readonly IRuntimeLogger _logger;

    public WpfClipboardTextStore(IRuntimeLogger logger)
    {
        _logger = logger;
    }

    public ClipboardTextContents Read()
    {
        return _thread.Invoke(
            () => ClipboardRetry.Run(() =>
            {
                var hasText = WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText);
                var text = hasText
                    ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
                    : string.Empty;
                _logger.Info($"Clipboard text read length={text.Length} hasText={hasText}.");
                return new ClipboardTextContents(text, hasText);
            }),
            "clipboard read");
    }

    public bool Write(string text)
    {
        return _thread.Invoke(
            () => ClipboardRetry.Run(() =>
            {
                if (text.Length == 0)
                {
                    WpfClipboard.Clear();
                }
                else
                {
                    WpfClipboard.SetText(text, WpfTextDataFormat.UnicodeText);
                }

                _logger.Info($"Clipboard text written length={text.Length}.");
                return true;
            }),
            "clipboard write");
    }

    public void Dispose() => _thread.Dispose();
}

/// <summary>Retries clipboard operations that fail because another process has the clipboard open.</summary>
internal static class ClipboardRetry
{
    internal const int MaxAttempts = 5;
    internal const int BackoffStepMs = 20;

    public static T Run<T>(Func<T> action, Action<int>? sleep = null)
    {
        sleep ??= Thread.Sleep;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (ExternalException) when (attempt < MaxAttempts)
            {
                // COMException derives from ExternalException.
                sleep(attempt * BackoffStepMs);
            }
        }
    }
}
