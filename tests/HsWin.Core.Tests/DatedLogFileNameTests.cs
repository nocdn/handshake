using HsWin.Core.Logging;

namespace HsWin.Core.Tests;

public sealed class DatedLogFileNameTests
{
    [Fact]
    public void CreateUniquePathUsesRequestedDateTimeFormat()
    {
        using var directory = TemporaryDirectory.Create();
        var timestamp = new DateTimeOffset(2026, 5, 19, 13, 47, 30, TimeSpan.Zero);

        var path = DatedLogFileName.CreateUniquePath(directory.Path, timestamp);

        Assert.Equal("2026-05-19-13-47.log", Path.GetFileName(path));
    }

    [Fact]
    public void CreateUniquePathDoesNotOverwriteSameMinuteLogs()
    {
        using var directory = TemporaryDirectory.Create();
        var timestamp = new DateTimeOffset(2026, 5, 19, 13, 47, 30, TimeSpan.Zero);
        var firstPath = DatedLogFileName.CreateUniquePath(directory.Path, timestamp);
        File.WriteAllText(firstPath, "existing");

        var secondPath = DatedLogFileName.CreateUniquePath(directory.Path, timestamp);

        Assert.Equal("2026-05-19-13-47-2.log", Path.GetFileName(secondPath));
    }
}
