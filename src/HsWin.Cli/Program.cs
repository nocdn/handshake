using HsWin.Core.Commands;
using HsWin.Core.Config;

namespace HsWin.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args, Console.Out, Console.Error);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"hspn: {exception.Message}");
            return 1;
        }
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteGeneralHelp(output);
            return 0;
        }

        if (IsHelpCommand(args))
        {
            WriteHelpTopic(args, output);
            return 0;
        }

        if (!string.Equals(args[0], "config", StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine($"hspn: unknown command '{args[0]}'.");
            error.WriteLine("Run 'hspn --help' for usage.");
            return 1;
        }

        return RunConfig(args[1..], output, error);
    }

    private static int RunConfig(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteConfigHelp(output);
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "reload" => RunConfigReload(args[1..], output, error),
            "lint" => RunConfigLint(args[1..], output, error),
            _ => UnknownConfigCommand(args[0], error)
        };
    }

    private static int RunConfigReload(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            WriteConfigReloadHelp(output);
            return 0;
        }

        if (args.Length > 0)
        {
            error.WriteLine("hspn config reload does not take positional arguments.");
            return 1;
        }

        try
        {
            var client = new HsWinCommandClient();
            var response = client.Send(new HsWinCommandRequest(HsWinCommandNames.ConfigReload));
            var writer = response.Success ? output : error;
            writer.WriteLine(response.Message);
            return response.Success ? 0 : 1;
        }
        catch (TimeoutException)
        {
            error.WriteLine("HsWin is not running. Start Hammerspoon (Windows Edition), then try again.");
            return 1;
        }
        catch (IOException exception)
        {
            error.WriteLine($"Could not contact the HsWin tray app: {exception.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            error.WriteLine("The HsWin command pipe belongs to another user; reload can only reach your own HsWin instance.");
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            error.WriteLine($"Could not read the HsWin tray app response: {exception.Message}");
            return 1;
        }
    }

    private static int RunConfigLint(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            WriteConfigLintHelp(output);
            return 0;
        }

        if (!TryReadLintPath(args, out var configPath, out var pathError))
        {
            error.WriteLine(pathError);
            return 1;
        }

        var linter = new ConfigLinter();
        var result = linter.LintFile(configPath);
        foreach (var diagnostic in result.Diagnostics)
        {
            WriteDiagnostic(error, configPath, diagnostic);
        }

        if (result.Success)
        {
            output.WriteLine($"OK: {configPath} passed lint.");
            return 0;
        }

        error.WriteLine(
            $"Config lint failed with {result.ErrorCount} error{Pluralize(result.ErrorCount)}" +
            $" and {result.WarningCount} warning{Pluralize(result.WarningCount)}.");
        return 1;
    }

    private static bool TryReadLintPath(string[] args, out string configPath, out string error)
    {
        configPath = HsWinPaths.FromAppData().ConfigFilePath;
        error = string.Empty;
        var hasPath = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--path", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "--config", StringComparison.OrdinalIgnoreCase))
            {
                if (hasPath)
                {
                    error = "hspn config lint accepts only one config path.";
                    return false;
                }

                if (index + 1 >= args.Length)
                {
                    error = $"{argument} requires a file path.";
                    return false;
                }

                configPath = args[++index];
                hasPath = true;
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                error = $"Unknown option '{argument}'.";
                return false;
            }

            if (hasPath)
            {
                error = "hspn config lint accepts only one config path.";
                return false;
            }

            configPath = argument;
            hasPath = true;
        }

        return true;
    }

    private static void WriteDiagnostic(TextWriter writer, string configPath, ConfigLintDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity.ToString().ToLowerInvariant();
        var location = diagnostic.Line is null
            ? configPath
            : diagnostic.Column is null
                ? $"{configPath}:{diagnostic.Line}"
                : $"{configPath}:{diagnostic.Line}:{diagnostic.Column}";
        writer.WriteLine($"{location}: {severity} {diagnostic.Code}: {diagnostic.Message}");
    }

    private static int UnknownConfigCommand(string command, TextWriter error)
    {
        error.WriteLine($"hspn config: unknown command '{command}'.");
        error.WriteLine("Run 'hspn config --help' for usage.");
        return 1;
    }

    private static void WriteHelpTopic(string[] args, TextWriter output)
    {
        if (args.Length == 1)
        {
            WriteGeneralHelp(output);
            return;
        }

        if (string.Equals(args[1], "config", StringComparison.OrdinalIgnoreCase))
        {
            WriteConfigHelp(output);
            return;
        }

        WriteGeneralHelp(output);
    }

    private static bool IsHelpCommand(string[] args) =>
        string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase);

    private static bool IsHelp(string value) =>
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase);

    private static string Pluralize(int count) => count == 1 ? string.Empty : "s";

    private static void WriteGeneralHelp(TextWriter output)
    {
        output.WriteLine(
            """
            hspn - command line tools for Hammerspoon (Windows Edition)

            Usage:
              hspn [--help]
              hspn config <command> [options]

            Commands:
              config reload       Ask the running tray app to reload config.js.
              config lint [path]  Lint a config file without touching the running app.
                                  If [path] is omitted, uses %APPDATA%\HsWin\config.js.

            Options:
              -h, --help          Show help.

            Run 'hspn config --help' for config command details.
            """);
    }

    private static void WriteConfigHelp(TextWriter output)
    {
        output.WriteLine(
            """
            Manage the HsWin JavaScript config.

            Usage:
              hspn config reload
              hspn config lint [path]

            Commands:
              reload              Ask the running tray app to reload %APPDATA%\HsWin\config.js.
              lint                Validate a config file with HsWin's script API checks.

            Defaults:
              hspn config lint    Lints %APPDATA%\HsWin\config.js when no path is given.

            Options:
              -h, --help          Show help.
            """);
    }

    private static void WriteConfigReloadHelp(TextWriter output)
    {
        output.WriteLine(
            """
            Ask the running HsWin tray app to reload config.js.

            Usage:
              hspn config reload

            The reload is handled by the tray app, so hotkeys, hooks, timers, logs,
            and reload toasts follow the same path as the tray menu.
            """);
    }

    private static void WriteConfigLintHelp(TextWriter output)
    {
        output.WriteLine(
            """
            Lint an HsWin JavaScript config file.

            Usage:
              hspn config lint
              hspn config lint <path>
              hspn config lint --path <path>

            When no path is supplied, hspn lints %APPDATA%\HsWin\config.js.
            Linting runs the config against stand-in services (nothing is registered or
            sent), so it reports syntax errors, script errors, and wrong API usage with
            their config.js line and column. It also catches literal mistakes inside
            callbacks: timer intervals below 1 ms, modifier keys passed to
            hs.keyboard.repeat, and hs.keyboard.repeatPulse without keyDownMs > 0.
            A config that does not finish within 5 seconds (an endless loop) is reported.
            """);
    }
}
