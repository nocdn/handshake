using HsWin.App.Lifecycle;
using HsWin.App.Threading;
using HsWin.Core.Alerts;
using HsWin.Core.Scripting;

namespace HsWin.App.Tests.Lifecycle;

/// <summary>A UI thread that runs posted work only when the test says so.</summary>
internal sealed class FakeUiThread : IUiThread
{
    private readonly Queue<Action> _posted = new();
    private readonly object _gate = new();

    public bool OnUiThread { get; set; }

    public int PostedCount
    {
        get
        {
            lock (_gate)
            {
                return _posted.Count;
            }
        }
    }

    public bool CheckAccess() => OnUiThread;

    public void Post(Action action)
    {
        lock (_gate)
        {
            _posted.Enqueue(action);
        }
    }

    public T Invoke<T>(Func<T> action, string description, TimeSpan timeout) => action();

    public void RunPosted()
    {
        while (true)
        {
            Action action;
            lock (_gate)
            {
                if (_posted.Count == 0)
                {
                    return;
                }

                action = _posted.Dequeue();
            }

            action();
        }
    }
}

internal sealed class RecordingAlertPresenter : ICountingAlertPresenter
{
    private readonly object _gate = new();
    private readonly List<AlertRequest> _shown = [];

    public long StandardContentShows { get; set; }

    public IReadOnlyList<AlertRequest> Shown
    {
        get
        {
            lock (_gate)
            {
                return [.. _shown];
            }
        }
    }

    public IReadOnlyList<string> Texts => [.. Shown.Select(request => request.Text)];

    public void Show(AlertRequest request)
    {
        lock (_gate)
        {
            _shown.Add(request);
        }
    }
}

/// <summary>Counts calls; the first <c>ReloadFromFile</c> can be held open to model a wedged reload.</summary>
internal sealed class FakeScriptRuntime : IScriptRuntimeLifecycle
{
    private int _reloads;
    private int _emergencyStops;
    private int _interrupts;
    private int _disposals;

    public ManualResetEventSlim FirstReloadEntered { get; } = new(false);

    public ManualResetEventSlim ReleaseFirstReload { get; } = new(true);

    public Exception? ReloadFailure { get; set; }

    public Exception? EmergencyStopFailure { get; set; }

    public string? LastReloadedPath { get; private set; }

    public int Reloads => Volatile.Read(ref _reloads);

    public int EmergencyStops => Volatile.Read(ref _emergencyStops);

    public int Interrupts => Volatile.Read(ref _interrupts);

    public int Disposals => Volatile.Read(ref _disposals);

    public void ReloadFromFile(string filePath)
    {
        LastReloadedPath = filePath;
        if (Interlocked.Increment(ref _reloads) == 1)
        {
            FirstReloadEntered.Set();
            Assert.True(ReleaseFirstReload.Wait(TimeSpan.FromSeconds(10)), "The held reload was never released.");
        }

        if (ReloadFailure is not null)
        {
            throw ReloadFailure;
        }
    }

    public void InterruptEngineOnly() => Interlocked.Increment(ref _interrupts);

    public void EmergencyStop()
    {
        Interlocked.Increment(ref _emergencyStops);
        if (EmergencyStopFailure is not null)
        {
            throw EmergencyStopFailure;
        }
    }

    public void Dispose() => Interlocked.Increment(ref _disposals);
}

internal sealed class RecordingInputKillSwitch : IInputKillSwitch
{
    private readonly object _gate = new();
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public void StopRepeats(string context) => Record($"{context}:stopRepeats");

    public void ReleaseHeldKeys(string context) => Record($"{context}:releaseKeys");

    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}

internal sealed class FakeScriptRuntimeTeardown : IScriptRuntimeTeardown
{
    public bool IsShutDown { get; set; }

    public Exception? Failure { get; set; }

    public int Teardowns { get; private set; }

    public void StopRuntimeForEmergency()
    {
        Teardowns++;
        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
