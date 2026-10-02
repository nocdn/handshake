using System.Text;

namespace HsWin.Core.Commands;

/// <summary>Reads and writes the protocol's newline-terminated UTF-8 messages with a size limit.</summary>
internal static class HsWinCommandLineIO
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Returns the next line, or null if the peer closed the stream before sending one.</summary>
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return line.Length == 0 ? null : Decode(line);
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            var count = newline < 0 ? read : newline;
            if (line.Length + count > HsWinCommandProtocol.MaxMessageLength)
            {
                throw new InvalidDataException(
                    $"HsWin command message exceeds {HsWinCommandProtocol.MaxMessageLength} bytes.");
            }

            line.Write(buffer, 0, count);
            if (newline >= 0)
            {
                return Decode(line);
            }
        }
    }

    public static async Task WriteLineAsync(Stream stream, string message, CancellationToken cancellationToken)
    {
        var bytes = Utf8NoBom.GetBytes(message + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Decode(MemoryStream line) =>
        Utf8NoBom.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
}
