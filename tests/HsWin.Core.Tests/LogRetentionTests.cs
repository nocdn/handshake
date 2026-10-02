using HsWin.Core.Logging;

namespace HsWin.Core.Tests;

public sealed class LogRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DeletesLogsOlderThanMaxAgeButKeepsTheMostRecent()
    {
        using var directory = TemporaryDirectory.Create();
        var old = CreateLog(directory, "old.log", Now.AddDays(-30));
        var older = CreateLog(directory, "older.log", Now.AddDays(-40));
        var recent = CreateLog(directory, "recent.log", Now.AddDays(-1));

        var deleted = LogRetention.Prune(directory.Path, Now, TimeSpan.FromDays(14), minimumKept: 1);

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(recent));
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(older));
    }

    [Fact]
    public void KeepsTheNewestFilesEvenWhenAllAreOld()
    {
        using var directory = TemporaryDirectory.Create();
        var newest = CreateLog(directory, "newest.log", Now.AddDays(-60));
        var middle = CreateLog(directory, "middle.log", Now.AddDays(-70));
        var oldest = CreateLog(directory, "oldest.log", Now.AddDays(-80));

        var deleted = LogRetention.Prune(directory.Path, Now, TimeSpan.FromDays(14), minimumKept: 2);

        Assert.Equal(1, deleted);
        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(middle));
        Assert.False(File.Exists(oldest));
    }

    [Fact]
    public void LeavesFilesThatAreNotLogs()
    {
        using var directory = TemporaryDirectory.Create();
        var notes = Path.Combine(directory.Path, "notes.txt");
        File.WriteAllText(notes, "keep me");
        File.SetLastWriteTimeUtc(notes, Now.AddDays(-90).UtcDateTime);

        var deleted = LogRetention.Prune(directory.Path, Now, TimeSpan.FromDays(14), minimumKept: 0);

        Assert.Equal(0, deleted);
        Assert.True(File.Exists(notes));
    }

    [Fact]
    public void SkipsFilesThatAreInUse()
    {
        using var directory = TemporaryDirectory.Create();
        var locked = CreateLog(directory, "locked.log", Now.AddDays(-30));
        var removable = CreateLog(directory, "removable.log", Now.AddDays(-31));

        using var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
        var deleted = LogRetention.Prune(directory.Path, Now, TimeSpan.FromDays(14), minimumKept: 0);

        Assert.Equal(1, deleted);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(removable));
    }

    [Fact]
    public void MissingDirectoryIsNotAnError()
    {
        using var directory = TemporaryDirectory.Create();

        var deleted = LogRetention.Prune(Path.Combine(directory.Path, "nope"), Now, TimeSpan.FromDays(14), minimumKept: 0);

        Assert.Equal(0, deleted);
    }

    private static string CreateLog(TemporaryDirectory directory, string name, DateTimeOffset lastWrite)
    {
        var path = Path.Combine(directory.Path, name);
        File.WriteAllText(path, "log");
        File.SetLastWriteTimeUtc(path, lastWrite.UtcDateTime);
        return path;
    }
}
