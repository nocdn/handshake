using HsWin.Core.Config;
using HsWin.Core.Drawing;
using HsWin.Core.Geometry;
using HsWin.Core.Screens;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class DrawingScriptRuntimeTests
{
    [Fact]
    public void RectangleSupportsChainingColorFrameAndVisibility()
    {
        var service = new Drawings();
        using var runtime = new ScriptRuntime(new() { Drawing = service });
        runtime.Reload("""
            if (!Object.isFrozen(hs.drawing) || !Object.isFrozen(hs.screen)) throw new Error('Unfrozen API');
            const rect = hs.drawing.rectangle({x: -1920, y: -100, w: 1920, h: 1080});
            if (rect.isVisible) throw new Error('Initially visible');
            rect.setFillColor({red: 0.2, green: 0.4, blue: 0.6, alpha: 0.5})
                .setFrame({x: 5, y: 6, width: 300, height: 400}).show();
            if (!rect.isVisible) throw new Error('Hidden after show');
            rect.hide();
            if (rect.isVisible) throw new Error('Visible after hide');
            """);
        var drawing = Assert.Single(service.Items);
        Assert.Equal(new(5, 6, 300, 400), drawing.Frame);
        Assert.Equal(new(0.2, 0.4, 0.6, 0.5), drawing.Color);
        Assert.Equal(1, drawing.Shows);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("dispose")]
    [InlineData("delete")]
    public void DeletionAliasesAreIdempotentAndRejectReuse(string alias)
    {
        var service = new Drawings();
        using var runtime = new ScriptRuntime(new() { Drawing = service });
        runtime.Reload($$"""
            const rect = hs.drawing.rectangle({x:0,y:0,w:10,h:10}).show();
            rect.{{alias}}(); rect.{{alias}}(); rect.hide();
            if (rect.isVisible) throw new Error('Disposed drawing visible');
            for (const update of [() => rect.show(), () => rect.setFrame({x:0,y:0,w:1,h:1}), () => rect.setFillColor({})]) {
                let rejected = false;
                try { update(); } catch { rejected = true; }
                if (!rejected) throw new Error('Disposed drawing reused');
            }
            """);
        runtime.Dispose();
        Assert.Equal(1, service.Items[0].Disposals);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("failedReload")]
    [InlineData("emergencyStop")]
    [InlineData("dispose")]
    public void LifecycleDestroysAllDrawings(string action)
    {
        var service = new Drawings();
        using var runtime = new ScriptRuntime(new() { Drawing = service });
        runtime.Reload("hs.drawing.rectangle({x:0,y:0,width:1920,height:1080}).show();");
        switch (action)
        {
            case "reload": runtime.Reload(""); break;
            case "failedReload":
                Assert.ThrowsAny<Exception>(() => runtime.Reload("hs.drawing.rectangle({x:0,y:0,w:50,h:50}).show(); throw new Error('failed');"));
                break;
            case "emergencyStop": runtime.EmergencyStop(); break;
            case "dispose": runtime.Dispose(); break;
        }
        Assert.All(service.Items, drawing => { Assert.False(drawing.IsVisible); Assert.Equal(1, drawing.Disposals); });
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{x:0,y:0,w:0,h:1}")]
    [InlineData("{x:0,y:0,w:-1,h:1}")]
    [InlineData("{x:NaN,y:0,w:1,h:1}")]
    [InlineData("{x:0.5,y:0,w:1,h:1}")]
    [InlineData("{x:2147483647,y:0,w:1,h:1}")]
    public void InvalidFramesNeverCreateWindows(string frame)
    {
        var service = new Drawings();
        using var runtime = new ScriptRuntime(new() { Drawing = service });
        Assert.ThrowsAny<Exception>(() => runtime.Reload($"hs.drawing.rectangle({frame});"));
        Assert.Empty(service.Items);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("'black'")]
    [InlineData("{alpha:-1}")]
    [InlineData("{red:1.01}")]
    [InlineData("{green:NaN}")]
    [InlineData("{blue:Infinity}")]
    [InlineData("{alpha:'1'}")]
    public void InvalidColorsPreserveExistingColor(string color)
    {
        var service = new Drawings();
        using var runtime = new ScriptRuntime(new() { Drawing = service });
        runtime.Reload($$"""
            const rect = hs.drawing.rectangle({x:0,y:0,w:10,h:10});
            let rejected = false;
            try { rect.setFillColor({{color}}); } catch { rejected = true; }
            if (!rejected) throw new Error('Invalid color accepted');
            """);
        Assert.Equal(new DrawingColor(), service.Items[0].Color);
    }

    [Fact]
    public void ScreenSnapshotsPreserveFullBoundsIncludingNegativeCoordinates()
    {
        using var runtime = new ScriptRuntime(new() { Screens = new Screens() });
        runtime.Reload("""
            const screens = hs.screen.allScreens();
            if (screens.length !== 2 || screens[0].bounds.x !== -1920 || screens[0].bounds.height !== 1080
                || screens[0].workingArea.height !== 1040 || screens[0].isPrimary || !screens[1].isPrimary
                || screens[1].id !== 'primary' || screens[1].name !== 'Primary') throw new Error('Incorrect screens');
            // Full monitor coverage is composed in JavaScript, rather than a blackout host API.
            const rects = screens.map(screen => hs.drawing.rectangle(screen.bounds).setFillColor({}).show());
            rects.forEach(rect => rect.delete());
            """);
    }

    [Fact]
    public void LintSupportsDrawingWithoutOpeningWindows()
    {
        Assert.True(new ConfigLinter().LintSource("hs.drawing.rectangle({x:-1920,y:0,w:1920,h:1080}).setFillColor({red:1,alpha:0.5}).show(); hs.screen.allScreens();").Success);
        Assert.False(new ConfigLinter().LintSource("hs.drawing.rectangle({x:0,y:0,w:0,h:1});").Success);
    }

    private sealed class Screens : IScreenProvider
    {
        public IReadOnlyList<ScreenSnapshot> AllScreens() => [
            new("left", "Left", false, new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040)),
            new("primary", "Primary", true, new(0, 0, 2560, 1440), new(0, 0, 2560, 1400))];
    }

    private sealed class Drawings : IDrawingService
    {
        public List<Drawing> Items { get; } = [];
        public IDrawing CreateRectangle(PixelRectangle frame)
        {
            var drawing = new Drawing(frame);
            Items.Add(drawing);
            return drawing;
        }
    }

    private sealed class Drawing(PixelRectangle frame) : IDrawing
    {
        public PixelRectangle Frame { get; private set; } = frame;
        public DrawingColor Color { get; private set; } = new();
        public int Shows { get; private set; }
        public int Disposals { get; private set; }
        public bool IsVisible { get; private set; }
        public void Show() { IsVisible = true; Shows++; }
        public void Hide() => IsVisible = false;
        public void SetFrame(PixelRectangle frame) => Frame = frame;
        public void SetFillColor(DrawingColor color) => Color = color;
        public void Dispose() { Disposals++; IsVisible = false; }
    }
}
