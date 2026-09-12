using HsWin.Core.Alerts;

namespace HsWin.Core.Tests;

public sealed class ScrittoTrendTests
{
    [Theory]
    [InlineData("2 minutes 12 seconds", "2 minutes 13 seconds", 1)]
    [InlineData("5", "3", -1)]
    [InlineData("3", "5", 1)]
    [InlineData("$1,204.00", "$1,205.00", 1)]
    [InlineData("100%", "99%", -1)]
    [InlineData("Battery 84%", "Battery 83%", -1)]
    public void FirstDifferingNumberDecides(string previous, string next, int expected)
    {
        Assert.Equal(expected, ScrittoTrend.Of(previous, next));
    }

    [Theory]
    [InlineData("59 seconds", "1 minute 0 seconds")]
    [InlineData("abc", "def")]
    [InlineData("5", "5")]
    [InlineData("-$4.00", "$4.00")]
    public void ChangedCountOrNoNumbersReadsAsRise(string previous, string next)
    {
        Assert.Equal(1, ScrittoTrend.Of(previous, next));
    }
}
