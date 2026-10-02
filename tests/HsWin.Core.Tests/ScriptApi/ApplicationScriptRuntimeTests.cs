using HsWin.Core.Alerts;
using HsWin.Core.Applications;
using HsWin.Core.Scripting;
using HsWin.Core.Shell;

namespace HsWin.Core.Tests;

public sealed class ApplicationScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesApplicationIsRunning()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var applications = new CapturingApplicationProvider(
            new ApplicationSnapshot(123, "r5apex", "Apex Legends", @"C:\Games\Apex\r5apex.exe"));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            Applications = applications
        });

        runtime.Reload("""
            if (hs.application.isRunning("r5apex.exe")) {
              hs.alert.show("Apex is running");
            }
            """);

        var request = Assert.Single(presenter.Requests);
        Assert.Equal("Apex is running", request.Text);
    }

    [Fact]
    public void ReloadExposesRunningApplications()
    {
        var presenter = new CapturingAlertPresenter();
        var hotkeys = new CapturingHotkeyRegistrar();
        var applications = new CapturingApplicationProvider(
            new ApplicationSnapshot(123, "chrome", "Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            new ApplicationSnapshot(456, "r5apex", "Apex Legends", @"C:\Games\Apex\r5apex.exe"));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Hotkeys = hotkeys,
            Applications = applications
        });

        runtime.Reload("""
            const apps = hs.application.runningApplications();
            const apex = apps.find(app => app.processName === "r5apex");
            hs.alert.show(`${apps.length}:${apex.pid}:${apex.path}`, "normal", 1);
            """);

        var request = Assert.Single(presenter.Requests);
        Assert.Equal(@"2:456:C:\Games\Apex\r5apex.exe", request.Text);
    }

    [Fact]
    public void ReloadExposesApplicationLaunch()
    {
        var presenter = new CapturingAlertPresenter();
        var shell = new CapturingShellService
        {
            LaunchResult = new LaunchResult("https://example.com", true, 42, null)
        };
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Shell = shell
        });

        runtime.Reload("""
            const result = hs.application.launch("https://example.com", { arguments: "--new-window", cwd: "C:\\Temp" });
            hs.alert.show(`${result.success}:${result.processId}`, "normal", 1);
            """);

        var launch = Assert.Single(shell.Launches);
        Assert.Equal("https://example.com", launch.Target);
        Assert.Equal("--new-window", launch.Options.Arguments);
        Assert.Equal(@"C:\Temp", launch.Options.WorkingDirectory);
        var request = Assert.Single(presenter.Requests);
        Assert.Equal("true:42", request.Text);
    }

    [Fact]
    public void RunningApplicationsAcceptsDetailsAlias()
    {
        var applications = new CapturingApplicationProvider(
            new ApplicationSnapshot(123, "chrome", "Chrome", @"C:\chrome.exe"));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Applications = applications
        });

        runtime.Reload("""hs.application.runningApplications({ details: false });""");

        Assert.False(applications.LastIncludeDetails);
    }

    [Fact]
    public void RunningApplicationsDefaultsToFullDetailsAndCanSkipThem()
    {
        var presenter = new CapturingAlertPresenter();
        var applications = new CapturingApplicationProvider(
            new ApplicationSnapshot(123, "chrome", "Chrome", @"C:\chrome.exe"));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Applications = applications
        });

        runtime.Reload("""hs.application.runningApplications();""");
        Assert.True(applications.LastIncludeDetails);

        runtime.Reload("""hs.application.runningApplications({ includeDetails: false });""");
        Assert.False(applications.LastIncludeDetails);
    }

    [Fact]
    public void RunningApplicationsRejectsNonBooleanIncludeDetails()
    {
        var presenter = new CapturingAlertPresenter();
        var applications = new CapturingApplicationProvider(
            new ApplicationSnapshot(123, "chrome", "Chrome", @"C:\chrome.exe"));
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Applications = applications
        });

        var exception = Assert.ThrowsAny<Exception>(
            () => runtime.Reload("""hs.application.runningApplications({ includeDetails: "yes" });"""));

        Assert.Contains("includeDetails", exception.Message, StringComparison.Ordinal);
    }
}
