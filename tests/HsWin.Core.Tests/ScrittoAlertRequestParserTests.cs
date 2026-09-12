using HsWin.Core.Alerts;

namespace HsWin.Core.Tests;

public sealed class ScrittoAlertRequestParserTests
{
    [Fact]
    public void FromScriptArgumentsLeavesRollOffByDefault()
    {
        var request = AlertRequestParser.FromScriptArguments("5");

        Assert.False(request.AnimateRoll);
        Assert.Equal(0, request.Trend);
        Assert.Equal(ScrittoTiming.DefaultDurationMs, request.RollDurationMs);
        Assert.False(request.Bounce);
    }

    [Theory]
    [InlineData("scritto")]
    [InlineData("roll")]
    [InlineData("animate")]
    [InlineData("animated")]
    public void FromScriptArgumentsAcceptsRollAliases(string name)
    {
        var request = AlertRequestParser.FromScriptArguments(
            "5",
            new Dictionary<string, object?> { [name] = true });

        Assert.True(request.AnimateRoll);
    }

    [Fact]
    public void FromScriptArgumentsReadsTrendBounceAndRollDuration()
    {
        var request = AlertRequestParser.FromScriptArguments(
            "5",
            new Dictionary<string, object?>
            {
                ["scritto"] = true,
                ["trend"] = -1,
                ["bounce"] = true,
                ["rollDurationMs"] = 300
            });

        Assert.True(request.AnimateRoll);
        Assert.Equal(-1, request.Trend);
        Assert.True(request.Bounce);
        Assert.Equal(300, request.RollDurationMs);
    }

    [Fact]
    public void FromScriptArgumentsReadsNestedTransitionDuration()
    {
        var request = AlertRequestParser.FromScriptArguments(
            "5",
            new Dictionary<string, object?>
            {
                ["scritto"] = true,
                ["transition"] = new Dictionary<string, object?> { ["duration"] = 300 }
            });

        Assert.Equal(300, request.RollDurationMs);
    }

    [Theory]
    [InlineData("up", 1)]
    [InlineData("down", -1)]
    [InlineData("auto", 0)]
    public void FromScriptArgumentsAcceptsTrendWords(string text, int expected)
    {
        var request = AlertRequestParser.FromScriptArguments(
            "5",
            new Dictionary<string, object?> { ["trend"] = text });

        Assert.Equal(expected, request.Trend);
    }

    [Fact]
    public void FromScriptArgumentsRejectsUnknownTrend()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlertRequestParser.FromScriptArguments(
                "5",
                new Dictionary<string, object?> { ["trend"] = 2 }));
    }

    [Fact]
    public void FromScriptArgumentsRejectsNegativeRollDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlertRequestParser.FromScriptArguments(
                "5",
                new Dictionary<string, object?> { ["rollDurationMs"] = -1 }));
    }

    [Fact]
    public void CreateRejectsUnknownTrend()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AlertRequest.Create("5", trend: 2));
    }
}
