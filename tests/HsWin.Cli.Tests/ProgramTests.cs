using HsWin.Cli;

namespace HsWin.Cli.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void RunWithHelpPrintsUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RunWithHelpDocumentsDefaultLintPath()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("If [path] is omitted", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"%APPDATA%\HsWin\config.js", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RunConfigHelpDocumentsDefaultLintPath()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["config", "--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("when no path is given", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"%APPDATA%\HsWin\config.js", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RunConfigLintHelpDescribesWhatLintCatches()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["config", "lint", "--help"], output, error);

        Assert.Equal(0, exitCode);
        var help = output.ToString();
        Assert.Contains("line and column", help, StringComparison.Ordinal);
        Assert.Contains("hs.keyboard.repeatPulse", help, StringComparison.Ordinal);
        Assert.Contains("5 seconds", help, StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RunConfigLintReturnsSuccessForValidConfig()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "config.js");
        File.WriteAllText(configPath, """hs.timer.doAfter(1, () => {});""");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["config", "lint", configPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("passed lint", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RunConfigLintReturnsFailureForInvalidTimer()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "config.js");
        File.WriteAllText(configPath, """hs.hotkey.bind(["ctrl"], "A", () => hs.timer.doAfter(0, () => {}));""");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = Program.Run(["config", "lint", configPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("HSWIN001", error.ToString(), StringComparison.Ordinal);
    }
}
