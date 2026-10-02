using HsWin.Core.Scripting;

namespace HsWin.Core.Input;

/// <summary>Interval rules shared by keyboard and mouse repeats, used by parsers, handles and services.</summary>
public static class RepeatTiming
{
    public const int DefaultIntervalMs = 10;
    public const int MinimumIntervalMs = 1;
    public const int MaximumIntervalMs = 1000;

    public static int ParseIntervalMs(object? value, string argumentName = "intervalMs")
    {
        return ScriptArgumentReader.RequireInt32InRange(value, argumentName, MinimumIntervalMs, MaximumIntervalMs);
    }

    /// <summary>Parses a held-phase duration, which must leave a release phase inside the interval.</summary>
    public static int ParseKeyDownMs(object? value, int intervalMs, string argumentName = "keyDownMs")
    {
        var keyDownMs = ScriptArgumentReader.RequireInt32(value, argumentName, "a number of milliseconds");
        ValidateKeyDownMs(keyDownMs, intervalMs, argumentName);
        return keyDownMs;
    }

    public static void ValidateIntervalMs(int intervalMs, string paramName = "intervalMs")
    {
        if (intervalMs is < MinimumIntervalMs or > MaximumIntervalMs)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                $"Repeat interval must be between {MinimumIntervalMs} and {MaximumIntervalMs} milliseconds.");
        }
    }

    public static void ValidateKeyDownMs(int keyDownMs, int intervalMs, string paramName = "keyDownMs")
    {
        if (keyDownMs < 0 || keyDownMs >= intervalMs)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                $"Repeat keyDownMs must be at least 0 and less than intervalMs ({intervalMs}).");
        }
    }
}
