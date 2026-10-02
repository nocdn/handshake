using System.Drawing;
using System.Windows.Forms;

namespace HsWin.App;

internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startAtLoginItem;
    private readonly ToolStripMenuItem _installCliItem;
    private readonly TrayMenuActions _actions;
    private bool _disposed;

    public TrayIconService(TrayMenuActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _actions = actions;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("Open Config", image: null, onClick: (_, _) => actions.OpenConfig()));
        contextMenu.Items.Add(new ToolStripMenuItem("Reload Config", image: null, onClick: (_, _) => actions.ReloadConfig()));
        contextMenu.Items.Add(new ToolStripMenuItem(
            "Emergency Stop (Ctrl+Alt+Shift+Esc)",
            image: null,
            onClick: (_, _) => actions.EmergencyStop()));

        _startAtLoginItem = new ToolStripMenuItem("Start at Login")
        {
            CheckOnClick = false
        };
        _startAtLoginItem.Click += (_, _) => ToggleStartAtLogin();
        contextMenu.Items.Add(_startAtLoginItem);

        _installCliItem = new ToolStripMenuItem("Install CLI");
        _installCliItem.Click += (_, _) => InstallCli();
        contextMenu.Items.Add(_installCliItem);

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem($"Version {AppBranding.Version}")
        {
            Enabled = false
        });
        contextMenu.Items.Add(new ToolStripMenuItem("Quit", image: null, onClick: (_, _) => actions.Quit()));
        contextMenu.Opening += (_, _) => RefreshMenuState();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Icon = SystemIcons.Application,
            Text = AppBranding.DisplayName,
            Visible = false
        };
    }

    /// <summary>The tray menu, exposed so tests can drive it without a visible shell icon.</summary>
    internal ContextMenuStrip ContextMenu => _notifyIcon.ContextMenuStrip!;

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _disposed = true;
    }

    private void ToggleStartAtLogin()
    {
        _actions.StartAtLogin.SetEnabled(!_actions.StartAtLogin.IsEnabled());
        RefreshMenuState();
    }

    private void InstallCli()
    {
        _actions.Cli.Install();
        RefreshMenuState();
    }

    private void RefreshMenuState()
    {
        _startAtLoginItem.Checked = _actions.StartAtLogin.IsEnabled();
        _installCliItem.Enabled = !_actions.Cli.IsInstalled();
    }
}
