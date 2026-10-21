using HsWin.Core.Input;
using HsWin.Core.Logging;
using HsWin.Core.Mouse;

namespace HsWin.App.Input;

internal interface IMouseInputSender
{
    void SendClick(MouseButton button, InputMethod inputMethod, IRuntimeLogger? logger = null);

    void SendScroll(int delta, IRuntimeLogger? logger = null);
}
