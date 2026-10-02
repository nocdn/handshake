using HsWin.Core.Scripting;

namespace HsWin.Core.Config;

public sealed class ConfigLinter
{
    private readonly TimeSpan _executionTimeout;

    public ConfigLinter()
        : this(ConfigLintRuntimeServices.DefaultExecutionTimeout)
    {
    }

    internal ConfigLinter(TimeSpan executionTimeout)
    {
        _executionTimeout = executionTimeout;
    }

    public ConfigLintResult LintFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            return new ConfigLintResult(
                [
                    new ConfigLintDiagnostic(
                        ConfigLintSeverity.Error,
                        "HSWIN000",
                        $"Config file was not found: {filePath}")
                ]);
        }

        return LintSource(File.ReadAllText(filePath), filePath);
    }

    public ConfigLintResult LintSource(string source, string documentName = "config.js")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        var diagnostics = new List<ConfigLintDiagnostic>();
        diagnostics.AddRange(ConfigTimerLiteralAnalyzer.Analyze(source));
        diagnostics.AddRange(ConfigKeyboardRepeatLiteralAnalyzer.Analyze(source));

        try
        {
            using var runtime = new ScriptRuntime(ConfigLintRuntimeServices.Create(_executionTimeout));
            runtime.Reload(source, documentName);
        }
        catch (Exception exception)
        {
            var location = ScriptErrorFormatter.FindConfigLocation(exception);

            // The static analyzers already reported this line with a more specific message.
            if (location is null || !diagnostics.Any(diagnostic => diagnostic.Line == location.Line))
            {
                diagnostics.Add(new ConfigLintDiagnostic(
                    ConfigLintSeverity.Error,
                    "HSWIN100",
                    ScriptErrorFormatter.GetMessage(exception),
                    location?.Line,
                    location?.Column));
            }
        }

        return new ConfigLintResult(diagnostics);
    }
}
