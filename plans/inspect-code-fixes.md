# inspect-code fixes — what changed, why, and where to look if something breaks

This is the change log for the fix pass that followed the full-repo audit in
[`inspect-code-findings.md`](inspect-code-findings.md) (audit date 2026-10-02, against `main` @
`142da87`). It is written for a human or another agent debugging a regression: **if something
behaves differently from before, find the matching area below, read "How" and "Where to look", and
suspect these changes first.**

Finding numbers (`#N`) match the findings document, which has the original problem descriptions.

## Read this first

- **Scope:** findings 1–22 and 26–63 were addressed. **Not addressed (out of scope by request):**
  #23 (`moveToScreen` can report success on the wrong monitor), #25 (`_disposed` flags read across
  threads without `volatile`), #34 (alert defaults/parsing, operation-toast `hide()` hack). #24
  (emergency-stop `Thread.Sleep(25)`, unsynchronized `_engine`) was only partly fixed as a side
  effect of #2.
- **Skipped on purpose:** the `DllImport` → `LibraryImport` migration (style only; each Win32 call
  would need runtime verification), a custom tray icon (the repo ships no `.ico`; the generic
  `SystemIcons.Application` is still used), and tautological style-metric tests.
- **Mixed working tree:** the changes were made on top of a working tree that already held
  uncommitted work that is *not* part of this fix pass: the magnifier feature (`hs.magnifier`,
  `src/HsWin.App/Magnifier/`, `src/HsWin.Core/Magnifier/`), the winget packaging templates, and
  code-signing/licence files. They ship in the same commit, but a regression in those areas is not
  necessarily from this fix pass. The fix pass touched the magnifier only for #18, #31, #36, #51,
  #52 and #53.
- **Verification actually done:** `dotnet build HsWin.slnx` (0 warnings), `dotnet test HsWin.slnx`
  (Core 449, App 363, CLI 6 — all passing), `hspn config lint` on the user's config, and the
  installer built. **The app was not exercised by hand** (no real keyboard/mouse/audio/multi-monitor
  testing). Anything involving real hardware or Win32 behavior is the most likely place for a
  regression to hide: input injection (#12, #21, #22), audio (#7, #28), hotkeys (#3, #19), the
  low-level hooks (#40), focus watching (#5), timers (#36), clipboard (#18), installer/uninstaller
  (#13, #58–#60).
- **Logs for debugging:** runtime diagnostics are in `%APPDATA%\HsWin\runtime-logs`, script
  `console.*` output in `%APPDATA%\HsWin\config-logs`. Log files are now named
  `yyyy-MM-dd-HH-mm.log` (see #39).
- **User config:** the shipped default `config.js` changed (#1). The user's own config was not
  modified; the old shipped template was backed up to
  `%APPDATA%\HsWin\config.js.shipped-default-template-20261002.bak`.

## Quick triage: symptom → suspect

| Symptom | Suspect (finding) |
| --- | --- |
| Hotkeys stop working after many reloads / "hotkey registration failed" | #19 `NativeHotkeyService` ID allocation |
| Held hotkey leaves Ctrl/Alt/Shift stuck, or its key-up reaches the app | #3 `HotkeyScriptApi` held-binding state |
| Timers/watches/repeats keep running after reload or emergency stop | #2 `ScriptResourceScope` / `ScriptGeneration` |
| Reload hangs, or errors with "did not finish within 10s" | #15 reload watchdog in `ScriptRuntime` |
| Remapped keys typed in the wrong order, stuck, or cannot be held | #12 `SerializedInputQueue`, #20 remap in `KeyboardScriptApi` |
| Wrong key sent by `hs.keyboard.tap` (media keys, numpad `/`, Apps, arrows) | #21 `Native/KeyboardScanCodes` |
| `windowMessage` clicks/keys land in the wrong place | #22 `ForegroundInputTarget` |
| Timers fire at odd rates, CPU use up, or repeats feel different | #36 `PreciseTimerScheduler` |
| Focus-change watchers deadlock or stop firing | #5 `NativeWindowService` + `DedicatedDispatcherThread` |
| Clipboard reads/writes fail or hang | #18 `WpfClipboardTextStore` (own STA thread) |
| Mic level always 0, recording fails to start, empty files | #7 `NativeAudioCaptureService` |
| Volume/mute calls throw | #28 `NativeAudioDeviceController` (now NAudio CoreAudioApi) |
| Play/pause reports the wrong status | #29 `NativeMediaController` |
| `hs.task.run` / `hs.execute` surprises (killed early, wrong timeout) | #16, #17 `ProcessShellService`, `TaskScriptApi` |
| `hs.http` callback missing or error text changed | #6 `SystemHttpService` |
| "Open Config" opens the wrong program | #26 `EditorLauncher` |
| Starting a new HsWin does not replace the old one, or apps launched by it die/survive | #4 `PreviousInstanceCleaner` |
| `hspn config reload` hangs or says no instance | #9 command pipe (per-user name, timeouts) |
| Install CLI changes/breaks `PATH` | #11 `RegistryUserPathStore` |
| Uninstall leaves startup entry/PATH entry, or installer fails to build | #13, #58–#60 `HsWin.iss`, `Build-Installer.ps1` |
| Toast/clipboard/hotkey log lines missing | #14, #38 logging reductions (intentional) |
| Log files missing or "Dropped N log entries" | #39 bounded queues + retention |
| Lint reports different/duplicate diagnostics | #15, #46 linter + `ConfigSourceScanner` |
| Tray menu item missing/wrong | #54 `TrayMenuActions` |
| Quit/reload/emergency stop ordering problems | #49 `AppController` split into `Lifecycle/*` |

---

## Critical / high

### #1 — Harmless default config
- **Why:** the shipped default `config.js` was a personal Apex Legends setup that swallowed keys
  system-wide (Alt+Shift+key blocking watcher, `RegisterHotKey` on `` ` ``, `delete`, `pageup`).
- **How:** `DefaultConfig` now logs `Config loaded` and binds Ctrl+Alt+Shift+H to a greeting toast,
  with commented-out one-line samples for other APIs. An existing config is never overwritten.
- **Files:** `src/HsWin.Core/Config/ConfigFileService.cs`; tests `ConfigFileServiceTests`; README
  "Example config".

### #2 — Resources created inside callbacks survive reload/emergency stop
- **Why:** `newRuntimeResources` was copied into `_runtimeResources` after the first execution, so
  anything created later (timers in hotkey callbacks, repeats, watches, HTTP requests, lenses) was
  added to an orphaned, unlocked list that nothing disposed.
- **How:** `ScriptRuntime` now owns a per-config **`ScriptGeneration`** (V8 engine + callback gate +
  resource scope). `ScriptResourceScope` is a thread-safe registry; handles remove themselves when
  disposed (`ScriptResourceHandle.Disposed` event). `ScriptCallbackRelay` ties host callbacks to the
  generation so late callbacks into a dead engine are dropped. Ending a generation closes the gate,
  drains running callbacks (2 s), optionally interrupts V8, then disposes every resource.
- **Files:** `Core/Scripting/ScriptRuntime.cs`, `ScriptResourceScope.cs`, `ScriptCallbackRelay.cs`,
  `ScriptResourceHandle.cs`, `HsScriptHost.cs`, `TimerScriptApi.cs`; tests
  `ScriptApi/ScriptLifecycleScriptRuntimeTests.cs`, `TimerScriptRuntimeTests.cs`.
- **Where to look if it breaks:** handles that appear to "disappear" early, callbacks that stop
  firing, or reload hanging on a running callback.

### #3 — Held hotkey swallowed modifier key-ups
- **Why:** the old release logic returned `Blocking` (default true) for modifier key-ups, eating
  Ctrl/Alt/Shift releases so Windows believed they stayed down. The old test asserted the bug.
- **How:** modifier events **always pass through**; the trigger key-up is swallowed only if the
  matching key-down was swallowed (`_triggerDownSwallowed`). Repeat fail-open no longer leaves the
  key-up swallowed.
- **Files:** `Core/Scripting/HotkeyScriptApi.cs` (nested held-binding state), tests
  `ScriptApi/HotkeyScriptRuntimeTests.cs`.

### #4 — New instance no longer kills the old instance's child processes
- **Why:** `Kill(entireProcessTree: true)` killed apps started via `hs.application.launch` /
  `hs.execute` and the user's config editor (unsaved edits lost).
- **How:** `PreviousInstanceCleaner` first sends `app.quit` over the command pipe and waits up to 8 s
  for a graceful exit (so held keys/repeats are released). Only an instance that does not exit is
  killed, and **never its process tree**. Older builds without `app.quit` fall back to the kill.
- **Files:** `App/PreviousInstanceCleaner.cs`, `Core/Commands/HsWinCommandNames.cs` (+`app.quit`),
  `AppController.HandleCommand`. **Tests:** only the process-name matcher (`PreviousInstanceProcessMatcherTests`)
  and the pipe tests; the graceful-quit/kill sequence itself is untested.

### #5 — Focus-watch teardown deadlock
- **Why:** `_gate` was held while marshalling to the UI dispatcher with `Send`, while the WinEvent
  handler on the UI thread took `_gate` — a lock cycle during reload.
- **How:** `NativeWindowService` no longer holds `_gate` across a marshal, and hook work runs on a
  new **`DedicatedDispatcherThread`** with bounded (`DispatcherGuard`) calls. The old
  `WindowHookThreadScheduler` is deleted.
- **Files:** `App/Windows/NativeWindowService.cs`, `App/Threading/DedicatedDispatcherThread.cs`,
  `DispatcherGuard.cs`; deleted `Windows/WindowHookThreadScheduler.cs` and its test.

### #6 — HTTP requests longer than 100 s never called back
- **How:** the shared `HttpClient` uses `Timeout.InfiniteTimeSpan` (the per-request token owns
  timeouts); disposal races no longer throw `ObjectDisposedException`; multipart streams are
  disposed if opening a later file fails; an unknown response charset falls back to UTF-8.
- **Files:** `Core/Http/SystemHttpService.cs`, `Scripting/HttpScriptApi.cs`; tests new
  `SystemHttpServiceTests`.

### #7 — Audio level always 0 / audio capture leaks
- **How:** `NativeAudioCaptureService` was rewritten; new `AudioLevelCalculator` resolves the real
  sample format for `WaveFormatExtensible`. Start-failure paths dispose the device, stop timer and
  writer; MP3/AAC encoding no longer runs on the UI thread.
- **Files:** `App/Audio/NativeAudioCaptureService.cs`, `AudioLevelCalculator.cs`,
  `Core/Audio/*`. **Tests:** none for `AudioLevelCalculator` or the capture service (they need real
  audio hardware/format fixtures) — only the script-facing audio API is covered. Treat as unverified.

### #8 — Quit now releases held keys
- **How:** `AppController.Dispose` runs through `InputKillSwitch` / shutdown sequence which stops
  repeats **and** calls `IKeyboardInputService.ReleaseHeldKeys()`.
- **Files:** `App/AppController.cs`, `App/Lifecycle/InputKillSwitch.cs`; tests `InputKillSwitchTests`.

### #9 — Command pipe is per-user, bounded and backs off
- **How:** pipe name derives from user + session (`HsWinCommandProtocol.CreatePipeName`) and uses
  `PipeOptions.CurrentUserOnly` on both ends. Server: exponential backoff (100 ms → 30 s) on
  accept failures, 10 s per-request timeout, message-size cap (`HsWinCommandProtocol.MaxMessageLength`, enforced in `HsWinCommandLineIO`). Client: separate
  connect (2 s) and response (15 s) timeouts, so `hspn config reload` cannot hang.
- **Files:** `Core/Commands/*`; tests new `HsWinCommandPipeTests`.
- **Note:** an old HsWin build uses the old fixed pipe name and is not reachable by the new CLI.

### #10 — Key names
- **How:** single table `Core/Keyboard/KeyNameTable.cs` drives parsing and display. Function keys are
  case-insensitive (`"f5"`); added alt/win/windows, numpad (`pad0`…, `numpad0`…), media keys,
  CapsLock/NumLock/ScrollLock/PrintScreen/Pause/Apps and left/right modifiers (see #44).
- **Files:** `Core/Hotkeys/HotkeyParser.cs`, `Core/Keyboard/KeyNameTable.cs`, `KeyboardKeyRules.cs`;
  tests `KeyboardKeyRulesTests`, parser tests.

### #11 — `hspn` PATH install no longer flattens the user PATH
- **How:** `RegistryUserPathStore` reads/writes `HKCU\Environment\Path` raw
  (`DoNotExpandEnvironmentNames`) and keeps `REG_EXPAND_SZ`; the duplicate `WM_SETTINGCHANGE`
  broadcast was removed.
- **Files:** `App/CliInstallService.cs`; tests in `CliInstallCoordinatorTests` and install service tests.

### #12 — Remapped typing injected in order
- **How:** deferred injected input goes through one **`SerializedInputQueue`** (single worker)
  instead of a `ThreadPool.QueueUserWorkItem` per keystroke.
- **Files:** `App/Keyboard/SerializedInputQueue.cs`, `KeyboardHookDispatchScope.cs`; tests
  `KeyboardWatchDispatcherTests`, dispatch-scope tests.

### #13 — Uninstall cleanup
- **How:** `installer/HsWin.iss` `[Code]` section removes the HKCU Run entries (`{#MyAppName}` and
  legacy `HsWin`) and the app directory from the user PATH on uninstall;
  `ChangesEnvironment=yes` broadcasts the change.
- **Files:** `installer/HsWin.iss`; tests `StartupServiceTests` (real HKCU Run key, throwaway names).

---

## Medium

### #14 — Privacy of diagnostics
- **How:** toast logs carry only text *length*; window-message target logs drop titles; hotkey
  presses are not logged; swallowed key names no longer logged; non-blocking watcher "returned true"
  warning logs once per watch (scroll warning removed). `LogSanitizer.DescribeTarget` reduces launch
  targets to a file name / redacted URL.
- **Files:** `ToastPresenter.cs`, `NativeKeyboardEventService.cs`, `KeyboardHookDispatchScope.cs`,
  `KeyboardWatchDispatcher.cs`, `Input/WindowMessage*InputSender.cs`, `Core/Logging/LogSanitizer.cs`,
  `ApplicationScriptApi.cs`, `ProcessShellService.cs`; tests `LogSanitizerTests`; `PRIVACY.md`.
- **If you are missing log lines when debugging:** this is intentional; temporarily add logging
  rather than reverting.

### #15 — Config can no longer hang reload or lint
- **How:** `ScriptRuntimeServices.ConfigExecutionTimeout` (10 s app, 5 s lint). A watchdog interrupts
  V8; a race-free state machine reports `TimeoutException` ("did not finish within …s") and tears the
  generation down. Lint reports all diagnostics, not only the first runtime error.
- **Files:** `ScriptRuntime.cs`, `ScriptRuntimeServices.cs`, `Config/ConfigLinter.cs`,
  `ConfigLintRuntimeServices.cs`; tests `ConfigLinterTests`, lifecycle tests.

### #16 / #17 — Tasks and `hs.execute`
- **How:** cancelling `hs.task.run` handles **kills the process tree** (`IShellService.Execute` takes a
  `CancellationToken`; result has `Cancelled`). `hs.execute` default timeout is 10 s
  (`ShellExecutionOptions.DefaultSynchronousTimeoutMs`); task default stays 30 s. `WaitForExit` uses a
  `TaskCompletionSource` (an earlier draft crashed the test host by signalling a disposed wait handle).
- **Files:** `App/Shell/ProcessShellService.cs`, `Core/Shell/*`, `TaskScriptApi.cs`,
  `ShellScriptApi.cs`, `ShellScriptOptionsParser.cs`; tests `ProcessShellServiceTests`.

### #18 — Bounded marshalling for clipboard / magnifier / toast
- **How:** `WpfClipboardTextStore` runs on its own STA thread (`DedicatedDispatcherThread`) with
  retry via `ClipboardRetry` (no `Thread.Sleep` on the UI thread); `NativeMagnifierService.Create`
  no longer holds `_gate` while marshalling; `ToastPresenter.Dispose` is fire-and-forget.
- **Files:** `App/Clipboard/*`, `Magnifier/NativeMagnifierService.cs`, `ToastPresenter.cs`; tests
  `ClipboardRetryTests`, `DispatcherGuardTests`.

### #19 — Hotkey IDs
- **How:** round-robin ID allocation that skips IDs still natively registered; `Dispose` snapshots
  callbacks under the lock; no per-press logging.
- **Files:** `App/Hotkeys/NativeHotkeyService.cs`; tests `NativeHotkeyServiceTests`.

### #20 — Remap hold semantics; repeat `modifiers`
- **How:** `hs.keyboard.remap` now holds the target key down while the source is held (including
  auto-repeat) and releases on source key-up or when the remap is disposed; remap watchers use
  `Prepend: true`. `ParseRepeatOptions` honors `modifiers`/`withModifiers`/`holdModifiers`.
- **Files:** `Core/Scripting/KeyboardScriptApi.cs` (`KeyRemap`), `KeyboardScriptOptionsParser.cs`.

### #21 — Extended-key scan codes
- **How:** `Native/KeyboardScanCodes` derives scan code + E0 prefix from
  `MapVirtualKeyEx(MAPVK_VK_TO_VSC_EX)` (Pause falls back to VK); exact-size input buffers replace
  fixed `stackalloc`s.
- **Files:** `App/Native/KeyboardScanCodes.cs`, `Input/KeyboardInputSender.cs`; tests `NativeInputTests`.

### #22 — `PostMessage` targets
- **How:** `ForegroundInputTarget` resolves the **focused child** via `GUITHREADINFO`; mouse messages
  use cursor position in client coordinates with proper `MK_*` flags.
- **Files:** `App/Input/ForegroundInputTarget.cs`, `WindowMessage*InputSender.cs`.

### #26 — Config editor selection
- **How:** order is `HSWIN_EDITOR` → `VISUAL` → `EDITOR`, then the program registered for `.js`
  (via `AssocQueryString`, `FileAssociations`/`DefaultEditorSelector`), **skipping script hosts**
  (Windows Script Host etc., which would *run* the config), then Notepad.
- **Files:** `App/EditorLauncher.cs`, `DefaultEditorSelector.cs`, `FileAssociations.cs`; tests
  `DefaultEditorSelectorTests`.

### #27 — Reload error toasts show line info
- **How:** `ScriptErrorFormatter` in Core is shared by lint and the reload toast;
  `UserFacingExceptionFormatter` was deleted.
- **Files:** `Core/Scripting/ScriptErrorFormatter.cs`, `ConfigReloadAlerts.cs`; tests
  `ScriptErrorFormatterTests`, `ConfigReloadAlertsTests` (renamed from `AppControllerTests`).

### #28 — Audio device control on NAudio
- **How:** `NativeAudioDeviceController` now uses `NAudio.CoreAudioApi`; a missing default device
  gives a clear `InvalidOperationException`, and listing still works.
- **Files:** `App/Audio/NativeAudioDeviceController.cs`; tests `NativeAudioDeviceControllerTests`.

### #29 — Media control
- **How:** cached session manager, 2 s timeouts, status polled for up to 400 ms after a command;
  action inference corrected so `statusAfter` is no longer stale.
- **Files:** `App/Media/NativeMediaController.cs`; tests `NativeMediaControllerTests`.

### #30 — Validation gaps
- **How:** invalid `files` values, NaN volume, bad booleans/ints now produce clear
  `ArgumentException`s (no raw `FormatException`/`OverflowException`); user `accept` headers in any
  casing are preserved.
- **Files:** `ScriptArgumentReader.cs`, `HttpScriptApi.cs`, `AudioScriptApi.cs`, `bootstrap.js`.

### #31 — Magnifier teardown
- **How:** `DestroyNative` and GPU `Dispose` always finish teardown even if a step throws; `_sessions`
  guarded by the lock; `BindHeld` ownership documented (binding ctors dispose the lens on failure).
- **Files:** `App/Magnifier/NativeMagnifierSession.cs`, `GpuMagnifierRenderer.cs`,
  `NativeMagnifierService.cs`.

### #32 — Toast DPI (partial)
- **How:** toast positioning and the cursor follower use the *target monitor's* DPI; popup offsets are
  kept in sync on update. **Mixed-DPI behavior was not verified on real hardware.**
- **Files:** `ToastPresenter.cs`, `CursorFollowingToastController.cs`, `FollowingToastWindow.cs`.

### #33 — Scroll watch semantics
- **How:** scroll callbacks are plain `Action`s (notification only); blocking scroll watches swallow
  all events, documented in the README. Dead return-value path removed.
- **Files:** `Mouse/MouseScrollWatchDispatcher.cs`, `NativeMouseEventService.cs`, `bootstrap.js`.

---

## Performance

### #36 — Precise timers
- **How:** `PreciseTimerScheduler` (+ `HighResolutionWaitableTimer`) drives `hs.timer`, keyboard/mouse
  repeats and the cursor follower; the magnifier shares the same waitable timer. Replaces
  `DispatcherTimer`/`System.Threading.Timer` (15.6 ms granularity).
- **Files:** `App/Threading/*`, `App/Timers/PreciseScriptTimerService.cs` (replaces the deleted
  `DispatcherScriptTimerService`), `Input/*InputService.cs`; deleted `Input/MouseRepeatTimer.cs`;
  tests `PreciseTimerSchedulerTests`, `PreciseScriptTimerServiceTests`, `ManualPreciseTimerFactory`.

### #37 — Scritto glyph blur
- **How:** each glyph's `BlurEffect` is attached only while animating (`EnableBlur`/`DisableBlur`).
- **Files:** `App/ScrittoTextPanel.cs`; tests `ScrittoTextPanelTests`.

### #38 / #39 — Logging volume, queues, retention
- **How:** clipboard-change and launch-completed logs removed; scroll callbacks capped at 256 queued
  per watch (one warning, rest dropped). `FileLogger` and `ReloadScriptConsoleLogger` use bounded queues
  (20,000) and write one "Dropped N log entries" line; no blank line for exception-less entries; a
  failed writer is disposed and retried. File names are `yyyy-MM-dd-HH-mm.log` (sortable).
  `LogRetention.Prune` (14 days, always keep newest 20) runs from `LogRetentionTask` off the startup
  path.
- **Files:** `App/FileLogger.cs`, `LogRetentionTask.cs`, `Core/Logging/*`; tests `FileLoggerTests`,
  `LogRetentionTests`, `DatedLogFileNameTests`, `ReloadScriptConsoleLoggerTests`.
- **Note:** old `MM-dd-yyyy` logs are still pruned by age.

---

## Duplication / structure

| # | What | New location |
| --- | --- | --- |
| 40 | One hook-thread implementation | `App/Native/LowLevelHookThread.cs` (used by keyboard + mouse hooks) |
| 41 | One SendInput interop | `App/Native/NativeInput.cs` (+`NativeInputSender`) used by keyboard, mouse, media |
| 42 | Shared Win32 structs/helpers | `App/Native/NativeStructs.cs`, `ScreenGeometry.cs`, `NativeWindowInfo.cs` |
| 43 | Keyboard/mouse twins merged | `Core/Input/` (`InputMethod`, `IRepeatSession`, `RepeatTiming`, `RepeatScriptHandle`), `App/Input/ActiveRepeatSlot.cs`; the `Keyboard*`/`Mouse*` duplicates were deleted |
| 44 | One key-name table | `Core/Keyboard/KeyNameTable.cs` |
| 45 | Script API helper copies | `ScriptArgumentReader`, `ScriptEventArguments` |
| 46 | Lint analyzers share a lexer | `Core/Config/ConfigSourceScanner.cs` |
| 47 | Null services consistent | `Core/Support/EmptyDisposable.cs`, `Null*Service` |
| 48 | One callback scheduler | `App/Threading/SynchronizationContextCallbackScheduler.cs` |

If a behavior differs between keyboard and mouse repeats after this pass, suspect #43.

### #49 — `AppController` split
`AppController` is now the composition root. Reload, emergency stop, CLI install and the input kill
switch live in `App/Lifecycle/` behind small interfaces (`IUiThread`, `IScriptRuntimeLifecycle`,
`ICliInstaller`, `ICountingAlertPresenter`): `ScriptReloadCoordinator`, `EmergencyStopController`,
`CliInstallCoordinator`, `InputKillSwitch`. A reload generation counter lets a newer reload,
emergency stop, or shutdown supersede an older reload; shutdown is marked early (`BeginShutdown`) so
no outcome toasts appear while quitting. `Thread.Sleep` for minimum toast time became `Task.Delay`.
Tests: `tests/HsWin.App.Tests/Lifecycle/*`.

### #50 — Test layout
`ScriptRuntimeTests.cs` (3101 lines) was split into `tests/HsWin.Core.Tests/ScriptApi/*ScriptRuntimeTests.cs`
over shared doubles in `tests/HsWin.Core.Tests/TestDoubles/`. `ScriptRuntime` keeps only the
parameterless and `ScriptRuntimeServices` constructors. `tests/Shared/CapturingRuntimeLogger.cs` is
linked into both test projects.

### #51 / #52 / #54 / #55–#57
- #51: `MagnifierScriptOptionsParser` + named limits; `KeyboardRepeatRules` owns repeat/pulse policy;
  composition root owns the shared mouse hook; `NoRepeat` is a Win32-only constant in
  `NativeHotkeyService`; `BootstrapModifierFlagTests` pins `bootstrap.js` flags to `HotkeyModifiers`.
- #52: `MagnifierScriptHandle` derives `ScriptResourceHandle` (`stop`/`dispose`/`delete`); keyboard
  watch `prepend` option; `AudioRecordingScriptHandle.stop()` finishes the file while `dispose()`
  ends immediately.
- #54: `TrayMenuActions` parameter object; `TrayIconService.ContextMenu` exposed for tests.
- #55–#57: dead Scritto/toast members removed, duplicate reload-alert branch merged, stale CLI lint
  help refreshed, `IToastView.BeginEnterAnimation` default no-op.
- #53: magnifier window styles/HRESULTs/SetWindowPos flags are named constants in `MagnifierNative`.

---

## Build, packaging, docs

- **#58** `scripts/Build-Installer.ps1` checks `$LASTEXITCODE` after each `dotnet publish`.
- **#59** The app and CLI are published separately and merged into
  `artifacts/publish/installer/<config>/<rid>`. Any file that differs between the two fails the build,
  **except** `WindowsBase.dll`, `System.Drawing.dll` and `Microsoft.VisualBasic.dll`: the CLI ships thin
  facades and the app ships the full Windows Desktop versions, so the **app's copy wins**. (Before this
  pass the CLI publish silently overwrote the app's files in the shared output folder.) `HsWin.iss`
  `MyPublishDir` now points at the staging folder.
- **#60** Local builds use version `0.0.0-dev.<timestamp>` with file version `0.0.0.0` (always older than
  a release); CI releases use `0.0.0.<run>`. `VersionInfoProductVersion` uses the numeric version
  (Inno rejects `0.0.0-dev…`).
- **#61** README updated (timeouts, task stop, audio stop/dispose, key names, `prepend`, remap hold,
  repeat `modifiers`, magnifier handles, config timeout, default config, editor order, log retention).
  The finding's `hs.log` claim was wrong: it is `console.*`, already documented.
- **#62** `PRIVACY.md` describes what diagnostics never contain and the log retention.
- **#63** Installer publisher is `nocdn`, not `Hammerspoon`.

## New tests added in this pass
`SystemHttpServiceTests`, `HsWinCommandPipeTests`, `ProcessApplicationProviderTests`,
`StartupServiceTests`, `LogRetentionTests`, `NullServiceTests`, `ScriptErrorFormatterTests`,
`ConfigSourceScannerTests`, `NativeInputTests`, `KeyboardRepeatRulesTests`, `BootstrapModifierFlagTests`,
`MagnifierScriptOptionsParserTests`, `DefaultEditorSelectorTests`, `ClipboardRetryTests`,
`DispatcherGuardTests`, `PreciseTimerSchedulerTests`, `PreciseScriptTimerServiceTests`,
`NativeAudioDeviceControllerTests`, `tests/HsWin.App.Tests/Lifecycle/*`,
`tests/HsWin.Core.Tests/ScriptApi/*`, plus additions to the existing hotkey, logging, toast, tray,
linter and parser tests. (Some of the magnifier-named tests belong to the pre-existing magnifier work.)

## Known test gaps (no automated coverage)
`AudioLevelCalculator` / `NativeAudioCaptureService` (#7), the graceful-quit path of
`PreviousInstanceCleaner` (#4), `NativeWindowService` focus watching (#5), real `SendInput` /
`PostMessage` delivery (#12, #21, #22), mixed-DPI toast placement (#32), the installer's `[Code]`
uninstall cleanup (#13), and `Build-Installer.ps1`. These need manual verification on real hardware.

## Deliberate behavior changes (not regressions)
- Default `config.js` is tiny (#1). Existing configs untouched.
- `hs.execute` default timeout 10 s (was 30 s); `hs.task.run` stop kills the process tree.
- `hs.keyboard.remap` holds the target while the source is held (was a tap on key-down).
- Held hotkeys never swallow modifier key-ups.
- Config top-level code must finish in 10 s (5 s lint).
- Diagnostics no longer contain toast text, window titles, key names, clipboard contents.
- Log file names changed to `yyyy-MM-dd-HH-mm`; old logs pruned after 14 days (newest 20 kept).
- `hs.keyboard.*` accepts more key names; function keys are case-insensitive.
- Starting a new instance asks the old one to quit rather than killing its process tree.
- Scroll watch callbacks beyond 256 queued per watch are dropped with one warning.

## Suggested prompt for a debugging agent
"Something regressed in HsWin. Read `plans/inspect-code-fixes.md` (symptom → suspect table and the
per-finding sections) and `plans/inspect-code-findings.md` (original problems). Reproduce, check
`%APPDATA%\HsWin\runtime-logs`, and compare the suspect files with `git log -p` for the commit that
introduced this pass before changing anything."
