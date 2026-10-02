using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HsWin.Core.Commands;

public static class HsWinCommandProtocol
{
    /// <summary>Longest request or response line either side accepts.</summary>
    public const int MaxMessageLength = 64 * 1024;

    private const string PipeNamePrefix = "HsWin.Command.5E65F5D3-AC46-43D2-B31C-B98F8757C640";

    /// <summary>
    /// The pipe for this user's instance in this logon session. Scoping the name keeps other users
    /// and other RDP sessions from colliding with (or squatting) it; both ends also use
    /// <see cref="System.IO.Pipes.PipeOptions.CurrentUserOnly"/>, which enforces the same boundary.
    /// </summary>
    public static string PipeName { get; } = CreatePipeName(
        $@"{Environment.UserDomainName}\{Environment.UserName}",
        GetSessionId());

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeRequest(HsWinCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    public static HsWinCommandRequest DeserializeRequest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<HsWinCommandRequest>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Command request payload was empty.");
    }

    public static string SerializeResponse(HsWinCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return JsonSerializer.Serialize(response, SerializerOptions);
    }

    public static HsWinCommandResponse DeserializeResponse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<HsWinCommandResponse>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Command response payload was empty.");
    }

    internal static string CreatePipeName(string userName, int sessionId)
    {
        // Hash the account name: it can contain characters a pipe name should not, and the name is visible.
        var userHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.ToUpperInvariant())))[..16];
        return $"{PipeNamePrefix}.{userHash}.{sessionId}";
    }

    private static int GetSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }
}
