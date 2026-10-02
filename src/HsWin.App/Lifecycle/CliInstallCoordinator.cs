using HsWin.App.Threading;
using HsWin.Core.Alerts;
using HsWin.Core.Logging;

namespace HsWin.App.Lifecycle;

/// <summary>Runs the hspn CLI install off the UI thread, one at a time, and reports the outcome in a toast.</summary>
internal sealed class CliInstallCoordinator(
    IRuntimeLogger logger,
    ICliInstaller installer,
    IAlertPresenter toasts,
    IUiThread ui)
{
    private int _installInProgress;
    private bool _shutDown;

    /// <summary>
    /// Shows the installing toast and starts the install. The returned task completes once the
    /// outcome toast has been queued; a request made while one is running is ignored.
    /// </summary>
    public Task InstallAsync()
    {
        if (Interlocked.Exchange(ref _installInProgress, 1) != 0)
        {
            logger.Info("hspn CLI install request ignored because an install is already in progress.");
            toasts.Show(CliInstallAlerts.CreateInstallingAlert());
            return Task.CompletedTask;
        }

        logger.Info("hspn CLI install requested.");
        toasts.Show(CliInstallAlerts.CreateInstallingAlert());

        return Task.Run(() =>
        {
            CliInstallResult? result = null;
            Exception? failure = null;

            try
            {
                result = installer.Install();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (Volatile.Read(ref _shutDown))
            {
                Interlocked.Exchange(ref _installInProgress, 0);
                return;
            }

            ui.Post(() => Complete(result, failure));
        });
    }

    /// <summary>Stops reporting outcomes; the app is exiting.</summary>
    public void Shutdown() => Volatile.Write(ref _shutDown, true);

    private void Complete(CliInstallResult? result, Exception? failure)
    {
        Interlocked.Exchange(ref _installInProgress, 0);
        if (Volatile.Read(ref _shutDown))
        {
            return;
        }

        if (failure is not null)
        {
            logger.Error("Installing hspn CLI failed.", failure);
            toasts.Show(CliInstallAlerts.CreateInstallFailedAlert(failure));
            return;
        }

        if (result == CliInstallResult.AlreadyInstalled)
        {
            logger.Info("hspn CLI already installed.");
            toasts.Show(CliInstallAlerts.CreateAlreadyInstalledAlert());
            return;
        }

        logger.Info($"hspn CLI installed path='{installer.CliPath}'.");
        toasts.Show(CliInstallAlerts.CreateInstalledAlert());
    }
}
