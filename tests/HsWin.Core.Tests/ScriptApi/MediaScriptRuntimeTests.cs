using HsWin.Core.Applications;
using HsWin.Core.Media;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class MediaScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesMediaControls()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var media = new CapturingMediaController();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            Media = media
        });

        runtime.Reload("""
            hs.media.playPause();
            hs.media.previousTrack();
            hs.media.nextTrack();
            """);

        Assert.Equal(["playPause", "previousTrack", "nextTrack"], media.Actions);
    }

    [Fact]
    public void ReloadExposesMediaCommandResult()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var media = new CapturingMediaController
        {
            PlayPauseResult = new MediaCommandResult("playPause", true, "paused", "playing", "paused", "mediaSession")
        };
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            Media = media
        });

        runtime.Reload("""
            const result = hs.media.playPause();
            hs.alert.show(`${result.action}:${result.statusBefore}:${result.statusAfter}:${result.success}`, "normal", 1);
            """);

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("paused:playing:paused:true", request.Text);
    }

    [Fact]
    public void HotkeysCanTriggerMediaControls()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var applications = new CapturingApplicationProvider(
            new ApplicationSnapshot(123, "r5apex_dx12", "Apex Legends", null));
        var media = new CapturingMediaController();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            Applications = applications,
            Media = media
        });

        runtime.Reload("""
            const apexProcessName = "r5apex_dx12.exe";
            hs.hotkey.bind([], "`", () => {
              if (hs.application.isRunning(apexProcessName)) hs.media.playPause();
            });
            """);

        hotkeys.TriggerOnlyRegistration();

        Assert.Equal(["playPause"], media.Actions);
    }
}
