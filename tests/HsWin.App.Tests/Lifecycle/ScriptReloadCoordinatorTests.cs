using System.IO;
using HsWin.App.Lifecycle;
using HsWin.Core.Alerts;
using HsWin.Core.Config;

namespace HsWin.App.Tests.Lifecycle;

public sealed class ScriptReloadCoordinatorTests : IDisposable
{
    private static readonly ReloadTimings FastTimings = new(
        MinimumReloadingVisible: TimeSpan.Zero,
        GateSlice: TimeSpan.FromMilliseconds(10),
        EmergencyGateBudget: TimeSpan.FromMilliseconds(120),
        ShutdownGateWait: TimeSpan.FromMilliseconds(120));

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HsWin.App.Tests", Guid.NewGuid().ToString("N"));
    private readonly CapturingRuntimeLogger _logger = new();
    private readonly FakeScriptRuntime _runtime = new();
    private readonly RecordingInputKillSwitch _input = new();
    private readonly RecordingAlertPresenter _toasts = new();
    private readonly FakeUiThread _ui = new() { OnUiThread = true };
    private readonly ConfigFileService _config;
    private readonly ScriptReloadCoordinator _coordinator;

    public ScriptReloadCoordinatorTests()
    {
        _config = new ConfigFileService(Path.Combine(_directory, "config.js"));
        _coordinator = new ScriptReloadCoordinator(
            _logger, _config, _runtime, _input, _toasts, _ui, () => "console.log", FastTimings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReloadShowsTheLoaderReloadsTheConfigAndReportsSuccess()
    {
        await _coordinator.ReloadAsync();
        _ui.RunPosted();

        Assert.Equal(_config.ConfigFilePath, _runtime.LastReloadedPath);
        Assert.True(File.Exists(_config.ConfigFilePath), "The default config should be created on first reload.");
        Assert.Equal(["Reloading config…", "Config reloaded"], _toasts.Texts);
        Assert.Equal(["Config reload:releaseKeys"], _input.Calls);
    }

    [Fact]
    public async Task FailedReloadShowsTheErrorInsteadOfSuccess()
    {
        _runtime.ReloadFailure = new InvalidOperationException("syntax error near line 3");

        await _coordinator.ReloadAsync();
        _ui.RunPosted();

        var failed = _toasts.Shown[^1];
        Assert.Equal(AlertKind.Error, failed.Kind);
        Assert.Contains("syntax error near line 3", failed.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Config reloaded", _toasts.Texts);
        Assert.Contains(_logger.Errors, error => error.Contains("Config reload failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReloadedToastYieldsToANewerToastThatAppearedMeanwhile()
    {
        await _coordinator.ReloadAsync();
        _toasts.StandardContentShows++;
        _ui.RunPosted();

        Assert.DoesNotContain("Config reloaded", _toasts.Texts);
        Assert.Contains(_logger.Infos, message => message.Contains("a newer toast is already showing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANewerReloadSupersedesAnOlderOneAndTearsDownWhatItInstalled()
    {
        _runtime.ReleaseFirstReload.Reset();
        var first = _coordinator.ReloadAsync();
        Assert.True(_runtime.FirstReloadEntered.Wait(TimeSpan.FromSeconds(10)));

        var second = _coordinator.ReloadAsync();
        _runtime.ReleaseFirstReload.Set();
        await Task.WhenAll(first, second);
        _ui.RunPosted();

        Assert.Equal(2, _runtime.Reloads);
        Assert.Equal(1, _runtime.EmergencyStops);
        Assert.Equal(1, _toasts.Texts.Count(text => text == "Config reloaded"));
    }

    [Fact]
    public async Task ShutdownSuppressesTheOutcomeToast()
    {
        await _coordinator.ReloadAsync();
        _coordinator.BeginShutdown();
        _ui.RunPosted();

        Assert.True(_coordinator.IsShutDown);
        Assert.DoesNotContain("Config reloaded", _toasts.Texts);
    }

    [Fact]
    public async Task ReloadRequestedAfterShutdownDoesNotTouchTheRuntime()
    {
        _coordinator.BeginShutdown();

        await _coordinator.ReloadAsync();

        Assert.Equal(0, _runtime.Reloads);
    }

    [Fact]
    public void EmergencyStopDisposesScriptResourcesWhenTheGateIsFree()
    {
        _coordinator.StopRuntimeForEmergency();

        Assert.Equal(1, _runtime.EmergencyStops);
        Assert.Equal(0, _runtime.Interrupts);
    }

    [Fact]
    public async Task EmergencyStopInterruptsAWedgedReloadInsteadOfWaitingForever()
    {
        _runtime.ReleaseFirstReload.Reset();
        var wedged = _coordinator.ReloadAsync();
        Assert.True(_runtime.FirstReloadEntered.Wait(TimeSpan.FromSeconds(10)));

        _coordinator.StopRuntimeForEmergency();

        Assert.Equal(1, _runtime.Interrupts);
        Assert.Equal(0, _runtime.EmergencyStops);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("could not acquire script gate", StringComparison.Ordinal));
        _runtime.ReleaseFirstReload.Set();
        await wedged;
        Assert.Equal(1, _runtime.EmergencyStops);
    }

    [Fact]
    public void DisposeRuntimeDisposesItWhenIdle()
    {
        _coordinator.DisposeRuntime();

        Assert.Equal(1, _runtime.Disposals);
    }

    [Fact]
    public async Task DisposeRuntimeGivesUpOnAReloadThatNeverFinishes()
    {
        _runtime.ReleaseFirstReload.Reset();
        var wedged = _coordinator.ReloadAsync();
        Assert.True(_runtime.FirstReloadEntered.Wait(TimeSpan.FromSeconds(10)));

        _coordinator.DisposeRuntime();

        Assert.Equal(0, _runtime.Disposals);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("skipping script dispose", StringComparison.Ordinal));
        _runtime.ReleaseFirstReload.Set();
        await wedged;
    }
}
