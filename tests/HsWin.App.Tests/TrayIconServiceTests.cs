using System.Windows.Forms;

namespace HsWin.App.Tests;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void ContextMenuShowsTheRunningAppVersion()
    {
        using var harness = new TrayHarness();

        var versionItem = Assert.Single(
            harness.Items,
            item => item.Text?.StartsWith("Version ", StringComparison.Ordinal) == true);

        Assert.Equal($"Version {AppBranding.Version}", versionItem.Text);
        Assert.False(versionItem.Enabled);
        Assert.Contains(harness.Items, item => item.Text?.Contains("Emergency Stop", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("Open Config", "open")]
    [InlineData("Reload Config", "reload")]
    [InlineData("Emergency Stop (Ctrl+Alt+Shift+Esc)", "stop")]
    [InlineData("Quit", "quit")]
    public void MenuItemsInvokeTheirActions(string text, string expected)
    {
        using var harness = new TrayHarness();

        harness.Item(text).PerformClick();

        Assert.Equal([expected], harness.Calls);
    }

    [Fact]
    public void StartAtLoginReflectsAndTogglesTheSetting()
    {
        using var harness = new TrayHarness { StartAtLogin = true };
        var item = harness.Item("Start at Login");

        harness.OpenMenu();
        Assert.True(item.Checked);

        item.PerformClick();

        Assert.False(harness.StartAtLogin);
        Assert.False(item.Checked);
    }

    [Fact]
    public void InstallCliRunsOnceThenDisables()
    {
        using var harness = new TrayHarness();
        var item = harness.Item("Install CLI");
        harness.OpenMenu();
        Assert.True(item.Enabled);

        item.PerformClick();

        Assert.Equal(["install"], harness.Calls);
        Assert.False(item.Enabled);
    }

    private sealed class TrayHarness : IDisposable
    {
        private readonly TrayIconService _service;

        public TrayHarness()
        {
            _service = new TrayIconService(new TrayMenuActions(
                OpenConfig: () => Calls.Add("open"),
                ReloadConfig: () => Calls.Add("reload"),
                EmergencyStop: () => Calls.Add("stop"),
                Quit: () => Calls.Add("quit"),
                StartAtLogin: new TrayToggle(() => StartAtLogin, enabled => StartAtLogin = enabled),
                Cli: new TrayInstaller(() => CliInstalled, () =>
                {
                    Calls.Add("install");
                    CliInstalled = true;
                })));
        }

        public List<string> Calls { get; } = [];

        public bool StartAtLogin { get; set; }

        public bool CliInstalled { get; private set; }

        public IEnumerable<ToolStripMenuItem> Items => _service.ContextMenu.Items.OfType<ToolStripMenuItem>();

        public ToolStripMenuItem Item(string text) => Items.Single(item => item.Text == text);

        /// <summary>Raises the same refresh the shell triggers when the menu opens.</summary>
        public void OpenMenu()
        {
            var opening = typeof(ToolStripDropDown)
                .GetMethod("OnOpening", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            opening.Invoke(_service.ContextMenu, [new System.ComponentModel.CancelEventArgs()]);
        }

        public void Dispose() => _service.Dispose();
    }
}
