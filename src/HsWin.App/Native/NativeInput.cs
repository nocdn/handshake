using System.Runtime.InteropServices;

namespace HsWin.App.Native;

/// <summary>
/// Win32 <c>INPUT</c>. The union is laid out by the runtime, so the struct is 40 bytes on x64 and
/// 28 on x86 without hard-coded offsets.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public uint Type;

    public NativeInputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeInputUnion
{
    [FieldOffset(0)]
    public NativeMouseInput Mouse;

    [FieldOffset(0)]
    public NativeKeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    public int Dx;

    public int Dy;

    public uint MouseData;

    public uint Flags;

    public uint Time;

    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyboardInput
{
    public ushort VirtualKey;

    public ushort ScanCode;

    public uint Flags;

    public uint Time;

    public nuint ExtraInfo;
}

/// <summary>
/// Builds and sends <c>SendInput</c> events. Every event carries <see cref="InjectedExtraInfo"/> so
/// HsWin's own hooks can recognize and ignore input it generated.
/// </summary>
internal static partial class NativeInputSender
{
    /// <summary>"HSWB": the <c>dwExtraInfo</c> marker on every event HsWin injects.</summary>
    public const nuint InjectedExtraInfo = 0x48535742;

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    public static int InputSize => Marshal.SizeOf<NativeInput>();

    /// <summary>
    /// A key event by scan code, which games and GLFW apps read more reliably than virtual keys.
    /// Keys without a scan code in the current layout fall back to virtual-key injection.
    /// </summary>
    public static NativeInput Key(uint virtualKey, bool keyUp)
    {
        var scanCode = KeyboardScanCodes.Resolve(virtualKey);
        if (scanCode.Code == 0)
        {
            return VirtualKey(virtualKey, keyUp, scanCode.IsExtended);
        }

        // wVk must be 0 with KEYEVENTF_SCANCODE; the system derives the virtual key from the layout.
        var flags = KeyEventScanCode | (keyUp ? KeyEventKeyUp : 0u) | (scanCode.IsExtended ? KeyEventExtendedKey : 0u);
        return CreateKeyboard(0, scanCode.Code, flags);
    }

    /// <summary>A key event by virtual key, for keys such as media keys that are defined by their VK.</summary>
    public static NativeInput VirtualKey(uint virtualKey, bool keyUp, bool isExtended = false)
    {
        var flags = (keyUp ? KeyEventKeyUp : 0u) | (isExtended ? KeyEventExtendedKey : 0u);
        return CreateKeyboard((ushort)virtualKey, 0, flags);
    }

    public static NativeInput Mouse(uint flags, uint mouseData)
    {
        return new NativeInput
        {
            Type = InputMouse,
            Data = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    MouseData = mouseData,
                    Flags = flags,
                    ExtraInfo = InjectedExtraInfo
                }
            }
        };
    }

    /// <summary>Sends all events atomically; returns false with the Win32 error if any were blocked.</summary>
    public static unsafe bool TrySend(ReadOnlySpan<NativeInput> inputs, out int sentCount, out int error)
    {
        error = 0;
        if (inputs.IsEmpty)
        {
            sentCount = 0;
            return true;
        }

        fixed (NativeInput* inputPointer = inputs)
        {
            sentCount = (int)SendInput((uint)inputs.Length, inputPointer, InputSize);
        }

        if (sentCount == inputs.Length)
        {
            return true;
        }

        error = Marshal.GetLastPInvokeError();
        return false;
    }

    private static NativeInput CreateKeyboard(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new NativeInput
        {
            Type = InputKeyboard,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    ExtraInfo = InjectedExtraInfo
                }
            }
        };
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint inputCount, NativeInput* inputs, int inputSize);
}
