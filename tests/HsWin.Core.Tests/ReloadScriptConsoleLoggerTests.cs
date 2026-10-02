using HsWin.Core.Logging;

namespace HsWin.Core.Tests;

public sealed class ReloadScriptConsoleLoggerTests
{
    [Fact]
    public void BeginReloadCreatesNewLogFile()
    {
        using var directory = TemporaryDirectory.Create();
        var timestamp = new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero);
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => timestamp);

        logger.BeginReload("config.js");

        Assert.Equal("2026-05-19-13-47.log", Path.GetFileName(logger.CurrentLogFilePath));
        Assert.True(File.Exists(logger.CurrentLogFilePath));
        logger.Dispose();
    }

    [Fact]
    public void BeginReloadRotatesToNewFile()
    {
        using var directory = TemporaryDirectory.Create();
        var timestamp = new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero);
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => timestamp);

        logger.BeginReload("config.js");
        var firstPath = logger.CurrentLogFilePath;
        logger.BeginReload("config.js");
        var secondPath = logger.CurrentLogFilePath;

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal("2026-05-19-13-47-2.log", Path.GetFileName(secondPath));
        logger.Dispose();
    }

    [Fact]
    public void WriteAppendsToCurrentReloadLog()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero));

        logger.BeginReload("config.js");
        logger.Write("log", "hello world");

        // Writes are queued to a background worker; Dispose flushes the tail before asserting.
        logger.Dispose();
        var contents = File.ReadAllText(logger.CurrentLogFilePath!);
        Assert.Contains("[log] hello world", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposeFlushesAllQueuedWritesInOrder()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero));

        logger.BeginReload("config.js");
        for (var i = 0; i < 200; i++)
        {
            logger.Write("log", $"line {i}");
        }

        logger.Dispose();
        var lines = File.ReadAllLines(logger.CurrentLogFilePath!);
        Assert.Contains(lines, line => line.Contains("[log] line 0", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("[log] line 199", StringComparison.Ordinal));
        Assert.Equal(201, lines.Length);
    }

    [Fact]
    public void RotationCompletesPreviousFileBeforeNextOne()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero));

        logger.BeginReload("config.js");
        logger.Write("log", "first file");
        logger.BeginReload("config.js");
        logger.Write("log", "second file");

        logger.Dispose();
        var firstLines = File.ReadAllLines(Path.Combine(directory.Path, "2026-05-19-13-47.log"));
        var secondLines = File.ReadAllLines(Path.Combine(directory.Path, "2026-05-19-13-47-2.log"));
        Assert.Contains(firstLines, line => line.Contains("[log] first file", StringComparison.Ordinal));
        Assert.Contains(secondLines, line => line.Contains("[log] second file", StringComparison.Ordinal));
        Assert.DoesNotContain(secondLines, line => line.Contains("first file", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteBeforeBeginReloadLazilyCreatesLogFile()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero));

        logger.Write("log", "before any reload");

        Assert.NotNull(logger.CurrentLogFilePath);
        logger.Dispose();
        var contents = File.ReadAllText(logger.CurrentLogFilePath!);
        Assert.Contains("[log] before any reload", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesAfterDisposeAreIgnored()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero));

        logger.BeginReload("config.js");
        logger.Dispose();
        logger.Write("log", "after dispose");

        var lines = File.ReadAllLines(logger.CurrentLogFilePath!);
        Assert.DoesNotContain(lines, line => line.Contains("after dispose", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposeCanBeCalledTwice()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = new ReloadScriptConsoleLogger(directory.Path, () => new DateTimeOffset(2026, 5, 19, 13, 47, 0, TimeSpan.Zero));

        logger.BeginReload("config.js");
        logger.Dispose();
        logger.Dispose();
    }
}
