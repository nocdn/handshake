using HsWin.Core.Alerts;

namespace HsWin.App.Tests;

public sealed class ConfigReloadAlertsTests
{
    [Fact]
    public void CreateConfigReloadedAlertUsesSuccessToastText()
    {
        var request = ConfigReloadAlerts.CreateReloadedAlert();

        Assert.Equal("Config reloaded", request.Text);
        Assert.Equal(AlertKind.Success, request.Kind);
        Assert.Equal(AlertIcon.Dot, request.EffectiveIcon);
        Assert.Equal(ConfigReloadAlerts.ReloadedDurationMs, request.DurationMs);
    }

    [Fact]
    public void CreateConfigReloadingAlertUsesNormalKind()
    {
        var request = ConfigReloadAlerts.CreateReloadingAlert();

        Assert.Equal("Reloading config…", request.Text);
        Assert.Equal(AlertKind.Normal, request.Kind);
        Assert.Equal(AlertIcon.Loader, request.EffectiveIcon);
    }
}
