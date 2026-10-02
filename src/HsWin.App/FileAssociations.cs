using System.Runtime.InteropServices;

namespace HsWin.App;

/// <summary>Looks up the program Windows has associated with a file type.</summary>
internal interface IFileAssociations
{
    /// <summary>The executable path for <paramref name="verb"/> on <paramref name="extension"/>, or null when none is registered.</summary>
    string? GetExecutable(string extension, string verb);
}

internal sealed partial class ShellFileAssociations : IFileAssociations
{
    private const uint NotTruncate = 0x00000020;
    private const int AssocStrExecutable = 2;
    private const int BufferLength = 1024;

    public static ShellFileAssociations Instance { get; } = new();

    private ShellFileAssociations()
    {
    }

    public string? GetExecutable(string extension, string verb)
    {
        var buffer = new char[BufferLength];
        var length = (uint)buffer.Length;
        var result = AssocQueryString(NotTruncate, AssocStrExecutable, extension, verb, buffer, ref length);
        return result == 0 && length > 1 ? new string(buffer, 0, (int)length - 1) : null;
    }

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryString(
        uint flags,
        int association,
        string assoc,
        string extra,
        [Out] char[] output,
        ref uint outputLength);
}
