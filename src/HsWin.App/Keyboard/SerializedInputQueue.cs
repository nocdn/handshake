using System.Collections.Concurrent;
using HsWin.Core.Logging;

namespace HsWin.App.Keyboard;

/// <summary>
/// Runs input deferred out of the keyboard hook on one dedicated thread, in the order the
/// keystrokes arrived. Pool work items could run two quick remapped keys in either order.
/// </summary>
internal sealed class SerializedInputQueue : IDisposable
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly BlockingCollection<Action[]> _batches = new();
    private readonly IRuntimeLogger _logger;
    private readonly Thread _worker;
    private int _disposed;

    public SerializedInputQueue(IRuntimeLogger logger)
    {
        _logger = logger;
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "HsWin Deferred Input"
        };
        _worker.Start();
    }

    public void Enqueue(Action[] batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!_batches.TryAdd(batch))
        {
            _logger.Warning($"Dropped {batch.Length} deferred keyboard input action(s) after shutdown.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Drain rather than drop: queued key-ups must still reach the OS.
        _batches.CompleteAdding();
        if (Thread.CurrentThread != _worker && !_worker.Join(DrainTimeout))
        {
            _logger.Warning("Deferred keyboard input did not drain before shutdown.");
        }
    }

    private void Run()
    {
        foreach (var batch in _batches.GetConsumingEnumerable())
        {
            foreach (var action in batch)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    _logger.Error("Deferred keyboard input failed.", exception);
                }
            }
        }

        _batches.Dispose();
    }
}
