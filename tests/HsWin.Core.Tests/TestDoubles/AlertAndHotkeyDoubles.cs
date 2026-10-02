using HsWin.Core.Alerts;
using HsWin.Core.Hotkeys;

namespace HsWin.Core.Tests;

internal sealed class CapturingAlertPresenter : IAlertPresenter
{
    public List<AlertRequest> Requests { get; } = [];

    public void Show(AlertRequest request)
    {
        Requests.Add(request);
    }
}

internal sealed class CapturingHotkeyRegistrar : IHotkeyRegistrar
{
    public List<CapturingRegistration> Registrations { get; } = [];

    public List<CapturingHeldRegistration> HeldRegistrations { get; } = [];

    public IDisposable Register(HotkeyDefinition hotkey, Action pressed)
    {
        var registration = new CapturingRegistration(hotkey, pressed);
        Registrations.Add(registration);
        return registration;
    }

    public IDisposable RegisterHeld(HotkeyDefinition hotkey, Action pressed, Action released, bool blocking)
    {
        var registration = new CapturingHeldRegistration(hotkey, pressed, released, blocking);
        HeldRegistrations.Add(registration);
        return registration;
    }

    public void TriggerOnlyRegistration()
    {
        Assert.Single(Registrations).Trigger();
    }
}

internal sealed class CapturingRegistration : IDisposable
{
    private readonly Action _pressed;

    public CapturingRegistration(HotkeyDefinition hotkey, Action pressed)
    {
        Hotkey = hotkey;
        _pressed = pressed;
    }

    public HotkeyDefinition Hotkey { get; }

    public bool IsDisposed { get; private set; }

    public void Trigger()
    {
        _pressed();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

internal sealed class CapturingHeldRegistration : IDisposable
{
    private readonly Action _pressed;
    private readonly Action _released;

    public CapturingHeldRegistration(HotkeyDefinition hotkey, Action pressed, Action released, bool blocking)
    {
        Hotkey = hotkey;
        _pressed = pressed;
        _released = released;
        Blocking = blocking;
    }

    public HotkeyDefinition Hotkey { get; }

    public bool Blocking { get; }

    public bool IsDisposed { get; private set; }

    public void TriggerPressed()
    {
        _pressed();
    }

    public void TriggerReleased()
    {
        _released();
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

internal sealed class CapturingDisposable : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
