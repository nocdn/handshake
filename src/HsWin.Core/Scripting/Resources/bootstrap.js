(() => {
  const host = globalThis.__hswinHost;
  const formatConsoleValue = (value) => {
    if (typeof value === "string") {
      return value;
    }

    if (value instanceof Error) {
      return value.stack || value.message || String(value);
    }

    try {
      const serialized = JSON.stringify(value);
      if (serialized !== undefined) {
        return serialized;
      }
    } catch {
    }

    return String(value);
  };

  const writeConsole = (level, values) => {
    host.Console.Log(level, values.map(formatConsoleValue).join(" "));
  };

  const parseJson = (json) => JSON.parse(json);

  // Hot-path event adapters: the host passes primitive fields (cheaper to marshal than a
  // serialized JSON string) and the helpers below rebuild the same frozen plain objects that
  // JSON.parse used to produce. Modifier flags mirror HsWin.Core's HotkeyModifiers enum.
  const MODIFIER_FLAG_ALT = 0x0001;
  const MODIFIER_FLAG_CONTROL = 0x0002;
  const MODIFIER_FLAG_SHIFT = 0x0004;
  const MODIFIER_FLAG_WIN = 0x0008;

  const modifierNamesFromFlags = (flags) => {
    const names = [];
    if (flags & MODIFIER_FLAG_CONTROL) { names.push("ctrl"); }
    if (flags & MODIFIER_FLAG_ALT) { names.push("alt"); }
    if (flags & MODIFIER_FLAG_SHIFT) { names.push("shift"); }
    if (flags & MODIFIER_FLAG_WIN) { names.push("win"); }
    return names;
  };

  const buildKeyboardEvent = (
    type,
    keyCode,
    key,
    modifierFlags,
    isKeyDown,
    isKeyUp,
    isModifier,
    isInjected,
    isExtended
  ) => ({
    type,
    keyCode,
    key,
    modifiers: modifierNamesFromFlags(modifierFlags),
    modifierFlags,
    isKeyDown,
    isKeyUp,
    isModifier,
    isInjected,
    isExtended
  });

  const buildMouseEvent = (type, button, isDown, isUp) => ({ type, button, isDown, isUp });

  // Held hotkeys come in two shapes: mouse buttons pass (type, button, isDown, isUp) while
  // keyboard keys pass the full keyboard-event field list.
  const buildHeldEvent = (args) =>
    args.length === 4
      ? buildMouseEvent(args[0], args[1], args[2], args[3])
      : buildKeyboardEvent(...args);

  const buildScrollEvent = (
    axis,
    direction,
    delta,
    notches,
    isVertical,
    isHorizontal,
    isInjected,
    modifierFlags,
    x,
    y
  ) => ({
    type: "scroll",
    axis,
    direction,
    delta,
    notches,
    isVertical,
    isHorizontal,
    isInjected,
    modifiers: modifierNamesFromFlags(modifierFlags),
    modifierFlags,
    x,
    y
  });

  const rectangleCenter = (rectangle) => ({
    x: rectangle.x + (rectangle.width / 2),
    y: rectangle.y + (rectangle.height / 2)
  });

  const containsPoint = (rectangle, point) =>
    point.x >= rectangle.x
      && point.x < rectangle.x + rectangle.width
      && point.y >= rectangle.y
      && point.y < rectangle.y + rectangle.height;

  const createWindowMoveResult = (windowId, success, moved, reason, frame, extra) => ({
    windowId,
    success,
    moved,
    reason,
    frame,
    ...(extra || {})
  });

  const createWindow = (windowSnapshot) => {
    if (!windowSnapshot) {
      return null;
    }

    return Object.freeze({
      ...windowSnapshot,

      refresh() {
        return createWindow(parseJson(host.Windows.GetWindowJson(windowSnapshot.id)));
      },

      moveToScreen(screen, options) {
        return parseJson(host.Windows.MoveToScreenJson(windowSnapshot.id, screen, options));
      },

      moveToMouseScreen(options) {
        const screen = hs.mouse.getCurrentScreen();
        if (!screen) {
          return {
            windowId: windowSnapshot.id,
            success: false,
            moved: false,
            reason: "mouse-screen-unavailable",
            frame: null
          };
        }

        return this.moveToScreen(screen, options);
      },

      moveToScreenNative(screen) {
        if (!screen) {
          return createWindowMoveResult(
            windowSnapshot.id,
            false,
            false,
            "screen-unavailable",
            null);
        }

        const frame = windowSnapshot.frame;
        const windowCenter = rectangleCenter(frame);
        if (containsPoint(screen.bounds, windowCenter)) {
          return createWindowMoveResult(
            windowSnapshot.id,
            true,
            false,
            "already-on-screen",
            frame);
        }

        const targetCenter = rectangleCenter(screen.bounds);
        if (Math.abs(targetCenter.x - windowCenter.x) < 1) {
          return createWindowMoveResult(
            windowSnapshot.id,
            false,
            false,
            "target-screen-not-horizontal",
            frame);
        }

        const direction = targetCenter.x < windowCenter.x ? "left" : "right";
        host.Keyboard.Tap(direction, { modifiers: ["win", "shift"] });
        return createWindowMoveResult(
          windowSnapshot.id,
          true,
          true,
          "sent-native-monitor-move",
          null,
          { direction });
      },

      moveToMouseScreenNative() {
        const screen = hs.mouse.getCurrentScreen();
        if (!screen) {
          return createWindowMoveResult(
            windowSnapshot.id,
            false,
            false,
            "mouse-screen-unavailable",
            null);
        }

        return this.moveToScreenNative(screen);
      }
    });
  };

  const createPasteboardReplaceResult = (previous, current) => Object.freeze({
    changed: previous !== current,
    previous,
    current
  });

  const pasteboard = Object.freeze({
    getContents() {
      return host.Clipboard.GetText();
    },

    setContents(text) {
      return host.Clipboard.SetText(text);
    },

    replaceContents(replacer) {
      const previous = this.getContents();
      let current = typeof replacer === "function"
        ? replacer(previous)
        : replacer;

      if (current === undefined || current === null || current === false) {
        return createPasteboardReplaceResult(previous, previous);
      }

      if (typeof current !== "string") {
        throw new Error("Clipboard replacement must be a string, null, undefined, or false.");
      }

      if (current !== previous) {
        this.setContents(current);
      }

      return createPasteboardReplaceResult(previous, current);
    },

    replaceText(searchValue, replaceValue) {
      return this.replaceContents(text => text.replace(searchValue, replaceValue));
    },

    watch(callback) {
      if (typeof callback !== "function") {
        throw new Error("Clipboard watch callback must be a function.");
      }

      return host.Clipboard.Watch(eventJson => callback(parseJson(eventJson)));
    },

    watchText(replacer) {
      if (typeof replacer !== "function") {
        throw new Error("Clipboard text watcher must be a function.");
      }

      return this.watch(event => this.replaceContents(text => replacer(text, event)));
    }
  });

  const createAudioDevice = (device, kind) => {
    const isInput = kind === "input";
    return Object.freeze({
      ...device,
      kind,

      getVolume() {
        const json = isInput
          ? host.Audio.GetInputVolumeJson(device.id)
          : host.Audio.GetVolumeJson(device.id);
        return parseJson(json).volume;
      },

      setVolume(volume) {
        return parseJson(isInput
          ? host.Audio.SetInputVolumeJson(device.id, volume)
          : host.Audio.SetVolumeJson(device.id, volume));
      },

      getMuted() {
        const json = isInput
          ? host.Audio.GetInputVolumeJson(device.id)
          : host.Audio.GetVolumeJson(device.id);
        return parseJson(json).muted;
      },

      setMuted(muted) {
        return parseJson(isInput
          ? host.Audio.SetInputMutedJson(device.id, muted)
          : host.Audio.SetMutedJson(device.id, muted));
      },

      toggleMute() {
        return parseJson(isInput
          ? host.Audio.ToggleInputMuteJson(device.id)
          : host.Audio.ToggleMuteJson(device.id));
      }
    });
  };

  const audiodevice = Object.freeze({
    defaultOutputDevice() {
      return createAudioDevice(parseJson(host.Audio.GetDefaultOutputDeviceJson()), "output");
    },

    allOutputDevices() {
      return parseJson(host.Audio.GetOutputDevicesJson()).map(device => createAudioDevice(device, "output"));
    },

    defaultInputDevice() {
      return createAudioDevice(parseJson(host.Audio.GetDefaultInputDeviceJson()), "input");
    },

    allInputDevices() {
      return parseJson(host.Audio.GetInputDevicesJson()).map(device => createAudioDevice(device, "input"));
    },

    getVolume(deviceId) {
      return parseJson(host.Audio.GetVolumeJson(deviceId)).volume;
    },

    setVolume(volume, deviceId) {
      return parseJson(host.Audio.SetVolumeJson(deviceId, volume));
    },

    getMuted(deviceId) {
      return parseJson(host.Audio.GetVolumeJson(deviceId)).muted;
    },

    setMuted(muted, deviceId) {
      return parseJson(host.Audio.SetMutedJson(deviceId, muted));
    },

    toggleMute(deviceId) {
      return parseJson(host.Audio.ToggleMuteJson(deviceId));
    },

    getInputVolume(deviceId) {
      return parseJson(host.Audio.GetInputVolumeJson(deviceId)).volume;
    },

    setInputVolume(volume, deviceId) {
      return parseJson(host.Audio.SetInputVolumeJson(deviceId, volume));
    },

    getInputMuted(deviceId) {
      return parseJson(host.Audio.GetInputVolumeJson(deviceId)).muted;
    },

    setInputMuted(muted, deviceId) {
      return parseJson(host.Audio.SetInputMutedJson(deviceId, muted));
    },

    toggleInputMute(deviceId) {
      return parseJson(host.Audio.ToggleInputMuteJson(deviceId));
    }
  });

  const sound = Object.freeze({
    getVolume() {
      return audiodevice.getVolume();
    },

    setVolume(volume) {
      return audiodevice.setVolume(volume);
    },

    getMuted() {
      return audiodevice.getMuted();
    },

    setMuted(muted) {
      return audiodevice.setMuted(muted);
    },

    toggleMute() {
      return audiodevice.toggleMute();
    }
  });

  const audio = Object.freeze({
    record(options, callback) {
      if (typeof options === "function") {
        callback = options;
        options = undefined;
      }

      if (typeof callback !== "function") {
        throw new Error("Audio record callback must be a function.");
      }

      return host.AudioCapture.Record(options, eventJson => callback(parseJson(eventJson)));
    },

    levels(options, callback) {
      if (typeof options === "function") {
        callback = options;
        options = undefined;
      }

      if (typeof callback !== "function") {
        throw new Error("Audio level callback must be a function.");
      }

      return host.AudioCapture.WatchLevels(options, eventJson => callback(parseJson(eventJson)));
    }
  });

  const parseHttpResult = (resultJson) => {
    const result = parseJson(resultJson);
    result.text = result.body;

    const contentType = result.headers && (result.headers["Content-Type"] || result.headers["content-type"]);
    if (result.body && contentType && contentType.toLowerCase().includes("json")) {
      try {
        result.json = JSON.parse(result.body);
      } catch {
      }
    }

    return result;
  };

  const normalizeHttpOptions = (method, urlOrOptions, options) => {
    const request = typeof urlOrOptions === "string"
      ? { ...(options || {}), url: urlOrOptions }
      : { ...(urlOrOptions || {}) };

    if (method && !request.method) {
      request.method = method;
    }

    if (request.json !== undefined) {
      request.body = JSON.stringify(request.json);
      request.contentType = request.contentType || "application/json";
      const headers = { ...(request.headers || {}) };
      // Header names are case-insensitive; keep any Accept the script already set.
      if (!Object.keys(headers).some(name => name.toLowerCase() === "accept")) {
        headers.Accept = "application/json";
      }

      request.headers = headers;
    }

    return request;
  };

  const httpRequest = (method, urlOrOptions, options, callback) => {
    if (typeof options === "function") {
      callback = options;
      options = undefined;
    }

    if (typeof callback !== "function") {
      throw new Error("HTTP callback must be a function.");
    }

    return host.Http.Request(normalizeHttpOptions(method, urlOrOptions, options), resultJson => callback(parseHttpResult(resultJson)));
  };

  const http = Object.freeze({
    request(options, callback) {
      return httpRequest(null, options, undefined, callback);
    },

    get(urlOrOptions, options, callback) {
      return httpRequest("GET", urlOrOptions, options, callback);
    },

    post(urlOrOptions, options, callback) {
      return httpRequest("POST", urlOrOptions, options, callback);
    },

    put(urlOrOptions, options, callback) {
      return httpRequest("PUT", urlOrOptions, options, callback);
    },

    patch(urlOrOptions, options, callback) {
      return httpRequest("PATCH", urlOrOptions, options, callback);
    },

    delete(urlOrOptions, options, callback) {
      return httpRequest("DELETE", urlOrOptions, options, callback);
    }
  });

  const formatElapsedSeconds = (startedAt) => {
    const seconds = Math.max(0, Math.floor((Date.now() - startedAt) / 1000));
    const minutes = Math.floor(seconds / 60);
    const remainder = seconds % 60;
    return `${minutes}:${String(remainder).padStart(2, "0")}`;
  };

  const createOperationToast = (text, options) => {
    let currentText = String(text);
    let currentOptions = { type: "normal", loading: true, durationMs: 60000, elapsed: true, ...(options || {}) };
    let startedAt = Date.now();
    let timer = null;
    let disposed = false;

    const render = () => {
      if (disposed) {
        return;
      }

      const showElapsed = currentOptions.elapsed !== false;
      const renderedText = showElapsed
        ? `${currentText} ${formatElapsedSeconds(startedAt)}`
        : currentText;
      const { elapsed, ...alertOptions } = currentOptions;
      host.Alerts.Show(renderedText, alertOptions);
    };

    const ensureTimer = () => {
      const wantsElapsed = currentOptions.elapsed !== false;
      if (wantsElapsed && !timer) {
        timer = host.Timers.DoEvery(1000, render);
      } else if (!wantsElapsed && timer) {
        timer.stop();
        timer = null;
      }
    };

    const stopTimer = () => {
      if (timer) {
        timer.stop();
        timer = null;
      }
    };

    const api = {
      update(nextText, nextOptions) {
        currentText = String(nextText);
        currentOptions = { ...currentOptions, ...(nextOptions || {}) };
        if (nextOptions && nextOptions.resetElapsed) {
          startedAt = Date.now();
        }

        ensureTimer();
        render();
        return api;
      },

      loading(nextText, nextOptions) {
        return api.update(nextText, { type: "normal", loading: true, durationMs: 60000, elapsed: false, ...(nextOptions || {}) });
      },

      success(nextText, nextOptions) {
        stopTimer();
        currentOptions = { type: "success", durationMs: 2500, elapsed: false, ...(nextOptions || {}) };
        currentText = String(nextText);
        render();
        return api;
      },

      error(nextText, nextOptions) {
        stopTimer();
        currentOptions = { type: "error", durationMs: 6000, elapsed: false, ...(nextOptions || {}) };
        currentText = String(nextText);
        render();
        return api;
      },

      hide() {
        api.dispose();
        host.Alerts.Show("Hidden", { ...currentOptions, durationMs: 0 });
      },

      stop() {
        api.dispose();
      },

      dispose() {
        if (disposed) {
          return;
        }

        disposed = true;
        stopTimer();
      },

      delete() {
        api.dispose();
      }
    };

    ensureTimer();
    render();
    return Object.freeze(api);
  };

  globalThis.hs = Object.freeze({
    magnifier: Object.freeze({
      new(options) {
        return host.Magnifier.Create(options);
      },

      bindHeld(modifiers, key, options) {
        return host.Magnifier.BindHeld(modifiers, key, options);
      }
    }),

    execute(command, options) {
      return parseJson(host.Shell.ExecuteCommandJson(command, options));
    },

    alert: Object.freeze({
      show(text, optionsOrKind, durationMs) {
        host.Alerts.Show(text, optionsOrKind, durationMs);
      },

      operation(text, options) {
        return createOperationToast(text, options);
      }
    }),

    pasteboard,
    clipboard: pasteboard,

    hotkey: Object.freeze({
      bind(modifiers, key, pressedFn) {
        return host.Hotkeys.Bind(modifiers, key, pressedFn);
      },

      bindHeld(modifiers, key, pressedFn, releasedFn, options) {
        if (typeof pressedFn !== "function") {
          throw new Error("Held hotkey pressed callback must be a function.");
        }

        if (typeof releasedFn !== "function") {
          throw new Error("Held hotkey released callback must be a function.");
        }

        return host.Hotkeys.BindHeld(
          modifiers,
          key,
          (...args) => pressedFn(buildHeldEvent(args)),
          (...args) => releasedFn(buildHeldEvent(args)),
          options);
      },

      whileHeld(modifiers, key, pressedFn, releasedFn, options) {
        return this.bindHeld(modifiers, key, pressedFn, releasedFn, options);
      }
    }),

    task: Object.freeze({
      run(command, options, callback) {
        if (typeof options === "function") {
          callback = options;
          options = undefined;
        }

        if (typeof callback !== "function") {
          throw new Error("Task callback must be a function.");
        }

        return host.Tasks.Run(command, options, (resultJson) => callback(parseJson(resultJson)));
      }
    }),

    application: Object.freeze({
      isRunning(processName) {
        return host.Applications.IsRunning(processName);
      },

      runningApplications(options) {
        return parseJson(host.Applications.GetRunningApplicationsJson(options));
      },

      launch(target, options) {
        return parseJson(host.Applications.LaunchJson(target, options));
      }
    }),

    window: Object.freeze({
      focusedWindow() {
        return createWindow(parseJson(host.Windows.GetFocusedWindowJson()));
      },

      get(id) {
        return createWindow(parseJson(host.Windows.GetWindowJson(id)));
      },

      watchFocused(callback) {
        if (typeof callback !== "function") {
          throw new Error("Window focus callback must be a function.");
        }

        return host.Windows.WatchFocused(windowJson => callback(createWindow(parseJson(windowJson))));
      },

      onFocused(callback) {
        return this.watchFocused(callback);
      },

      moveFocusedToScreen(screen, options) {
        const win = this.focusedWindow();
        if (!win) {
          return createWindowMoveResult("", false, false, "no-focused-window", null);
        }

        return win.moveToScreen(screen, options);
      },

      moveFocusedToScreenNative(screen) {
        const win = this.focusedWindow();
        if (!win) {
          return createWindowMoveResult("", false, false, "no-focused-window", null);
        }

        return win.moveToScreenNative(screen);
      },

      moveFocusedToMouseScreen(options) {
        const screen = hs.mouse.getCurrentScreen();
        if (!screen) {
          return createWindowMoveResult("", false, false, "mouse-screen-unavailable", null);
        }

        return this.moveFocusedToScreen(screen, options);
      },

      moveFocusedToMouseScreenNative() {
        const screen = hs.mouse.getCurrentScreen();
        if (!screen) {
          return createWindowMoveResult("", false, false, "mouse-screen-unavailable", null);
        }

        return this.moveFocusedToScreenNative(screen);
      }
    }),

    media: Object.freeze({
      playPause() {
        return parseJson(host.Media.PlayPauseJson());
      },

      previousTrack() {
        return parseJson(host.Media.PreviousTrackJson());
      },

      nextTrack() {
        return parseJson(host.Media.NextTrackJson());
      }
    }),

    audiodevice,
    sound,
    audio,
    http,

    mouse: Object.freeze({
      getCurrentScreen() {
        return parseJson(host.Mouse.GetCurrentScreenJson());
      },

      isOnPrimaryScreen() {
        return host.Mouse.IsOnPrimaryScreen();
      },

      click(button) {
        return host.Mouse.Click(button);
      },

      scroll(notches) {
        return host.Mouse.Scroll(notches);
      },

      repeat(button, options) {
        return host.Mouse.Repeat(button, options);
      },

      stopRepeat() {
        host.Mouse.StopRepeat();
      },

      watchScroll(callback, options) {
        if (typeof callback !== "function") {
          throw new Error("Mouse scroll watch callback must be a function.");
        }

        // Callbacks always run off the low-level mouse hook. preventDefault swallows every
        // matching event natively while the watcher is registered; the return value is ignored.
        return host.Mouse.WatchScroll(
          (axis, direction, delta, notches, isVertical, isHorizontal, isInjected, modifierFlags, x, y) => {
            callback(buildScrollEvent(axis, direction, delta, notches, isVertical, isHorizontal, isInjected, modifierFlags, x, y));
          }, options);
      }
    }),

    keyboard: Object.freeze({
      watch(callback, options) {
        if (typeof callback !== "function") {
          throw new Error("Keyboard watch callback must be a function.");
        }

        return host.Keyboard.Watch(
          (type, keyCode, key, modifierFlags, isKeyDown, isKeyUp, isModifier, isInjected, isExtended) =>
            callback(buildKeyboardEvent(type, keyCode, key, modifierFlags, isKeyDown, isKeyUp, isModifier, isInjected, isExtended)) === true,
          options);
      },

      remap(sourceKey, targetKey) {
        return host.Keyboard.Remap(sourceKey, targetKey);
      },

      tap(key, options) {
        host.Keyboard.Tap(key, options);
      },

      repeat(key, options) {
        return host.Keyboard.Repeat(key, options);
      },

      repeatPulse(key, options) {
        return host.Keyboard.RepeatPulse(key, options);
      },

      stopRepeat() {
        host.Keyboard.StopRepeat();
      },

      keyDown(key) {
        host.Keyboard.KeyDown(key);
      },

      keyUp(key) {
        host.Keyboard.KeyUp(key);
      },

      isDown(key) {
        return host.Keyboard.IsDown(key);
      }
    }),

    timer: Object.freeze({
      doAfter(delayMs, callback) {
        if (typeof callback !== "function") {
          throw new Error("Timer callback must be a function.");
        }

        return host.Timers.DoAfter(delayMs, callback);
      },

      doEvery(intervalMs, callback) {
        if (typeof callback !== "function") {
          throw new Error("Timer callback must be a function.");
        }

        return host.Timers.DoEvery(intervalMs, callback);
      }
    })
  });

  globalThis.console = Object.freeze({
    log(...values) {
      writeConsole("log", values);
    },

    info(...values) {
      writeConsole("info", values);
    },

    warn(...values) {
      writeConsole("warn", values);
    },

    error(...values) {
      writeConsole("error", values);
    }
  });
})();
