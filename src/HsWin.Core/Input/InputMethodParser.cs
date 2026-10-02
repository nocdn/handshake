using HsWin.Core.Scripting;

namespace HsWin.Core.Input;

public static class InputMethodParser
{
    public static InputMethod Parse(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return InputMethod.SendInput;
        }

        var method = ScriptArgumentReader.RequireNonWhiteSpaceString(value, "inputMethod");
        return method.Trim().ToLowerInvariant() switch
        {
            "sendinput" or "send-input" or "global" => InputMethod.SendInput,
            "windowmessage" or "window-message" or "postmessage" or "window" => InputMethod.WindowMessage,
            _ => throw new ArgumentException("inputMethod must be 'sendInput' or 'windowMessage'.", "inputMethod")
        };
    }

    public static string GetDisplayName(InputMethod method)
    {
        return method switch
        {
            InputMethod.SendInput => "sendInput",
            InputMethod.WindowMessage => "windowMessage",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported input method.")
        };
    }
}
