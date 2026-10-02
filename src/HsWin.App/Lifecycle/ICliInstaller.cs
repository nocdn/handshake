namespace HsWin.App.Lifecycle;

internal interface ICliInstaller
{
    string CliPath { get; }

    CliInstallResult Install();
}
