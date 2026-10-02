using System.Globalization;
using HsWin.Core.Logging;
using HsWin.Core.Windows;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public sealed class WindowScriptApi
{
    private readonly IWindowService _windows;
    private readonly IRuntimeLogger _logger;
    private readonly IScriptCallbackScheduler _callbackScheduler;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal WindowScriptApi(
        IWindowService windows,
        IRuntimeLogger logger,
        IScriptCallbackScheduler callbackScheduler,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _windows = windows;
        _logger = logger;
        _callbackScheduler = callbackScheduler;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public string GetFocusedWindowJson()
    {
        var window = _windows.GetFocusedWindow();
        if (window is null)
        {
            _logger.Info("Script hs.window.focusedWindow() returned null.");
            return "null";
        }

        _logger.Info($"Script hs.window.focusedWindow() returned id='{window.Id}'.");
        return ScriptJson.Serialize(window);
    }

    public string GetWindowJson(object? id)
    {
        var normalizedId = ScriptArgumentReader.RequireNonWhiteSpaceString(id, "id");
        var window = _windows.GetWindow(normalizedId);
        if (window is null)
        {
            _logger.Info($"Script hs.window.get('{normalizedId}') returned null.");
            return "null";
        }

        _logger.Info($"Script hs.window.get('{normalizedId}') found the window.");
        return ScriptJson.Serialize(window);
    }

    public string MoveToScreenJson(object? id, object? targetScreen, object? options = null)
    {
        var normalizedId = ScriptArgumentReader.RequireNonWhiteSpaceString(id, "id");
        var parsedScreen = ParseTargetScreen(targetScreen);
        var parsedOptions = ParseMoveOptions(options);
        var result = _windows.MoveToScreen(normalizedId, parsedScreen, parsedOptions);
        _logger.Info(
            $"Script hs.window.moveToScreen() id='{normalizedId}' screen='{parsedScreen.Id}' success={result.Success} moved={result.Moved} reason='{result.Reason ?? string.Empty}'.");
        return ScriptJson.Serialize(result);
    }

    public ScriptResourceHandle WatchFocused(object? callback)
    {
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Window focus callback must be a JavaScript function.", nameof(callback));
        }

        var relay = new ScriptCallbackRelay(scriptFunction, _callbackScheduler, _callbacks);
        var handle = relay.Attach(_windows.WatchFocused(window => relay.Post(ScriptJson.Serialize(window))));
        _trackResource(handle);
        _logger.Info("Script hs.window.watchFocused() registered.");
        return handle;
    }

    private static WindowMoveOptions ParseMoveOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return new WindowMoveOptions();
        }

        return new WindowMoveOptions(
            PreserveSize: ScriptArgumentReader.OptionalBoolean(
                ScriptArgumentReader.GetPropertyValue(value, "preserveSize"),
                "preserveSize",
                defaultValue: true),
            UseWorkingArea: ScriptArgumentReader.OptionalBoolean(
                ScriptArgumentReader.GetPropertyValue(value, "useWorkingArea", "workingArea"),
                "useWorkingArea",
                defaultValue: true));
    }

    private static WindowTargetScreen ParseTargetScreen(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            throw new ArgumentException("screen is required.", nameof(value));
        }

        return new WindowTargetScreen(
            ScriptArgumentReader.RequireNonWhiteSpaceString(
                ScriptArgumentReader.GetPropertyValue(value, "id"),
                "screen.id"),
            ScriptArgumentReader.RequireNonWhiteSpaceString(
                ScriptArgumentReader.GetPropertyValue(value, "name"),
                "screen.name"),
            ParseRectangle(
                ScriptArgumentReader.GetPropertyValue(value, "bounds"),
                "screen.bounds"),
            ParseRectangle(
                ScriptArgumentReader.GetPropertyValue(value, "workingArea"),
                "screen.workingArea"));
    }

    private static WindowRectangleSnapshot ParseRectangle(object? value, string argumentName)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        return new WindowRectangleSnapshot(
            ScriptArgumentReader.RequireInt32(
                ScriptArgumentReader.GetPropertyValue(value, "x"),
                $"{argumentName}.x",
                "an integer"),
            ScriptArgumentReader.RequireInt32(
                ScriptArgumentReader.GetPropertyValue(value, "y"),
                $"{argumentName}.y",
                "an integer"),
            ScriptArgumentReader.RequireInt32(
                ScriptArgumentReader.GetPropertyValue(value, "width"),
                $"{argumentName}.width",
                "an integer"),
            ScriptArgumentReader.RequireInt32(
                ScriptArgumentReader.GetPropertyValue(value, "height"),
                $"{argumentName}.height",
                "an integer"));
    }

}
