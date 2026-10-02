using HsWin.Core.Audio;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class AudioScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesAudioDeviceAndSoundApis()
    {
        var presenter = new CapturingAlertPresenter();
        var audio = new CapturingAudioDeviceController(
            new AudioDeviceSnapshot("default-id", "Speakers", IsDefault: true, Volume: 25, Muted: false),
            new AudioDeviceSnapshot("headphones-id", "Headphones", IsDefault: false, Volume: 40, Muted: true));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            AudioDevices = audio
        });

        runtime.Reload("""
            const device = hs.audiodevice.defaultOutputDevice();
            const devices = hs.audiodevice.allOutputDevices();
            device.setVolume(33);
            const mute = hs.sound.toggleMute();
            hs.alert.show(`${devices.length}:${device.name}:${hs.sound.getVolume()}:${mute.muted}`, "normal", 1);
            """);

        Assert.Equal(["default-id:33", "default-id:toggle"], audio.Actions);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("2:Speakers:33:true", request.Text);
    }

    [Fact]
    public void ReloadExposesAudioInputDeviceApis()
    {
        var presenter = new CapturingAlertPresenter();
        var audio = new CapturingAudioDeviceController(
            new AudioDeviceSnapshot("speakers-id", "Speakers", IsDefault: true, Volume: 25, Muted: false));
        audio.SetInputDevices(
            new AudioDeviceSnapshot("mic-id", "Studio Mic", IsDefault: true, Volume: 75, Muted: false),
            new AudioDeviceSnapshot("webcam-id", "Webcam Mic", IsDefault: false, Volume: 55, Muted: true));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            AudioDevices = audio
        });

        runtime.Reload("""
            const mic = hs.audiodevice.defaultInputDevice();
            const inputs = hs.audiodevice.allInputDevices();
            mic.setVolume(62);
            const mute = mic.toggleMute();
            hs.alert.show(`${inputs.length}:${mic.kind}:${mic.name}:${hs.audiodevice.getInputVolume()}:${mute.muted}`, "normal", 1);
            """);

        Assert.Equal(["input:mic-id:62", "input:mic-id:toggle"], audio.Actions);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("2:input:Studio Mic:62:true", request.Text);
    }

    [Fact]
    public void ReloadExposesAudioRecordWithFriendlyOptionsAndCallbacks()
    {
        var presenter = new CapturingAlertPresenter();
        var callbacks = new QueuedScriptCallbackScheduler();
        var capture = new CapturingAudioCaptureService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            AudioCapture = capture,
            CallbackScheduler = callbacks
        });

        runtime.Reload("""
            const recorder = hs.audio.record({
              path: "C:\\Temp\\voice.m4a",
              deviceId: "mic-id",
              quality: "high",
              levelIntervalMs: 0
            }, event => {
              if (event.type === "stopped") {
                hs.alert.show(`${event.path}:${event.format}:${event.bytes}`, "normal", 1);
              }
            });

            hs.alert.show(`${recorder.path}:${recorder.isRecording}`, "normal", 1);
            """);

        var recording = Assert.Single(capture.Recordings);
        Assert.Equal(@"C:\Temp\voice.m4a", recording.Options.Path);
        Assert.Equal("mic-id", recording.Options.DeviceId);
        Assert.Equal(AudioRecordingFormat.Aac, recording.Options.Format);
        Assert.Equal(256, recording.Options.BitrateKbps);
        Assert.Equal(0, recording.Options.LevelIntervalMs);

        recording.Emit(new AudioCaptureEvent("stopped", Path: @"C:\Temp\voice.m4a", Format: "m4a", Bytes: 1234));
        callbacks.RunNext();

        Assert.Collection(
            presenter.Requests,
            request => Assert.Equal(@"C:\Temp\voice.m4a:true", request.Text),
            request => Assert.Equal(@"C:\Temp\voice.m4a:m4a:1234", request.Text));
    }

    [Fact]
    public void ReloadDisposesAudioRecordingsOnReload()
    {
        var capture = new CapturingAudioCaptureService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            AudioCapture = capture
        });

        runtime.Reload("""hs.audio.record("C:\\Temp\\note.wav", () => {});""");
        var recording = Assert.Single(capture.Recordings);
        runtime.Reload("""console.log("reloaded");""");

        Assert.True(recording.IsDisposed);
    }

    [Fact]
    public void ReloadExposesAudioLevels()
    {
        var presenter = new CapturingAlertPresenter();
        var callbacks = new QueuedScriptCallbackScheduler();
        var capture = new CapturingAudioCaptureService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            AudioCapture = capture,
            CallbackScheduler = callbacks
        });

        runtime.Reload("""
            hs.audio.levels({ deviceId: "mic-id", intervalMs: 75 }, event => {
              hs.alert.show(`${event.type}:${event.deviceId}:${event.peak}:${event.rms}`, "normal", 1);
            });
            """);

        var watch = Assert.Single(capture.LevelWatches);
        Assert.Equal("mic-id", watch.Options.DeviceId);
        Assert.Equal(75, watch.Options.IntervalMs);

        watch.Emit(new AudioCaptureEvent("level", DeviceId: "mic-id", Peak: 0.5, Rms: 0.25));
        callbacks.RunNext();

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("level:mic-id:0.5:0.25", request.Text);
    }
}
