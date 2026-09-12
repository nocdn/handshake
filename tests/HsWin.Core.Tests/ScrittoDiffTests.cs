using HsWin.Core.Alerts;

namespace HsWin.Core.Tests;

public sealed class ScrittoDiffTests
{
    private static IReadOnlyList<string> Old(string value) => ScrittoGraphemes.Split(value);

    [Fact]
    public void CounterGainingADigitKeepsNothing()
    {
        var result = ScrittoDiff.Compute(Old("9"), "10");

        Assert.Equal(0, result.PrefixCount);
        Assert.Equal(0, result.SuffixCount);
    }

    [Fact]
    public void SingleDigitChangeKeepsTheRest()
    {
        var result = ScrittoDiff.Compute(Old("1,204"), "1,205");

        Assert.Equal(4, result.PrefixCount);
        Assert.Equal(0, result.SuffixCount);
        Assert.Equal(5, result.OldSuffix);
        Assert.Equal(5, result.MidEnd);
    }

    [Fact]
    public void UnitSuffixSurvivesNumberChange()
    {
        var result = ScrittoDiff.Compute(Old("1 unread"), "2 unread");

        Assert.Equal(0, result.PrefixCount);
        Assert.Equal(7, result.SuffixCount);
    }

    [Fact]
    public void FloatingRunInTheMiddleIsKept()
    {
        var result = ScrittoDiff.Compute(Old("xxlightxx"), "yylightyy");

        Assert.Equal(0, result.PrefixCount);
        Assert.Equal(5, result.SuffixCount);
        Assert.Equal(["y", "y", "l", "i", "g", "h", "t", "y", "y"], result.Labels);
    }

    [Fact]
    public void SingleSharedLetterIsNotARun()
    {
        var result = ScrittoDiff.Compute(Old("seven"), "nine");

        Assert.Equal(0, result.SuffixCount);
    }

    [Fact]
    public void GroupSeparatorKeepsBothDigits()
    {
        var result = ScrittoDiff.Compute(Old("11"), "1,001");

        Assert.Equal(1, result.PrefixCount);
        Assert.Equal(1, result.SuffixCount);
    }

    [Fact]
    public void LosingMinusKeepsTheAmount()
    {
        var result = ScrittoDiff.Compute(Old("-$4.00"), "$4.00");

        Assert.Equal(0, result.PrefixCount);
        Assert.Equal(5, result.SuffixCount);
    }

    [Fact]
    public void SentenceKeepsBothEnds()
    {
        var result = ScrittoDiff.Compute(Old("Track 1 of 12"), "Track 2 of 12");

        Assert.Equal(6, result.PrefixCount);
        Assert.Equal(6, result.SuffixCount);
    }

    [Fact]
    public void PrefixOnlyMatch()
    {
        var result = ScrittoDiff.Compute(Old("abcdef"), "abcXY");

        Assert.Equal(3, result.PrefixCount);
        Assert.Equal(0, result.SuffixCount);
    }

    [Fact]
    public void SuffixOnlyMatch()
    {
        var result = ScrittoDiff.Compute(Old("abcdef"), "XYdef");

        Assert.Equal(0, result.PrefixCount);
        Assert.Equal(3, result.SuffixCount);
    }

    [Fact]
    public void EmptyPreviousValueEntersEverything()
    {
        var result = ScrittoDiff.Compute([], "hi");

        Assert.Equal(0, result.PrefixCount);
        Assert.Equal(0, result.SuffixCount);
        Assert.Equal(["h", "i"], result.Labels);
    }

    [Fact]
    public void TravelBudgetRefusesLongSwims()
    {
        var result = ScrittoDiff.Compute(
            Old("the light you seek is within you"),
            "you are the daylight");

        Assert.Equal(0, result.PrefixCount);
        Assert.True(result.SuffixCount < 2);
    }
}
