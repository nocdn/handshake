namespace HsWin.App.Clipboard;

internal interface IClipboardTextStore : IDisposable
{
    ClipboardTextContents Read();

    bool Write(string text);
}
