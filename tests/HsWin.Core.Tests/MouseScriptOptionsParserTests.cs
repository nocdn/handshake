using HsWin.Core.Input;
using HsWin.Core.Mouse;

namespace HsWin.Core.Tests;

public sealed class MouseScriptOptionsParserTests
{
    [Fact]
    public void ParseRepeatOptionsDefaultsToTenMilliseconds()
    {
        var options = MouseScriptOptionsParser.ParseRepeatOptions(null);

        Assert.Equal(10, options.IntervalMs);
    }

    [Fact]
    public void ParseRepeatOptionsReadsIntervalAliases()
    {
        var intervalMs = MouseScriptOptionsParser.ParseRepeatOptions(
            new Dictionary<string, object?> { ["interval"] = 20 });

        Assert.Equal(20, intervalMs.IntervalMs);
    }

    [Fact]
    public void ParseRepeatOptionsReadsWindowMessageInputMethod()
    {
        var options = MouseScriptOptionsParser.ParseRepeatOptions(
            new Dictionary<string, object?>
            {
                ["intervalMs"] = 20,
                ["inputMethod"] = "windowMessage"
            });

        Assert.Equal(InputMethod.WindowMessage, options.InputMethod);
    }

    [Fact]
    public void ParseRepeatOptionsDefaultsToSendInput()
    {
        var options = MouseScriptOptionsParser.ParseRepeatOptions(null);

        Assert.Equal(InputMethod.SendInput, options.InputMethod);
    }

    [Fact]
    public void ParseRepeatOptionsRejectsUnknownInputMethod()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MouseScriptOptionsParser.ParseRepeatOptions(
                new Dictionary<string, object?> { ["inputMethod"] = "unknown" }));

        Assert.Contains("sendInput", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void ParseRepeatOptionsRejectsIntervalsOutsideSupportedRange(int intervalMs)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MouseScriptOptionsParser.ParseRepeatOptions(
                new Dictionary<string, object?> { ["intervalMs"] = intervalMs }));

        Assert.Contains("between 1 and 1000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseScrollWatchOptionsDefaultsToBothAxesNonBlocking()
    {
        var options = MouseScriptOptionsParser.ParseScrollWatchOptions(null);

        Assert.False(options.IncludeInjected);
        Assert.False(options.Blocking);
        Assert.Equal(MouseScrollAxis.Both, options.Axes);
    }

    [Fact]
    public void ParseScrollWatchOptionsReadsBlockingAndAxisAliases()
    {
        var options = MouseScriptOptionsParser.ParseScrollWatchOptions(
            new Dictionary<string, object?>
            {
                ["swallow"] = true,
                ["includeInjected"] = true,
                ["axis"] = "vertical"
            });

        Assert.True(options.Blocking);
        Assert.True(options.IncludeInjected);
        Assert.Equal(MouseScrollAxis.Vertical, options.Axes);
    }

    [Theory]
    [InlineData("preventDefault")]
    [InlineData("prevent")]
    [InlineData("capture")]
    [InlineData("blocking")]
    [InlineData("swallow")]
    public void ParseScrollWatchOptionsTreatsSwallowAliasesAsBlocking(string optionName)
    {
        var options = MouseScriptOptionsParser.ParseScrollWatchOptions(
            new Dictionary<string, object?> { [optionName] = true });

        Assert.True(options.Blocking);
    }

    [Fact]
    public void ParseScrollWatchOptionsReadsAxisArray()
    {
        var options = MouseScriptOptionsParser.ParseScrollWatchOptions(
            new Dictionary<string, object?>
            {
                ["axes"] = new object[] { "horizontal", "vertical" }
            });

        Assert.Equal(MouseScrollAxis.Both, options.Axes);
    }

    [Fact]
    public void ParseScrollWatchOptionsRejectsUnknownAxis()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MouseScriptOptionsParser.ParseScrollWatchOptions(
                new Dictionary<string, object?> { ["axes"] = "diagonal" }));

        Assert.Contains("vertical", exception.Message, StringComparison.Ordinal);
    }
}
