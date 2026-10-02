using HsWin.Core.Audio;

namespace HsWin.Core.Scripting;

/// <summary>
/// A running recording. <c>stop()</c> finishes the file (encoding MP3/AAC) and reports a final
/// <c>stopped</c> event; <c>dispose()</c>/<c>delete()</c> end it immediately, as reload does.
/// </summary>
public sealed class AudioRecordingScriptHandle : ScriptResourceHandle
{
    private readonly IAudioRecordingSession _session;

    internal AudioRecordingScriptHandle(IAudioRecordingSession session, ScriptCallbackRelay relay)
        : base(new Support.DisposableAction(() =>
        {
            relay.Cancel();
            session.Dispose();
        }))
    {
        _session = session;
    }

    public string Path => _session.Path;

    public string path => Path;

    public bool IsRecording => !IsDisposed && _session.IsRecording;

    public bool isRecording => IsRecording;

    /// <summary>Finishes the recording; the handle is released after the final event is delivered.</summary>
    public override void Stop()
    {
        if (!IsDisposed)
        {
            _session.Stop();
        }
    }
}
