using HsWin.Core.Logging;
using HsWin.Core.Mouse;
using HsWin.Core.Scripting;

namespace HsWin.App.Mouse;

/// <summary>Native press/release decisions; scripts receive ordered, bounded, off-hook observations.</summary>
internal sealed class MouseButtonWatchDispatcher(IRuntimeLogger logger, IScriptCallbackScheduler scheduler, Action changed)
{
    private readonly object _gate = new();
    private readonly List<MouseButtonWatchSubscription> _subscriptions = [];
    // A swallowed release must drain even after its watch is stopped. Synthetic clicks must not
    // release a physical press, and a watch enabled mid-drag must preserve the original release.
    private readonly Dictionary<(MouseButton Button, bool Injected), bool> _presses = [];

    public bool NeedsHook
    {
        get { lock (_gate) return _subscriptions.Count != 0 || _presses.Values.Any(swallowed => swallowed); }
    }

    public IDisposable Watch(MouseButtonWatchOptions options, Action<MouseButtonEventSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);
        var subscription = new MouseButtonWatchSubscription(options, callback, Remove);
        lock (_gate) _subscriptions.Add(subscription);
        return subscription;
    }

    public bool Dispatch(MouseButtonEventSnapshot input)
    {
        MouseButtonWatchSubscription[] subscriptions;
        bool swallowed;
        lock (_gate)
        {
            subscriptions = _subscriptions.Where(watch => !input.IsInjected || watch.Options.IncludeInjected).ToArray();
            var key = (input.Button, input.IsInjected);
            if (input.IsDown)
            {
                if (!_presses.TryGetValue(key, out swallowed))
                {
                    swallowed = subscriptions.Any(watch => watch.Options.Blocking);
                    _presses[key] = swallowed;
                }
            }
            else
            {
                swallowed = _presses.Remove(key, out var blockedPress) && blockedPress;
            }
        }

        foreach (var subscription in subscriptions) subscription.Enqueue(input, scheduler, logger);
        if (!input.IsDown && swallowed) changed();
        return swallowed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var subscription in _subscriptions) subscription.Deactivate();
            _subscriptions.Clear();
            _presses.Clear();
        }
    }

    private void Remove(MouseButtonWatchSubscription subscription)
    {
        lock (_gate) _subscriptions.Remove(subscription);
        changed();
    }
}
