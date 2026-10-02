using HsWin.Core.Scripting;

namespace HsWin.Core.Shell;

/// <summary>Parses the option objects shared by <c>hs.execute</c>, <c>hs.task.run</c> and <c>hs.application.launch</c>.</summary>
public static class ShellScriptOptionsParser
{
    public static ShellExecutionOptions ParseExecutionOptions(object? value, int defaultTimeoutMs = ShellExecutionOptions.DefaultTimeoutMs)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return new ShellExecutionOptions(null, defaultTimeoutMs);
        }

        var workingDirectory = ScriptArgumentReader.OptionalString(
            ScriptArgumentReader.GetPropertyValue(value, "cwd", "workingDirectory", "directory"));
        var timeoutValue = ScriptArgumentReader.GetPropertyValue(value, "timeoutMs", "timeout");
        var timeoutMs = ScriptArgumentReader.IsMissing(timeoutValue)
            ? defaultTimeoutMs
            : ScriptArgumentReader.RequirePositiveInt32(timeoutValue, "timeoutMs");

        return new ShellExecutionOptions(workingDirectory, timeoutMs);
    }

    public static LaunchOptions ParseLaunchOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return LaunchOptions.Default;
        }

        var workingDirectory = ScriptArgumentReader.OptionalString(
            ScriptArgumentReader.GetPropertyValue(value, "cwd", "workingDirectory", "directory"));
        var arguments = ScriptArgumentReader.OptionalString(
            ScriptArgumentReader.GetPropertyValue(value, "arguments", "args"));
        return new LaunchOptions(workingDirectory, arguments);
    }
}
