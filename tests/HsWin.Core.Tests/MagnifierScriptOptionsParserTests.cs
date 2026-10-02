using HsWin.Core.Magnifier;

namespace HsWin.Core.Tests;

public sealed class MagnifierScriptOptionsParserTests
{
    [Fact]
    public void MissingOptionsUseTheDefaults()
    {
        Assert.Equal(MagnifierOptions.Defaults, MagnifierScriptOptionsParser.Parse(null));
        Assert.Equal(MagnifierOptions.Defaults, MagnifierScriptOptionsParser.Parse(new Dictionary<string, object?>()));
    }

    [Fact]
    public void ProvidedValuesOverrideTheDefaults()
    {
        var options = MagnifierScriptOptionsParser.Parse(new Dictionary<string, object?>
        {
            ["zoom"] = 4.0,
            ["diameter"] = 400,
            ["borderWidth"] = 0,
            ["fps"] = 60,
            ["screen"] = "Mouse"
        });

        Assert.Equal(new MagnifierOptions(4, 400, 0, MagnifierScreen.Mouse, 60), options);
    }

    [Theory]
    [InlineData("zoom", 0.5)]
    [InlineData("zoom", 17.0)]
    [InlineData("zoom", double.NaN)]
    [InlineData("diameter", 79)]
    [InlineData("diameter", 1601)]
    [InlineData("diameter", 400.5)]
    [InlineData("borderWidth", -1)]
    [InlineData("borderWidth", 17)]
    [InlineData("fps", 29)]
    [InlineData("fps", 361)]
    public void OutOfRangeValuesAreRejected(string name, double value)
    {
        var options = new Dictionary<string, object?> { [name] = value };

        Assert.Throws<ArgumentException>(() => MagnifierScriptOptionsParser.Parse(options));
    }

    [Fact]
    public void UnknownScreenAndNonObjectOptionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => MagnifierScriptOptionsParser.Parse(new Dictionary<string, object?> { ["screen"] = "left" }));
        Assert.Throws<ArgumentException>(() => MagnifierScriptOptionsParser.Parse("zoom"));
    }
}
