using HsWin.App.Lifecycle;
using HsWin.Core.Alerts;
using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;

namespace HsWin.App.Tests.Lifecycle;

public sealed class EmergencyStopControllerTests
{
    private const uint Escape = 0x1B;
    private const uint ChordFlags = (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);

    private readonly CapturingRuntimeLogger _logger = new();
    private readonly RecordingInputKillSwitch _input = new();
    private readonly FakeScriptRuntimeTeardown _runtime = new();
    private readonly RecordingAlertPresenter _toasts = new();
    private readonly FakeUiThread _ui = new() { OnUiThread = true };
    private readonly List<Action> _queuedWork = [];

    private EmergencyStopController CreateController(bool runInline = true) =>
        new(_logger, _input, _runtime, _toasts, _ui, work =>
        {
            if (runInline)
            {
                work();
            }
            else
            {
                _queuedWork.Add(work);
            }

            return true;
        });

    private static KeyboardEventSnapshot Key(
        uint keyCode = Escape,
        uint modifiers = ChordFlags,
        bool down = true,
        bool injected = false) =>
        new("key", keyCode, "escape", [], modifiers, IsKeyDown: down, IsKeyUp: !down, IsModifier: false, IsInjected: injected, IsExtended: false);

    [Fact]
    public void TheChordStopsInputTearsDownTheRuntimeAndSwallowsTheKey()
    {
        var controller = CreateController();

        var swallowed = controller.TryHandleKey(Key());

        Assert.True(swallowed);
        Assert.Equal(1, _runtime.Teardowns);
        Assert.Equal("Stopped", Assert.Single(_toasts.Shown).Text);
        Assert.Equal(AlertKind.Normal, _toasts.Shown[0].Kind);
        Assert.Contains("Emergency stop:stopRepeats", _input.Calls);
        Assert.Contains("Emergency stop:releaseKeys", _input.Calls);
    }

    [Theory]
    [InlineData(0x41u, ChordFlags, false)]
    [InlineData(Escape, (uint)(HotkeyModifiers.Control | HotkeyModifiers.Alt), false)]
    [InlineData(Escape, 0u, false)]
    [InlineData(Escape, ChordFlags, true)]
    public void OtherKeysAndInjectedInputAreIgnored(uint keyCode, uint modifiers, bool injected)
    {
        var controller = CreateController();

        var swallowed = controller.TryHandleKey(Key(keyCode, modifiers, injected: injected));

        Assert.False(swallowed);
        Assert.Equal(0, _runtime.Teardowns);
        Assert.Empty(_input.Calls);
    }

    [Fact]
    public void HoldingEscapeDoesNotRepeatTheStopUntilItIsReleased()
    {
        var controller = CreateController();

        controller.TryHandleKey(Key());
        var repeatSwallowed = controller.TryHandleKey(Key());
        Assert.True(repeatSwallowed);
        Assert.Equal(1, _runtime.Teardowns);

        Assert.False(controller.TryHandleKey(Key(down: false)));
        controller.TryHandleKey(Key());

        Assert.Equal(2, _runtime.Teardowns);
    }

    [Fact]
    public void ARequestDuringATeardownStopsInputAgainButQueuesNoSecondWorker()
    {
        var controller = CreateController(runInline: false);

        controller.Request("first");
        controller.Request("second");

        Assert.Single(_queuedWork);
        Assert.Equal(2, _input.Calls.Count(call => call == "Emergency stop:stopRepeats"));
        Assert.Contains(_logger.Infos, message => message.Contains("already in progress", StringComparison.Ordinal));

        _queuedWork[0]();
        controller.Request("third");
        Assert.Equal(2, _queuedWork.Count);
    }

    [Fact]
    public void AFailedTeardownIsReportedAndDoesNotBlockTheNextStop()
    {
        var controller = CreateController();
        _runtime.Failure = new InvalidOperationException("engine wedged");

        controller.Request("tray");

        var failure = Assert.Single(_toasts.Shown);
        Assert.Equal(AlertKind.Error, failure.Kind);
        Assert.Contains("engine wedged", failure.Text, StringComparison.Ordinal);

        _runtime.Failure = null;
        controller.Request("tray");
        Assert.Equal(2, _runtime.Teardowns);
    }

    [Fact]
    public void WorkerQueueRefusalRunsTheTeardownInline()
    {
        var controller = new EmergencyStopController(_logger, _input, _runtime, _toasts, _ui, _ => false);

        controller.Request("tray");

        Assert.Equal(1, _runtime.Teardowns);
        Assert.Contains(_logger.Warnings, warning => warning.Contains("running teardown inline", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkerQueueFailureStillAttemptsTheTeardownInline()
    {
        var controller = new EmergencyStopController(
            _logger, _input, _runtime, _toasts, _ui, _ => throw new InvalidOperationException("no threads"));

        controller.Request("tray");

        Assert.Equal(1, _runtime.Teardowns);
        Assert.Contains(_logger.Errors, error => error.Contains("failed to queue worker", StringComparison.Ordinal));
    }

    [Fact]
    public void AShutDownAppStopsInputButSkipsTheTeardownAndToast()
    {
        var controller = CreateController();
        _runtime.IsShutDown = true;

        controller.Request("tray");

        Assert.Equal(0, _runtime.Teardowns);
        Assert.Empty(_toasts.Shown);
        Assert.Contains("Emergency stop:stopRepeats", _input.Calls);
    }

    [Fact]
    public void ToastIsPostedToTheUiThreadWhenTheWorkerRunsElsewhere()
    {
        var controller = CreateController();
        _ui.OnUiThread = false;

        controller.Request("tray");

        Assert.Empty(_toasts.Shown);
        Assert.Equal(1, _ui.PostedCount);
        _ui.RunPosted();
        Assert.Equal("Stopped", Assert.Single(_toasts.Shown).Text);
    }

    [Fact]
    public void TheHotkeyDefinitionIsCtrlAltShiftEscape()
    {
        Assert.Equal(HotkeyInputKind.Keyboard, EmergencyStopController.Hotkey.InputKind);
        Assert.Equal(Escape, EmergencyStopController.Hotkey.VirtualKey);
        Assert.Equal(
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            EmergencyStopController.Hotkey.Modifiers);
    }
}
