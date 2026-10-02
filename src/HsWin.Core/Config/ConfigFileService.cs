namespace HsWin.Core.Config;

public sealed class ConfigFileService
{
    public const string ConfigFileName = "config.js";

    /// <summary>
    /// Starter config for first launch. It must stay harmless: no global keyboard watches,
    /// no blocking input, and no hotkeys on keys people type every day.
    /// </summary>
    public const string DefaultConfig = """
        // Hammerspoon (Windows Edition) config
        // Edit this file, then use the tray menu (or `hspn config reload`) to reload it.

        console.log("Config loaded");

        // Ctrl+Alt+Shift+H shows a greeting. Change or remove it once you have your own bindings.
        hs.hotkey.bind(["ctrl", "alt", "shift"], "H", () => {
          hs.alert.show("Hello from Hammerspoon (Windows Edition)", { type: "success", durationMs: 1500 });
        });

        // More examples (uncomment to try):
        // hs.alert.show("Plain message", { type: "normal", durationMs: 1500 });
        // hs.alert.show("Working", { type: "normal", loading: true, durationMs: 60000 });
        // hs.hotkey.bind(["ctrl", "alt"], "F5", () => hs.media.playPause());
        // hs.timer.doEvery(60000, () => console.log("Another minute"));
        // hs.task.run("git status --short", result => console.log(result.output));
        // console.log(hs.application.runningApplications());
        // const mic = hs.audiodevice.defaultInputDevice();
        // console.log("Default mic", mic.name, mic.volume, mic.muted);
        // console.log("Mouse on primary monitor?", hs.mouse.isOnPrimaryScreen());
        // hs.window.watchFocused(win => console.log("Focused", win.title));
        // hs.hotkey.bind(["ctrl", "alt"], "mouse.middle", () => hs.alert.show("Middle mouse"));
        // hs.mouse.watchScroll(event => console.log(event.direction, event.delta));
        """;

    public ConfigFileService(string configFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ConfigFilePath = configFilePath;
    }

    public string ConfigFilePath { get; }

    public string ConfigDirectory => Path.GetDirectoryName(ConfigFilePath)
        ?? throw new InvalidOperationException("The config path must have a parent directory.");

    public void EnsureConfigFile()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigFilePath))
        {
            File.WriteAllText(ConfigFilePath, DefaultConfig);
        }
    }
}
