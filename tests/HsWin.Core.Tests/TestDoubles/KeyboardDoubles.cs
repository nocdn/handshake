using HsWin.Core.Input;
using HsWin.Core.Keyboard;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

internal sealed class CapturingKeyboardEventService : IKeyboardEventService
{
    public List<CapturingKeyboardWatch> Watches { get; } = [];

    public IDisposable Watch(KeyboardEventWatchOptions options, Func<KeyboardEventSnapshot, bool> callback)
    {
        var registration = new CapturingDisposable();
        Watches.Add(new CapturingKeyboardWatch(options, callback, registration));
        return registration;
    }

    public bool IsKeyDown(uint virtualKey)
    {
        return virtualKey == (uint)'W';
    }
}

internal sealed record CapturingKeyboardWatch(
    KeyboardEventWatchOptions Options,
    Func<KeyboardEventSnapshot, bool> Callback,
    CapturingDisposable Registration);

internal sealed class CapturingKeyboardInputService : IKeyboardInputService
{
    public List<CapturingTap> Taps { get; } = [];

    public List<CapturingRepeat> Repeats { get; } = [];

    public List<uint> KeyDowns { get; } = [];

    public List<uint> KeyUps { get; } = [];

    public void KeyDown(uint virtualKey)
    {
        KeyDowns.Add(virtualKey);
    }

    public void KeyUp(uint virtualKey)
    {
        KeyUps.Add(virtualKey);
    }

    public void Tap(uint virtualKey, KeyboardTapOptions options)
    {
        Taps.Add(new CapturingTap(virtualKey, options));
    }

    public IRepeatSession Repeat(uint virtualKey, KeyboardRepeatOptions options)
    {
        var registration = new CapturingRepeatSession(options.IntervalMs);
        Repeats.Add(new CapturingRepeat(virtualKey, options, registration));
        return registration;
    }

    public void StopActiveRepeat()
    {
    }

    public void ReleaseHeldKeys()
    {
    }
}

internal sealed record CapturingTap(uint VirtualKey, KeyboardTapOptions Options);

internal sealed record CapturingRepeat(
    uint VirtualKey,
    KeyboardRepeatOptions Options,
    CapturingRepeatSession Registration);
