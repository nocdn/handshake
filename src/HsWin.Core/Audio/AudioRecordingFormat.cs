namespace HsWin.Core.Audio;

public enum AudioRecordingFormat
{
    Wav,
    Mp3,
    Aac
}

public static class AudioRecordingFormatExtensions
{
    /// <summary>The file extension (without a dot) and the format name reported to scripts.</summary>
    public static string ToFileExtension(this AudioRecordingFormat format) => format switch
    {
        AudioRecordingFormat.Mp3 => "mp3",
        AudioRecordingFormat.Aac => "m4a",
        _ => "wav"
    };
}
