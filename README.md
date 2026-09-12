# Hammerspoon (Windows Edition)

Hammerspoon (Windows Edition) is a tray-first Windows automation host inspired by Hammerspoon. It starts without a main window, loads JavaScript from `%APPDATA%\HsWin\config.js`, and exposes a frozen `hs` global plus `console` logging.

Reload the config from the tray menu at any time. Reload work runs off the UI thread so the tray stays responsive. A `Reloading config…` toast with a spinning loader appears immediately; when reload finishes, it is replaced by a `Config reloaded` success toast (or an error toast if reload failed). A newer toast shown from the fresh config meanwhile keeps the channel: the success receipt yields instead of clobbering it, so script-driven feedback (for example a rolling number) is never buried under older news. Error toasts always show. Each reload starts a fresh JavaScript engine, disposes previous hotkey bindings, keyboard watchers, clipboard watchers, and timers, and rotates the config console log file.

Startup is single-instance. Before the tray app creates hotkeys, keyboard hooks, timers, or the JavaScript runtime, the new process stops any older `Hammerspoon (Windows Edition)` or `HsWin.App` processes in the current Windows session and takes a named instance guard. This prevents stale instances from keeping global hotkeys registered after an upgrade or test launch.

## Tray menu

Right-click the notification area icon:

- **Open Config** — opens `%APPDATA%\HsWin\config.js` in your default editor (creates the default file first if missing).
- **Reload Config** — reloads `config.js` on a background thread (shows reload toasts; same engine reset as startup reload).
- **Emergency Stop (Ctrl+Alt+Shift+Esc)** — host safety valve: immediately stops mouse/keyboard auto-repeat, interrupts the JavaScript engine, and disposes all config hotkeys, watches, and timers. Automation stays off until you **Reload Config**. The chord is handled on the low-level keyboard hook **before** any config watchers (so it still works when the UI/script thread is wedged); the tray item is a second entry point.
- **Start at Login** — toggles whether HsWin starts when you sign in to Windows.
- **Install CLI** — adds the installed app directory to your user `PATH` so new terminals can run `hspn` (shows installing and success/error toasts).
- **Version** — shows the version of the currently running HsWin process.
- **Quit** — exits the app.

### Emergency stop

If automation runs away (for example a stuck mouse-repeat), press **Ctrl+Alt+Shift+Esc** or choose **Emergency Stop** from the tray menu. That:

1. **Immediately** stops any active `hs.mouse.repeat` / `hs.keyboard.repeat` at the host layer (does not wait on script locks)
2. Interrupts the V8 engine if a script callback is mid-execution
3. Disposes every script-owned resource (hotkeys, scroll watches, timers, etc.)
4. Shows a short **Stopped** toast; reload config when you want automation again

The chord is registered two ways: on the host keyboard hook (ahead of config, for when the UI is wedged) and via `RegisterHotKey` (for when the keyboard hook thread itself is stuck in a blocking script callback). Tray **Emergency Stop** is a third entry point. Injected input is always stopped first; full script teardown runs off the UI thread.

Blocking keyboard watches fail open (pass the key through) if the script gate is busy for more than ~30ms, so a slow toast/hotkey callback cannot freeze the keyboard. Prefer key filters and tiny blocking callbacks.

`Ctrl+Alt+Shift+Esc` is reserved for this host feature; avoid binding the same combo in `config.js`.

## Command line

The optional `hspn` CLI ships with the app but is not added to `PATH` until you choose **Install CLI** from the tray menu. It installs per-user and does not require administrator elevation. Open a new terminal after installing so Windows picks up the updated `PATH`.

```powershell
hspn --help
hspn config reload
hspn config lint                                      # lints %APPDATA%\HsWin\config.js
hspn config lint C:\Users\you\AppData\Roaming\HsWin\config.js
```

`hspn config reload` sends a command to the running tray app, so reload uses the same live runtime, hotkeys, hooks, logs, and toast path as the tray menu. `hspn config lint` validates a config file offline without touching the running app; by default it lints `%APPDATA%\HsWin\config.js`.

## Current API

The `hs` global and `console` are frozen. Reloading config rebuilds the JavaScript engine and disposes hotkeys, keyboard watchers, clipboard watchers, timers, and native keyboard repeats from the previous load.

### `hs.alert.show(text, optionsOrKind?, durationMs?)`

Shows a toast notification.

```js
hs.alert.show("Config loaded");
hs.alert.show("Saved", "success", 2000);
hs.alert.show("Something failed", { type: "error", durationMs: 4000 });
hs.alert.show("Plain message", { type: "normal", durationMs: 1500 });
hs.alert.show("Working", { type: "normal", loading: true, durationMs: 60000 });
hs.alert.show("Still working", { type: "normal", icon: "loader", durationMs: 60000 });
hs.alert.show("Testing", { style: "following", durationMs: 6000 });
```

Defaults when omitted: `type` is `success`, `style` is `standard`, and `durationMs` is `2000`.

Object options accept `type`/`kind`/`state`/`status` and `durationMs`/`duration`. Types are `normal`, `success`, and `error`; aliases include `none`, `plain`, `info`, `ok`, `done`, `fail`, `failure`, and `failed`.

Object options also accept `icon`/`indicator` and `loading`/`loader`/`spinner`. Icons are `auto`, `none`, `dot`, and `loader`; aliases include `default`, `status`, `loading`, `spinner`, `progress`, and `busy`. `loading: true` is a shortcut for `icon: "loader"`, while `loading: false` returns to automatic icon behavior. Automatic icons show no icon for `normal` toasts and a green/red dot for `success`/`error` toasts.

Set `style: "following"` (or the `variant` alias) for a click-through, text-only pill that smoothly follows the cursor. Following aliases are `follow`, `cursor`, and `cursor-following`; use `standard`, `default`, or `normal` to select the normal toast. A following toast displays only `text`—icons and status dots are intentionally suppressed—but still honors `durationMs`. Its 12 pt **Inter Medium** type, 8×4 padding, capsule colors, down-right cursor offset, screen-edge clamping, and 120 ms ease-out / 160 ms ease-in fade are based on Snap's native cursor status pill. Cursor movement uses a slightly quicker 60 ms exponential response on an independent native 240 Hz loop, while standard and following toasts use independent lifecycles, so a standard toast cannot throttle, replace, or dismiss the following pill.

The app prewarms the standard toast window at startup and keeps toast windows alive offscreen between alerts, so repeated hotkey feedback avoids recreating or remapping WPF windows. Standard toast text uses embedded **Inter Regular**, while following pills use embedded **Inter Medium**. Inter is distributed under the SIL Open Font License 1.1; see `src/HsWin.App/Assets/Fonts/LICENSE.txt`.

### `hs.alert.show` with Scritto rolling text

Pass `scritto: true` (aliases `roll`, `animate`, `animated`) and later text changes on the same toast roll glyph-by-glyph instead of snapping, in the style of [Scritto](https://github.com/JaceThings/Scritto) (SwiftUI's numeric text transition): surviving glyphs slide to their new places while the rest roll out and their replacements roll in. Any string rolls, not just digits, and graphemes (emoji, combining marks) are never torn apart.

```js
hs.alert.show("50", { type: "normal", durationMs: 1500, scritto: true });
hs.alert.show("51", { type: "normal", durationMs: 1500, scritto: true }); // rolls 0 -> 1
```

Options are `trend` (`1`/`"up"` arrives from below, `-1`/`"down"` from above, `0`/`"auto"` reads it off the numbers, default `0`), `bounce` (bouncier spring preset, default `false`), and `rollDurationMs` (per-glyph roll duration, default `550`; aliases `rollDuration`, `animationDuration`, `transitionDuration`, plus nested `transition: { duration }`). Keep the roll shorter than the gap between updates: a value changing faster than its own roll stacks outgoing ghosts, same as the web library. `hs.alert.operation(...).update(text, options)` passes these through, so operation toasts can roll too.

The first `scritto` show sets the text with no motion (rolling on first paint would animate from empty); equal text is a no-op. Rolling applies to standard toasts; following pills show instantly. With Windows animations turned off, or a zero roll duration, the text sets instantly.

### `hs.alert.operation(text, options?)`

Shows one long-running operation toast and returns a handle that can update it through each stage. This is useful for workflows such as recording, uploading, transcribing, and copying text without stacking multiple toasts.

```js
const toast = hs.alert.operation("Recording");

toast.loading("Uploading");
toast.loading("Transcribing");
toast.success("Copied");
```

Operation toasts show a loading spinner and elapsed timer by default. `update(text, options)` accepts the same options as `hs.alert.show`, plus `elapsed: false` and `resetElapsed: true`. `loading(text, options)` shows a long-lived loading toast without elapsed time unless you pass `elapsed: true`. `success(text, options)` and `error(text, options)` stop the timer and show a final toast. Handles also support `stop()`, `dispose()`, `delete()`, and `hide()`.

### `console.log`, `console.info`, `console.warn`, `console.error`

Writes to `%APPDATA%\HsWin\config-logs\MM-dd-yyyy-HH-mm.log`. Objects are JSON-serialized; `Error` values use their stack or message.

```js
console.log("Reloading config");
console.warn("Deprecated binding");
console.error(new Error("Something went wrong"));
console.log("Apps", hs.application.runningApplications());
```

### `hs.pasteboard.getContents()`, `hs.pasteboard.setContents(text)`, `hs.pasteboard.watch(callback)`, `hs.clipboard`

Gets or sets Unicode text on the Windows clipboard. `hs.clipboard` is an alias for `hs.pasteboard`.

```js
const previous = hs.pasteboard.getContents();
hs.clipboard.setContents(`${previous}\nUpdated by HsWin`);
```

`getContents()` returns an empty string when the clipboard does not currently contain text. `setContents(text)` returns `true` after the clipboard is updated.

`watch(callback)` subscribes to native Windows clipboard change notifications. The callback receives an event object with `sequence`, `contents`, and `hasText`. Returned handles support `stop()`, `dispose()`, and `delete()`; config reload disposes active watchers automatically.

```js
const watcher = hs.pasteboard.watch(event => {
  console.log("Clipboard changed", event.sequence, event.contents);
});

watcher.stop();
```

For in-place text transforms, `replaceContents(replacer)` and `replaceText(searchValue, replaceValue)` read the current clipboard, write only when the text changes, and return `{ changed, previous, current }`. `watchText(replacer)` combines `watch()` and `replaceContents()` so clipboard rewriting can be a one-liner:

```js
hs.pasteboard.watchText(text => text.replace(/\bnpm\b/g, "bun"));

const result = hs.pasteboard.replaceText(/\bnpm\b/g, "bun");
if (result.changed) {
  hs.alert.show("Clipboard updated", { durationMs: 800 });
}
```

### `hs.execute(command, options?)`

Runs a command through `cmd.exe /S /C` and returns a result object. This call is synchronous; use `hs.task.run` when you want a loader toast or other UI to keep updating while the command runs.

```js
const result = hs.execute("git status --short", {
  cwd: "C:\\Users\\me\\project",
  timeoutMs: 5000
});

if (result.success) {
  console.log(result.output);
} else {
  console.error(result.error);
}
```

Options are `cwd`/`workingDirectory`/`directory` and `timeoutMs`/`timeout`; the default timeout is 30000 ms. The result has `command`, `success`, `status` (same as `success`), `exitCode`, `output`, `error`, and `timedOut`.

### `hs.task.run(command, options?, callback)`

Runs a command through `cmd.exe /S /C` on a background thread and calls `callback(result)` when it finishes. The returned handle supports `stop()`, `dispose()`, and `delete()`; stopping it suppresses the callback, and config reload stops outstanding task handles automatically.

```js
hs.alert.show("Working…", {
  type: "normal",
  loading: true,
  durationMs: 60000
});

const task = hs.task.run("git status --short", {
  cwd: "C:\\Users\\me\\project",
  timeoutMs: 30000
}, result => {
  if (result.success) {
    hs.alert.show("Done", { type: "success", durationMs: 2500 });
  } else {
    hs.alert.show(result.error || "Command failed", { type: "error", durationMs: 6000 });
  }
});
```

If you do not need the handle, you can omit `const task =`. Options and result fields are the same as `hs.execute`.

### `hs.http.request(options, callback)`, `hs.http.get/post/put/patch/delete(urlOrOptions, options?, callback)`

Runs an HTTP request on a background thread and calls `callback(result)` when it finishes. Returned handles support `stop()`, `dispose()`, and `delete()`; stopping a request cancels it and suppresses the callback. Config reload cancels outstanding HTTP requests automatically.

```js
hs.http.get("https://api.example.com/status", {
  headers: { Authorization: "Bearer token" },
  query: { verbose: "true" },
  timeoutMs: 10000
}, result => {
  if (result.success) {
    console.log(result.statusCode, result.body);
  } else {
    console.error(result.error || result.status);
  }
});
```

Request options are `url`, `method`, `headers`, `query`/`params`, `body`, `json`, `contentType`, `form`, `multipart`, `files`, `timeoutMs`, and `responseType` (`text`, `json`, or `base64`). `json` is serialized and sent as `application/json`. `form` sends `application/x-www-form-urlencoded`. `multipart` is an array of parts; each part has `name` plus either `value` or `path`, with optional `fileName` and `contentType`. `files` can be a single path, an object such as `{ file: path }`, or an array of file parts.

```js
hs.http.post("https://api.example.com/transcribe", {
  headers: { Authorization: `Bearer ${apiKey}` },
  multipart: [
    { name: "file", path: "C:\\Users\\me\\Desktop\\clip.wav", fileName: "clip.wav", contentType: "audio/wav" },
    { name: "model", value: "scribe-v1" }
  ],
  timeoutMs: 120000
}, result => {
  if (result.success) {
    hs.pasteboard.setContents(result.json.text);
  }
});
```

HTTP results have `success`, `statusCode`, `status`, `headers`, `body`, `text`, `json`, `timedOut`, and `error`. `json` is populated when the response `Content-Type` looks like JSON and parsing succeeds.

Combined with `hs.audio.record()` and `hs.alert.operation()`, a transcription-style workflow can keep one toast alive through the whole operation:

```js
const toast = hs.alert.operation("Recording");
const path = "C:\\Users\\me\\Desktop\\clip.wav";

const recording = hs.audio.record({ path, overwrite: true }, event => {
  if (event.type !== "stopped") return;

  toast.loading("Uploading");
  hs.http.post("https://api.example.com/transcribe", {
    headers: { Authorization: `Bearer ${apiKey}` },
    multipart: [{ name: "file", path, contentType: "audio/wav" }],
    timeoutMs: 120000
  }, result => {
    if (!result.success) {
      toast.error(result.error || "Transcription failed");
      return;
    }

    toast.loading("Transcribing");
    hs.pasteboard.setContents(result.json.text);
    toast.success("Copied");
  });
});

hs.timer.doAfter(5000, () => recording.stop());
```

### `hs.hotkey.bind(modifiers, key, callback)`

Registers a global hotkey or mouse-button binding. Bindings from a previous config reload are disposed automatically.

```js
hs.hotkey.bind(["ctrl", "alt"], "R", () => hs.alert.show("Hotkey pressed"));
hs.hotkey.bind([], "`", () => hs.media.playPause());
hs.hotkey.bind(["ctrl", "alt"], "mouse.middle", () => hs.alert.show("Middle mouse"));
```

Supported modifiers are `alt`, `option`, `opt`, `ctrl`, `control`, `shift`, `cmd`, `command`, `win`, `windows`, and `meta`.

`key` is a letter (`A`-`Z`), digit (`0`-`9`), function key (`F1`-`F24`), a named key, or a mouse button. Named keyboard keys include `backspace`, `delete`/`del`, `tab`, `enter`/`return`, `escape`/`esc`, `space`, `pageup`, `pagedown`, `home`, `end`, arrows, `insert`/`ins`, punctuation names (`semicolon`, `comma`, `period`, `slash`, and others), and punctuation literals such as `` ` ``, `-`, `=`, `[`, `]`. Mouse button keys include `mouse.middle`, `mouse.back`, `mouse.forward`, `middle`, `back`, `forward`, `thumb1`, `thumb2`, `xbutton1`, `xbutton2`, `button3`–`button5`, and related `mouse.*` forms.

`bind` returns a registration handle; config reload disposes it automatically. You rarely need to keep the return value.

### `hs.hotkey.bindHeld(modifiers, key, pressedFn, releasedFn, options?)`, `hs.hotkey.whileHeld(...)`

Runs `pressedFn(event)` once when a keyboard hotkey or supported mouse button is pressed, and `releasedFn(event)` when it is released. This is the ergonomic wrapper for press-and-hold workflows such as recording only while a hotkey is held.

```js
let recording = null;
let toast = null;
const path = "C:\\Users\\me\\Desktop\\dictation.wav";

hs.hotkey.bindHeld(["ctrl", "alt"], "space", () => {
  toast = hs.alert.operation("Recording");
  recording = hs.audio.record({ path, overwrite: true }, event => {
    if (event.type === "stopped") {
      toast.loading("Uploading");
      // Upload/transcribe with hs.http.post(...), then copy and toast.success("Copied").
    }
  });
}, () => {
  if (recording) {
    const activeRecording = recording;
    recording = null;
    activeRecording.stop();
  }
});
```

Mouse buttons use the same API:

```js
let repeat = null;

hs.hotkey.bindHeld([], "mouse.back", () => {
  repeat = hs.mouse.repeat("right", { intervalMs: 20 });
}, () => {
  if (repeat) {
    repeat.stop();
    repeat = null;
  }
}, { blocking: false });
```

Mouse held callbacks support `blocking`/`swallow` (default `true`). Set it to `false` when the physical button should continue through to the active application, such as preserving the browser Back action outside a game. Keyboard-only options are `includeInjected` (default `false`), `allowExtraModifiers` (default `false`), and `repeat` (default `false`). Returned handles support `stop()`, `dispose()`, and `delete()`; config reload disposes them automatically.

If a hotkey, keyboard-watch, or timer callback throws, the runtime shows an error toast instead of crashing the host.

### `hs.application.isRunning(processName)`

Returns whether a process with that name is running. The name may include or omit the `.exe` extension; matching is case-insensitive and compares the executable file name.

Each call asks Windows for processes with that name, which costs a few milliseconds. That is fine on a hotkey press, but avoid calling it from a tight timer loop: a `doEvery(1000, () => apex.refresh())` poll keeps the process-table cost and a log line running forever. Cache the answer in JavaScript and refresh it when the user actually presses a binding:

```js
const apex = {
  processName: "r5apex_dx12.exe",
  isRunning: false,

  refresh() {
    this.isRunning = hs.application.isRunning(this.processName);
    return this.isRunning;
  }
};

apex.refresh();

hs.hotkey.bind([], "`", () => {
  if (apex.refresh()) {
    hs.alert.show("Play/Pause", { durationMs: 400 });
    hs.media.playPause();
  }
});
```

Hotkey callbacks (`hs.hotkey.bind`) run off the input hook, so the few-millisecond query never delays physical input. **Blocking keyboard-watch callbacks (`hs.keyboard.watch(..., { blocking: true })`) do run on the input hook**, though — if you refresh state from one, keep a short cache (for example, trust a `true` result for a few seconds) so the hook path stays tiny.

A slow timer refresh is another option when a script genuinely needs the answer to stay current without user interaction, but pick the longest interval that still works — a process query per tick is real CPU and a runtime-log line per tick.

### `hs.application.runningApplications(options?)`

Returns an array of running application snapshots:

```js
for (const app of hs.application.runningApplications()) {
  console.log(app.pid, app.processName, app.mainWindowTitle, app.path);
}
```

Each item has `pid`, `processName`, `mainWindowTitle`, and `path`. Reading titles and executable paths requires per-process module queries that get expensive on large process tables and can fail for elevated processes, so pass `{ includeDetails: false }` when you only need names and pids (for example to find an app by name):

```js
const apps = hs.application.runningApplications({ includeDetails: false });
const apexRunning = apps.some(app => app.processName === "r5apex");
```

### `hs.application.launch(target, options?)`

Opens a URL, document, executable, or registered app target through the Windows shell.

```js
if (!hs.application.isRunning("notepad.exe")) {
  hs.application.launch("notepad.exe");
}

hs.application.launch("https://www.hammerspoon.org/");
```

Options are `cwd`/`workingDirectory`/`directory` and `arguments`/`args`. The result has `target`, `success`, `processId`, and `error`; `processId` can be `null` for shell-handled targets such as URLs.

### `hs.window.focusedWindow()`, `hs.window.get(id)`

Returns the focused window, or looks up a window by id. Window objects expose their snapshot fields and helper methods for moving or refreshing the window.

```js
const win = hs.window.focusedWindow();

if (win) {
  console.log(win.id, win.title, win.processName);
  console.log(win.frame.x, win.frame.y, win.frame.width, win.frame.height);
}
```

Window snapshots have `id`, `title`, `processId`, `processName`, `frame`, `isMinimized`, `isMaximized`, and `isVisible`. `frame` uses Windows virtual desktop pixels, so windows and monitors left of or above the primary display can have negative `x`/`y` values.

Window objects support:

```js
win.refresh();
win.moveToScreen(hs.mouse.getCurrentScreen());
win.moveToMouseScreen();
win.moveToScreenNative(hs.mouse.getCurrentScreen());
win.moveToMouseScreenNative();
```

`moveToScreen(screen, options?)` and `moveToMouseScreen(options?)` return `{ windowId, success, moved, reason, frame }`. Options are `preserveSize` (default `true`) and `useWorkingArea`/`workingArea` (default `true`). The move preserves the window's relative position on the source monitor when possible, clamps it into the target monitor's working area, and handles minimized or maximized windows through Windows window placement.

`moveToScreenNative(screen)` and `moveToMouseScreenNative()` use Windows' own `Win+Shift+Left/Right` monitor move shortcut. This is best for focused maximized windows because it lets the Windows shell move them the same way it does for a physical keyboard shortcut. It currently supports horizontally arranged monitors; use `moveToScreen()` as the fallback for non-horizontal layouts.

### `hs.window.watchFocused(callback)`, `hs.window.onFocused(callback)`

Subscribes to foreground-window changes and calls `callback(win)` with a window object. The returned handle supports `stop()`, `dispose()`, and `delete()`; config reload stops active window watches automatically.

This example makes taskbar activation and normal app switching move the focused app window to the display currently under the mouse cursor:

```js
hs.window.watchFocused(win => {
  win.moveToMouseScreenNative();
});
```

For a one-shot move, use:

```js
hs.window.moveFocusedToMouseScreen();
hs.window.moveFocusedToScreen(hs.mouse.getCurrentScreen());
hs.window.moveFocusedToMouseScreenNative();
hs.window.moveFocusedToScreenNative(hs.mouse.getCurrentScreen());
```

### `hs.media.playPause()`, `hs.media.previousTrack()`, `hs.media.nextTrack()`

Controls the current Windows media session when one is available, otherwise sends the corresponding media key.

```js
// Show feedback first; media session work can take tens of ms on a cold call.
hs.alert.show("Play/Pause", { durationMs: 400 });
const result = hs.media.playPause();

hs.media.previousTrack();
hs.media.nextTrack();
```

Each call returns `command`, `success`, `action`, `statusBefore`, `statusAfter`, and `backend` (`mediaSession` when a Windows media session handled the command, otherwise `sendInput`). `playPause` actions are `played`, `paused`, `toggled`, or `playPause`; track actions use `previousTrack` and `nextTrack`. Playback status strings are typically `playing`, `paused`, `stopped`, or `unknown`.

### `hs.audiodevice`, `hs.sound`, `hs.audio`

Reads and updates Windows audio endpoint volume and mute state, including microphones. `hs.sound` is a default-output shortcut with `getVolume()`, `setVolume(volume)`, `getMuted()`, `setMuted(muted)`, and `toggleMute()`. `hs.audiodevice` can target a specific output or input device by id.

```js
const output = hs.audiodevice.defaultOutputDevice();
console.log(output.name, output.volume, output.muted);

output.setVolume(35);
output.setMuted(false);

for (const device of hs.audiodevice.allOutputDevices()) {
  console.log(device.id, device.name, device.volume, device.muted);
}

hs.sound.setVolume(20);
hs.sound.toggleMute();
```

Device objects have `id`, `name`, `kind` (`output` or `input`), `isDefault`, `volume`, `muted`, plus `getVolume()`, `setVolume(volume)`, `getMuted()`, `setMuted(muted)`, and `toggleMute()`. The `set*` and `toggleMute()` methods return a volume snapshot (`id`, `name`, `volume`, `muted`). Module-level output calls are `hs.audiodevice.getVolume(deviceId?)`, `setVolume(volume, deviceId?)`, `getMuted(deviceId?)`, `setMuted(muted, deviceId?)`, and `toggleMute(deviceId?)`. Module-level input calls are `getInputVolume(deviceId?)`, `setInputVolume(volume, deviceId?)`, `getInputMuted(deviceId?)`, `setInputMuted(muted, deviceId?)`, and `toggleInputMute(deviceId?)`. Volume is 0-100.

```js
const mic = hs.audiodevice.defaultInputDevice();
console.log(mic.name, mic.volume, mic.muted);

for (const device of hs.audiodevice.allInputDevices()) {
  console.log(device.id, device.name, device.isDefault);
}

mic.setVolume(80);
mic.setMuted(false);
```

`hs.audio.record(optionsOrPath?, callback)` records from the default microphone unless `deviceId` is supplied. The callback receives `started`, `level`, `stopped`, and `error` events. Recording handles support `stop()`, `dispose()`, and `delete()`; config reload stops active recordings automatically.

```js
const recorder = hs.audio.record({
  path: "C:\\Users\\me\\Desktop\\note.m4a",
  deviceId: hs.audiodevice.defaultInputDevice().id,
  quality: "high",
  levelIntervalMs: 250
}, event => {
  if (event.type === "level") {
    console.log("mic peak", event.peak, "rms", event.rms);
  }

  if (event.type === "stopped") {
    hs.alert.show(`Recorded ${event.path}`, { type: "success", durationMs: 2500 });
  }

  if (event.type === "error") {
    hs.alert.show(event.message, { type: "error", durationMs: 6000 });
  }
});

hs.timer.doAfter(5000, () => recorder.stop());
```

For quick one-off WAV recording, pass a path string:

```js
const recording = hs.audio.record("C:\\Users\\me\\Desktop\\clip.wav", event => {
  if (event.type === "stopped") console.log(event.path, event.durationMs);
});
```

Recording options are `path`, `deviceId`, `format` (`wav`, `mp3`, `m4a`, or `aac`), `quality` (`low`, `medium`, or `high`), `bitrateKbps`, `overwrite`, `levelIntervalMs`, and `maxDurationMs`/`durationMs`/`stopAfterMs`. If `path` is omitted, recordings go to `%APPDATA%\HsWin\recordings`. If `overwrite` is false and the target exists, HsWin writes to a numbered sibling path instead of clobbering the old file.

Use `hs.audio.levels(options?, callback)` when you only need microphone level events without writing a file:

```js
const meter = hs.audio.levels({ intervalMs: 100 }, event => {
  console.log(event.deviceName, event.peak, event.rms);
});

hs.timer.doAfter(3000, () => meter.stop());
```

### `hs.mouse.click(button)`, `hs.mouse.repeat(button, options?)`, `hs.mouse.stopRepeat()`

Sends a native mouse click at the current cursor position, or starts a native repeat loop. Supported buttons are `left`, `right`, `middle`, `back`/`xbutton1`, and `forward`/`xbutton2`, with `button1` through `button5` and `mouse.*` aliases. `repeat` starts with one immediate click and accepts `intervalMs`/`interval` from `1` to `1000` milliseconds. The optional `inputMethod` is `sendInput` (the default global Windows input path) or `windowMessage` (posts button messages to the focused window).

The repeat handle supports `stop()` / `dispose()` / `delete()`, plus `setIntervalMs(ms)` / `intervalMs` to change rate **without** tearing down the session (preferred when adjusting rate from scroll). Only one mouse-repeat session is active at a time; starting a new one replaces the previous. `hs.mouse.stopRepeat()` force-stops the active session even if a script lost its handle. Config reload stops active repeats automatically.

```js
hs.mouse.click("right");

const repeat = hs.mouse.repeat("right", { intervalMs: 20, inputMethod: "windowMessage" });
repeat.setIntervalMs(10);
hs.timer.doAfter(1000, () => {
  repeat.stop();
  // or: hs.mouse.stopRepeat();
});
```

### `hs.mouse.watchScroll(callback, options?)`

Subscribes to global mouse wheel events (vertical wheel and horizontal tilt wheel). **Callbacks always run off the Windows low-level mouse hook path**, so they never delay physical mouse input or hold the script lock on the hook thread.

To **prevent the focused app from seeing the scroll** while a feature is active (for example so Minecraft does not change inventory slots), register the watcher with preventDefault **only for the duration of that feature**, then `stop()` it when done:

- Options: `{ preventDefault: true }` (aliases: `blocking`, `swallow`, `prevent`, `capture`)
- While registered with preventDefault, matching scroll events are swallowed **natively on the hook path** (no JavaScript on the hook)
- The callback still receives the event asynchronously for rate changes / toasts / logging

```js
// Observe all scroll events without affecting apps.
hs.mouse.watchScroll(event => {
  console.log(event.direction, event.delta, event.axis, event.x, event.y);
});

// Swallow vertical scroll only while auto-click is held.
let scrollWatch = null;
let clickIntervalMs = 20;
let repeat = null;

function startAutoClick() {
  repeat = hs.mouse.repeat("right", { intervalMs: clickIntervalMs, inputMethod: "windowMessage" });
  scrollWatch = hs.mouse.watchScroll(event => {
    if (event.axis !== "vertical" || !repeat) {
      return;
    }

    const stepMs = event.direction === "up" ? -5 : 5;
    clickIntervalMs = Math.min(1000, Math.max(1, clickIntervalMs + stepMs));
    repeat.setIntervalMs(clickIntervalMs);
    hs.alert.show(`Click every ${clickIntervalMs}ms`, { type: "normal", durationMs: 600 });
  }, { preventDefault: true, axes: "vertical" });
}

function stopAutoClick() {
  if (scrollWatch) {
    scrollWatch.stop();
    scrollWatch = null;
  }

  if (repeat) {
    repeat.stop();
    repeat = null;
  }

  hs.mouse.stopRepeat(); // belt-and-suspenders if a race lost the handle
}
```

Each event has:

- `type` — always `"scroll"`
- `axis` — `"vertical"` or `"horizontal"`
- `direction` — `"up"` / `"down"` for vertical, `"left"` / `"right"` for horizontal
- `delta` — signed Windows wheel delta (one notch is typically `120`; positive means up/away or right)
- `notches` — `delta / 120` (can be fractional for high-resolution wheels)
- `isVertical` / `isHorizontal` / `isInjected`
- `modifiers` / `modifierFlags` — keyboard modifiers held at the time of the scroll
- `x` / `y` — cursor position in virtual desktop pixels (can be negative on multi-monitor setups)

Options:

- `includeInjected` (default `false`) — also receive synthetic/injected wheel events
- `preventDefault` / `blocking` / `swallow` / `prevent` / `capture` (default `false`) — while this watcher is registered, matching scroll events are swallowed natively so the active app does not receive them; callbacks still run off-hook
- `axes` / `axis` — `"vertical"`, `"horizontal"`, `"both"` (default), or an array such as `["vertical"]` (aliases include `v`/`y`/`wheel` and `h`/`x`/`tilt`)
- `prepend` / `priority` / `first` (default `false`) — register this watcher ahead of existing ones

Returned handles support `stop()`, `dispose()`, and `delete()`; config reload disposes them automatically. The low-level mouse hook is shared with mouse-button hotkeys and is installed only while at least one mouse hotkey or scroll watcher is registered.

**Safety:** never put heavy work in a way that assumes the mouse hook waits for JavaScript. With this API, preventDefault swallow is host-side and callbacks are always asynchronous.

### `hs.mouse.getCurrentScreen()`, `hs.mouse.isOnPrimaryScreen()`

Returns the Windows monitor containing the current mouse cursor, or `null` if the host cannot resolve it.

```js
const screen = hs.mouse.getCurrentScreen();

if (screen) {
  console.log(screen.id, screen.name, screen.isPrimary);
  console.log(screen.mousePosition.x, screen.mousePosition.y);
  console.log(screen.bounds.x, screen.bounds.y, screen.bounds.width, screen.bounds.height);
}

if (hs.mouse.isOnPrimaryScreen()) {
  hs.alert.show("Mouse is on the primary monitor", { durationMs: 800 });
}
```

Screen snapshots have `id`, `name`, `isPrimary`, `mousePosition`, `bounds`, and `workingArea`. Rectangle fields use Windows virtual desktop pixels, so monitors left of or above the primary display can have negative `x`/`y` values. `isOnPrimaryScreen()` is a shortcut for checking whether the current cursor monitor is primary.

### `hs.keyboard.watch(callback, options?)`

Subscribes to global keyboard events. Watchers are non-blocking by default: callbacks run off the Windows keyboard hook path, so they can observe input without delaying keystrokes. Non-blocking callbacks cannot swallow input; returning `true` is ignored and logged as a warning.

Use `{ blocking: true }` (aliases: `swallow`, `preventDefault`, `prevent`, `capture`) only for watchers that must return `true` to swallow the physical event so the active app does not receive it. Blocking watchers run on the keyboard hook path, so keep slow work in `hs.timer.doAfter`, `hs.task.run`, or a normal hotkey callback. For simple key-to-key remaps, prefer `hs.keyboard.remap(fromKey, toKey)` so the hook path stays native and fast.

```js
const watcher = hs.keyboard.watch(event => {
  console.log(event.key);
});

const blocker = hs.keyboard.watch(event => {
  if (event.type === "keydown" && event.key === "w" && event.modifiers.includes("alt")) {
    hs.alert.show("Alt+W");
    return true;
  }

  return false;
}, { preventDefault: true });

const pageWatcher = hs.keyboard.watch(event => {
  console.log("Navigation key", event.key, event.type);
}, { keys: ["pageup", "pagedown"] });

watcher.stop();
blocker.stop();
pageWatcher.stop();
```

Returns a handle with `stop()`, `dispose()`, and `delete()` (any casing). Config reload stops active watchers automatically.

The implementation uses `WH_KEYBOARD_LL` for global key events and blocking. Injected events are ignored by default to avoid feedback loops when scripts call `hs.keyboard.tap`.

Event fields are `type` (`keydown` or `keyup`), `keyCode`, `key`, `modifiers` (`ctrl`, `alt`, `shift`, `win`), `modifierFlags`, `isKeyDown`, `isKeyUp`, `isModifier`, `isInjected`, and `isExtended`.

Options:

| Option | Default | Description |
| --- | --- | --- |
| `includeInjected` | `false` | When `true`, callbacks also receive synthetic events. |
| `blocking` | `false` | When `true`, the callback runs synchronously on the keyboard hook path and may return `true` to swallow the event. Aliases are `synchronous`, `sync`, `swallow`, `preventDefault`, `prevent`, and `capture`. |
| `key` / `keys` | all keys | Restricts the watcher to one key or an array of keys. Values can be key names or numeric virtual-key codes. Use this for blocking watchers whenever possible so unrelated keypresses never enter JavaScript on the hook path. Aliases are `keyCode` and `keyCodes`. |

### `hs.keyboard.remap(sourceKey, targetKey)`

Remaps one physical keyboard key to another. The source key is swallowed on both keydown and keyup; each source keydown sends the target key. Remaps are handled in native host code rather than JavaScript callbacks, so normal typing stays responsive even while remaps are active. Config reload disposes remaps automatically.

```js
hs.keyboard.remap("pageup", "end");
hs.keyboard.remap("pagedown", "home");
```

### `hs.keyboard.tap(key, options?)`, `hs.keyboard.repeat(key, options?)`, `hs.keyboard.repeatPulse(key, options)`, `hs.keyboard.keyDown(key)`, `hs.keyboard.keyUp(key)`, `hs.keyboard.isDown(key)`

Sends or queries keyboard input. `key` accepts the same keyboard key names as `hs.hotkey.bind`, or a numeric virtual-key code (0–255).

```js
hs.keyboard.tap("w");
hs.keyboard.tap("right", { modifiers: ["win", "shift"] });
const repeat = hs.keyboard.repeat("w", { intervalMs: 15 });
// Continuous game actions need a visible key-down phase, not an instantaneous tap:
const gameRepeat = hs.keyboard.repeatPulse("shift", {
  intervalMs: 120,
  keyDownMs: 60,
  inputMethod: "sendInput",
  suppressPhysicalModifiers: ["ctrl", "shift"]
});
repeat.stop();
hs.keyboard.keyDown("shift");
hs.keyboard.keyUp("shift");

if (hs.keyboard.isDown("alt")) {
  console.log("Alt is physically down");
}
```

`tap` defaults to `SendInput`, the supported Win32 global input injection API. `repeat` emits immediate taps and rejects modifier keys because an instantaneous modifier transition is generally unobservable. For continuously sampled actions, use `repeatPulse` with a positive `keyDownMs` so the application observes a held phase. Pass `inputMethod: "windowMessage"` (aliases: `window-message`, `postMessage`, `window`) only as an alternate delivery path for applications that ignore injected input but process posted `WM_KEYDOWN`/`WM_KEYUP`; some GLFW applications do not update gameplay state from posted messages. When called inside a blocking `hs.keyboard.watch` callback, HsWin queues the injected input until the hook callback returns so the physical key can be swallowed first. For ordinary key-to-key remaps, use `hs.keyboard.remap` instead. Pass `modifiers` / `withModifiers` / `holdModifiers` to hold modifiers while tapping the key. To use modifiers as a trigger chord while sending a plain key, temporarily suppress them around the tap:

```js
hs.keyboard.tap(event.keyCode, { suppressPhysicalModifiers: ["alt", "shift"] });
```

Aliases for `suppressPhysicalModifiers` are `suppressModifiers` and `withoutModifiers`.

For modifier keys, `hs.keyboard.isDown` uses the low-level hook's physical-key tracker while the hook is active. Injected key-up events used by `suppressPhysicalModifiers` therefore do not make a physically held Ctrl, Shift, Alt, or Win key appear released to script lifecycle checks.

Both repeat APIs accept `intervalMs`/`interval` (default `10`, allowed range `1`–`1000`), the same `suppressPhysicalModifiers` / `suppressModifiers` / `withoutModifiers` aliases as `tap`, and optional `inputMethod` / `method` (`sendInput` default, or `windowMessage`). `repeatPulse` additionally requires `keyDownMs` (aliases: `holdMs`, `pressDurationMs`) greater than `0` and less than `intervalMs`; `repeat` rejects held-duration options so tap and pulse semantics cannot be confused. The repeat handle supports `setIntervalMs(ms)` / `intervalMs` to change rate without restarting; pulse intervals must remain greater than `keyDownMs`. `hs.keyboard.stopRepeat()` force-stops the active keyboard repeat session.

`repeat` and `repeatPulse` run natively and log start/stop performance summaries including the effective interval. They are much faster than implementing a high-frequency loop in JavaScript with `hs.timer.doEvery`. The host keeps only one active native repeat at a time, replaces any previous repeat when a new one starts, and releases suppressed modifiers without re-pressing them on every tick. While a repeat is active, suppressed physical modifiers also have a native hook fallback so they cannot leak merely because the JavaScript callback gate is busy. Window-message repeats release those modifiers in both global input state and the target window's message state. Returned handles support `stop()`, `dispose()`, and `delete()` (any casing).

### `hs.timer.doAfter(delayMs, callback)`, `hs.timer.doEvery(intervalMs, callback)`

Runs JavaScript callbacks later or repeatedly on the app dispatcher thread. Timer delays and intervals must be at least 1 millisecond. Returned handles support `stop()`, `dispose()`, and `delete()`; config reload stops active timers automatically.

```js
hs.timer.doAfter(500, () => hs.alert.show("Half a second later"));

const timer = hs.timer.doEvery(35, () => {
  hs.keyboard.tap("w");
});

timer.stop();
```

### Example config

The default `config.js` created on first run combines application checks, alerts, hotkeys, media controls, and a user-scripted Alt+Shift turbo-repeat. The repeat helper uses an explicit `starting` state so rapid physical keydown callbacks cannot create multiple native repeat handles before the first one is stored.

```js
console.log("Reloading config");

const turboRepeat = {
  modifiers: ["alt", "shift"],
  intervalMs: 15,
  keyCode: null,
  repeat: null,
  state: "idle",
  sequence: 0,

  stop() {
    this.sequence++;

    const repeat = this.repeat;
    this.repeat = null;
    this.keyCode = null;
    this.state = "idle";

    if (repeat) {
      repeat.stop();
    }
  },

  hasTriggerModifiers(event) {
    return this.modifiers.every(modifier => event.modifiers.includes(modifier));
  },

  start(event) {
    if (this.keyCode !== null && this.keyCode !== event.keyCode) {
      this.stop();
    }

    if (this.state !== "idle") {
      return;
    }

    this.state = "starting";
    this.keyCode = event.keyCode;
    const sequence = ++this.sequence;

    try {
      const repeat = hs.keyboard.repeat(this.keyCode, {
        intervalMs: this.intervalMs,
        suppressPhysicalModifiers: this.modifiers
      });

      if (this.sequence !== sequence || this.state !== "starting") {
        repeat.stop();
        return;
      }

      this.repeat = repeat;
      this.state = "running";
    } catch (error) {
      if (this.sequence === sequence) {
        this.repeat = null;
        this.keyCode = null;
        this.state = "idle";
      }

      throw error;
    }
  }
};

hs.keyboard.watch(event => {
  if (event.isModifier) {
    if (!turboRepeat.hasTriggerModifiers(event)) {
      turboRepeat.stop();
    }

    return false;
  }

  if (event.type === "keydown" && turboRepeat.hasTriggerModifiers(event)) {
    turboRepeat.start(event);
    return true;
  }

  if (event.type === "keyup" && event.keyCode === turboRepeat.keyCode) {
    turboRepeat.stop();
    return true;
  }

  return false;
}, { blocking: true });

const apex = {
  processName: "r5apex_dx12.exe",
  isRunning: false,

  refresh() {
    this.isRunning = hs.application.isRunning(this.processName);
    return this.isRunning;
  }
};

apex.refresh();
hs.timer.doEvery(1000, () => apex.refresh());

hs.hotkey.bind(["ctrl", "alt", "shift"], "F12", () => {
  const isRunning = apex.refresh();

  hs.alert.show(
    isRunning ? "Apex is running" : "Apex is not running",
    { type: isRunning ? "success" : "error", durationMs: 2000 }
  );
});

hs.hotkey.bind([], "`", () => {
  if (apex.isRunning) {
    hs.alert.show("Play/Pause", { durationMs: 400 });
    hs.media.playPause();
  }
});

hs.hotkey.bind([], "delete", () => {
  if (apex.isRunning) {
    hs.media.previousTrack();
  }
});

hs.hotkey.bind([], "pageup", () => {
  if (apex.isRunning) {
    hs.media.nextTrack();
  }
});

// Other examples:
// console.log("Any values you want to inspect", { hello: "world" });
// hs.alert.show("Plain message", { type: "normal", durationMs: 1500 });
// hs.alert.show("Working", { type: "normal", loading: true, durationMs: 60000 });
// hs.task.run("git status --short", result => console.log(result.output));
// console.log(hs.application.runningApplications());
// hs.hotkey.bind(["ctrl", "alt"], "mouse.middle", () => hs.alert.show("Middle mouse"));
// hs.hotkey.bind([], "mouse.back", () => hs.alert.show("Thumb back"));
// hs.hotkey.bind([], "mouse.forward", () => hs.alert.show("Thumb forward"));
// hs.mouse.watchScroll(event => console.log(event.direction, event.delta));
```

## Projects

- `src/HsWin.App`: WPF tray application, toast window, native hotkeys, keyboard hook, input injection, timers, clipboard, shell launching, audio/media control, startup integration, editor launching.
- `src/HsWin.Cli`: `hspn` console executable for config reload and lint commands.
- `src/HsWin.Core`: config file creation, alert contracts, service contracts, ClearScript runtime, and script-facing API wiring.
- `tests/HsWin.Core.Tests`: parser, config, and JavaScript bridge tests.
- `tests/HsWin.Cli.Tests`: command-line parser, help, and lint command behavior.
- `tests/HsWin.App.Tests`: WPF-side constants and app-layer behavior that can be tested without fragile screenshots.

## Logs

- Runtime diagnostics: `%APPDATA%\HsWin\runtime-logs\MM-dd-yyyy-HH-mm.log`
- JavaScript console output: `%APPDATA%\HsWin\config-logs\MM-dd-yyyy-HH-mm.log`

The runtime diagnostics log rotates on every app launch. The JavaScript console log rotates on every config reload.

Runtime diagnostics intentionally redact shell command text and URL query values. Logs keep command fingerprints, lengths, HTTP method, host/path, and other request metadata instead of persisting likely secrets from automation scripts.

Recent builds also write timing lines to the runtime log for hotkey dispatch, toast show/layout/position, and media commands (`Toast show timing`, `Media session timing`, `elapsedMs=...`). Keyboard remapping diagnostics log physical navigation key events, watcher swallow decisions, deferred injected input, and deferred input completion. Use these when tuning perceived latency or debugging remaps.

Startup cleanup writes `Previous instance cleanup completed`, `Stopping previous HsWin instance`, and `Single instance guard acquired` lines to the runtime log when it scans for or terminates older instances.

## Development

```powershell
dotnet build HsWin.slnx
dotnet test HsWin.slnx
.\scripts\Build-Installer.ps1
```

The installer is built with Inno Setup and written to `artifacts\installer\hswin-x64-setup.exe`. Local installer builds get a timestamp-based development version by default so repeated installs are easy to distinguish; releases pass the GitHub Actions run number explicitly. It installs the published `win-x64` app and shows a checked `Launch Hammerspoon (Windows Edition)` option on the final setup page.

## Releases

The GitHub Actions release workflow runs on `main` pushes and manual dispatches. It builds and tests on Windows, compiles the Inno Setup installer, uploads the installer as a workflow artifact, and publishes a normal GitHub release tagged with the workflow run number.

Each release also generates the three versioned manifests required for the Windows Package Manager community repository, hashes the exact release installer, and attaches `winget-manifests-v<version>.zip` to the release. See [WinGet packaging](packaging/winget/README.md) for first-submission, local-test, and update instructions. Once the initial package is accepted, users can install it with `winget install --exact --id nocdn.HammerspoonWindows`.

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- **Authors and committers:** [Bartosz Bak (@nocdn)](https://github.com/nocdn)
- **Reviewers:** [Bartosz Bak (@nocdn)](https://github.com/nocdn). Contributions from anyone without direct commit access must be reviewed before they are merged.
- **Approvers:** [Bartosz Bak (@nocdn)](https://github.com/nocdn). Every release signing request requires manual approval.

All members assigned to these roles must use multi-factor authentication for GitHub and SignPath. Official release binaries must be produced from this repository by the GitHub Actions release workflow. The project signs only its own binaries; bundled upstream dependencies retain their original signatures or remain unsigned and are not re-signed as project code. Product and version metadata must match across every binary in a signed release.

See the [privacy policy](PRIVACY.md). This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License

Hammerspoon (Windows Edition) is available under the [MIT License](LICENSE). The bundled [Inter](https://github.com/rsms/inter) fonts are available under the SIL Open Font License 1.1 in `src/HsWin.App/Assets/Fonts/LICENSE.txt`. Third-party dependencies remain under their respective licenses.
