using System.Diagnostics;
using HsWin.Core.Applications;

namespace HsWin.Core.Tests;

public sealed class ProcessApplicationProviderTests
{
    private readonly CapturingRuntimeLogger _logger = new();

    private static string CurrentProcessName()
    {
        using var process = Process.GetCurrentProcess();
        return process.ProcessName;
    }

    [Fact]
    public void IsRunningFindsTheCurrentProcessByNameWithOrWithoutExeSuffix()
    {
        var provider = new ProcessApplicationProvider(_logger);
        var name = CurrentProcessName();

        Assert.True(provider.IsRunning(name));
        Assert.True(provider.IsRunning(name + ".exe"));
        Assert.True(provider.IsRunning(name.ToUpperInvariant()));
    }

    [Fact]
    public void IsRunningIsFalseForAProcessThatDoesNotExist()
    {
        var provider = new ProcessApplicationProvider(_logger);

        Assert.False(provider.IsRunning($"definitely-not-running-{Guid.NewGuid():N}"));
    }

    [Fact]
    public void RunningApplicationsListsTheCurrentProcessSortedByName()
    {
        var provider = new ProcessApplicationProvider(_logger);

        var applications = provider.GetRunningApplications(includeDetails: false);

        var current = Assert.Single(applications, application => application.Pid == Environment.ProcessId);
        Assert.Equal(CurrentProcessName(), current.ProcessName);
        Assert.Equal(
            applications.Select(application => application.ProcessName).Order(StringComparer.OrdinalIgnoreCase),
            applications.Select(application => application.ProcessName));
    }

    [Fact]
    public void DetailsAreOnlyReadWhenRequested()
    {
        var provider = new ProcessApplicationProvider(_logger);

        var without = provider.GetRunningApplications(includeDetails: false);
        var with = provider.GetRunningApplications(includeDetails: true);

        Assert.All(without, application =>
        {
            Assert.Null(application.Path);
            Assert.Null(application.MainWindowTitle);
        });
        var current = Assert.Single(with, application => application.Pid == Environment.ProcessId);
        Assert.False(string.IsNullOrEmpty(current.Path));
    }

    [Fact]
    public void ListingNeverThrowsForProcessesItCannotInspect()
    {
        var provider = new ProcessApplicationProvider(_logger);

        var applications = provider.GetRunningApplications(includeDetails: true);

        Assert.NotEmpty(applications);
    }
}
