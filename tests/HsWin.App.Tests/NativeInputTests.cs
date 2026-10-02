using System.Runtime.InteropServices;
using HsWin.App.Input;
using HsWin.App.Native;
using HsWin.Core.Keyboard;
using HsWin.Core.Mouse;

namespace HsWin.App.Tests;

public sealed class NativeInputTests
{
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    [Fact]
    public void InputSizeMatchesTheWin32Layout()
    {
        Assert.Equal(Environment.Is64BitProcess ? 40 : 28, NativeInputSender.InputSize);
        Assert.Equal(Environment.Is64BitProcess ? 8 : 4, (int)Marshal.OffsetOf<NativeInput>(nameof(NativeInput.Data)));
    }

    [Fact]
    public void KeyEventsUseScanCodesAndCarryTheInjectedMarker()
    {
        var input = NativeInputSender.Key(KeyboardKeyRules.VkLeftShift, keyUp: true);

        Assert.Equal(1u, input.Type);
        Assert.Equal(0, input.Data.Keyboard.VirtualKey);
        Assert.Equal(0x2A, input.Data.Keyboard.ScanCode);
        Assert.Equal(KeyEventScanCode | KeyEventKeyUp, input.Data.Keyboard.Flags);
        Assert.Equal(NativeInputSender.InjectedExtraInfo, input.Data.Keyboard.ExtraInfo);
    }

    [Fact]
    public void RightHandModifiersAreExtended()
    {
        var input = NativeInputSender.Key(KeyboardKeyRules.VkRightControl, keyUp: false);

        Assert.Equal(0x1D, input.Data.Keyboard.ScanCode);
        Assert.Equal(KeyEventScanCode | KeyEventExtendedKey, input.Data.Keyboard.Flags);
    }

    [Fact]
    public void VirtualKeyEventsCarryTheInjectedMarker()
    {
        const uint vkMediaPlayPause = 0xB3;

        var input = NativeInputSender.VirtualKey(vkMediaPlayPause, keyUp: false, isExtended: true);

        Assert.Equal((ushort)vkMediaPlayPause, input.Data.Keyboard.VirtualKey);
        Assert.Equal(KeyEventExtendedKey, input.Data.Keyboard.Flags);
        Assert.Equal(NativeInputSender.InjectedExtraInfo, input.Data.Keyboard.ExtraInfo);
    }

    [Fact]
    public void MouseEventsCarryTheInjectedMarker()
    {
        var input = NativeInputSender.Mouse(0x0002, 0);

        Assert.Equal(0u, input.Type);
        Assert.Equal(0x0002u, input.Data.Mouse.Flags);
        Assert.Equal(NativeInputSender.InjectedExtraInfo, input.Data.Mouse.ExtraInfo);
    }

    [Theory]
    [InlineData(0x6Fu, 0xE035u, 0x35, true)] // numpad divide
    [InlineData(0xB3u, 0xE022u, 0x22, true)] // media play/pause
    [InlineData(0x5Du, 0xE05Du, 0x5D, true)] // apps
    [InlineData(0x41u, 0x001Eu, 0x1E, false)] // A
    public void MappedScanCodesKeepTheExtendedPrefix(uint virtualKey, uint mapped, int expectedCode, bool expectedExtended)
    {
        var scanCode = KeyboardScanCodes.FromMappedValue(virtualKey, mapped);

        Assert.Equal(new ScanCode((ushort)expectedCode, expectedExtended), scanCode);
    }

    [Fact]
    public void PauseHasNoSingleScanCodeAndFallsBackToVirtualKeyInjection()
    {
        const uint vkPause = 0x13;

        Assert.Equal(new ScanCode(0, false), KeyboardScanCodes.FromMappedValue(vkPause, 0xE11D));
    }

    [Fact]
    public void KeysWithoutAScanCodeAreSentByVirtualKey()
    {
        const uint vkPause = 0x13;

        var input = NativeInputSender.Key(vkPause, keyUp: false);

        Assert.Equal((ushort)vkPause, input.Data.Keyboard.VirtualKey);
        Assert.Equal(0u, input.Data.Keyboard.Flags & KeyEventScanCode);
    }

    [Fact]
    public void KeyMessageLParamEncodesScanCodeExtendedAndTransitionBits()
    {
        var down = WindowMessageKeyboardInputSender.BuildKeyLParam(new ScanCode(0x35, true), keyUp: false);
        var up = WindowMessageKeyboardInputSender.BuildKeyLParam(new ScanCode(0x35, true), keyUp: true);

        Assert.Equal(0x0135_0001, (int)down);
        Assert.Equal(unchecked((int)0xC135_0001), (int)up);
    }

    [Theory]
    [InlineData(MouseButton.Left, 0x0201u, 0x0001u, 0x0202u, 0x0000u)]
    [InlineData(MouseButton.Right, 0x0204u, 0x0002u, 0x0205u, 0x0000u)]
    [InlineData(MouseButton.Middle, 0x0207u, 0x0010u, 0x0208u, 0x0000u)]
    [InlineData(MouseButton.XButton1, 0x020Bu, 0x0001_0020u, 0x020Cu, 0x0001_0000u)]
    [InlineData(MouseButton.XButton2, 0x020Bu, 0x0002_0040u, 0x020Cu, 0x0002_0000u)]
    public void MouseMessagesCarryButtonStateOnlyWhilePressed(
        MouseButton button,
        uint down,
        uint downWParam,
        uint up,
        uint upWParam)
    {
        var messages = WindowMessageMouseInputSender.GetButtonMessages(button);

        Assert.Equal((down, (nuint)downWParam, up, (nuint)upWParam), messages);
    }

    [Theory]
    [InlineData(10, 20, 0x0014_000A)]
    [InlineData(-1, 5, 0x0005_FFFF)]
    [InlineData(3, -2, unchecked((int)0xFFFE_0003))]
    public void PointLParamPacksSignedClientCoordinates(int x, int y, int expected)
    {
        var lParam = WindowMessageMouseInputSender.BuildPointLParam(new NativePoint { X = x, Y = y });

        Assert.Equal(expected, unchecked((int)lParam));
    }
}
