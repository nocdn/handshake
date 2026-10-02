using HsWin.Core.Hotkeys;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace HsWin.App.Hotkeys;

internal sealed partial class NativeHotkeyService : IHotkeyRegistrar, IDisposable
{
    private const int MaximumApplicationHotkeyId = 0xBFFF;
    private const int FirstHotkeyId = 1;
    private const int WmHotkey = 0x0312;

    /// <summary><c>MOD_NOREPEAT</c>: a held hotkey fires once, not on every key repeat.</summary>
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly MessageWindow _window;
    private readonly IRuntimeLogger _logger;
    private readonly NativeMouseHotkeyHook _mouseHotkeys;
    private readonly bool _ownsMouseHotkeys;
    private readonly IHotkeyThreadInvoker _threadInvoker;
    private readonly IHotkeyPlatform _platform;
    private readonly Dictionary<int, Action> _callbacks = [];
    private readonly object _callbackGate = new();

    // IDs still registered with Windows. Owner thread only: native register and unregister both
    // run there, and an ID is reusable only once its native unregister has completed.
    private readonly HashSet<int> _nativeIds = [];
    private int _nextId = FirstHotkeyId;
    private bool _disposed;

    public NativeHotkeyService()
        : this(NullRuntimeLogger.Instance)
    {
    }

    public NativeHotkeyService(IRuntimeLogger logger)
        : this(logger, new DispatcherHotkeyThreadInvoker(Dispatcher.CurrentDispatcher), NativeHotkeyPlatform.Instance)
    {
    }

    /// <summary>
    /// Registers button hotkeys on <paramref name="mouseHotkeys"/>, the shared WH_MOUSE_LL host the
    /// caller also hands to the scroll-watch service. The caller keeps ownership and disposes it.
    /// </summary>
    public NativeHotkeyService(IRuntimeLogger logger, NativeMouseHotkeyHook mouseHotkeys)
        : this(
            logger,
            new DispatcherHotkeyThreadInvoker(Dispatcher.CurrentDispatcher),
            NativeHotkeyPlatform.Instance,
            mouseHotkeys)
    {
    }

    /// <summary>Creates, and later disposes, its own mouse hook.</summary>
    internal NativeHotkeyService(
        IRuntimeLogger logger,
        IHotkeyThreadInvoker threadInvoker,
        IHotkeyPlatform platform)
        : this(logger, threadInvoker, platform, new NativeMouseHotkeyHook(logger), ownsMouseHotkeys: true)
    {
    }

    internal NativeHotkeyService(
        IRuntimeLogger logger,
        IHotkeyThreadInvoker threadInvoker,
        IHotkeyPlatform platform,
        NativeMouseHotkeyHook mouseHotkeys,
        bool ownsMouseHotkeys = false)
    {
        ArgumentNullException.ThrowIfNull(mouseHotkeys);
        _logger = logger;
        _threadInvoker = threadInvoker;
        _platform = platform;
        _mouseHotkeys = mouseHotkeys;
        _ownsMouseHotkeys = ownsMouseHotkeys;
        _window = new MessageWindow(DispatchHotkey);
        _logger.Info($"Hotkey message window created. HWND=0x{_window.Handle.ToInt64():X}");
    }

    public IDisposable Register(HotkeyDefinition hotkey, Action pressed)
    {
        if (!_threadInvoker.CheckAccess())
        {
            return _threadInvoker.Invoke(() => RegisterOnOwnerThread(hotkey, pressed));
        }

        return RegisterOnOwnerThread(hotkey, pressed);
    }

    public IDisposable RegisterHeld(HotkeyDefinition hotkey, Action pressed, Action released, bool blocking)
    {
        if (!_threadInvoker.CheckAccess())
        {
            return _threadInvoker.Invoke(() => RegisterHeldOnOwnerThread(hotkey, pressed, released, blocking));
        }

        return RegisterHeldOnOwnerThread(hotkey, pressed, released, blocking);
    }

    public void Dispose()
    {
        if (!_threadInvoker.CheckAccess())
        {
            // Teardown must never block a gate-holding reload thread on a wedged
            // owner thread; disposal is idempotent when it runs late.
            _threadInvoker.BeginInvoke(DisposeOnOwnerThread);
            return;
        }

        DisposeOnOwnerThread();
    }

    private IDisposable RegisterOnOwnerThread(HotkeyDefinition hotkey, Action pressed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(pressed);

        if (hotkey.InputKind == HotkeyInputKind.MouseButton)
        {
            return _mouseHotkeys.Register(hotkey, pressed);
        }

        var id = AllocateId();
        var modifierFlags = (uint)hotkey.Modifiers | ModNoRepeat;
        if (!_platform.RegisterHotKey(_window.Handle, id, modifierFlags, hotkey.VirtualKey, out var errorCode))
        {
            var message = CreateRegistrationFailureMessage(errorCode, hotkey);
            var exception = new Win32Exception(errorCode, message);
            _logger.Error($"Hotkey registration failed for id={id}.", exception);
            throw exception;
        }

        _nativeIds.Add(id);
        lock (_callbackGate)
        {
            _callbacks[id] = pressed;
        }

        _logger.Info($"Hotkey registered id={id} {hotkey}.");
        return new HotkeyRegistration(this, id);
    }

    private IDisposable RegisterHeldOnOwnerThread(
        HotkeyDefinition hotkey,
        Action pressed,
        Action released,
        bool blocking)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(pressed);
        ArgumentNullException.ThrowIfNull(released);

        if (hotkey.InputKind != HotkeyInputKind.MouseButton)
        {
            throw new ArgumentException("Held hotkey registration requires a mouse-button definition.", nameof(hotkey));
        }

        return _mouseHotkeys.RegisterHeld(hotkey, pressed, released, blocking);
    }

    private void DisposeOnOwnerThread()
    {
        if (_disposed)
        {
            return;
        }

        int[] ids;
        lock (_callbackGate)
        {
            ids = [.. _callbacks.Keys];
        }

        foreach (var id in ids)
        {
            Unregister(id);
        }

        if (_ownsMouseHotkeys)
        {
            _mouseHotkeys.Dispose();
            _logger.Info("Mouse hotkey service disposed.");
        }

        _window.DestroyHandle();
        _logger.Info("Hotkey message window destroyed.");
        _disposed = true;
    }

    /// <summary>
    /// Hands out IDs round-robin, skipping ones still registered, so reloads never exhaust the
    /// range and a just-freed ID is not reused while a stale WM_HOTKEY for it may be queued.
    /// </summary>
    private int AllocateId()
    {
        for (var attempt = FirstHotkeyId; attempt <= MaximumApplicationHotkeyId; attempt++)
        {
            var candidate = _nextId;
            _nextId = candidate == MaximumApplicationHotkeyId ? FirstHotkeyId : candidate + 1;
            if (!_nativeIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No hotkey registration identifiers are available.");
    }

    internal void DispatchHotkey(int id)
    {
        Action? callback;
        lock (_callbackGate)
        {
            _callbacks.TryGetValue(id, out callback);
        }

        // An unknown ID is a press that raced its own unregistration; nothing to run.
        callback?.Invoke();
    }

    private void Unregister(int id)
    {
        bool removed;
        lock (_callbackGate)
        {
            removed = _callbacks.Remove(id);
        }

        if (!removed)
        {
            return;
        }

        if (!_threadInvoker.CheckAccess())
        {
            // Unregister runs during reload disposal while holding the script gate,
            // so the native call must not wait on a wedged owner thread. The script
            // callback is already gone, so a late native unregister is harmless.
            _threadInvoker.BeginInvoke(() => UnregisterNative(id));
            return;
        }

        UnregisterNative(id);
    }

    private void UnregisterNative(int id)
    {
        _nativeIds.Remove(id);
        if (_platform.UnregisterHotKey(_window.Handle, id, out var errorCode))
        {
            return;
        }

        var exception = new Win32Exception(errorCode, $"Could not unregister hotkey id={id}.");
        _logger.Error($"Hotkey unregister failed id={id}.", exception);
    }

    internal static string CreateRegistrationFailureMessage(int errorCode, HotkeyDefinition hotkey)
    {
        return errorCode == ErrorHotkeyAlreadyRegistered
            ? $"Hotkey already in use: {hotkey}."
            : $"Could not register hotkey {hotkey}.";
    }

    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<int> _dispatchHotkey;

        public MessageWindow(Action<int> dispatchHotkey)
        {
            _dispatchHotkey = dispatchHotkey;
            CreateHandle(new CreateParams
            {
                Caption = $"{AppBranding.DisplayName} Hotkey Message Window",
                Parent = HwndMessage
            });
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                _dispatchHotkey(message.WParam.ToInt32());
                return;
            }

            base.WndProc(ref message);
        }
    }

    private sealed class HotkeyRegistration : IDisposable
    {
        private readonly NativeHotkeyService _owner;
        private readonly int _id;
        private bool _disposed;

        public HotkeyRegistration(NativeHotkeyService owner, int id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner.Unregister(_id);
            _disposed = true;
        }
    }

    internal interface IHotkeyThreadInvoker
    {
        bool CheckAccess();

        T Invoke<T>(Func<T> callback);

        void Invoke(Action callback);

        void BeginInvoke(Action callback);
    }

    private sealed class DispatcherHotkeyThreadInvoker : IHotkeyThreadInvoker
    {
        private readonly Dispatcher _dispatcher;

        public DispatcherHotkeyThreadInvoker(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public bool CheckAccess()
        {
            return _dispatcher.CheckAccess();
        }

        public T Invoke<T>(Func<T> callback)
        {
            // Register runs during config evaluation while holding the script gate:
            // fail fast instead of deadlocking on a wedged owner thread.
            return Threading.DispatcherGuard.InvokeWithTimeout(
                _dispatcher,
                callback,
                "hotkey registration",
                Threading.DispatcherGuard.MarshalTimeout);
        }

        public void Invoke(Action callback)
        {
            Threading.DispatcherGuard.InvokeWithTimeout<object?>(
                _dispatcher,
                () =>
                {
                    callback();
                    return null;
                },
                "hotkey registration",
                Threading.DispatcherGuard.MarshalTimeout);
        }

        public void BeginInvoke(Action callback)
        {
            // Teardown (unregister/dispose) runs during reload disposal while holding
            // the script gate: never wait on the owner thread.
            Threading.DispatcherGuard.FireAndForget(_dispatcher, callback, "hotkey teardown");
        }
    }

    internal interface IHotkeyPlatform
    {
        bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int errorCode);

        bool UnregisterHotKey(IntPtr windowHandle, int id, out int errorCode);
    }

    private sealed class NativeHotkeyPlatform : IHotkeyPlatform
    {
        public static NativeHotkeyPlatform Instance { get; } = new();

        public bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey, out int errorCode)
        {
            if (User32.RegisterHotKey(windowHandle, id, modifiers, virtualKey))
            {
                errorCode = 0;
                return true;
            }

            errorCode = Marshal.GetLastPInvokeError();
            return false;
        }

        public bool UnregisterHotKey(IntPtr windowHandle, int id, out int errorCode)
        {
            if (User32.UnregisterHotKey(windowHandle, id))
            {
                errorCode = 0;
                return true;
            }

            errorCode = Marshal.GetLastPInvokeError();
            return false;
        }
    }

    private static partial class User32
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
