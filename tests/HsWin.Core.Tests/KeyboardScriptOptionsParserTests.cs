using HsWin.Core.Input;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;

namespace HsWin.Core.Tests;

public sealed class KeyboardScriptOptionsParserTests
{
    [Fact]
    public void ParseWatchOptionsDefaultsToIgnoringInjectedEvents()
    {
        var options = KeyboardScriptOptionsParser.ParseWatchOptions(null);

        Assert.False(options.IncludeInjected);
        Assert.False(options.Blocking);
    }

    [Fact]
    public void ParseWatchOptionsReadsIncludeInjected()
    {
        var options = KeyboardScriptOptionsParser.ParseWatchOptions(
            new Dictionary<string, object?> { ["includeInjected"] = true });

        Assert.True(options.IncludeInjected);
    }

    [Fact]
    public void ParseWatchOptionsReadsBlocking()
    {
        var options = KeyboardScriptOptionsParser.ParseWatchOptions(
            new Dictionary<string, object?> { ["blocking"] = true });

        Assert.True(options.Blocking);
    }

    [Theory]
    [InlineData("synchronous")]
    [InlineData("sync")]
    [InlineData("swallow")]
    [InlineData("preventDefault")]
    [InlineData("prevent")]
    [InlineData("capture")]
    public void ParseWatchOptionsReadsBlockingAliases(string optionName)
    {
        var options = KeyboardScriptOptionsParser.ParseWatchOptions(
            new Dictionary<string, object?> { [optionName] = true });

        Assert.True(options.Blocking);
    }

    [Fact]
    public void ParseWatchOptionsReadsSingleKeyFilter()
    {
        var options = KeyboardScriptOptionsParser.ParseWatchOptions(
            new Dictionary<string, object?> { ["key"] = "pageup" });

        Assert.NotNull(options.KeyFilter);
        Assert.Contains(0x21u, options.KeyFilter);
    }

    [Fact]
    public void ParseWatchOptionsReadsMultipleKeyFilters()
    {
        var options = KeyboardScriptOptionsParser.ParseWatchOptions(
            new Dictionary<string, object?> { ["keys"] = new object[] { "pageup", "pagedown", 0xC0 } });

        Assert.NotNull(options.KeyFilter);
        Assert.Contains(0x21u, options.KeyFilter);
        Assert.Contains(0x22u, options.KeyFilter);
        Assert.Contains(0xC0u, options.KeyFilter);
    }

    [Fact]
    public void ParseTapOptionsReadsSuppressedModifiers()
    {
        var options = KeyboardScriptOptionsParser.ParseTapOptions(
            new Dictionary<string, object?> { ["suppressPhysicalModifiers"] = new[] { "alt", "shift" } });

        Assert.Equal(HotkeyModifiers.Alt | HotkeyModifiers.Shift, options.SuppressPhysicalModifiers);
        Assert.Equal(HotkeyModifiers.None, options.Modifiers);
    }

    [Fact]
    public void ParseTapOptionsReadsModifiers()
    {
        var options = KeyboardScriptOptionsParser.ParseTapOptions(
            new Dictionary<string, object?> { ["modifiers"] = new[] { "win", "shift" } });

        Assert.Equal(HotkeyModifiers.None, options.SuppressPhysicalModifiers);
        Assert.Equal(HotkeyModifiers.Win | HotkeyModifiers.Shift, options.Modifiers);
    }

    [Fact]
    public void ParseRepeatOptionsReadsIntervalAndSuppressedModifiers()
    {
        var options = KeyboardScriptOptionsParser.ParseRepeatOptions(
            new Dictionary<string, object?>
            {
                ["intervalMs"] = 5,
                ["suppressPhysicalModifiers"] = new[] { "alt", "shift" }
            });

        Assert.Equal(5, options.IntervalMs);
        Assert.Equal(HotkeyModifiers.Alt | HotkeyModifiers.Shift, options.SuppressPhysicalModifiers);
        Assert.Equal(InputMethod.SendInput, options.InputMethod);
    }

    [Fact]
    public void ParseRepeatOptionsReadsWindowMessageInputMethod()
    {
        var options = KeyboardScriptOptionsParser.ParseRepeatOptions(
            new Dictionary<string, object?>
            {
                ["intervalMs"] = 20,
                ["inputMethod"] = "windowMessage",
                ["keyDownMs"] = 8
            });

        Assert.Equal(20, options.IntervalMs);
        Assert.Equal(InputMethod.WindowMessage, options.InputMethod);
        Assert.Equal(8, options.KeyDownMs);
    }

    [Theory]
    [InlineData("holdMs")]
    [InlineData("pressDurationMs")]
    public void ParseRepeatOptionsReadsKeyDownDurationAliases(string optionName)
    {
        var options = KeyboardScriptOptionsParser.ParseRepeatOptions(
            new Dictionary<string, object?>
            {
                ["intervalMs"] = 100,
                [optionName] = 60
            });

        Assert.Equal(60, options.KeyDownMs);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20)]
    [InlineData(21)]
    public void ParseRepeatOptionsRejectsInvalidKeyDownDuration(int keyDownMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KeyboardScriptOptionsParser.ParseRepeatOptions(
                new Dictionary<string, object?>
                {
                    ["intervalMs"] = 20,
                    ["keyDownMs"] = keyDownMs
                }));
    }

    [Fact]
    public void ParseTapOptionsReadsWindowMessageInputMethod()
    {
        var options = KeyboardScriptOptionsParser.ParseTapOptions(
            new Dictionary<string, object?> { ["method"] = "postMessage" });

        Assert.Equal(InputMethod.WindowMessage, options.InputMethod);
    }

    [Fact]
    public void ParseRepeatOptionsRejectsUnknownInputMethod()
    {
        Assert.Throws<ArgumentException>(() =>
            KeyboardScriptOptionsParser.ParseRepeatOptions(
                new Dictionary<string, object?> { ["inputMethod"] = "unknown" }));
    }
}
