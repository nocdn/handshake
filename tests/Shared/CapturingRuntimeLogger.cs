using HsWin.Core.Logging;

namespace HsWin.Tests.Shared;

/// <summary>Records log lines by level; safe to use from hook and timer threads.</summary>
internal sealed class CapturingRuntimeLogger : IRuntimeLogger
{
    private readonly object _gate = new();
    private readonly List<string> _infos = [];
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];
    private readonly List<string> _all = [];

    public IReadOnlyList<string> Infos => Snapshot(_infos);

    public IReadOnlyList<string> Warnings => Snapshot(_warnings);

    public IReadOnlyList<string> Errors => Snapshot(_errors);

    /// <summary>Every line in the order it was written.</summary>
    public IReadOnlyList<string> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _all];
            }
        }
    }

    public void Info(string message) => Add(_infos, message);

    public void Warning(string message) => Add(_warnings, message);

    public void Error(string message, Exception exception) => Add(_errors, $"{message} {exception.Message}");

    private void Add(List<string> lines, string message)
    {
        lock (_gate)
        {
            lines.Add(message);
            _all.Add(message);
        }
    }

    private IReadOnlyList<string> Snapshot(List<string> lines)
    {
        lock (_gate)
        {
            return [.. lines];
        }
    }
}
