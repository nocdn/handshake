using System.Collections;
using System.Globalization;
using HsWin.Core.Http;
using HsWin.Core.Logging;
using Microsoft.ClearScript;
using HsHttpRequestOptions = HsWin.Core.Http.HttpRequestOptions;

namespace HsWin.Core.Scripting;

public sealed class HttpScriptApi
{
    private readonly IHttpService _http;
    private readonly IRuntimeLogger _logger;
    private readonly IScriptCallbackScheduler _callbackScheduler;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal HttpScriptApi(
        IHttpService http,
        IRuntimeLogger logger,
        IScriptCallbackScheduler callbackScheduler,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _http = http;
        _logger = logger;
        _callbackScheduler = callbackScheduler;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public ScriptResourceHandle Request(object? options, object? callback)
    {
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("HTTP callback must be a JavaScript function.", nameof(callback));
        }

        var parsedOptions = ParseRequestOptions(options);
        var relay = new ScriptCallbackRelay(scriptFunction, _callbackScheduler, _callbacks);
        var request = _http.Send(parsedOptions, response => relay.PostFinal(ScriptJson.Serialize(response)));
        var handle = relay.Attach(request);
        _trackResource(handle);
        _logger.Info($"Script hs.http.request() started method='{parsedOptions.Method}' url={LogSanitizer.DescribeUrl(parsedOptions.Url)}.");
        return handle;
    }

    public static HsHttpRequestOptions ParseRequestOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            throw new ArgumentException("HTTP request options are required.", nameof(value));
        }

        if (!ScriptArgumentReader.IsOptionsObject(value))
        {
            return CreateDefaultRequest(ScriptArgumentReader.RequireNonWhiteSpaceString(value, "url"));
        }

        var url = ScriptArgumentReader.RequireNonWhiteSpaceString(
            ScriptArgumentReader.GetPropertyValue(value, "url", "uri"),
            "url");
        var headers = ScriptArgumentReader.ReadStringMap(
            ScriptArgumentReader.GetPropertyValue(value, "headers", "header"),
            "headers");
        var query = ScriptArgumentReader.ReadStringMap(
            ScriptArgumentReader.GetPropertyValue(value, "query", "params", "search"),
            "query");
        var form = ScriptArgumentReader.ReadStringMap(
            ScriptArgumentReader.GetPropertyValue(value, "form", "formData"),
            "form");
        var multipart = ReadMultipartParts(
            ScriptArgumentReader.GetPropertyValue(value, "multipart", "parts"),
            ScriptArgumentReader.GetPropertyValue(value, "files", "file"));
        var body = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "body", "data"));
        var contentType = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "contentType", "mimeType", "mediaType"));
        var timeoutValue = ScriptArgumentReader.GetPropertyValue(value, "timeoutMs", "timeout");
        var timeoutMs = ScriptArgumentReader.IsMissing(timeoutValue)
            ? HsHttpRequestOptions.DefaultTimeoutMs
            : ScriptArgumentReader.RequirePositiveInt32(timeoutValue, "timeoutMs");
        var responseType = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "responseType", "response"));

        return new HsHttpRequestOptions(
            NormalizeMethod(ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "method", "verb")), body, form, multipart),
            url,
            headers,
            query,
            body,
            contentType,
            form,
            multipart,
            timeoutMs,
            NormalizeResponseType(responseType));
    }

    private static HsHttpRequestOptions CreateDefaultRequest(string url)
    {
        return new HsHttpRequestOptions(
            "GET",
            url,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [],
            HsHttpRequestOptions.DefaultTimeoutMs,
            "text");
    }

    private static string NormalizeMethod(
        string? method,
        string? body,
        IReadOnlyDictionary<string, string> form,
        IReadOnlyList<HttpMultipartPart> multipart)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            return body is not null || form.Count > 0 || multipart.Count > 0 ? "POST" : "GET";
        }

        var normalized = method.Trim().ToUpperInvariant();
        if (normalized.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("HTTP method must contain only ASCII letters.", nameof(method));
        }

        return normalized;
    }

    private static string NormalizeResponseType(string? responseType)
    {
        if (string.IsNullOrWhiteSpace(responseType))
        {
            return "text";
        }

        return responseType.Trim().ToLowerInvariant() switch
        {
            "text" or "json" => "text",
            "base64" or "binary" => "base64",
            _ => throw new ArgumentException("HTTP responseType must be text, json, or base64.", nameof(responseType))
        };
    }

    private static IReadOnlyList<HttpMultipartPart> ReadMultipartParts(object? multipartValue, object? filesValue)
    {
        var parts = new List<HttpMultipartPart>();
        foreach (var partValue in ScriptArgumentReader.EnumerateIndexedValues(multipartValue))
        {
            parts.Add(ReadMultipartPart(partValue));
        }

        if (ScriptArgumentReader.IsMissing(filesValue))
        {
            return parts;
        }

        if (filesValue is string)
        {
            parts.Add(new HttpMultipartPart("file", null, ScriptArgumentReader.RequireNonWhiteSpaceString(filesValue, "file"), null, null));
            return parts;
        }

        if (ScriptArgumentReader.HasIndexedValues(filesValue))
        {
            foreach (var fileValue in ScriptArgumentReader.EnumerateIndexedValues(filesValue))
            {
                parts.Add(ReadMultipartPart(fileValue));
            }

            return parts;
        }

        if (ScriptArgumentReader.IsOptionsObject(filesValue))
        {
            foreach (var item in ReadFileMap(filesValue!))
            {
                parts.Add(new HttpMultipartPart(item.Key, null, item.Value, null, null));
            }

            return parts;
        }

        throw new ArgumentException("files must be a path, an array of { name, path } parts, or an object mapping field names to paths.", "files");
    }

    private static HttpMultipartPart ReadMultipartPart(object? value)
    {
        if (!ScriptArgumentReader.IsOptionsObject(value))
        {
            throw new ArgumentException("Multipart parts must be objects.", nameof(value));
        }

        var name = ScriptArgumentReader.RequireNonWhiteSpaceString(
            ScriptArgumentReader.GetPropertyValue(value, "name", "field"),
            "name");
        var path = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "path", "file", "filePath"));
        var partValue = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "value", "text", "body"));
        if (path is null && partValue is null)
        {
            throw new ArgumentException("Multipart part must have either value or path.", nameof(value));
        }

        return new HttpMultipartPart(
            name,
            partValue,
            path,
            ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "fileName", "filename")),
            ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "contentType", "mimeType", "mediaType")));
    }

    private static IReadOnlyDictionary<string, string> ReadFileMap(object value)
    {
        if (value is ScriptObject scriptObject)
        {
            return scriptObject.PropertyNames.ToDictionary(
                propertyName => propertyName,
                propertyName => ScriptArgumentReader.RequireNonWhiteSpaceString(scriptObject.GetProperty(propertyName), propertyName),
                StringComparer.OrdinalIgnoreCase);
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary.ToDictionary(
                item => item.Key,
                item => ScriptArgumentReader.RequireNonWhiteSpaceString(item.Value, item.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry item in dictionary)
            {
                if (item.Key is string key)
                {
                    result[key] = ScriptArgumentReader.RequireNonWhiteSpaceString(item.Value, key);
                }
            }

            return result;
        }

        throw new ArgumentException("files must be a path, array, or object.", nameof(value));
    }
}
