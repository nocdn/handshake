using HsWin.Core.Config;

namespace HsWin.Core.Tests;

public sealed class ConfigFileServiceTests
{
    [Fact]
    public void EnsureConfigFileCreatesDefaultConfigJs()
    {
        using var directory = TemporaryDirectory.Create();
        var configPath = Path.Combine(directory.Path, "config.js");
        var service = new ConfigFileService(configPath);

        service.EnsureConfigFile();

        Assert.True(File.Exists(configPath));
        Assert.Contains("hs.alert.show", File.ReadAllText(configPath), StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultConfigIsHarmlessForNewUsers()
    {
        var config = ConfigFileService.DefaultConfig;

        // A first-run config must never swallow input or steal everyday keys system-wide.
        Assert.DoesNotContain("hs.keyboard.watch(", ActiveLines(config), StringComparison.Ordinal);
        Assert.DoesNotContain("blocking", ActiveLines(config), StringComparison.Ordinal);
        foreach (var everydayKey in new[] { "\"`\"", "\"delete\"", "\"pageup\"", "\"space\"", "\"enter\"" })
        {
            Assert.DoesNotContain($"hs.hotkey.bind([], {everydayKey}", config, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("hs.hotkey.bind([\"ctrl\", \"alt\", \"shift\"], \"H\"", config, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultConfigPassesLint()
    {
        using var directory = TemporaryDirectory.Create();
        var configPath = Path.Combine(directory.Path, "config.js");
        new ConfigFileService(configPath).EnsureConfigFile();

        var result = new ConfigLinter().LintFile(configPath);

        Assert.Empty(result.Diagnostics);
    }

    private static string ActiveLines(string config) =>
        string.Join('\n', config.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void EnsureConfigFileDoesNotOverwriteExistingConfig()
    {
        using var directory = TemporaryDirectory.Create();
        var configPath = Path.Combine(directory.Path, "config.js");
        const string customConfig = "hs.alert.show('Custom');";
        File.WriteAllText(configPath, customConfig);
        var service = new ConfigFileService(configPath);

        service.EnsureConfigFile();

        Assert.Equal(customConfig, File.ReadAllText(configPath));
    }
}
