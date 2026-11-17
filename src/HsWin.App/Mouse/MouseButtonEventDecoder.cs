using HsWin.Core.Mouse;

namespace HsWin.App.Mouse;

internal static class MouseButtonEventDecoder
{
    public static bool TryDecode(int message, uint data, uint flags, int x, int y, uint modifiers,
        out MouseButtonEventSnapshot snapshot)
    {
        MouseButton? button = message switch
        {
            0x0201 or 0x0202 => MouseButton.Left,
            0x0204 or 0x0205 => MouseButton.Right,
            0x0207 or 0x0208 => MouseButton.Middle,
            0x020B or 0x020C => (data >> 16) switch
            {
                1 => MouseButton.XButton1,
                2 => MouseButton.XButton2,
                _ => null
            },
            _ => null
        };
        snapshot = button is { } value
            ? new(value, message is 0x0201 or 0x0204 or 0x0207 or 0x020B, (flags & 1) != 0, modifiers, x, y)
            : null!;
        return button.HasValue;
    }
}
