namespace HsWin.Core.Audio;

public sealed record AudioCaptureEvent(
    string Type,
    string? DeviceId = null,
    string? DeviceName = null,
    string? Path = null,
    string? Format = null,
    int? SampleRate = null,
    int? Channels = null,
    long? Bytes = null,
    double? DurationMs = null,
    double? Peak = null,
    double? Rms = null,
    string? Reason = null,
    string? ErrorCode = null,
    string? Message = null)
{
    public const string Started = "started";
    public const string Level = "level";
    public const string Error = "error";

    /// <summary>The last event of every recording; errors are followed by it with Reason "error".</summary>
    public const string Stopped = "stopped";

    public bool IsTerminal => Type == Stopped;
}
