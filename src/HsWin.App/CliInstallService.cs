using System.IO;
using HsWin.App.Lifecycle;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HsWin.App;

/// <summary>The user's persisted PATH value, read and written without expanding <c>%VARIABLES%</c>.</summary>
internal interface IUserPathStore
{
    string? Read();

    void Write(string value);
}

internal sealed class CliInstallService : ICliInstaller
{
    public const string CliFileName = "hspn.exe";

    private readonly string _installDirectory;
    private readonly IUserPathStore _userPath;

    public CliInstallService(string installDirectory)
        : this(installDirectory, RegistryUserPathStore.Instance)
    {
    }

    internal CliInstallService(string installDirectory, IUserPathStore userPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        _installDirectory = Path.GetFullPath(installDirectory);
        _userPath = userPath;
    }

    public string CliPath => Path.Combine(_installDirectory, CliFileName);

    public bool IsInstalled()
    {
        return PathContainsDirectory(_userPath.Read(), _installDirectory);
    }

    public CliInstallResult Install()
    {
        if (!File.Exists(CliPath))
        {
            throw new FileNotFoundException(
                $"The {CliFileName} executable was not found next to the installed app.",
                CliPath);
        }

        var userPath = _userPath.Read();
        if (PathContainsDirectory(userPath, _installDirectory))
        {
            return CliInstallResult.AlreadyInstalled;
        }

        _userPath.Write(AddDirectoryToPath(userPath, _installDirectory));

        var processPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(
            "Path",
            AddDirectoryToPath(processPath, _installDirectory),
            EnvironmentVariableTarget.Process);
        return CliInstallResult.Installed;
    }

    internal static bool PathContainsDirectory(string? pathValue, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        var normalizedDirectory = NormalizeDirectory(directory);
        return pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static entry => entry.Trim('"'))
            .Any(entry =>
                TryNormalizeDirectory(entry, out var normalizedEntry)
                && string.Equals(normalizedEntry, normalizedDirectory, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Appends <paramref name="directory"/>, leaving every existing entry exactly as written.</summary>
    internal static string AddDirectoryToPath(string? pathValue, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (PathContainsDirectory(pathValue, directory))
        {
            return pathValue ?? string.Empty;
        }

        var normalizedDirectory = NormalizeDirectory(directory);
        return string.IsNullOrWhiteSpace(pathValue)
            ? normalizedDirectory
            : $"{pathValue.TrimEnd(Path.PathSeparator)}{Path.PathSeparator}{normalizedDirectory}";
    }

    private static string NormalizeDirectory(string directory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
        return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool TryNormalizeDirectory(string directory, out string normalizedDirectory)
    {
        try
        {
            normalizedDirectory = NormalizeDirectory(directory);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedDirectory = string.Empty;
            return false;
        }
    }
}

/// <summary>
/// <c>HKCU\Environment\Path</c> accessed raw. <see cref="Environment.GetEnvironmentVariable(string, EnvironmentVariableTarget)"/>
/// returns the value already expanded and writes it back as REG_SZ, which would permanently replace
/// entries such as <c>%USERPROFILE%\bin</c> with today's expansion.
/// </summary>
internal sealed partial class RegistryUserPathStore : IUserPathStore
{
    private const string EnvironmentKeyPath = "Environment";
    private const string PathValueName = "Path";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint BroadcastTimeoutMs = 5000;
    private static readonly nint HwndBroadcast = 0xffff;

    public static RegistryUserPathStore Instance { get; } = new();

    private RegistryUserPathStore()
    {
    }

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKeyPath, writable: false);
        return key?.GetValue(PathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Write(string value)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(EnvironmentKeyPath, writable: true))
        {
            // REG_EXPAND_SZ, as Windows stores the user PATH, so %VARIABLES% keep expanding.
            key.SetValue(PathValueName, value, RegistryValueKind.ExpandString);
        }

        // Tell Explorer and new shells to reload the environment.
        _ = SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, EnvironmentKeyPath, SmtoAbortIfHung, BroadcastTimeoutMs, out _);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(
        nint windowHandle,
        uint message,
        nuint wParam,
        string lParam,
        uint flags,
        uint timeoutMs,
        out nuint result);
}
