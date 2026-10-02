namespace HsWin.App;

/// <summary>A checkable tray setting backed by app state.</summary>
internal sealed record TrayToggle(Func<bool> IsEnabled, Action<bool> SetEnabled);

/// <summary>A one-time tray action that disables itself once done.</summary>
internal sealed record TrayInstaller(Func<bool> IsInstalled, Action Install);

/// <summary>What the tray menu does; keeps <see cref="TrayIconService"/> free of app wiring.</summary>
internal sealed record TrayMenuActions(
    Action OpenConfig,
    Action ReloadConfig,
    Action EmergencyStop,
    Action Quit,
    TrayToggle StartAtLogin,
    TrayInstaller Cli);
