using HsWin.Core.Support;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

/// <summary>
/// Delivers events from a host source (HTTP, tasks, audio, clipboard, window focus) to one script
/// callback through the script scheduler. Disposing the handle cancels delivery before the source
/// is released, so events already queued for a stopped watch never reach the script. A final
/// delivery completes the relay and releases its handle, so finished work stops being tracked.
/// </summary>
internal sealed class ScriptCallbackRelay
{
    private readonly ScriptObject _function;
    private readonly IScriptCallbackScheduler _scheduler;
    private readonly ScriptCallbackInvoker _callbacks;
    private ScriptResourceHandle? _handle;
    private int _cancelled;
    private int _completed;

    public ScriptCallbackRelay(ScriptObject function, IScriptCallbackScheduler scheduler, ScriptCallbackInvoker callbacks)
    {
        _function = function;
        _scheduler = scheduler;
        _callbacks = callbacks;
    }

    public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;

    /// <summary>Queues one delivery; dropped if the handle is disposed before it runs.</summary>
    public void Post(params object?[] args)
    {
        if (IsCancelled)
        {
            return;
        }

        _scheduler.Schedule(() =>
        {
            if (!IsCancelled)
            {
                _callbacks.InvokeScriptCallback(_function, args);
            }
        });
    }

    /// <summary>Queues the last delivery, then releases the handle once the script has seen it.</summary>
    public void PostFinal(params object?[] args)
    {
        if (IsCancelled)
        {
            return;
        }

        _scheduler.Schedule(() =>
        {
            try
            {
                if (!IsCancelled)
                {
                    _callbacks.InvokeScriptCallback(_function, args);
                }
            }
            finally
            {
                Complete();
            }
        });
    }

    /// <summary>Marks the work finished without a delivery and releases the handle.</summary>
    public void Complete()
    {
        Volatile.Write(ref _completed, 1);
        Volatile.Read(ref _handle)?.Dispose();
    }

    /// <summary>Wraps the source so script disposal cancels delivery, then releases the source.</summary>
    public ScriptResourceHandle Attach(IDisposable source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return AttachHandle(new ScriptResourceHandle(new DisposableAction(() =>
        {
            Volatile.Write(ref _cancelled, 1);
            source.Dispose();
        })));
    }

    /// <summary>Uses a custom handle; its disposal must call <see cref="Cancel"/>.</summary>
    public T AttachHandle<T>(T handle) where T : ScriptResourceHandle
    {
        Volatile.Write(ref _handle, handle);
        if (Volatile.Read(ref _completed) != 0)
        {
            // The source finished synchronously, before its handle existed.
            handle.Dispose();
        }

        return handle;
    }

    public void Cancel()
    {
        Volatile.Write(ref _cancelled, 1);
    }
}
