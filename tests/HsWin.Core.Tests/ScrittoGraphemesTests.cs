using HsWin.Core.Alerts;

namespace HsWin.Core.Tests;

public sealed class ScrittoGraphemesTests
{
    [Fact]
    public void SplitKeepsPlainAsciiPerCodeUnit()
    {
        Assert.Equal(["a", "b", "c"], ScrittoGraphemes.Split("abc"));
    }

    [Fact]
    public void SplitMapsOrdinarySpacesToNonBreakingSpaces()
    {
        Assert.Equal(["a", " ", "b"], ScrittoGraphemes.Split("a b"));
    }

    [Fact]
    public void SplitKeepsSurrogatePairEmojiAsOneGrapheme()
    {
        Assert.Single(ScrittoGraphemes.Split("👋"));
    }

    [Fact]
    public void SplitKeepsCombiningMarkWithItsBase()
    {
        Assert.Single(ScrittoGraphemes.Split("é"));
    }

    [Fact]
    public void SplitHandlesEmptyString()
    {
        Assert.Empty(ScrittoGraphemes.Split(string.Empty));
    }

    [Fact]
    public void SplitKeepsDigitsStable()
    {
        Assert.Equal(["1", ",", "2", "0", "4"], ScrittoGraphemes.Split("1,204"));
    }
}
