using System.IO;
using HsWin.App.Lifecycle;

namespace HsWin.App.Tests.Lifecycle;

public sealed class CliInstallCoordinatorTests
{
    private readonly CapturingRuntimeLogger _logger = new();
    private readonly RecordingAlertPresenter _toasts = new();
    private readonly FakeUiThread _ui = new();
    private readonly FakeInstaller _installer = new();
    private readonly CliInstallCoordinator _coordinator;

    public CliInstallCoordinatorTests()
    {
        _coordinator = new CliInstallCoordinator(_logger, _installer, _toasts, _ui);
    }

    [Fact]
    public async Task SuccessfulInstallShowsTheLoaderThenTheInstalledToast()
    {
        await _coordinator.InstallAsync();
        _ui.RunPosted();

        Assert.Equal(
            [CliInstallAlerts.CreateInstallingAlert().Text, CliInstallAlerts.CreateInstalledAlert().Text],
            _toasts.Texts);
        Assert.Contains(_logger.Infos, message => message.Contains(_installer.CliPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AlreadyInstalledIsReportedAsSuch()
    {
        _installer.Result = CliInstallResult.AlreadyInstalled;

        await _coordinator.InstallAsync();
        _ui.RunPosted();

        Assert.Equal(CliInstallAlerts.CreateAlreadyInstalledAlert().Text, _toasts.Texts[^1]);
    }

    [Fact]
    public async Task FailedInstallShowsTheError()
    {
        var failure = new FileNotFoundException("hspn.exe is missing");
        _installer.Failure = failure;

        await _coordinator.InstallAsync();
        _ui.RunPosted();

        Assert.Equal(CliInstallAlerts.CreateInstallFailedAlert(failure).Text, _toasts.Texts[^1]);
        Assert.Contains(_logger.Errors, error => error.Contains("Installing hspn CLI failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARequestWhileOneIsRunningIsIgnoredButAcknowledged()
    {
        _installer.Release.Reset();
        var first = _coordinator.InstallAsync();
        Assert.True(_installer.Entered.Wait(TimeSpan.FromSeconds(10)));

        await _coordinator.InstallAsync();
        _installer.Release.Set();
        await first;
        _ui.RunPosted();

        Assert.Equal(1, _installer.Installs);
        Assert.Contains(_logger.Infos, message => message.Contains("already in progress", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheNextInstallIsAllowedOnceTheOutcomeHasBeenReported()
    {
        await _coordinator.InstallAsync();
        _ui.RunPosted();

        await _coordinator.InstallAsync();

        Assert.Equal(2, _installer.Installs);
    }

    [Fact]
    public async Task ShutdownSuppressesTheOutcomeToast()
    {
        _installer.Release.Reset();
        var install = _coordinator.InstallAsync();
        Assert.True(_installer.Entered.Wait(TimeSpan.FromSeconds(10)));

        _coordinator.Shutdown();
        _installer.Release.Set();
        await install;
        _ui.RunPosted();

        Assert.Equal([CliInstallAlerts.CreateInstallingAlert().Text], _toasts.Texts);
    }

    private sealed class FakeInstaller : ICliInstaller
    {
        private int _installs;

        public string CliPath => @"C:\Apps\HsWin\hspn.exe";

        public CliInstallResult Result { get; set; } = CliInstallResult.Installed;

        public Exception? Failure { get; set; }

        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(true);

        public int Installs => Volatile.Read(ref _installs);

        public CliInstallResult Install()
        {
            Interlocked.Increment(ref _installs);
            Entered.Set();
            Assert.True(Release.Wait(TimeSpan.FromSeconds(10)), "The held install was never released.");
            return Failure is null ? Result : throw Failure;
        }
    }
}
