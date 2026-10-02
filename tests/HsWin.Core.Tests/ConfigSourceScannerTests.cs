using HsWin.Core.Config;

namespace HsWin.Core.Tests;

public sealed class ConfigSourceScannerTests
{
    [Theory]
    [InlineData("'a\\'b' rest", 6)]
    [InlineData("\"x\" rest", 3)]
    [InlineData("`t${1}` rest", 7)]
    [InlineData("// note\nrest", 8)]
    [InlineData("/* a */rest", 7)]
    public void TrySkipNonCodeSkipsStringsAndComments(string source, int expectedNext)
    {
        Assert.True(ConfigSourceScanner.TrySkipNonCode(source, 0, out var next));
        Assert.Equal(expectedNext, next);
    }

    [Fact]
    public void TrySkipNonCodeLeavesCodeAndDivisionAlone()
    {
        Assert.False(ConfigSourceScanner.TrySkipNonCode("a / b", 2, out _));
        Assert.False(ConfigSourceScanner.TrySkipNonCode("hs", 0, out _));
    }

    [Theory]
    [InlineData("hs.timer.doAfter", 0, true)]
    [InlineData("x = hs.timer.doAfter", 4, true)]
    [InlineData("myhs.timer.doAfter", 2, false)]
    [InlineData("obj.hs.timer.doAfter", 4, false)]
    public void IsCallPrefixRequiresATokenBoundary(string source, int index, bool expected)
    {
        Assert.Equal(expected, ConfigSourceScanner.IsCallPrefix(source, index, "hs.timer."));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-5", -5)]
    [InlineData("1_000", 1000)]
    [InlineData(".5", 0.5)]
    [InlineData("2e3", 2000)]
    [InlineData("1e-1", 0.1)]
    public void TryReadNumberParsesDecimalLiterals(string source, double expected)
    {
        Assert.True(ConfigSourceScanner.TryReadNumber(source, 0, out var value, out var next));
        Assert.Equal(expected, value, precision: 10);
        Assert.Equal(source.Length, next);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-")]
    [InlineData(".")]
    public void TryReadNumberRejectsNonNumbers(string source)
    {
        Assert.False(ConfigSourceScanner.TryReadNumber(source, 0, out _, out _));
    }

    [Theory]
    [InlineData("0x10", 16)]
    [InlineData("0XA5", 165)]
    [InlineData("42", 42)]
    public void TryReadIntegerParsesDecimalAndHex(string source, int expected)
    {
        Assert.True(ConfigSourceScanner.TryReadInteger(source, 0, out var value, out _));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryReadIntegerRejectsFractions()
    {
        Assert.False(ConfigSourceScanner.TryReadInteger("1.5", 0, out _, out _));
    }

    [Fact]
    public void TryReadQuotedValueUnescapesCharacters()
    {
        Assert.True(ConfigSourceScanner.TryReadQuotedValue("'it\\'s' + 1", 0, out var value, out var next));
        Assert.Equal("it's", value);
        Assert.Equal(7, next);
    }

    [Fact]
    public void FindMatchingBraceIgnoresBracesInStringsAndComments()
    {
        const string source = "{ a: '}', /* } */ b: { c: 1 } } tail";

        var end = ConfigSourceScanner.FindMatchingBrace(source, 0);

        Assert.Equal(source.IndexOf(" tail", StringComparison.Ordinal), end);
    }

    [Fact]
    public void GetLocationCountsLinesAndColumnsFromOne()
    {
        Assert.Equal((1, 1), ConfigSourceScanner.GetLocation("ab\ncd", 0));
        Assert.Equal((2, 2), ConfigSourceScanner.GetLocation("ab\ncd", 4));
    }
}
