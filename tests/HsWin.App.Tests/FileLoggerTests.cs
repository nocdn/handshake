using System.IO;
using HsWin.Core.Logging;

namespace HsWin.App.Tests;

public sealed class FileLoggerTests
{
    [Fact]
    public void DisposeFlushesQueuedInfoWarningAndError()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = FileLogger.CreateForLaunch(directory.Path);

        logger.Info("hello");
        logger.Warning("careful");
        logger.Error("failed", new InvalidOperationException("boom"));
        logger.Dispose();

        var contents = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[INFO] hello", contents, StringComparison.Ordinal);
        Assert.Contains("[WARN] careful", contents, StringComparison.Ordinal);
        Assert.Contains("[ERROR] failed", contents, StringComparison.Ordinal);
        Assert.Contains("boom", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void EntriesWithoutAnExceptionAreNotFollowedByABlankLine()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = FileLogger.CreateForLaunch(directory.Path);

        logger.Info("one");
        logger.Warning("two");
        logger.Dispose();

        Assert.DoesNotContain(File.ReadAllLines(logger.LogFilePath), string.IsNullOrWhiteSpace);
    }

    [Fact]
    public async Task LogRetentionTaskRemovesOldLogsAndReportsIt()
    {
        using var directory = TemporaryDirectory.Create();
        var extra = 5;
        for (var index = 0; index < LogRetention.DefaultMinimumKept + extra; index++)
        {
            var path = Path.Combine(directory.Path, $"log-{index}.log");
            File.WriteAllText(path, "old");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-365).AddMinutes(index));
        }

        var logger = new CapturingRuntimeLogger();

        await LogRetentionTask.Start(logger, directory.Path, Path.Combine(directory.Path, "missing"));

        Assert.Equal(LogRetention.DefaultMinimumKept, Directory.GetFiles(directory.Path, "*.log").Length);
        Assert.Contains(logger.Infos, message => message.Contains($"Removed {extra} old log", StringComparison.Ordinal));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public void CreateForLaunchWritesInitializationLineImmediately()
    {
        using var directory = TemporaryDirectory.Create();
        using var logger = FileLogger.CreateForLaunch(directory.Path);

        var contents = File.ReadAllText(logger.LogFilePath);

        Assert.Contains("[INFO] Runtime logger initialized.", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesFromMultipleThreadsAreFlushed()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = FileLogger.CreateForLaunch(directory.Path);

        Parallel.For(0, 100, index => logger.Info($"message-{index}"));
        logger.Dispose();

        var contents = File.ReadAllText(logger.LogFilePath);
        for (var index = 0; index < 100; index++)
        {
            Assert.Contains($"message-{index}", contents, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DisposeCanBeCalledTwice()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = FileLogger.CreateForLaunch(directory.Path);

        logger.Info("before dispose");
        logger.Dispose();
        logger.Dispose();

        Assert.Contains("before dispose", File.ReadAllText(logger.LogFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void WritesAfterDisposeAreIgnored()
    {
        using var directory = TemporaryDirectory.Create();
        var logger = FileLogger.CreateForLaunch(directory.Path);

        logger.Info("before dispose");
        logger.Dispose();
        logger.Info("after dispose");

        var contents = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("before dispose", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("after dispose", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateForLaunchCreatesLogDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var nested = Path.Combine(directory.Path, "runtime-logs");

        using var logger = FileLogger.CreateForLaunch(nested);

        Assert.True(Directory.Exists(nested));
        Assert.StartsWith(nested, logger.LogFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateForLaunchUsesUniqueLogFiles()
    {
        using var directory = TemporaryDirectory.Create();
        using var first = FileLogger.CreateForLaunch(directory.Path);
        using var second = FileLogger.CreateForLaunch(directory.Path);

        Assert.NotEqual(first.LogFilePath, second.LogFilePath);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            return new TemporaryDirectory(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "HsWin.App.Tests",
                Guid.NewGuid().ToString("N")));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
