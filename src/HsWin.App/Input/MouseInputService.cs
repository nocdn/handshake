using System.Diagnostics;
using HsWin.App.Keyboard;
using HsWin.App.Threading;
using HsWin.Core.Input;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Input;

internal sealed class MouseInputService : IMouseInputService
{
    private readonly IRuntimeLogger _logger;
    private readonly IMouseInputSender _inputSender;
    private readonly IPreciseTimerFactory _timers;
    private readonly ActiveRepeatSlot<MouseRepeatHandle> _repeat;

    public MouseInputService(IRuntimeLogger logger)
        : this(logger, NativeMouseInputSender.Instance, PreciseTimerScheduler.Shared)
    {
    }

    internal MouseInputService(IRuntimeLogger logger, IMouseInputSender inputSender, IPreciseTimerFactory? timers = null)
    {
        _logger = logger;
        _inputSender = inputSender;
        _timers = timers ?? PreciseTimerScheduler.Shared;
        _repeat = new ActiveRepeatSlot<MouseRepeatHandle>("mouse", logger);
    }

    public void Click(MouseButton button)
    {
        _inputSender.SendClick(button, InputMethod.SendInput, _logger);
    }

    public void Scroll(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        // Like keyboard injection, wheel input must wait for a blocking keyboard hook to return.
        if (!KeyboardHookDispatchScope.TryDefer(() => _inputSender.SendScroll(delta, _logger)))
        {
            _inputSender.SendScroll(delta, _logger);
        }
    }

    public IRepeatSession Repeat(MouseButton button, MouseRepeatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RepeatTiming.ValidateIntervalMs(options.IntervalMs, nameof(options));

        return _repeat.Start(
            () => new MouseRepeatHandle(button, options, _logger, _inputSender, _timers, _repeat.Clear),
            repeat => repeat.Start());
    }

    public void StopActiveRepeat() => _repeat.StopActive();

    private sealed class MouseRepeatHandle : IRepeatSession
    {
        private const int ProgressLogEveryClicks = 50;

        private readonly MouseButton _button;
        private readonly InputMethod _inputMethod;
        private readonly IRuntimeLogger _logger;
        private readonly IMouseInputSender _inputSender;
        private readonly Action<MouseRepeatHandle> _clearActiveRepeat;
        private readonly object _gate = new();
        private readonly Stopwatch _stopwatch = new();
        private readonly IPreciseTimer _timer;

        private int _intervalMs;
        private bool _disposed;
        private long _clickCount;
        private long _lastLoggedClickCount;

        public MouseRepeatHandle(
            MouseButton button,
            MouseRepeatOptions options,
            IRuntimeLogger logger,
            IMouseInputSender inputSender,
            IPreciseTimerFactory timers,
            Action<MouseRepeatHandle> clearActiveRepeat)
        {
            _button = button;
            _intervalMs = options.IntervalMs;
            _inputMethod = options.InputMethod;
            _logger = logger;
            _inputSender = inputSender;
            _clearActiveRepeat = clearActiveRepeat;
            _timer = timers.Create(Tick);
        }

        public int IntervalMs
        {
            get
            {
                lock (_gate)
                {
                    return _intervalMs;
                }
            }
        }

        private string ButtonName => MouseButtonParser.GetDisplayName(_button);

        public void Start()
        {
            lock (_gate)
            {
                _stopwatch.Start();
                _logger.Info(
                    $"Mouse repeat started button={ButtonName} intervalMs={_intervalMs} " +
                    $"inputMethod={InputMethodParser.GetDisplayName(_inputMethod)}.");
                _inputSender.SendClick(_button, _inputMethod, _logger);
                _clickCount++;
                _timer.Change(_intervalMs, _intervalMs);
            }
        }

        public void SetIntervalMs(int intervalMs)
        {
            RepeatTiming.ValidateIntervalMs(intervalMs, nameof(intervalMs));
            lock (_gate)
            {
                if (_disposed || _intervalMs == intervalMs)
                {
                    return;
                }

                _intervalMs = intervalMs;
                _timer.Change(intervalMs, intervalMs);
                _logger.Info($"Mouse repeat interval changed button={ButtonName} intervalMs={intervalMs}.");
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Dispose();
                _stopwatch.Stop();
                LogStop();
                _clearActiveRepeat(this);
            }
        }

        private void Tick()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _inputSender.SendClick(_button, _inputMethod, _logger);
                    _clickCount++;
                    LogProgressIfNeeded();
                }
                catch (Exception exception)
                {
                    _logger.Error($"Mouse repeat tick failed button={ButtonName} click={_clickCount}.", exception);
                    Dispose();
                }
            }
        }

        private void LogProgressIfNeeded()
        {
            if (_clickCount - _lastLoggedClickCount < ProgressLogEveryClicks)
            {
                return;
            }

            _lastLoggedClickCount = _clickCount;
            var elapsedMs = Math.Max(1, _stopwatch.Elapsed.TotalMilliseconds);
            _logger.Info(
                $"Mouse repeat progress button={ButtonName} clicks={_clickCount} " +
                $"elapsedMs={elapsedMs:F0} effectiveIntervalMs={elapsedMs / _clickCount:F2}.");
        }

        private void LogStop()
        {
            var elapsedMs = Math.Max(1, _stopwatch.Elapsed.TotalMilliseconds);
            _logger.Info(
                $"Mouse repeat stopped button={ButtonName} clicks={_clickCount} elapsedMs={elapsedMs:F0} " +
                $"requestedIntervalMs={_intervalMs} effectiveIntervalMs={elapsedMs / Math.Max(1, _clickCount):F2}.");
        }
    }

    private sealed class NativeMouseInputSender : IMouseInputSender
    {
        public static NativeMouseInputSender Instance { get; } = new();

        private NativeMouseInputSender()
        {
        }

        public void SendScroll(int delta, IRuntimeLogger? logger = null)
        {
            MouseInputSender.SendScroll(delta, logger);
        }

        public void SendClick(MouseButton button, InputMethod inputMethod, IRuntimeLogger? logger = null)
        {
            if (inputMethod == InputMethod.WindowMessage)
            {
                WindowMessageMouseInputSender.SendClick(button, logger);
                return;
            }

            MouseInputSender.SendClick(button, logger);
        }
    }
}
