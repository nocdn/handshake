using Microsoft.Win32;

namespace HsWin.App.Tests;

/// <summary>
/// Exercises the real HKCU Run key, but only with throwaway value names, which are removed afterwards.
/// </summary>
public sealed class StartupServiceTests : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName = $"HsWinTest-{Guid.NewGuid():N}";
    private readonly string _legacyName = $"HsWinTestLegacy-{Guid.NewGuid():N}";

    public void Dispose()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.DeleteValue(_valueName, throwOnMissingValue: false);
        key.DeleteValue(_legacyName, throwOnMissingValue: false);
    }

    private static string? ReadValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    [Fact]
    public void ItIsDisabledUntilTurnedOn()
    {
        var service = new StartupService(_valueName, @"C:\Apps\HsWin\hswin.exe");

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void EnablingWritesTheQuotedExecutablePathAndDisablingRemovesIt()
    {
        var service = new StartupService(_valueName, @"C:\Program Files\HsWin\hswin.exe");

        service.SetEnabled(true);

        Assert.True(service.IsEnabled());
        Assert.Equal("\"C:\\Program Files\\HsWin\\hswin.exe\"", ReadValue(_valueName));

        service.SetEnabled(false);

        Assert.False(service.IsEnabled());
        Assert.Null(ReadValue(_valueName));
    }

    [Fact]
    public void ItIsNotEnabledWhenTheEntryPointsAtAnotherExecutable()
    {
        var service = new StartupService(_valueName, @"C:\Apps\HsWin\hswin.exe");
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
        {
            key.SetValue(_valueName, "\"C:\\Other\\app.exe\"", RegistryValueKind.String);
        }

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void LegacyEntriesAreRemovedWhenEnablingAndDisabling()
    {
        var service = new StartupService(_valueName, @"C:\Apps\HsWin\hswin.exe", _legacyName);
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
        {
            key.SetValue(_legacyName, "\"C:\\Old\\old.exe\"", RegistryValueKind.String);
        }

        service.SetEnabled(true);
        Assert.Null(ReadValue(_legacyName));

        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
        {
            key.SetValue(_legacyName, "\"C:\\Old\\old.exe\"", RegistryValueKind.String);
        }

        service.SetEnabled(false);
        Assert.Null(ReadValue(_legacyName));
    }

    [Fact]
    public void DisablingWhenNothingIsRegisteredIsHarmless()
    {
        var service = new StartupService(_valueName, @"C:\Apps\HsWin\hswin.exe", _legacyName);

        service.SetEnabled(false);

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void ABlankNameOrPathIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new StartupService(" ", @"C:\x.exe"));
        Assert.Throws<ArgumentException>(() => new StartupService("Name", ""));
    }
}
