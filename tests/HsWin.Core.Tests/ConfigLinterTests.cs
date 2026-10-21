using HsWin.Core.Config;

namespace HsWin.Core.Tests;

public sealed class ConfigLinterTests
{
    [Fact]
    public void LintSourceAllowsMouseScrollWithoutInjectingInput()
    {
        var result = new ConfigLinter().LintSource("hs.mouse.scroll(1); hs.mouse.scroll(-0.5);");

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void LintSourceAllowsDefaultConfig()
    {
        var result = new ConfigLinter().LintSource(ConfigFileService.DefaultConfig);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void LintSourceCatchesLiteralZeroTimerInsideCallback()
    {
        var result = new ConfigLinter().LintSource("""
            hs.hotkey.bind(["ctrl"], "A", () => {
              hs.timer.doAfter(0, () => {});
            });
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "HSWIN001");
        Assert.Equal(ConfigLintSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Line);
        Assert.Contains("at least 1 millisecond", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LintSourceIgnoresTimerTextInStringsAndComments()
    {
        var result = new ConfigLinter().LintSource("""
            console.log("hs.timer.doAfter(0, () => {})");
            // hs.timer.doEvery(0, () => {});
            /* hs.timer.doAfter(0, () => {}); */
            hs.timer.doAfter(1, () => {});
            """);

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void LintSourceCatchesScriptApiValidationErrors()
    {
        var result = new ConfigLinter().LintSource("""hs.hotkey.bind(["hyper"], "A", () => {});""");

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "HSWIN100");
        Assert.Equal(ConfigLintSeverity.Error, diagnostic.Severity);
        Assert.Contains("Unsupported hotkey modifier", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LintSourceRejectsModifierTapRepeatInsideCallback()
    {
        var result = new ConfigLinter().LintSource("""
            hs.hotkey.bind(["ctrl"], "A", () => {
              hs.keyboard.repeat("shift", { intervalMs: 120 });
            });
            """);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "HSWIN002");
        Assert.Equal(ConfigLintSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Line);
        Assert.Contains("repeatPulse", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""hs.keyboard.repeatPulse("shift", { intervalMs: 120 });""")]
    [InlineData("""hs.keyboard.repeatPulse("shift", { intervalMs: 120, keyDownMs: 0 });""")]
    public void LintSourceRejectsPulseWithoutPositiveLiteralDuration(string source)
    {
        var result = new ConfigLinter().LintSource(source);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == "HSWIN003");
        Assert.Equal(ConfigLintSeverity.Error, diagnostic.Severity);
        Assert.Contains("keyDownMs", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LintSourceAllowsDynamicPositivePulseDuration()
    {
        var result = new ConfigLinter().LintSource("""
            const pulse = { keyDownMs: 60 };
            hs.hotkey.bind(["ctrl"], "A", () => {
              hs.keyboard.repeatPulse("shift", {
                intervalMs: 120,
                keyDownMs: pulse.keyDownMs
              });
            });
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Code is "HSWIN002" or "HSWIN003");
    }

    [Fact]
    public void LintSourceIgnoresKeyboardRepeatTextInStringsAndComments()
    {
        var result = new ConfigLinter().LintSource("""
            console.log("hs.keyboard.repeat('shift', { intervalMs: 10 })");
            // hs.keyboard.repeat("ctrl", { intervalMs: 10 });
            /* hs.keyboard.repeatPulse("shift", { intervalMs: 120 }); */
            """);

        Assert.DoesNotContain(result.Diagnostics, item => item.Code is "HSWIN002" or "HSWIN003");
    }

    [Fact]
    public void LintSourceReportsRuntimeErrorLocationInsideTheUserConfig()
    {
        var result = new ConfigLinter().LintSource("""
            const unused = 1;

            hs.hotkey.bind(["hyper"], "A", () => {});
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("HSWIN100", diagnostic.Code);
        Assert.Equal(3, diagnostic.Line);
        Assert.NotNull(diagnostic.Column);
        Assert.DoesNotContain("config.js:", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LintSourceKeepsRuntimeErrorOnADifferentLineFromAStaticDiagnostic()
    {
        var result = new ConfigLinter().LintSource("""
            hs.hotkey.bind(["ctrl"], "A", () => {
              hs.timer.doAfter(0, () => {});
            });
            hs.hotkey.bind(["hyper"], "B", () => {});
            """);

        Assert.Contains(result.Diagnostics, item => item.Code == "HSWIN001" && item.Line == 2);
        Assert.Contains(result.Diagnostics, item => item.Code == "HSWIN100" && item.Line == 4);
    }

    [Fact]
    public void LintSourceDoesNotRepeatAStaticDiagnosticAsARuntimeError()
    {
        var result = new ConfigLinter().LintSource("""hs.timer.doEvery(0, () => {});""");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("HSWIN001", diagnostic.Code);
    }

    [Fact]
    public void LintSourceStopsAConfigThatNeverFinishesLoading()
    {
        var linter = new ConfigLinter(TimeSpan.FromMilliseconds(200));

        var result = linter.LintSource("""
            let count = 0;
            while (true) {
              count++;
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("HSWIN100", diagnostic.Code);
        Assert.Contains("did not finish", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("endless loop", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("alt")]
    [InlineData("WIN")]
    [InlineData("lctrl")]
    [InlineData("rshift")]
    [InlineData("0x10")]
    [InlineData("165")]
    public void LintSourceRecognizesEveryModifierNameTheRuntimeAccepts(string key)
    {
        var literal = char.IsAsciiDigit(key[0]) ? key : $"\"{key}\"";

        var result = new ConfigLinter().LintSource($"hs.keyboard.repeat({literal}, {{ intervalMs: 120 }});");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("HSWIN002", diagnostic.Code);
    }

    [Fact]
    public void LintSourceLeavesUnknownKeyNamesToTheRuntimeCheck()
    {
        var result = new ConfigLinter().LintSource("""hs.keyboard.repeat("hyper", { intervalMs: 120 });""");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("HSWIN100", diagnostic.Code);
    }

    [Theory]
    [InlineData("holdMs")]
    [InlineData("pressDurationMs")]
    public void LintSourceAcceptsKeyDownAliasesForPulses(string propertyName)
    {
        var result = new ConfigLinter().LintSource(
            $$"""hs.keyboard.repeatPulse("shift", { intervalMs: 120, {{propertyName}}: 40 });""");

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
    }
}
