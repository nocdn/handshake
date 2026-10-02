using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace HsWin.App;

/// <summary>
/// Opens the config in an editor: an explicit <c>HSWIN_EDITOR</c>/<c>VISUAL</c>/<c>EDITOR</c>
/// command, else the program Windows has registered for <c>.js</c> files, else Notepad.
/// </summary>
internal static class EditorLauncher
{
    private static readonly string[] EditorEnvironmentVariables = ["HSWIN_EDITOR", "VISUAL", "EDITOR"];

    public static void Open(string filePath) => Open(filePath, ShellFileAssociations.Instance);

    internal static void Open(string filePath, IFileAssociations associations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        foreach (var variableName in EditorEnvironmentVariables)
        {
            var command = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrWhiteSpace(command) && TryStart(command, filePath))
            {
                return;
            }
        }

        if (TryOpenWithDefaultEditor(associations, filePath))
        {
            return;
        }

        StartProcess("notepad.exe", Quote(filePath));
    }

    private static bool TryOpenWithDefaultEditor(IFileAssociations associations, string filePath)
    {
        var verb = DefaultEditorSelector.SelectVerb(associations, Path.GetExtension(filePath));
        if (verb is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                Verb = verb,
                UseShellExecute = true
            });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static bool TryStart(string command, string filePath)
    {
        try
        {
            var parsed = EditorCommand.Parse(command);
            StartProcess(parsed.FileName, JoinArguments(parsed.Arguments, Quote(filePath)));
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static void StartProcess(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false
        });
    }

    private static string JoinArguments(string existingArguments, string fileArgument)
    {
        return string.IsNullOrWhiteSpace(existingArguments)
            ? fileArgument
            : $"{existingArguments} {fileArgument}";
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private sealed record EditorCommand(string FileName, string Arguments)
    {
        public static EditorCommand Parse(string command)
        {
            command = command.Trim();
            if (command.Length == 0)
            {
                throw new ArgumentException("Editor command cannot be empty.", nameof(command));
            }

            if (command[0] != '"')
            {
                var separatorIndex = command.IndexOf(' ', StringComparison.Ordinal);
                return separatorIndex < 0
                    ? new EditorCommand(command, string.Empty)
                    : new EditorCommand(command[..separatorIndex], command[(separatorIndex + 1)..].Trim());
            }

            var closingQuoteIndex = command.IndexOf('"', 1);
            if (closingQuoteIndex < 0)
            {
                throw new ArgumentException("Quoted editor command is missing its closing quote.", nameof(command));
            }

            var fileName = command[1..closingQuoteIndex];
            var arguments = command[(closingQuoteIndex + 1)..].Trim();
            return new EditorCommand(fileName, arguments);
        }
    }
}
