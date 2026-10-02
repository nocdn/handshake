using System.Runtime.InteropServices;
using HsWin.Core.Audio;
using HsWin.Core.Logging;
using NAudio.CoreAudioApi;

namespace HsWin.App.Audio;

/// <summary>
/// Reads and changes Windows audio endpoint volume and mute through NAudio's Core Audio wrapper.
/// Enumerators and devices are created and disposed per call: they are cheap, and holding COM
/// objects across calls would tie them to whichever thread first used them.
/// </summary>
internal sealed class NativeAudioDeviceController(IRuntimeLogger logger) : IAudioDeviceController
{
    private const int ENotFound = unchecked((int)0x80070490);
    private const int EInvalidArgument = unchecked((int)0x80070057);

    public AudioDeviceSnapshot GetDefaultOutputDevice() => GetDefaultDevice(DataFlow.Render);

    public IReadOnlyList<AudioDeviceSnapshot> GetOutputDevices() => GetDevices(DataFlow.Render);

    public AudioDeviceSnapshot GetDefaultInputDevice() => GetDefaultDevice(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceSnapshot> GetInputDevices() => GetDevices(DataFlow.Capture);

    public AudioDeviceVolumeSnapshot GetVolume(string? deviceId) =>
        WithDevice(deviceId, DataFlow.Render, CreateVolumeSnapshot);

    public AudioDeviceVolumeSnapshot SetVolume(string? deviceId, double volume) =>
        SetVolume(deviceId, DataFlow.Render, volume, "Audio volume");

    public AudioDeviceVolumeSnapshot SetMuted(string? deviceId, bool muted) =>
        SetMuted(deviceId, DataFlow.Render, muted, "Audio mute");

    public AudioDeviceVolumeSnapshot ToggleMute(string? deviceId) =>
        ToggleMute(deviceId, DataFlow.Render, "Audio mute");

    public AudioDeviceVolumeSnapshot GetInputVolume(string? deviceId) =>
        WithDevice(deviceId, DataFlow.Capture, CreateVolumeSnapshot);

    public AudioDeviceVolumeSnapshot SetInputVolume(string? deviceId, double volume) =>
        SetVolume(deviceId, DataFlow.Capture, volume, "Audio input volume");

    public AudioDeviceVolumeSnapshot SetInputMuted(string? deviceId, bool muted) =>
        SetMuted(deviceId, DataFlow.Capture, muted, "Audio input mute");

    public AudioDeviceVolumeSnapshot ToggleInputMute(string? deviceId) =>
        ToggleMute(deviceId, DataFlow.Capture, "Audio input mute");

    /// <summary>True for the errors Core Audio raises when a device (or any default device) does not exist.</summary>
    internal static bool IsDeviceMissing(COMException exception) =>
        exception.HResult is ENotFound or EInvalidArgument;

    private static string FlowName(DataFlow flow) => flow == DataFlow.Render ? "output" : "input";

    private static AudioDeviceSnapshot GetDefaultDevice(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = OpenDevice(enumerator, deviceId: null, flow);
        return CreateDeviceSnapshot(device, device.ID);
    }

    private IReadOnlyList<AudioDeviceSnapshot> GetDevices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = TryGetDefaultDeviceId(enumerator, flow);
        var devices = new List<AudioDeviceSnapshot>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(CreateDeviceSnapshot(device, defaultId));
            }
        }

        logger.Info($"Audio {FlowName(flow)} devices enumerated count={devices.Count}.");
        return devices;
    }

    private AudioDeviceVolumeSnapshot SetVolume(string? deviceId, DataFlow flow, double volume, string description)
    {
        return WithDevice(deviceId, flow, device =>
        {
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(volume / 100, 0, 1);
            return LogChange(description, "set", CreateVolumeSnapshot(device));
        });
    }

    private AudioDeviceVolumeSnapshot SetMuted(string? deviceId, DataFlow flow, bool muted, string description)
    {
        return WithDevice(deviceId, flow, device =>
        {
            device.AudioEndpointVolume.Mute = muted;
            return LogChange(description, "set", CreateVolumeSnapshot(device));
        });
    }

    private AudioDeviceVolumeSnapshot ToggleMute(string? deviceId, DataFlow flow, string description)
    {
        return WithDevice(deviceId, flow, device =>
        {
            var endpoint = device.AudioEndpointVolume;
            endpoint.Mute = !endpoint.Mute;
            return LogChange(description, "toggled", CreateVolumeSnapshot(device));
        });
    }

    private AudioDeviceVolumeSnapshot LogChange(string description, string verb, AudioDeviceVolumeSnapshot snapshot)
    {
        logger.Info($"{description} {verb} id='{snapshot.Id}' volume={snapshot.Volume} muted={snapshot.Muted}.");
        return snapshot;
    }

    private static T WithDevice<T>(string? deviceId, DataFlow flow, Func<MMDevice, T> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = OpenDevice(enumerator, deviceId, flow);
        return action(device);
    }

    private static MMDevice OpenDevice(MMDeviceEnumerator enumerator, string? deviceId, DataFlow flow)
    {
        var useDefault = string.IsNullOrWhiteSpace(deviceId);
        try
        {
            return useDefault
                ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia)
                : enumerator.GetDevice(deviceId!);
        }
        catch (COMException exception) when (IsDeviceMissing(exception))
        {
            throw new InvalidOperationException(
                useDefault
                    ? $"There is no default audio {FlowName(flow)} device."
                    : $"Audio device '{deviceId}' was not found.",
                exception);
        }
    }

    /// <summary>The default device id, or null when there is none (so listing still works).</summary>
    private static string? TryGetDefaultDeviceId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch (COMException exception) when (IsDeviceMissing(exception))
        {
            return null;
        }
    }

    private static AudioDeviceSnapshot CreateDeviceSnapshot(MMDevice device, string? defaultDeviceId)
    {
        var volume = CreateVolumeSnapshot(device);
        return new AudioDeviceSnapshot(
            volume.Id,
            volume.Name,
            string.Equals(volume.Id, defaultDeviceId, StringComparison.OrdinalIgnoreCase),
            volume.Volume,
            volume.Muted);
    }

    private static AudioDeviceVolumeSnapshot CreateVolumeSnapshot(MMDevice device)
    {
        var endpoint = device.AudioEndpointVolume;
        return new AudioDeviceVolumeSnapshot(
            device.ID,
            device.FriendlyName,
            Math.Round(endpoint.MasterVolumeLevelScalar * 100, 2),
            endpoint.Mute);
    }
}
