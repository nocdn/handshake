using HsWin.Core.Logging;

namespace HsWin.Core.Scripting;

/// <summary>
/// Owns every host resource one config generation creates: hotkeys, watches, timers, repeats,
/// requests and lenses, including ones created later from inside callbacks. Disposing the scope
/// ends the generation, and anything a stale callback creates afterwards is disposed immediately,
/// so reload and emergency stop always leave nothing of the old config running.
/// </summary>
internal sealed class ScriptResourceScope : IDisposable
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _resources = [];
    private readonly IRuntimeLogger _logger;
    private bool _closed;

    public ScriptResourceScope(IRuntimeLogger logger)
    {
        _logger = logger;
    }

    /// <summary>The number of live resources; finished or script-disposed handles are removed.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _resources.Count;
            }
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    public void Track(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            if (!_closed)
            {
                _resources.Add(resource);
                if (resource is ScriptResourceHandle handle)
                {
                    handle.Disposed += Untrack;
                    if (handle.IsDisposed)
                    {
                        // Finished (or was stopped) before it could be tracked.
                        _resources.Remove(handle);
                    }
                }

                return;
            }
        }

        // A callback from an ended generation created this; it must not outlive its config.
        DisposeQuietly(resource);
    }

    public void Dispose()
    {
        IDisposable[] resources;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            resources = [.. _resources];
            _resources.Clear();
        }

        for (var index = resources.Length - 1; index >= 0; index--)
        {
            DisposeQuietly(resources[index]);
        }
    }

    private void Untrack(ScriptResourceHandle handle)
    {
        lock (_gate)
        {
            _resources.Remove(handle);
        }
    }

    private void DisposeQuietly(IDisposable resource)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            // One failing resource must not keep the rest of the generation alive.
            _logger.Warning($"Script resource {resource.GetType().Name} failed to dispose. {exception.Message}");
        }
    }
}
