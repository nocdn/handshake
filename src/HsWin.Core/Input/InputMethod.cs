namespace HsWin.Core.Input;

/// <summary>How synthetic keyboard or mouse input reaches the target application.</summary>
public enum InputMethod
{
    /// <summary>Global <c>SendInput</c>; the foreground window receives it like physical input.</summary>
    SendInput,

    /// <summary><c>PostMessage</c> to the focused window; works for some background targets.</summary>
    WindowMessage
}
