using System.Diagnostics;
using HsWin.App.Threading;
using HsWin.Core.Alerts;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;

namespace HsWin.App.Lifecycle;

/// <summary>
/// The host-owned safety valve (Ctrl+Alt+Shift+Esc, not part of config.js): stop all injected input
/// immediately, on the calling thread, then tear the script runtime down on a worker so the tray
/// UI is never blocked. Reachable two ways so it still works when one path is wedged: the keyboard
/// hook priority handler, and a RegisterHotKey or tray command.
/// </summary>
internal sealed class EmergencyStopController
{
    private const uint EscapeVirtualKey = 0x1B;
    private const HotkeyModifiers ChordModifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;
    private const int StoppedToastMs = 2000;
    private const int FailedToastMs = 6000;

    /// <summary>Ctrl+Alt+Shift+Esc.</summary>
    public static readonly HotkeyDefinition Hotkey = HotkeyDefinition.CreateKeyboard(ChordModifiers, EscapeVirtualKey);

    private readonly IRuntimeLogger _logger;
    private readonly IInputKillSwitch _input;
    private readonly IScriptRuntimeTeardown _runtime;
    private readonly IAlertPresenter _toasts;
    private readonly IUiThread _ui;
    private readonly Func<Action, bool> _queueWorker;

    private int _inProgress;
    private int _chordLatched;

    public EmergencyStopController(
        IRuntimeLogger logger,
        IInputKillSwitch input,
        IScriptRuntimeTeardown runtime,
        IAlertPresenter toasts,
        IUiThread ui,
        Func<Action, bool>? queueWorker = null)
    {
        _logger = logger;
        _input = input;
        _runtime = runtime;
        _toasts = toasts;
        _ui = ui;
        _queueWorker = queueWorker ?? QueueOnThreadPool;
    }

    /// <summary>
    /// Keyboard-hook path: match the chord, kill injection on this thread, schedule the full stop.
    /// Returns true to swallow the key so games and apps never see it.
    /// </summary>
    public bool TryHandleKey(KeyboardEventSnapshot snapshot)
    {
        if (snapshot.IsInjected)
        {
            return false;
        }

        // Clear the latch on Escape key-up so a later press can trigger again.
        if (snapshot.IsKeyUp && snapshot.KeyCode == EscapeVirtualKey)
        {
            Volatile.Write(ref _chordLatched, 0);
            return false;
        }

        if (!snapshot.IsKeyDown || snapshot.KeyCode != EscapeVirtualKey)
        {
            return false;
        }

        if (((HotkeyModifiers)snapshot.ModifierFlags & ChordModifiers) != ChordModifiers)
        {
            return false;
        }

        // Debounce auto-repeat while Esc is held.
        if (Interlocked.Exchange(ref _chordLatched, 1) != 0)
        {
            return true;
        }

        _logger.Warning("Emergency stop chord matched on keyboard hook (Ctrl+Alt+Shift+Esc).");
        Request("keyboard-hook");
        return true;
    }

    public void Request(string source)
    {
        // Always kill injected input first, even if a full stop is already in progress.
        KillInjectedInput();

        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
        {
            _logger.Info($"Emergency stop ({source}): input killed; full teardown already in progress.");
            return;
        }

        _logger.Warning($"Emergency stop requested ({source}).");
        try
        {
            if (!_queueWorker(RunWorker))
            {
                _logger.Warning("Emergency stop: worker queue refused the work; running teardown inline.");
                RunWorker();
            }
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _inProgress, 0);
            _logger.Error("Emergency stop: failed to queue worker.", exception);
            // Still attempt inline teardown so a queue failure is not a dead end.
            try
            {
                RunWorker();
            }
            catch (Exception inlineException)
            {
                _logger.Error("Emergency stop: inline teardown failed.", inlineException);
            }
        }
    }

    private static bool QueueOnThreadPool(Action work) =>
        ThreadPool.QueueUserWorkItem(static state => ((Action)state!)(), work);

    private void RunWorker()
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            if (_runtime.IsShutDown)
            {
                return;
            }

            KillInjectedInput();
            _runtime.StopRuntimeForEmergency();
            if (!_runtime.IsShutDown)
            {
                ShowToast("Stopped", AlertKind.Normal, StoppedToastMs);
            }

            _logger.Warning(
                $"Emergency stop worker completed elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}.");
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop worker failed.", exception);
            if (!_runtime.IsShutDown)
            {
                ShowToast($"Emergency stop failed: {exception.Message}", AlertKind.Error, FailedToastMs);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _inProgress, 0);
        }
    }

    private void KillInjectedInput()
    {
        _input.StopRepeats("Emergency stop");
        _input.ReleaseHeldKeys("Emergency stop");
    }

    private void ShowToast(string message, AlertKind kind, int durationMs)
    {
        try
        {
            if (_ui.CheckAccess())
            {
                _toasts.Show(AlertRequest.Create(message, kind, durationMs));
                return;
            }

            _ui.Post(() =>
            {
                try
                {
                    _toasts.Show(AlertRequest.Create(message, kind, durationMs));
                }
                catch (Exception exception)
                {
                    _logger.Error("Emergency stop toast failed.", exception);
                }
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Emergency stop toast schedule failed.", exception);
        }
    }
}
