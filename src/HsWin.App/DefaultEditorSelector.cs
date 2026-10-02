using System.IO;

namespace HsWin.App;

/// <summary>
/// Chooses how to open the config in the user's own editor. <c>.js</c> is commonly bound to
/// Windows Script Host (or Node), where the <c>open</c> verb would run the config instead of
/// editing it, so those are skipped in favour of the <c>edit</c> verb.
/// </summary>
internal static class DefaultEditorSelector
{
    private static readonly string[] Verbs = ["open", "edit"];

    private static readonly HashSet<string> ScriptRunners = new(StringComparer.OrdinalIgnoreCase)
    {
        "wscript.exe",
        "cscript.exe",
        "node.exe",
        "bun.exe",
        "deno.exe"
    };

    /// <summary>The shell verb whose handler is an editor, or null when only script runners are registered.</summary>
    public static string? SelectVerb(IFileAssociations associations, string extension)
    {
        ArgumentNullException.ThrowIfNull(associations);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        foreach (var verb in Verbs)
        {
            var executable = associations.GetExecutable(extension, verb);
            if (!string.IsNullOrWhiteSpace(executable) && !ScriptRunners.Contains(Path.GetFileName(executable)))
            {
                return verb;
            }
        }

        return null;
    }
}
