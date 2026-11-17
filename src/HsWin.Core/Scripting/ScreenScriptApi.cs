using HsWin.Core.Screens;

namespace HsWin.Core.Scripting;

public sealed class ScreenScriptApi(IScreenProvider screens)
{
    public string AllScreensJson() => ScriptJson.Serialize(screens.AllScreens());
}
