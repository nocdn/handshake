using HsWin.App.Lifecycle;
using HsWin.Core.Input;
using HsWin.Core.Keyboard;
using HsWin.Core.Mouse;

namespace HsWin.App.Tests.Lifecycle;

public sealed class InputKillSwitchTests
{
    private readonly CapturingRuntimeLogger _logger = new();
    private readonly FakeMouseInput _mouse = new();
    private readonly FakeKeyboardInput _keyboard = new();

    private InputKillSwitch CreateSwitch() => new(_mouse, _keyboard, _logger);

    [Fact]
    public void StopRepeatsStopsBothDevices()
    {
        CreateSwitch().StopRepeats("Shutdown");

        Assert.Equal(1, _mouse.StopRepeats);
        Assert.Equal(1, _keyboard.StopRepeats);
    }

    [Fact]
    public void AFailingMouseStopDoesNotSkipTheKeyboardAndIsLoggedWithItsContext()
    {
        _mouse.StopFailure = new InvalidOperationException("mouse hook gone");

        CreateSwitch().StopRepeats("Emergency stop");

        Assert.Equal(1, _keyboard.StopRepeats);
        Assert.Contains(_logger.Errors, error => error.Contains("Emergency stop: mouse StopActiveRepeat failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseHeldKeysFailureIsLoggedNotThrown()
    {
        _keyboard.ReleaseFailure = new InvalidOperationException("sender busy");

        CreateSwitch().ReleaseHeldKeys("Config reload");

        Assert.Contains(_logger.Errors, error => error.Contains("Config reload: keyboard ReleaseHeldKeys failed", StringComparison.Ordinal));
    }

    private sealed class FakeMouseInput : IMouseInputService
    {
        public int StopRepeats { get; private set; }

        public Exception? StopFailure { get; set; }

        public void StopActiveRepeat()
        {
            StopRepeats++;
            if (StopFailure is not null)
            {
                throw StopFailure;
            }
        }

        public void Click(MouseButton button) => throw new NotSupportedException();

        public IRepeatSession Repeat(MouseButton button, MouseRepeatOptions options) => throw new NotSupportedException();
    }

    private sealed class FakeKeyboardInput : IKeyboardInputService
    {
        public int StopRepeats { get; private set; }

        public Exception? ReleaseFailure { get; set; }

        public void StopActiveRepeat() => StopRepeats++;

        public void ReleaseHeldKeys()
        {
            if (ReleaseFailure is not null)
            {
                throw ReleaseFailure;
            }
        }

        public void KeyDown(uint virtualKey) => throw new NotSupportedException();

        public void KeyUp(uint virtualKey) => throw new NotSupportedException();

        public void Tap(uint virtualKey, KeyboardTapOptions options) => throw new NotSupportedException();

        public IRepeatSession Repeat(uint virtualKey, KeyboardRepeatOptions options) => throw new NotSupportedException();
    }
}
