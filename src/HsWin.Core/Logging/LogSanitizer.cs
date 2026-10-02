using System.Security.Cryptography;
using System.Text;

namespace HsWin.Core.Logging;

public static class LogSanitizer
{
    public static string DescribeCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var length = command.Length;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command)))[..16];
        return $"length={length} sha256={fingerprint}";
    }

    /// <summary>
    /// Describes a launch target without its private parts: a URL loses its query values, a path is
    /// reduced to the file name, and a bare program name is kept as written.
    /// </summary>
    public static string DescribeTarget(string target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Contains("://", StringComparison.Ordinal))
        {
            return DescribeUrl(target);
        }

        // A trailing separator means a folder: its name can identify a user, so only its length is kept.
        var name = Path.GetFileName(target);
        return string.IsNullOrEmpty(name) ? $"target length={target.Length}" : name;
    }

    public static string DescribeUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"invalid-url length={url.Length}";
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme);
        builder.Append("://");
        builder.Append(uri.Host);
        if (!uri.IsDefaultPort)
        {
            builder.Append(':');
            builder.Append(uri.Port);
        }

        builder.Append(uri.AbsolutePath);

        if (!string.IsNullOrEmpty(uri.Query))
        {
            var keys = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2)[0])
                .Where(key => !string.IsNullOrEmpty(key))
                .ToArray();

            builder.Append(keys.Length > 0 ? $"?keys={string.Join(",", keys)}" : "?<redacted-query>");
        }

        return builder.ToString();
    }
}
