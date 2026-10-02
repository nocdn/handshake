using HsWin.Core.Scripting;

namespace HsWin.App.Threading;

/// <summary>
/// Moves hook-thread work onto the given context (normally the UI dispatcher), or the thread pool
/// when there is none, so low-level hooks never wait for script callbacks.
/// </summary>
internal sealed class SynchronizationContextCallbackScheduler(SynchronizationContext? context) : IScriptCallbackScheduler
{
    public void Schedule(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (context is null)
        {
            ThreadPool.QueueUserWorkItem(static state => ((Action)state!).Invoke(), callback);
            return;
        }

        context.Post(static state => ((Action)state!).Invoke(), callback);
    }
}
