using HsWin.Core.Logging;

namespace HsWin.App.Input;

/// <summary>
/// Holds the one global repeat a keyboard or mouse input service may run. Starting a new repeat
/// stops the previous one first, so two loops can never inject at once.
/// </summary>
internal sealed class ActiveRepeatSlot<TRepeat>(string kind, IRuntimeLogger logger)
    where TRepeat : class, IDisposable
{
    private readonly object _startGate = new();
    private readonly object _gate = new();
    private TRepeat? _active;

    /// <summary>Replaces the active repeat with <paramref name="create"/>'s result and starts it.</summary>
    public TRepeat Start(Func<TRepeat> create, Action<TRepeat> start)
    {
        lock (_startGate)
        {
            var previous = Take();
            if (previous is not null)
            {
                logger.Warning($"Replacing active {kind} repeat before starting a new repeat.");
                previous.Dispose();
            }

            var repeat = create();
            lock (_gate)
            {
                _active = repeat;
            }

            try
            {
                start(repeat);
                return repeat;
            }
            catch
            {
                Clear(repeat);
                repeat.Dispose();
                throw;
            }
        }
    }

    public void StopActive()
    {
        var active = Take();
        if (active is null)
        {
            return;
        }

        logger.Info($"Stopping active {kind} repeat via StopActiveRepeat().");
        active.Dispose();
    }

    /// <summary>Forgets <paramref name="repeat"/> if it is still the active one; called when it ends.</summary>
    public void Clear(TRepeat repeat)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_active, repeat))
            {
                _active = null;
            }
        }
    }

    private TRepeat? Take()
    {
        lock (_gate)
        {
            var active = _active;
            _active = null;
            return active;
        }
    }
}
