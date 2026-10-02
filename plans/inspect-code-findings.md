# inspect-code findings — full repo

Discovery-only audit (no code changed). Started 2026-10-02 against `main` @ `142da87` plus the
uncommitted magnifier/threading work in the working tree. Findings are ordered by severity, then
by how much each one matters. Line numbers refer to the working tree on the audit date.

Legend: **[C]** confirmed by reading the code path end to end · **[P]** plausible; depends on
runtime or platform behavior that should be checked by reproducing it.
"Previously deferred" means `plans/README.md` already lists the issue under deferred findings.

---

## Critical / High: user-visible bugs, data loss, stuck input

1. **[C] The default config hijacks keys system-wide for every new user.**
   `src/HsWin.Core/Config/ConfigFileService.cs:~90-135`. The shipped `DefaultConfig` is a personal
   Apex Legends setup:
   - It installs a global *blocking* `hs.keyboard.watch` that swallows Alt+Shift+key.
   - It references `r5apex_dx12.exe`.
   - It uses `RegisterHotKey` on bare `` ` `` (`:119`), `delete` (`:126`) and `pageup` (`:132`).

   A fresh install therefore stops Backtick, Delete and PageUp from working in every app. The
   default should be a small, harmless example (an alert, a reload hotkey, commented samples).

2. **[C] Resources created inside callbacks survive reload and emergency stop.**
   `src/HsWin.Core/Scripting/ScriptRuntime.cs:143-152`.
   - **Root cause:** `HsScriptHost` receives `newRuntimeResources.Add` as `trackResource`. After
     the first execution that list is copied into `_runtimeResources`, and the local list is
     orphaned. Anything created later (`hs.timer.doEvery` in a hotkey callback, repeats, watches,
     HTTP requests, audio watches, magnifier lenses) is added to the orphaned list. Nothing
     disposes it.
   - **Effects:**
     - `hs.timer.doEvery` started from a hotkey keeps ticking after reload into a disposed V8
       engine (`TimerScriptApi.cs:39-46` has no generation or disposal guard). That means errors
       on every tick, or work that keeps running.
     - Emergency stop does not stop these timers (`AppController.KillInjectedInputImmediate` only
       stops repeats).
     - `List.Add` is called from the UI, hook and ThreadPool threads with no lock.
     - One-shot timers and handles are never removed, so the list grows for the whole session.
   - **Test gap:** `ReloadDisposesKeyboardWatchesAndTimers` only covers resources created at top
     level.
   - **Fix:** track into a thread-safe per-generation registry that removes completed or disposed
     handles.

3. **[C] A held hotkey swallows modifier key-ups, so modifiers stick in the OS.**
   `src/HsWin.Core/Scripting/HotkeyScriptApi.cs:160-166,220-224`.
   - **What happens:** `ShouldRelease` fires on a modifier change and then returns
     `_options.Blocking`, which defaults to true. The Alt/Ctrl/Shift *key-up* is eaten, so Windows
     still thinks it is down.
   - **Inconsistency:** `MagnifierHeldBinding` deliberately passes modifier releases through.
   - **The test codifies the bug:** `tests/HsWin.Core.Tests/ScriptRuntimeTests.cs:245-288`
     asserts `Assert.True(releasedModifier)`.
   - **Related:** on repeat fail-open, the key-down passes through while `_active` stays true, so
     the matching key-up is swallowed.
   - **Fix:** never swallow a modifier key-up, and only swallow the trigger key-up when the
     trigger key-down was swallowed.

4. **[C] Launching a new instance kills child apps of the old instance (data loss).**
   `src/HsWin.App/PreviousInstanceCleaner.cs:113`. `process.Kill(entireProcessTree: true)` also
   kills:
   - apps started with `hs.application.launch` or `hs.execute`;
   - the config editor (Notepad or VS Code) opened from the tray, losing unsaved edits.

   It also hard-kills an instance that cannot release its held keys or repeats. "HsWin.App" is on
   the known-name list, so dev builds are killed too. **Fix:**
   - ask the old instance to quit over the existing command pipe;
   - fall back to `Kill(false)` on the process only, never the tree.

5. **[C] Focus-watch teardown can deadlock with a focus change (cross-thread lock + Send).**
   `src/HsWin.App/Windows/NativeWindowService.cs:125-130,142-155,239-242,406-418`.
   - **The cycle:**
     - `RemoveWatch`, `WatchFocused` and `Dispose` hold `_gate`, then call
       `_hookThread.Run(...)`. That is `SynchronizationContext.Send` to the **UI dispatcher**
       (`WindowHookThreadScheduler.cs:22`, `AppController.cs:98`).
     - `HandleWinEvent` runs on the UI thread and takes `lock (_gate)`.
   - **Trigger:** reload runs on the ThreadPool under `_scriptReloadGate`. If a foreground change
     arrives while a focus watch is being disposed, both threads wait on each other forever.
     Reload, emergency stop and Quit all hang.
   - **Policy violation:** this also breaks the bounded-marshal rule in `DispatcherGuard`.
   - **Fix:** do not hold `_gate` across `Send`, and use the bounded `DispatcherGuard` marshal.

6. **[C] HTTP requests longer than 100 s never call back.**
   `src/HsWin.Core/Http/SystemHttpService.cs:9,59,86-92`.
   - The static `HttpClient` keeps the default 100 s `Timeout`.
   - With `timeoutMs > 100000`, `HttpClient` throws `TaskCanceledException` first. The first
     `catch` filter (`timeout.IsCancellationRequested`) is false, and the generic
     `OperationCanceledException` catch treats it as a disposal. The script callback never runs.
   - **Fix:** `Timeout = Timeout.InfiniteTimeSpan` (the per-request CTS already owns timeouts).
   - **Related:**
     - Disposing while the request is starting can make `CTS.Token` throw
       `ObjectDisposedException`, producing a spurious error log.
     - If opening a multipart file fails, the streams already opened leak.
     - An unknown response charset turns the whole response into an error instead of falling back
       to UTF-8.

7. **[C] Audio level watching likely always reports 0.**
   `src/HsWin.App/Audio/NativeAudioCaptureService.cs:128,223,491-500`.
   - `new WasapiCapture(Device)` uses the device mix format. On practically all modern drivers
     that is `WaveFormatExtensible` (`Encoding == Extensible`).
   - `AudioLevelCalculator` only matches `IeeeFloat` or `Pcm`, so it falls to
     `AudioLevelStats.Empty`.
   - **Fix:** resolve `SubFormat` for extensible formats.
   - **Test gap:** no unit tests cover `AudioLevelCalculator`.
   - **Related:**
     - On start failure, the `MMDevice`, the armed stop timer and the `WaveFileWriter` (or an
       empty file) leak.
     - `RecordingStopped` (MP3/AAC encode) can run on the UI thread through the captured
       SynchronizationContext.

8. **[C] Quit does not release keys held by scripts.**
   `src/HsWin.App/AppController.cs:589-630`. `Dispose` calls `StopActiveRepeat` but not
   `_keyboardInputService.ReleaseHeldKeys()` (reload does call it, at `:481`). Quitting while a
   script holds a key (e.g. `keyDown` without `keyUp`) leaves it logically stuck in the OS.

9. **[C] The command pipe is not per-user and can hot-loop.** `src/HsWin.Core/Commands/`.
   - **Name and ACL:** fixed global pipe name `HsWinCommandProtocol.cs:7`. There is no
     `PipeOptions.CurrentUserOnly` or ACL (`HsWinCommandServer.cs:77`), and `maxInstances: 1`.
     - On a multi-user or RDP machine another session can own the name, or send `config.reload`
       to someone else's instance.
     - Same-session instances fight over it.
   - **Server:**
     - The accept loop has no backoff, so a persistent failure (pipe owned elsewhere) spins and
       floods logs.
     - There is no read timeout or line-length limit (`:114`).
   - **Client:** `ReadLine()` has no timeout (`HsWinCommandClient.cs:57`), so
     `hspn config reload` can hang forever.

10. **[C] Lowercase function keys are rejected.**
    `src/HsWin.Core/Hotkeys/HotkeyParser.cs:194`. `key.StartsWith('F')` is case-sensitive, so
    `"f5"` fails. That is the Hammerspoon spelling, and the display name this app itself emits.
    Related gaps:
    - `alt`, `win` and `windows` don't parse as *key names*.
    - Numpad, media, CapsLock and PrintScreen keys are missing.
    - Test gap: there is no `"f5"` test.

11. **[P] `hspn` CLI install can flatten the user PATH.**
    `src/HsWin.App/CliInstallService.cs:28,41,48`.
    `Environment.GetEnvironmentVariable("Path", User)` returns the **expanded** value, and
    `SetEnvironmentVariable` writes `REG_SZ`. Entries like `%USERPROFILE%\bin` are permanently
    expanded, and the value type changes from `REG_EXPAND_SZ`.
    - **Fix:** read and write `HKCU\Environment\Path` raw with
      `RegistryValueOptions.DoNotExpandEnvironmentNames`.
    - The manual `WM_SETTINGCHANGE` broadcast duplicates what .NET already sends.

12. **[C] Fast remapped typing can be injected out of order.**
    `src/HsWin.App/Keyboard/KeyboardHookDispatchScope.cs:90`. Each keystroke's deferred actions
    are a separate `ThreadPool.QueueUserWorkItem`. Two quick remapped keys can run on two pool
    threads in either order. **Fix:** use a single serialized injection queue (one worker or a
    `Channel`).

13. **[C] Uninstall leaves the startup entry and the PATH entry behind.**
    `installer/HsWin.iss` has no `[Registry]`/`[UninstallRun]`/`[UninstallDelete]` handling. These
    are written at runtime and never cleaned up:
    - the HKCU Run entry from `StartupService`, which then points at a deleted exe on every logon;
    - the user PATH entry from `CliInstallService`.

## Medium: correctness, robustness, security and privacy

14. **[C] Swallowed keystrokes are logged with key names, which works like a keylogger in
    plain-text logs.**
    - `NativeKeyboardEventService.cs:413-426` logs every *swallowed* key (`key='a'`).
    - `KeyboardHookDispatchScope.cs:66,89` logs 2+ Info lines per remapped keystroke, which
      contradicts the "no per-keystroke logging" comment.
    - `KeyboardWatchDispatcher.cs:109` and `MouseScrollWatchDispatcher.cs:95-98` log a Warning on
      *every event* for non-blocking watchers that return true. That is unbounded spam.
    - Toast text is Info-logged 2–3× per toast (`ToastPresenter.cs:111,116,160,203`).
    - Window titles are logged.

    Plan 002 redacted shell and URL data, but these channels remain. Logs also have no retention
    (previously deferred).

15. **[C] Lint can hang forever.** `ConfigLinter`/`ConfigLintRuntimeServices` run user JS with no
    timeout or interrupt. A top-level `while (true)` hangs `hspn config lint`. Lint also reports
    only the first runtime error.

16. **[C] `hs.task` stop/dispose does not kill the process** (`TaskScriptApi`). It only suppresses
    the callback, leaving orphaned processes running after reload or emergency stop.

17. **[C] `hs.execute` blocks the calling thread for up to 30 s** (`ProcessShellService`). Called
    from a hotkey or timer callback, it blocks the UI thread or holds the script gate. Hook paths
    then fail open and toasts freeze.

18. **[C] WinEvent and clipboard marshals violate the DispatcherGuard policy.**
    - `WpfClipboardTextStore.cs:57` uses unbounded `_dispatcher.Invoke`, and `Thread.Sleep` retries
      run *on the UI thread* (`:70,74`).
    - `NativeMagnifierService` uses `Invoke` with a 5 s timeout while holding `_gate`.
    - `ToastPresenter.Dispose` uses an unbounded `Invoke`.

19. **[C] Hotkey IDs are never reused.** `NativeHotkeyService.cs:14,27,186-191`. IDs grow
    monotonically up to `0xBFFF`, so after enough reloads (about 49k registrations in total)
    every `hs.hotkey.bind` fails until restart. Also:
    - `_callbacks.Keys` is enumerated without the lock in `Dispose`.
    - Two Info logs are written per hotkey press.

20. **[C] The remap tap fires on key-down, so a remapped key cannot be held**
    (`KeyboardScriptApi` remap). Also, `ParseRepeatOptions` silently ignores `modifiers` for
    repeats.

21. **[C] SendInput can produce the wrong scancode for extended keys.** `KeyboardInputSender` takes
    the extended flag from a hard-coded `IsExtendedVirtualKey` list and drops the `E0` prefix that
    `MapVirtualKeyEx(MAPVK_VK_TO_VSC_EX)` returns. Media keys, numpad `/`, Apps and others are
    affected. `stackalloc` buffers of 24 and 9 can overflow with many modifiers (edge case).

22. **[C] `PostMessage` input senders use poor targets.** They post to the foreground *top-level*
    window rather than the focused child. Mouse messages use `lParam = 0`, so clicks land at client
    (0,0).

23. **[C] `moveToScreen` can report success on the wrong monitor.** In `bootstrap.js`,
    `moveToScreenNative` sends Win+Shift+Arrow, which only moves one monitor at a time.

24. **[C] Emergency stop relies on `Thread.Sleep(25)`** (`ScriptRuntime.cs:200`). `_engine` is
    read and written across threads without synchronization. Reload can dispose an engine while a
    hook or UI callback is still inside it (the invoker gate is per runtime and not shared with
    reload).

25. **[C] `_disposed` flags are read across threads without `volatile`/`Volatile`.** This happens
    in `AppController`, `ToastPresenter`, `NativeClipboardChangeNotifier`, `NativeWindowService`
    and `NativeHotkeyService`.

26. **[C] The editor fallback to VS Code is effectively dead.**
    `EditorLauncher.cs:24,56`. `"code"` with `UseShellExecute = false` cannot resolve `code.cmd`,
    so the launcher always falls through to Notepad.

27. **[C] Reload error toasts lack line info.** `UserFacingExceptionFormatter` uses `Message`, not
    ClearScript `ErrorDetails`. Lint shows line and column; the reload toast does not.
    `UserFacingExceptionFormatter` also duplicates `ConfigLinter.Unwrap`.

28. **[C] Audio device control misbehaves without a default device.**
    `NativeAudioDeviceController` (~500 lines of hand-rolled Core Audio COM) throws on enumeration
    when there is no default device. It also duplicates what `NAudio.CoreAudioApi` already gives
    you, and creates new COM objects per call (previously deferred).

29. **[C] Media control does sync-over-async WinRT calls**
    (`NativeMediaController`). The session manager is not cached, and `statusAfter` is read
    immediately after the toggle, so it is usually stale.

30. **[C] HTTP and value validation gaps.**
    - `ReadMultipartParts` silently ignores invalid `files` values.
    - `normalizeHttpOptions` in `bootstrap.js` overrides a user's lowercase `accept` header.
    - `AudioScriptApi` lets `NaN` through the volume range check.
    - `ScriptArgumentReader.RequireBoolean` throws a raw `FormatException`.
    - `RequireInt32` banker's-rounds doubles and lets `OverflowException` escape.

31. **[C] Magnifier lifecycle edge cases.**
    - `MagnifierScriptApi.BindHeld` creates the lens before the binding, and leaks it if the
      binding constructor throws.
    - `NativeMagnifierSession.DestroyNative`: if `_gpu.Dispose()` throws, the HWND is never
      destroyed and `_remove` never runs.
    - `GpuMagnifierRenderer.Dispose`: `SetRoot`/`Commit` can throw on device loss and skip
      resource disposal.
    - `NativeMagnifierService._sessions` is mutated on the magnifier thread and read in `finally`
      without the lock.

32. **[P] Toast positioning on mixed-DPI setups.**
    - `ToastPresenter.Position` and `CursorFollowingToastController.CaptureFallbackSnapshot` take
      `TransformFromDevice` from the window's *current* monitor (the toast is parked at −1,000,000)
      instead of the target monitor's DPI.
    - The native follower uses `GetDpiForWindow` of the toast window, not the cursor's monitor.
    - The native follower moves the popup HWND with `SetWindowPos` from a ThreadPool timer, which
      desyncs `Popup.HorizontalOffset`. A relayout can snap it back.

33. **[C] Scroll-watch and keyboard-watch "blocking" semantics differ.** Scroll blocking swallows
    every event unconditionally. Keyboard blocking is decided per event by the callback's return
    value. The bootstrap scroll watch always returns false, which makes the C# warning path dead.

34. **[C] Alert defaults and parsing are surprising.**
    - `AlertRequest.DefaultKind = Success` shows a green dot for a plain `hs.alert.show("x")`.
    - Boolean parsing differs between `animateRoll`, `bounce` and the other fields.
    - The operation-toast `hide()` in `bootstrap.js` shows a "Hidden" toast with `durationMs: 0`
      (hack), and operation-toast timers are tracked forever.

35. **[C] Tests codify or miss known bugs.**
    - The held-hotkey modifier test asserts the bug (#3).
    - No tests for:
      - callback-created resources across reload (#2);
      - `"f5"` (#10);
      - HTTP timeouts above 100 s (#6);
      - lint infinite loops (#15);
      - `AudioLevelCalculator` (#7).
    - No test files at all for: `SystemHttpService`, `HsWinCommandServer`/`HsWinCommandClient`
      (only the protocol), `NativeWindowService`, `NativeAudioCaptureService`,
      `NativeAudioDeviceController`, `WpfClipboardTextStore`, `EditorLauncher`, `StartupService`,
      `PreviousInstanceCleaner` (only the matcher), `ProcessApplicationProvider`.
    - `ToastStyleMetricsTests`/`FollowingToastStyleMetricsTests` assert constants equal themselves
      (tautological).

## Performance

36. **Timer resolution versus the advertised intervals.**
    - `hs.timer` accepts 1 ms, but `DispatcherTimer` gives about 15.6 ms (previously deferred).
    - Keyboard repeat defaults to 10 ms on `System.Threading.Timer` (about 15.6 ms granularity).
    - The cursor-following toast asks for 240 Hz from a ThreadPool timer.
    - `HighResolutionFrameTimer` (Magnifier) already exists and could back all three.

37. **Every Scritto glyph permanently carries its own `BlurEffect`**
    (`ScrittoTextPanel.cs:524,535`), even at radius 0. Each glyph then renders through a separate
    effect pass. Attach the effect only while a glyph animates. The 1024-entry glyph pool is
    retained forever.

38. **Too much per-event and per-operation logging and work.**
    - Clipboard operations log three times.
    - Hotkey presses log twice.
    - Toast shows log three times.
    - API calls log, sometimes double-logged with provider logging (`ProcessApplicationProvider`).
    - Many JSON round-trips remain in `bootstrap.js`.
    - `ScriptArgumentReader.GetPropertyValue` builds a `HashSet` and then does a linear scan
      (previously deferred).
    - Scroll callbacks queue without backpressure (previously deferred).

39. **Loggers have unbounded queues.**
    - `FileLogger.cs:238` writes `entry.Exception` even when it is null, so every entry gets a
      blank line.
    - `ReloadScriptConsoleLogger.ApplyEntry` leaks the new writer if a write fails.
    - `DatedLogFileName` uses `MM-dd-yyyy`, which does not sort.
    - Logs have no retention.

## Duplication / DRY

40. **Low-level hook threads are implemented twice.** `NativeKeyboardEventService` and
    `NativeMouseHotkeyHook` duplicate the hook-thread install, uninstall and message pump almost
    verbatim. Extract a `LowLevelHookThread`.

41. **SendInput interop is copied three times.** The `INPUT` struct and P/Invoke appear in
    `KeyboardInputSender`, `MouseInputSender` and `NativeMediaController`. The injected-marker
    constant is duplicated, and the media key carries no marker. The scan-code table is duplicated
    in `WindowMessageKeyboardInputSender`.

42. **Win32 structs and helpers are redefined per file.**
    - `NativeRect`/`NativePoint`/`MonitorInfo`/`NativeMessage` are redefined in at least five
      files (`MagnifierNative`, `CursorFollowingToastController`, the hooks, `NativeWindowService`).
    - `GetProcessName`/`GetWindowTitle`/`LogTargetIfChanged` are copied three times.
    - The `Sw*` constants are duplicated with `WindowMoveStrategySelector`.
    - P/Invoke style is mixed: `LibraryImport` in the toast files, `DllImport` elsewhere.

43. **Keyboard and mouse twins.** These pairs are identical:
    - `MouseInputMethod`/`KeyboardInputMethod`, and their parsers;
    - `IMouseRepeatSession`/`IKeyboardRepeatSession`;
    - `KeyboardRepeatScriptHandle`/`MouseRepeatScriptHandle`;
    - the single-active-repeat orchestration in `KeyboardInputService`/`MouseInputService`.

    Keyboard uses `System.Threading.Timer` directly while mouse has an injectable factory, so their
    testability is inconsistent.

44. **There are three key-name tables.** `HotkeyParser.NamedKeys`/`SingleCharacterKeys`,
    `KeyboardKeyRules.KeyNames` and `HotkeyDefinition.FormatVirtualKey`. The mouse-button aliases
    are duplicated across `MouseButtonParser` and `HotkeyParser` (`MouseButton` vs
    `HotkeyMouseButton`).

45. **Script API helpers are copied.**
    - `ConvertOptionalBoolean` appears four times, and `ConvertPositiveInt` three times.
    - `CallbackSuppressingDisposable` is copied verbatim (`Http`, `AudioCapture`).
    - The disposed-flag + schedule pattern appears four times.
    - The nine-field snapshot argument list appears four times (`HotkeyScriptApi`,
      `KeyboardScriptApi`).
    - `EnumerateModifierValues` is duplicated with `ScriptArgumentReader`.

46. **Lint analyzers share no code.** `ConfigTimerLiteralAnalyzer` and
    `ConfigKeyboardRepeatLiteralAnalyzer` each hand-roll the same JS lexer. The analyzer's modifier
    list duplicates `KeyboardKeyRules`, and its advice is wrong for `alt`/`win`.
    `IsDuplicateStaticDiagnostic` matches exception message strings.

47. **Null services are inconsistent.**
    - There are five or more private no-op disposables.
    - `NullWindowService.DisposableAction` collides by name with `Support/DisposableAction`.
    - Some null services throw `NotSupportedException` (Clipboard, Shell, Media, Http, Audio);
      others no-op silently.

48. **There are three scheduler abstractions.** `KeyboardWatchDispatcher` and
    `MouseScrollWatchDispatcher` each define a SynchronizationContext scheduler class, and Core has
    `IScriptCallbackScheduler`.

## Design / structure (SOLID, oversized files, module boundaries)

49. **`AppController.cs` (769 lines) is a god class.** It is the composition root and also owns
    emergency stop, reload orchestration, CLI install and command handling. It mixes
    `WpfApplication.Current.Dispatcher` with `_dispatcher` (assigned later), uses `Thread.Sleep` for
    the minimum toast time, and has test-only `internal static Create*Alert` indirection.

50. **`ScriptRuntimeTests.cs` is 3101 lines with 115 tests.** Each test hand-builds runtime services
    (six telescoping `ScriptRuntime` constructors mirror `ScriptRuntimeServices`). Split it per API
    and use a builder.

51. **Ownership is misplaced in several places.**
    - `ShellScriptApi.ParseLaunchOptions`/`ParseExecutionOptions` are public statics reused by
      other APIs.
    - Repeat validation lives in the API class.
    - `MagnifierOptions.Parse` lives on the record and duplicates default literals.
    - `NativeHotkeyService` owns the mouse hook and exposes it.
    - The `HotkeyModifiers` enum mixes `NoRepeat` into the domain flags.
    - Modifier flag constants in `bootstrap.js` duplicate the C# enum.

52. **API consistency.**
    - `MagnifierScriptHandle` doesn't derive from `ScriptResourceHandle` (no `stop` alias).
    - `AudioRecordingScriptHandle.stop()` means "stop recording", whereas elsewhere it means
      dispose.
    - `setZoom` is typed `double`.
    - Exceptions are thrown without `paramName`.
    - Constructor visibility is mixed (public vs internal).
    - Blocking has seven aliases.
    - `prepend` is parsed for scroll watches but not keyboard watches.

53. **Magnifier code style.** It is dense, with one-line terse members and many magic numbers:
    - window style values such as `0x00200000`, `0x11`, `-20`, and SW codes 4/5/0;
    - HRESULTs `0x887A0027` and `0x887A000A`;
    - DXGI or COM GUIDs inline.

    That clashes with the rest of the codebase. Give the HRESULT and style constants names in
    `MagnifierNative`.

54. **`TrayIconService`** has an eight-delegate constructor and uses the generic
    `SystemIcons.Application` icon.

## Dead code / leftovers

55. **Dead or test-only members.**
    - `ScrittoTiming.EdgeFadeEm`/`EdgeSlackEm`/`BlockSlackEm`/`EdgeSlackPx`/`BlockSlackPx` and the
      `ShrinkEasing` string are unused.
    - `CrossingFraction` is used only by tests.
    - `ToastStyleMetrics.DotTextGap`/`DotStateLeftPadding` are used only by tests.
    - `IToastView.BeginEnterAnimation` is empty in `ToastWindow`.

56. **Unreachable branches.**
    - `WpfClipboardTextStore.cs:78` final `return action();` is unreachable in practice, and the
      `COMException` catch is shadowed by `ExternalException`.
    - The `bootstrap.js` second `typeof options === "function"` branch in `httpRequest` is dead.
    - The scroll-watch non-blocking warning path is dead (#33).
    - Uninstall-when-empty in the keyboard hook is mostly dead, because the host priority handler
      is always set.

57. **Small leftovers.**
    - A comment in `bootstrap.js` (~line 786) is misindented.
    - The `MouseScrollWatchDispatcher.cs:98` warning is a non-interpolated string, so a literal
      `{{ preventDefault: true }}` appears in logs.
    - `ConfigReloadAlerts` has two branches appending identical text and matches English
      strings.
    - The CLI help and lint help text is stale (it mentions only timers).

## Build, packaging and docs

58. **[C] `Build-Installer.ps1` ignores `dotnet publish` failures.**
    `scripts/Build-Installer.ps1:98-118`. `$ErrorActionPreference = "Stop"` does not apply to
    native commands, and `$LASTEXITCODE` is never checked after either publish. If the CLI publish
    fails after the app publish succeeds, the script still produces an installer, just without
    `hspn.exe`. This affects both the local verification loop and the CI release. Check
    `$LASTEXITCODE` after each publish, as is already done for ISCC.

59. **[C] Both self-contained publishes write into one directory.**
    `scripts/Build-Installer.ps1:98-118`. The WPF app and the CLI each bring their own runtime and
    shared assemblies, and the second publish silently overwrites overlapping files from the first.
    This works today because both pin the same versions, but it is fragile.

60. **[C] Local and CI builds use incompatible version schemes.**
    - Local builds default to `0.MMdd.HHmm.ss`.
    - CI uses the run number (`123`, so `VersionInfoVersion` is `0.0.0.123`).

    Installing a local build over a CI build, or the reverse, compares as an upgrade or downgrade
    more or less at random.

61. **[C] The README is missing some exposed APIs.**
    - `hs.log`, `hs.info`, `hs.warn` and `hs.error` (`bootstrap.js:861-875`) are exposed but not
      documented in "Current API". AGENTS.md requires every exposed API to be listed there.
    - Every other exposed `hs.*` member is mentioned in the README.

62. **[C] `PRIVACY.md` understates what diagnostics contain.** It says runtime diagnostics redact
    shell and URL data. It does not mention that swallowed key names, toast text and window titles
    are written to `%APPDATA%\HsWin\runtime-logs` (see #14).

63. **[C] The installer's publisher field reads as the upstream project.** In `installer/HsWin.iss`,
    `MyAppPublisher "Hammerspoon"` labels the installer and Apps & Features entry as published by
    the upstream Hammerspoon project, which is a branding/attribution issue. *(Low)*

Reviewed and clean: `scripts/Build-WinGetManifests.ps1` validates its inputs, rejects unresolved
template tokens and writes UTF-8 without BOM. `.github/workflows/release.yml` builds, tests, then
packages (it publishes on every push to `main`, which is by design per AGENTS.md). The root `*.log`
files are empty and gitignored.

---

Audit complete. Every source file in `src/`, plus the tests, scripts, installer, workflow,
README, plans and PRIVACY were reviewed.

---

## Fix progress (phase 2)

Scope: fix 1–22, 26–33, 35–63. Findings 23, 24, 25 and 34 are out of scope (24 partly fixed as a
side effect of #2). **done** = code changed and builds (tests still to be updated at the end;
new tests already written: ConfigLinterTests additions, ScriptErrorFormatterTests,
ConfigSourceScannerTests, NativeInputTests, hotkey ID tests; shared tests/Shared/CapturingRuntimeLogger
linked into both test projects replaces six private copies).

- done: 1 (harmless default config; old template backed up to
  `%APPDATA%\HsWin\config.js.shipped-default-template-20261002.bak`), 2 (ScriptGeneration +
  ScriptResourceScope + ScriptCallbackRelay), 3, 7 (audio capture rewrite + AudioLevelCalculator),
  4 (app.quit over pipe, Kill(false) fallback), 5 (DedicatedDispatcherThread, no lock across
  marshal), 6, 8, 9, 10 (KeyNameTable), 11 (RegistryUserPathStore), 12 (SerializedInputQueue), 13,
  16, 17, 20 (remap hold + repeat modifiers), 30, 36
  (PreciseTimerScheduler drives hs.timer, repeats, follower; magnifier shares
  HighResolutionWaitableTimer), 15 (reload watchdog: ConfigExecutionTimeout, lint 5 s, race-free
  state), 27 (ScriptErrorFormatter in Core used by lint + reload toast; UserFacingExceptionFormatter
  deleted), 40 (LowLevelHookThread), 46 (ConfigSourceScanner shared by both analyzers; modifier
  check uses KeyboardKeyRules; dedup by line; key-down aliases shared via
  KeyboardScriptOptionsParser.KeyDownPropertyNames), 19 (round-robin IDs skipping ones still
  registered natively; no per-press logs; Dispose snapshots under lock), 21 (Native/KeyboardScanCodes:
  E0 prefix from MAPVK_VK_TO_VSC_EX, Pause falls back to VK; exact-size input buffers), 22
  (ForegroundInputTarget: GUITHREADINFO focus child; mouse lParam = cursor in client coords, MK_*
  wParam), 29 (cached session manager, 2 s timeouts, status polled up to 400 ms; action inference
  fixed), 33 (scroll callbacks are Action, documented as swallow-all; dead return path removed), 41
  (Native/NativeInput + NativeInputSender shared by keyboard, mouse, media; media key carries
  marker), 48 (one SynchronizationContextCallbackScheduler implementing Core IScriptCallbackScheduler), 43 (Core/Input: InputMethod,
  IRepeatSession, RepeatTiming, RepeatScriptHandle; App ActiveRepeatSlot), 44, 58, 59, 60, 63
- done also (session 3): 18 (WpfClipboardTextStore on its own STA thread via DedicatedDispatcherThread
  + ClipboardRetry; magnifier Create no longer holds _gate while marshalling and uses DispatcherMarshal;
  ToastPresenter.Dispose is FireAndForget), 26 (EditorLauncher: env vars, then .js association via
  AssocQueryString/DefaultEditorSelector, skipping script hosts so `open` never runs the config; falls
  back to Notepad), 28 (NativeAudioDeviceController on NAudio CoreAudioApi; missing default device ->
  clear InvalidOperationException, listing still works), 31 (DestroyNative/GPU Dispose always finish
  teardown; _sessions guarded; BindHeld already safe via binding ctors), 52 (MagnifierScriptHandle
  derives ScriptResourceHandle, setZoom type-checked). TESTS: full solution builds and all suites
  pass (Core 387, App 289, CLI 5) as of this point.
- done also: 14 (toast logs carry only textLength; window-message target logs drop titles; hotkey
  presses not logged; keyboard non-blocking-return warning once per watch; scroll warning removed)
- partial: 32 (toast + follower use target-monitor DPI; popup offsets
  synced on update), 42 (Native/NativeStructs, ScreenGeometry, NativeWindowInfo, ShowWindowCommand; remaining
  DllImport style todo), 45
  (helpers, keyboard arg lists, EnumerateModifierValues done), 52 (keyboard watch `prepend`)
- done also: 39 (FileLogger/ReloadScriptConsoleLogger bounded queues with a "dropped N" line, no blank
  line for entries without an exception, writer leak fixed, sortable `yyyy-MM-dd-HH-mm` names,
  `LogRetention` 14 days / keep newest 20, run off the startup path by `LogRetentionTask`). Also fixed a
  crash I introduced in `ProcessShellService.WaitForExit` (disposed wait handle set after kill; now a
  TaskCompletionSource). Core 392, App 314, CLI 5 pass.
- done also: 37 (glyph BlurEffect only while animating), 47 (shared EmptyDisposable; Null* watches/timers
  never throw, action-style null services throw NotSupportedException), 53 (named window-style/HRESULT/
  SetWindowPos constants; ShowWindowCommand extended), 55 (dead Scritto/toast members removed;
  IToastView.BeginEnterAnimation default no-op), 56, 57 (merged duplicate reload-alert branch, lint help
  refreshed; the other 56/57 items were already fixed). Flaky nested-scope test fixed.
- done also: 51 (MagnifierScriptOptionsParser + named limits on the record; KeyboardRepeatRules owns the
  repeat/pulse policy; the composition root owns the shared mouse hook; NoRepeat is a Win32-only constant
  in NativeHotkeyService; a test pins bootstrap.js modifier flags to HotkeyModifiers), 54 (TrayMenuActions
  parameter object, tray tests drive real menu items; the generic icon stays: the repo ships no .ico).
  Shared FakeMouseHookPlatform replaces two copies.
- done also: 49 (AppController is now the composition root: ScriptReloadCoordinator, EmergencyStopController,
  CliInstallCoordinator, InputKillSwitch behind IUiThread / IScriptRuntimeLifecycle / ICliInstaller /
  ICountingAlertPresenter; Thread.Sleep replaced by Task.Delay; test-only Create*Alert indirection removed;
  32 new lifecycle tests).
- done also: 50 (ScriptRuntime keeps only the parameterless and ScriptRuntimeServices constructors; the
  3140-line ScriptRuntimeTests is split into 14 ScriptApi/*ScriptRuntimeTests classes over shared
  TestDoubles; same 417 Core tests).
- done also: 38 rest (clipboard change and launch-completed logs dropped; scroll callbacks capped at 256
  queued per watch with one warning), 14 rest (LogSanitizer.DescribeTarget for launch logs), 35 (new tests
  for SystemHttpService, the command pipe, ProcessApplicationProvider, StartupService), 42/45/32 reviewed:
  struct/helper duplication already consolidated; DllImport->LibraryImport migration deliberately NOT done
  (style only, needs runtime verification of each Win32 call).
- todo: 61, 62; then tests (#35/#50), README (#61), PRIVACY (#62), full AGENTS verification loop.
- #61 done: console.* (not hs.log) was already documented; README updated for hs.execute/task timeouts and stop, audio stop vs dispose, key names, watch prepend, remap hold, repeat modifiers, magnifier handles, config timeout, default config, editor order, log retention.
- #62 done: PRIVACY.md updated.
- Verification loop done: build OK, tests Core 449 / App 363 / CLI 6 pass, `hspn config lint` OK, installer built and launched. Fixed along the way: Build-Installer merge now keeps the app's WindowsBase/System.Drawing/Microsoft.VisualBasic over the CLI's facades; HsWin.iss ProductVersion uses the numeric version.
- todo: final report (out of scope: 23, 24 (partly fixed via #2), 25, 34).
