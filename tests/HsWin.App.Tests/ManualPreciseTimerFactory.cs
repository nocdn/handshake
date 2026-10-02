using HsWin.App.Threading;

namespace HsWin.App.Tests;

/// <summary>A timer factory whose timers only tick when a test calls <see cref="ManualPreciseTimer.Fire"/>.</summary>
internal sealed class ManualPreciseTimerFactory : IPreciseTimerFactory
{
    private readonly List<ManualPreciseTimer> _timers = [];

    public IReadOnlyList<ManualPreciseTimer> Timers => _timers;

    /// <summary>The most recently created timer.</summary>
    public ManualPreciseTimer Timer => _timers[^1];

    public IPreciseTimer Create(Action tick)
    {
        var timer = new ManualPreciseTimer(tick);
        _timers.Add(timer);
        return timer;
    }
}

internal sealed class ManualPreciseTimer(Action tick) : IPreciseTimer
{
    public (int DueTimeMs, int PeriodMs) LastSchedule { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsDisposed => DisposeCount > 0;

    public void Change(int dueTimeMs, int periodMs)
    {
        LastSchedule = (dueTimeMs, periodMs);
    }

    public void Fire() => tick();

    public void Dispose() => DisposeCount++;
}
