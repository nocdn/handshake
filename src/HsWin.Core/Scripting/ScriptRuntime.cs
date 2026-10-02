using HsWin.Core.Alerts;
using HsWin.Core.Applications;
using HsWin.Core.Audio;
using HsWin.Core.Clipboard;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Media;
using HsWin.Core.Shell;
using HsWin.Core.Timers;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace HsWin.Core.Scripting;

public sealed class ScriptRuntime : IScriptRuntimeLifecycle
{
    private static readonly string BootstrapScript = ScriptBootstrap.Load();

    /// <summary>How long ending a generation waits for a callback already inside the engine.</summary>
    private static readonly TimeSpan CallbackDrainTimeout = TimeSpan.FromSeconds(2);

    // Reload watchdog states; whoever leaves Running first decides whether the load timed out.
    private const int WatchdogRunning = 0;
    private const int WatchdogFinished = 1;
    private const int WatchdogTimedOut = 2;

    private readonly object _lifecycleGate = new();
    private readonly ScriptRuntimeServices _services;
    private ScriptGeneration? _generation;
    private bool _disposed;

    public ScriptRuntime()
        : this(new ScriptRuntimeServices())
    {
    }

    public ScriptRuntime(ScriptRuntimeServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public void ReloadFromFile(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Reload(File.ReadAllText(filePath), filePath);
    }

    public void Reload(string source, string documentName = "config.js")
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);

        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            EndGeneration(interruptRunningCallback: false);
            _services.Console.BeginReload(documentName);

            var generation = new ScriptGeneration(_services);
            var watchdogState = WatchdogRunning;
            try
            {
                using (new Timer(
                    _ =>
                    {
                        if (Interlocked.CompareExchange(ref watchdogState, WatchdogTimedOut, WatchdogRunning) == WatchdogRunning)
                        {
                            generation.Interrupt(_services.Logger);
                        }
                    },
                    null,
                    _services.ConfigExecutionTimeout,
                    Timeout.InfiniteTimeSpan))
                {
                    generation.Execute(BootstrapScript, source, documentName);
                }

                // Lost the race to the watchdog: its interrupt may land on a later callback, so
                // this generation cannot be kept.
                if (Interlocked.CompareExchange(ref watchdogState, WatchdogFinished, WatchdogRunning) != WatchdogRunning)
                {
                    throw CreateExecutionTimeout(documentName, innerException: null);
                }
            }
            catch (ScriptInterruptedException exception) when (Volatile.Read(ref watchdogState) == WatchdogTimedOut)
            {
                generation.End(CallbackDrainTimeout, interruptRunningCallback: true, _services.Logger);
                throw CreateExecutionTimeout(documentName, exception);
            }
            catch
            {
                generation.End(CallbackDrainTimeout, interruptRunningCallback: true, _services.Logger);
                throw;
            }

            Volatile.Write(ref _generation, generation);
        }
    }

    private TimeoutException CreateExecutionTimeout(string documentName, Exception? innerException) =>
        new(
            $"{Path.GetFileName(documentName)} did not finish within {_services.ConfigExecutionTimeout.TotalSeconds:0.#}s " +
            "and was stopped. Check for an endless loop in code that runs at load time.",
            innerException);

    /// <summary>
    /// Interrupts a runaway V8 callback without disposing resources. Safe to call from any thread
    /// while another thread is inside the engine; it never waits for the reload lock.
    /// </summary>
    public void InterruptEngineOnly()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Volatile.Read(ref _generation)?.Interrupt(_services.Logger);
    }

    /// <summary>
    /// Immediately stops script automation: blocks new callbacks, interrupts the running one,
    /// disposes every resource the config created (including ones created from callbacks), and
    /// tears down the engine. The runtime stays usable so a later <see cref="Reload"/> can restore config.
    /// </summary>
    public void EmergencyStop()
    {
        _services.Logger.Warning("ScriptRuntime emergency stop: interrupting engine and disposing all script resources.");
        lock (_lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EndGeneration(interruptRunningCallback: true);
        }

        _services.Logger.Warning("ScriptRuntime emergency stop completed. Reload config to resume automation.");
    }

    public bool HasActiveEngine => Volatile.Read(ref _generation) is not null;

    /// <summary>Live resources owned by the current config, for diagnostics and tests.</summary>
    internal int ActiveResourceCount => Volatile.Read(ref _generation)?.Resources.Count ?? 0;

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            EndGeneration(interruptRunningCallback: true);
            _disposed = true;
        }
    }

    private void EndGeneration(bool interruptRunningCallback)
    {
        Interlocked.Exchange(ref _generation, null)?.End(CallbackDrainTimeout, interruptRunningCallback, _services.Logger);
    }

    /// <summary>
    /// One evaluated config: its engine, the callback gate its callbacks run under, and every
    /// resource it created. Ending a generation closes all three together, in that order.
    /// </summary>
    private sealed class ScriptGeneration
    {
        private readonly V8ScriptEngine _engine = new();
        private readonly ScriptCallbackInvoker _callbacks;

        public ScriptGeneration(ScriptRuntimeServices services)
        {
            _callbacks = new ScriptCallbackInvoker(services.Alerts, services.Logger);
            Resources = new ScriptResourceScope(services.Logger);
            _engine.AddHostObject("__hswinHost", new HsScriptHost(services, Resources, _callbacks));
        }

        public ScriptResourceScope Resources { get; }

        public void Execute(string bootstrap, string source, string documentName)
        {
            _engine.Execute("hswin:bootstrap", bootstrap);
            _engine.Execute(documentName, source);
        }

        public void Interrupt(IRuntimeLogger logger)
        {
            try
            {
                _engine.Interrupt();
                logger.Warning("ScriptRuntime: V8 interrupt signaled.");
            }
            catch (Exception exception)
            {
                logger.Warning($"ScriptRuntime: V8 interrupt failed. {exception.Message}");
            }
        }

        public void End(TimeSpan drainTimeout, bool interruptRunningCallback, IRuntimeLogger logger)
        {
            if (interruptRunningCallback)
            {
                Interrupt(logger);
            }

            // Close the gate first: no new callback starts, and a running one gets to finish
            // (or is interrupted) before its resources and engine disappear underneath it.
            if (!_callbacks.Close(drainTimeout))
            {
                Interrupt(logger);
                if (!_callbacks.Close(drainTimeout))
                {
                    logger.Warning("ScriptRuntime: a script callback did not finish before engine teardown.");
                }
            }

            Resources.Dispose();
            _engine.Dispose();
        }
    }
}
