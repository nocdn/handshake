using System.Diagnostics;
using HsWin.App.Threading;
using HsWin.Core.Config;
using HsWin.Core.Logging;
using HsWin.Core.Scripting;

namespace HsWin.App.Lifecycle;

/// <summary>What emergency stop needs from whoever owns the script runtime.</summary>
internal interface IScriptRuntimeTeardown
{
    bool IsShutDown { get; }

    /// <summary>Abandons any in-flight reload and disposes everything the running config created.</summary>
    void StopRuntimeForEmergency();
}

/// <summary>How long the coordinator waits at each blocking step; tests shrink these.</summary>
internal sealed record ReloadTimings(
    TimeSpan MinimumReloadingVisible,
    TimeSpan GateSlice,
    TimeSpan EmergencyGateBudget,
    TimeSpan ShutdownGateWait)
{
    public static ReloadTimings Default { get; } = new(
        TimeSpan.FromMilliseconds(ConfigReloadAlerts.MinimumReloadingVisibleMs),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(5));
}

/// <summary>
/// Owns config reload: the reload generation (so a newer reload, emergency stop or shutdown
/// supersedes an older one), the gate serialising script-runtime access, and the reload toasts.
/// Background work never blocks forever on the UI thread while holding the gate.
/// </summary>
internal sealed class ScriptReloadCoordinator : IScriptRuntimeTeardown
{
    private readonly IRuntimeLogger _logger;
    private readonly ConfigFileService _config;
    private readonly IScriptRuntimeLifecycle _runtime;
    private readonly IInputKillSwitch _input;
    private readonly ICountingAlertPresenter _toasts;
    private readonly IUiThread _ui;
    private readonly Func<string?> _consoleLogPath;
    private readonly ReloadTimings _timings;
    private readonly object _gate = new();

    private int _generation;
    private bool _shutDown;

    public ScriptReloadCoordinator(
        IRuntimeLogger logger,
        ConfigFileService config,
        IScriptRuntimeLifecycle runtime,
        IInputKillSwitch input,
        ICountingAlertPresenter toasts,
        IUiThread ui,
        Func<string?> consoleLogPath,
        ReloadTimings? timings = null)
    {
        _logger = logger;
        _config = config;
        _runtime = runtime;
        _input = input;
        _toasts = toasts;
        _ui = ui;
        _consoleLogPath = consoleLogPath;
        _timings = timings ?? ReloadTimings.Default;
    }

    public bool IsShutDown => Volatile.Read(ref _shutDown);

    /// <summary>
    /// Shows the reloading toast, then reloads on a background thread. The returned task completes
    /// once the outcome toast has been queued (or the reload was superseded).
    /// </summary>
    public Task ReloadAsync()
    {
        var generation = Interlocked.Increment(ref _generation);
        var startedAt = Stopwatch.GetTimestamp();
        _logger.Info($"Reload Config requested generation={generation}.");
        _toasts.Show(ConfigReloadAlerts.CreateReloadingAlert());
        var toastBaseline = CaptureStandardToastBaseline();

        return Task.Run(async () =>
        {
            var outcome = ReloadUnderGate(generation);
            if (outcome.Superseded)
            {
                return;
            }

            // Keep the loader up long enough to read, so a fast reload does not flash.
            var remaining = _timings.MinimumReloadingVisible - Stopwatch.GetElapsedTime(startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining).ConfigureAwait(false);
            }

            _ui.Post(() => CompleteReload(generation, outcome.Failure, startedAt, toastBaseline));
        });
    }

    public void StopRuntimeForEmergency()
    {
        // Abandon any in-flight reload first: when its worker unblocks (dispatcher marshaling is
        // timeout-bounded), it sees the stale generation and tears down the superseded runtime
        // itself instead of installing a stale config.
        var abandonedGeneration = Interlocked.Increment(ref _generation);
        _logger.Warning($"Emergency stop: abandoned in-flight config reload generation={abandonedGeneration - 1}.");

        var lockTaken = false;
        try
        {
            // Do not hang forever if a reload holds the gate on a stuck Execute. Gate holds are
            // bounded (dispatcher marshaling fails fast), so a wedged reload unblocks within
            // seconds; keep retrying long enough to outlast it.
            var waited = TimeSpan.Zero;
            Monitor.TryEnter(_gate, _timings.GateSlice, ref lockTaken);
            waited += _timings.GateSlice;
            if (!lockTaken)
            {
                _logger.Warning(
                    "Emergency stop: script reload gate busy; interrupting engine without full lock, then retrying dispose.");
                try
                {
                    // Interrupt is safe without the gate; dispose still needs exclusive access.
                    _runtime.InterruptEngineOnly();
                }
                catch (Exception exception)
                {
                    _logger.Error("Emergency stop: InterruptEngineOnly failed.", exception);
                }

                while (!lockTaken && waited < _timings.EmergencyGateBudget && !IsShutDown)
                {
                    Monitor.TryEnter(_gate, _timings.GateSlice, ref lockTaken);
                    waited += _timings.GateSlice;
                }
            }

            if (lockTaken)
            {
                _runtime.EmergencyStop();
            }
            else
            {
                _logger.Warning(
                    "Emergency stop: could not acquire script gate for full teardown; injected input is stopped. Reload or quit the app.");
            }
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_gate);
            }
        }
    }

    /// <summary>
    /// Supersedes any reload in flight and refuses new outcome toasts. Call before tearing down the
    /// services a reload would touch.
    /// </summary>
    public void BeginShutdown()
    {
        Volatile.Write(ref _shutDown, true);
        Interlocked.Increment(ref _generation);
    }

    /// <summary>
    /// Disposes the runtime, waiting a bounded time for a reload in progress: a wedged reload must
    /// not hang process shutdown, and skipping the dispose at exit is safe (the OS reclaims it).
    /// </summary>
    public void DisposeRuntime()
    {
        var lockTaken = false;
        try
        {
            Monitor.TryEnter(_gate, _timings.ShutdownGateWait, ref lockTaken);
            if (lockTaken)
            {
                _runtime.Dispose();
                _logger.Info("Script runtime disposed.");
            }
            else
            {
                _logger.Warning("Shutdown: config reload still in progress; skipping script dispose (process is exiting).");
            }
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_gate);
            }
        }
    }

    private ReloadOutcome ReloadUnderGate(int generation)
    {
        try
        {
            _config.EnsureConfigFile();
            lock (_gate)
            {
                if (IsSuperseded(generation))
                {
                    _logger.Info($"Config reload superseded generation={generation}.");
                    return ReloadOutcome.SupersededReload;
                }

                var reloadStartedAt = Stopwatch.GetTimestamp();
                // Script-held keys (bare keyDown without keyUp) are not tracked resources: release
                // them so the previous config can never leave a key stuck down across reload.
                _input.ReleaseHeldKeys("Config reload");

                _runtime.ReloadFromFile(_config.ConfigFilePath);
                if (IsSuperseded(generation))
                {
                    // Abandoned while wedged (emergency stop) or superseded by a newer reload:
                    // tear down what was just installed instead of leaving a stale config behind.
                    _logger.Warning($"Config reload abandoned generation={generation}; tearing down superseded runtime.");
                    TearDownSupersededRuntime();
                    return ReloadOutcome.SupersededReload;
                }

                _logger.Info(
                    $"Script runtime reload completed generation={generation} elapsedMs={Stopwatch.GetElapsedTime(reloadStartedAt).TotalMilliseconds:F3}.");
                return ReloadOutcome.Succeeded;
            }
        }
        catch (Exception exception)
        {
            return ReloadOutcome.Failed(exception);
        }
    }

    private bool IsSuperseded(int generation) =>
        generation != Volatile.Read(ref _generation) || IsShutDown;

    private void TearDownSupersededRuntime()
    {
        try
        {
            _runtime.EmergencyStop();
        }
        catch (Exception exception)
        {
            _logger.Error("Superseded reload teardown failed.", exception);
        }
    }

    /// <summary>
    /// Counts the reload loader itself, so only toasts shown after it move the baseline. Barriers
    /// through the UI thread when off-thread: the loader toast is queued just before at the same
    /// priority, so it is always counted first. Exact when already on the UI thread.
    /// </summary>
    private long? CaptureStandardToastBaseline()
    {
        if (_ui.CheckAccess())
        {
            return _toasts.StandardContentShows;
        }

        try
        {
            return _ui.Invoke(
                () => _toasts.StandardContentShows,
                "reload toast baseline",
                DispatcherGuard.BaselineTimeout);
        }
        catch (TimeoutException exception)
        {
            _logger.Warning($"Reload toast baseline skipped; receipt will always show. {exception.Message}");
            return null;
        }
    }

    private void CompleteReload(int generation, Exception? failure, long startedAt, long? toastBaseline)
    {
        if (IsSuperseded(generation))
        {
            return;
        }

        if (failure is not null)
        {
            _logger.Error("Config reload failed.", failure);
            _toasts.Show(ConfigReloadAlerts.CreateReloadFailedAlert(failure));
            return;
        }

        if (toastBaseline.HasValue && _toasts.StandardContentShows != toastBaseline.Value)
        {
            _logger.Info($"Config reloaded toast skipped generation={generation}: a newer toast is already showing.");
            return;
        }

        _logger.Info(
            $"Config reloaded generation={generation} elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F3}. " +
            $"Console log: {_consoleLogPath()}");
        _toasts.Show(ConfigReloadAlerts.CreateReloadedAlert());
    }

    private readonly record struct ReloadOutcome(bool Superseded, Exception? Failure)
    {
        public static ReloadOutcome Succeeded => new(false, null);

        public static ReloadOutcome SupersededReload => new(true, null);

        public static ReloadOutcome Failed(Exception exception) => new(false, exception);
    }
}
