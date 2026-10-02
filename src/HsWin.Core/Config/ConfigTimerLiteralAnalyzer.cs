using static HsWin.Core.Config.ConfigSourceScanner;

namespace HsWin.Core.Config;

internal static class ConfigTimerLiteralAnalyzer
{
    private const string TimerPrefix = "hs.timer.";
    private static readonly string[] TimerMethods = ["doAfter", "doEvery"];

    public static IReadOnlyList<ConfigLintDiagnostic> Analyze(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var diagnostics = new List<ConfigLintDiagnostic>();
        for (var index = 0; index < source.Length;)
        {
            if (TrySkipNonCode(source, index, out var nextIndex))
            {
                index = nextIndex;
                continue;
            }

            if (TryReadBadTimerLiteral(source, index, out var methodName, out var literalStart))
            {
                var argumentName = methodName == "doAfter" ? "delay" : "interval";
                diagnostics.Add(CreateError(
                    source,
                    literalStart,
                    "HSWIN001",
                    $"`hs.timer.{methodName}` {argumentName} must be at least 1 millisecond."));
            }

            index++;
        }

        return diagnostics;
    }

    private static bool TryReadBadTimerLiteral(
        string source,
        int index,
        out string methodName,
        out int literalStart)
    {
        methodName = string.Empty;
        literalStart = 0;
        if (!IsCallPrefix(source, index, TimerPrefix))
        {
            return false;
        }

        var methodIndex = index + TimerPrefix.Length;
        methodName = TimerMethods.FirstOrDefault(method => HasPrefix(source, methodIndex, method)) ?? string.Empty;
        if (methodName.Length == 0)
        {
            return false;
        }

        var callIndex = SkipWhiteSpace(source, methodIndex + methodName.Length);
        if (callIndex >= source.Length || source[callIndex] != '(')
        {
            return false;
        }

        literalStart = SkipWhiteSpace(source, callIndex + 1);
        return TryReadNumber(source, literalStart, out var value, out _) && value < 1;
    }
}
