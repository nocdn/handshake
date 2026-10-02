using HsWin.Core.Audio;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

internal sealed class CapturingAudioDeviceController : IAudioDeviceController
{
    private readonly Dictionary<string, AudioDeviceSnapshot> _outputDevices;
    private readonly Dictionary<string, AudioDeviceSnapshot> _inputDevices = new(StringComparer.OrdinalIgnoreCase);
    private string _defaultOutputDeviceId = string.Empty;
    private string _defaultInputDeviceId = string.Empty;

    public CapturingAudioDeviceController(params AudioDeviceSnapshot[] devices)
    {
        _outputDevices = devices.ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
        _defaultOutputDeviceId = devices.Single(device => device.IsDefault).Id;
        SetInputDevices(devices);
    }

    public List<string> Actions { get; } = [];

    public void SetInputDevices(params AudioDeviceSnapshot[] devices)
    {
        _inputDevices.Clear();
        foreach (var device in devices)
        {
            _inputDevices.Add(device.Id, device);
        }

        _defaultInputDeviceId = devices.Single(device => device.IsDefault).Id;
    }

    public AudioDeviceSnapshot GetDefaultOutputDevice()
    {
        return _outputDevices[_defaultOutputDeviceId];
    }

    public IReadOnlyList<AudioDeviceSnapshot> GetOutputDevices()
    {
        return _outputDevices.Values.ToArray();
    }

    public AudioDeviceSnapshot GetDefaultInputDevice()
    {
        return _inputDevices[_defaultInputDeviceId];
    }

    public IReadOnlyList<AudioDeviceSnapshot> GetInputDevices()
    {
        return _inputDevices.Values.ToArray();
    }

    public AudioDeviceVolumeSnapshot GetVolume(string? deviceId)
    {
        return ToVolumeSnapshot(ResolveOutputDevice(deviceId));
    }

    public AudioDeviceVolumeSnapshot SetVolume(string? deviceId, double volume)
    {
        var device = ResolveOutputDevice(deviceId);
        Actions.Add($"{device.Id}:{volume}");
        var updated = device with { Volume = volume };
        _outputDevices[device.Id] = updated;
        return ToVolumeSnapshot(updated);
    }

    public AudioDeviceVolumeSnapshot SetMuted(string? deviceId, bool muted)
    {
        var device = ResolveOutputDevice(deviceId);
        Actions.Add($"{device.Id}:muted:{muted}");
        var updated = device with { Muted = muted };
        _outputDevices[device.Id] = updated;
        return ToVolumeSnapshot(updated);
    }

    public AudioDeviceVolumeSnapshot ToggleMute(string? deviceId)
    {
        var device = ResolveOutputDevice(deviceId);
        Actions.Add($"{device.Id}:toggle");
        var updated = device with { Muted = !device.Muted };
        _outputDevices[device.Id] = updated;
        return ToVolumeSnapshot(updated);
    }

    public AudioDeviceVolumeSnapshot GetInputVolume(string? deviceId)
    {
        return ToVolumeSnapshot(ResolveInputDevice(deviceId));
    }

    public AudioDeviceVolumeSnapshot SetInputVolume(string? deviceId, double volume)
    {
        var device = ResolveInputDevice(deviceId);
        Actions.Add($"input:{device.Id}:{volume}");
        var updated = device with { Volume = volume };
        _inputDevices[device.Id] = updated;
        return ToVolumeSnapshot(updated);
    }

    public AudioDeviceVolumeSnapshot SetInputMuted(string? deviceId, bool muted)
    {
        var device = ResolveInputDevice(deviceId);
        Actions.Add($"input:{device.Id}:muted:{muted}");
        var updated = device with { Muted = muted };
        _inputDevices[device.Id] = updated;
        return ToVolumeSnapshot(updated);
    }

    public AudioDeviceVolumeSnapshot ToggleInputMute(string? deviceId)
    {
        var device = ResolveInputDevice(deviceId);
        Actions.Add($"input:{device.Id}:toggle");
        var updated = device with { Muted = !device.Muted };
        _inputDevices[device.Id] = updated;
        return ToVolumeSnapshot(updated);
    }

    private AudioDeviceSnapshot ResolveOutputDevice(string? deviceId)
    {
        return string.IsNullOrWhiteSpace(deviceId)
            ? _outputDevices[_defaultOutputDeviceId]
            : _outputDevices[deviceId];
    }

    private AudioDeviceSnapshot ResolveInputDevice(string? deviceId)
    {
        return string.IsNullOrWhiteSpace(deviceId)
            ? _inputDevices[_defaultInputDeviceId]
            : _inputDevices[deviceId];
    }

    private static AudioDeviceVolumeSnapshot ToVolumeSnapshot(AudioDeviceSnapshot device)
    {
        return new AudioDeviceVolumeSnapshot(device.Id, device.Name, device.Volume, device.Muted);
    }
}

internal sealed class CapturingAudioCaptureService : IAudioCaptureService
{
    public List<CapturingAudioRecording> Recordings { get; } = [];

    public List<CapturingAudioLevelWatch> LevelWatches { get; } = [];

    public IAudioRecordingSession Record(AudioRecordingOptions options, Action<AudioCaptureEvent> callback)
    {
        var recording = new CapturingAudioRecording(options, callback);
        Recordings.Add(recording);
        return recording;
    }

    public IDisposable WatchLevels(AudioLevelWatchOptions options, Action<AudioCaptureEvent> callback)
    {
        var watch = new CapturingAudioLevelWatch(options, callback);
        LevelWatches.Add(watch);
        return watch;
    }
}

internal sealed class CapturingAudioRecording : IAudioRecordingSession
{
    private readonly Action<AudioCaptureEvent> _callback;

    public CapturingAudioRecording(AudioRecordingOptions options, Action<AudioCaptureEvent> callback)
    {
        Options = options;
        _callback = callback;
    }

    public AudioRecordingOptions Options { get; }

    public string Path => Options.Path ?? @"C:\Users\Test\AppData\Roaming\HsWin\recordings\recording.wav";

    public bool IsRecording { get; private set; } = true;

    public bool IsDisposed { get; private set; }

    public void Emit(AudioCaptureEvent audioEvent)
    {
        _callback(audioEvent);
    }

    public void Stop()
    {
        IsRecording = false;
    }

    public void Dispose()
    {
        IsDisposed = true;
        Stop();
    }
}

internal sealed class CapturingAudioLevelWatch : IDisposable
{
    private readonly Action<AudioCaptureEvent> _callback;

    public CapturingAudioLevelWatch(AudioLevelWatchOptions options, Action<AudioCaptureEvent> callback)
    {
        Options = options;
        _callback = callback;
    }

    public AudioLevelWatchOptions Options { get; }

    public bool IsDisposed { get; private set; }

    public void Emit(AudioCaptureEvent audioEvent)
    {
        _callback(audioEvent);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
