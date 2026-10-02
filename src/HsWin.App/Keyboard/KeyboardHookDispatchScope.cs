namespace HsWin.App.Keyboard;

/// <summary>
/// Per-dispatch scope that lets input triggered synchronously by a blocking keyboard watcher
/// (for example a remap's injected key) be deferred until after the hook callback returns.
/// Entered once per keystroke on the dedicated WH_KEYBOARD_LL thread; deferral is rare, so the
/// scope avoids allocating until a caller actually defers. The scope is tracked with a
/// thread-static field rather than AsyncLocal because every dispatch runs on that single hook
/// thread and ExecutionContext churn is measurable at keystroke rate. Nothing here logs: it runs
/// for every keystroke and must not record what the user types.
/// </summary>
internal sealed class KeyboardHookDispatchScope : IDisposable
{
    [ThreadStatic]
    private static KeyboardHookDispatchScope? currentScope;

    private readonly SerializedInputQueue _queue;
    private readonly KeyboardHookDispatchScope? _previousScope;
    private List<Action>? _deferredActions;
    private bool _disposed;

    private KeyboardHookDispatchScope(SerializedInputQueue queue)
    {
        _queue = queue;
        _previousScope = currentScope;
        currentScope = this;
    }

    public static int CurrentDeferredActionCount => currentScope?._deferredActions?.Count ?? 0;

    public static IDisposable Enter(SerializedInputQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return new KeyboardHookDispatchScope(queue);
    }

    /// <summary>
    /// Queues <paramref name="action"/> to run after the current hook dispatch, in keystroke order.
    /// Returns false (run it now) when the caller is not inside a dispatch on this thread.
    /// </summary>
    public static bool TryDefer(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var scope = currentScope;
        if (scope is null || scope._disposed)
        {
            return false;
        }

        scope._deferredActions ??= [];
        scope._deferredActions.Add(action);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        currentScope = _previousScope;
        if (_deferredActions is { Count: > 0 })
        {
            _queue.Enqueue([.. _deferredActions]);
        }
    }
}
