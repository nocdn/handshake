namespace HsWin.App.Tests;

public sealed class DefaultEditorSelectorTests
{
    [Fact]
    public void OpenIsUsedWhenTheAssociatedProgramIsAnEditor()
    {
        var associations = new FakeAssociations
        {
            ["open"] = @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            ["edit"] = @"C:\Windows\System32\notepad.exe"
        };

        Assert.Equal("open", DefaultEditorSelector.SelectVerb(associations, ".js"));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\WScript.exe")]
    [InlineData(@"C:\Windows\System32\cscript.exe")]
    [InlineData(@"C:\Program Files\nodejs\node.exe")]
    public void ScriptRunnersAreNotEditorsSoEditVerbIsUsed(string openHandler)
    {
        var associations = new FakeAssociations
        {
            ["open"] = openHandler,
            ["edit"] = @"C:\Windows\System32\notepad.exe"
        };

        Assert.Equal("edit", DefaultEditorSelector.SelectVerb(associations, ".js"));
    }

    [Fact]
    public void NoEditorIsSelectedWhenOnlyScriptRunnersOrNothingIsRegistered()
    {
        var onlyRunner = new FakeAssociations { ["open"] = @"C:\Windows\System32\wscript.exe" };

        Assert.Null(DefaultEditorSelector.SelectVerb(onlyRunner, ".js"));
        Assert.Null(DefaultEditorSelector.SelectVerb(new FakeAssociations(), ".js"));
    }

    [Fact]
    public void EditVerbIsUsedWhenThereIsNoOpenHandler()
    {
        var associations = new FakeAssociations { ["edit"] = @"C:\Windows\System32\notepad.exe" };

        Assert.Equal("edit", DefaultEditorSelector.SelectVerb(associations, ".js"));
    }

    private sealed class FakeAssociations : Dictionary<string, string>, IFileAssociations
    {
        public string? GetExecutable(string extension, string verb) => TryGetValue(verb, out var path) ? path : null;
    }
}
