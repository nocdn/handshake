using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

/// <summary>
/// Turns a config failure into one readable line, such as
/// <c>Unsupported hotkey key 'f55'. (config.js:12:5)</c>. Used by lint and the reload toast so both
/// report the same message and location.
/// </summary>
public static partial class ScriptErrorFormatter
{
    /// <summary>The message plus the config location, for places that show a single line (the reload toast).</summary>
    public static string Format(Exception exception)
    {
        var message = GetMessage(exception);
        var location = FindConfigLocation(exception)?.ToString();
        return location is null || message.Contains(location, StringComparison.Ordinal)
            ? message
            : $"{message} ({location})";
    }

    /// <summary>The first line of the error message, without the location.</summary>
    public static string GetMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var unwrapped = Unwrap(exception);
        var message = unwrapped is ScriptEngineException scriptException
            ? scriptException.ErrorDetails ?? scriptException.Message
            : unwrapped.Message;
        message = FirstNonEmptyLine(message).Trim();
        return message.StartsWith("Error:", StringComparison.Ordinal)
            ? message[6..].Trim()
            : message;
    }

    /// <summary>The innermost meaningful exception: host errors thrown from script calls, not their wrappers.</summary>
    public static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (true)
        {
            switch (current)
            {
                case TargetInvocationException { InnerException: { } inner }:
                    current = inner;
                    continue;
                case AggregateException { InnerException: { } inner }:
                    current = inner;
                    continue;
                case ScriptEngineException scriptException when scriptException.InnerException is not null:
                    current = scriptException.InnerException;
                    continue;
            }

            return current;
        }
    }

    /// <summary>
    /// The first stack frame that is not inside the bootstrap, so a host error raised through an
    /// <c>hs.*</c> wrapper points at the user's own line.
    /// </summary>
    public static ScriptErrorLocation? FindConfigLocation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not ScriptEngineException { ErrorDetails: { } details })
            {
                continue;
            }

            foreach (Match match in StackFrame().Matches(details))
            {
                var file = match.Groups["file"].Value.Trim();
                if (!file.StartsWith("hswin:", StringComparison.Ordinal))
                {
                    return new ScriptErrorLocation(
                        Path.GetFileName(file),
                        int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                        int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        return null;
    }

    private static string FirstNonEmptyLine(string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return value;
    }

    // V8 frames: "at config.js:12:5 -> code" or "at name (C:\path\config.js:12:5)".
    [GeneratedRegex(@"\bat (?:[^\r\n(]*\()?(?<file>[^\r\n()]+?):(?<line>\d+):(?<column>\d+)")]
    private static partial Regex StackFrame();
}

public sealed record ScriptErrorLocation(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";
}
