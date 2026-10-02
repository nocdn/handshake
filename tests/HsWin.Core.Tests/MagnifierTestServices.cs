using HsWin.Core.Keyboard;
using HsWin.Core.Magnifier;
using HsWin.Core.Mouse;

namespace HsWin.Core.Tests;

internal sealed class CapturingMagnifierService : IMagnifierService
{
    public List<CapturingMagnifierSession> Sessions { get; } = [];
    public IMagnifierSession Create(MagnifierOptions options)
    {
        var session = new CapturingMagnifierSession(options);
        Sessions.Add(session);
        return session;
    }
}

internal sealed class CapturingMagnifierSession(MagnifierOptions options) : IMagnifierSession
{
    public MagnifierOptions Options { get; } = options;
    public double Zoom { get; private set; } = options.Zoom;
    public bool IsVisible { get; private set; }
    public int Shows { get; private set; }
    public int Hides { get; private set; }
    public int Disposals { get; private set; }
    public void Show() { Shows++; IsVisible = true; }
    public void Hide() { Hides++; IsVisible = false; }
    public void SetZoom(double zoom) => Zoom = zoom;
    public void Dispose() { Disposals++; IsVisible = false; }
}

internal sealed class MagnifierTestKeyboard : IKeyboardEventService
{
    public KeyboardEventWatchOptions? Options { get; private set; }
    public Func<KeyboardEventSnapshot, bool>? Callback { get; private set; }
    public bool Disposed { get; private set; }
    public bool FailRegistration { get; init; }
    public IDisposable Watch(KeyboardEventWatchOptions options, Func<KeyboardEventSnapshot, bool> callback)
    {
        if (FailRegistration) throw new InvalidOperationException("Registration failed");
        Options = options;
        Callback = callback;
        return new Subscription(() => Disposed = true);
    }
    public bool IsKeyDown(uint virtualKey) => false;
    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

internal sealed class MagnifierTestMouse : IMouseButtonEventService
{
    public Action<PhysicalMouseButtonEvent>? Callback { get; private set; }
    public bool Disposed { get; private set; }
    public bool FailRegistration { get; init; }
    public IDisposable WatchPhysicalButtons(Action<PhysicalMouseButtonEvent> callback)
    {
        if (FailRegistration) throw new InvalidOperationException("Mouse registration failed");
        Callback = callback;
        return new Subscription(() => Disposed = true);
    }
    private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
