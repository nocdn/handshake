using HsWin.Core.Config;
using HsWin.Core.Magnifier;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class MagnifierScriptRuntimeTests
{
    [Fact]
    public void LensExposesDefaultsVisibilityAndZoom()
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });
        runtime.Reload("""
            if (!Object.isFrozen(hs.magnifier)) throw new Error("API not frozen");
            const lens = hs.magnifier.new();
            if (lens.isVisible) throw new Error("Initially visible");
            lens.show();
            if (!lens.isVisible) throw new Error("Not visible after show");
            lens.setZoom(3.5);
            lens.hide();
            if (lens.isVisible) throw new Error("Still visible after hide");
            """);
        var session = Assert.Single(service.Sessions);
        Assert.Equal(new MagnifierOptions(), session.Options);
        Assert.Equal(720, session.Options.Diameter);
        Assert.Equal(240, session.Options.Fps);
        Assert.Equal(3.5, session.Zoom);
        Assert.Equal(1, session.Shows);
        Assert.Equal(1, session.Hides);
    }

    [Fact]
    public void CustomOptionsReachHostAndDeleteIsIdempotent()
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });
        runtime.Reload("""
            const lens = hs.magnifier.new({ zoom: 4, diameter: 600, borderWidth: 0, screen: "mouse", fps: 144 });
            lens.show();
            lens.delete();
            lens.hide();
            lens.delete();
            if (lens.isVisible) throw new Error("Deleted lens visible");
            let rejected = false;
            try { lens.show(); } catch { rejected = true; }
            if (!rejected) throw new Error("Deleted lens reusable");
            """);
        Assert.Equal(new MagnifierOptions(4, 600, 0, MagnifierScreen.Mouse, 144), service.Sessions[0].Options);
        runtime.Dispose();
        Assert.Equal(1, service.Sessions[0].Disposals);
    }

    [Theory]
    [InlineData("{ zoom: 0 }")]
    [InlineData("{ zoom: 17 }")]
    [InlineData("{ zoom: NaN }")]
    [InlineData("{ zoom: Infinity }")]
    [InlineData("{ diameter: 79 }")]
    [InlineData("{ diameter: 1601 }")]
    [InlineData("{ diameter: 100.5 }")]
    [InlineData("{ diameter: NaN }")]
    [InlineData("{ borderWidth: -1 }")]
    [InlineData("{ borderWidth: 17 }")]
    [InlineData("{ borderWidth: 1.5 }")]
    [InlineData("{ screen: 'unknown' }")]
    [InlineData("{ fps: 0 }")]
    [InlineData("{ fps: 29 }")]
    [InlineData("{ fps: 361 }")]
    [InlineData("{ fps: 144.5 }")]
    [InlineData("{ fps: Infinity }")]
    [InlineData("42")]
    public void InvalidOptionsFailBeforeAllocatingNativeResources(string options)
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });
        Assert.ThrowsAny<Exception>(() => runtime.Reload($"hs.magnifier.new({options});"));
        Assert.Empty(service.Sessions);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    [InlineData(360)]
    public void HeldBindingPassesRequestedFrameRateToHost(int fps)
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });
        runtime.Reload($"hs.magnifier.bindHeld(['ctrl', 'alt'], 'z', {{ fps: {fps} }});");
        Assert.Equal(fps, Assert.Single(service.Sessions).Options.Fps);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("failedReload")]
    [InlineData("emergencyStop")]
    [InlineData("dispose")]
    public void RuntimeLifecycleDestroysLensAndBinding(string action)
    {
        var service = new CapturingMagnifierService();
        var keyboard = new MagnifierTestKeyboard();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service, KeyboardEvents = keyboard });
        runtime.Reload("""
            hs.magnifier.new().show();
            hs.magnifier.bindHeld(["ctrl", "alt"], "z", { zoom: 3 });
            """);
        switch (action)
        {
            case "reload": runtime.Reload(""); break;
            case "failedReload":
                Assert.ThrowsAny<Exception>(() => runtime.Reload("hs.magnifier.new().show(); throw new Error('failed');"));
                break;
            case "emergencyStop": runtime.EmergencyStop(); break;
            case "dispose": runtime.Dispose(); break;
        }
        Assert.True(keyboard.Disposed);
        Assert.All(service.Sessions, lens => { Assert.False(lens.IsVisible); Assert.Equal(1, lens.Disposals); });
    }

    [Theory]
    [InlineData("'ctrl'")]
    public void HeldBindingRejectsUnsupportedKeys(string key)
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });
        Assert.ThrowsAny<Exception>(() => runtime.Reload($"hs.magnifier.bindHeld([], {key});"));
        Assert.Empty(service.Sessions);
    }

    [Fact]
    public void InvalidZoomChangePreservesCurrentZoom()
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });
        runtime.Reload("""
            const lens = hs.magnifier.new();
            let rejected = false;
            try { lens.setZoom(NaN); } catch { rejected = true; }
            if (!rejected) throw new Error("Invalid zoom accepted");
            """);
        Assert.Equal(2, service.Sessions[0].Zoom);
    }

    [Theory]
    [InlineData("'abc'")]
    [InlineData("null")]
    [InlineData("undefined")]
    [InlineData("({})")]
    public void SetZoomRejectsNonNumbersWithAScriptError(string zoom)
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });

        var exception = Assert.ThrowsAny<Exception>(() => runtime.Reload($"hs.magnifier.new().setZoom({zoom});"));

        Assert.Contains("zoom", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, service.Sessions[0].Zoom);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("dispose")]
    [InlineData("delete")]
    public void EveryHandleAliasDestroysTheLensOnce(string alias)
    {
        var service = new CapturingMagnifierService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices { Magnifier = service });

        runtime.Reload($"const lens = hs.magnifier.new(); lens.{alias}(); lens.{alias}();");
        runtime.Dispose();

        Assert.Equal(1, service.Sessions[0].Disposals);
    }

    [Fact]
    public void LintSupportsMagnifierWithoutOpeningWindows()
    {
        var result = new ConfigLinter().LintSource("""
            hs.magnifier.new({ screen: "primary" }).show();
            hs.magnifier.bindHeld(["ctrl", "alt"], "z", { zoom: 2 });
            hs.magnifier.bindHeld([], "mouse.back", { diameter: 720, fps: 240 });
            """);
        Assert.True(result.Success);
        Assert.False(new ConfigLinter().LintSource("hs.magnifier.new({ zoom: 0 });").Success);
    }
}
