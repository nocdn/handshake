using System.Net.Http.Headers;
using System.Text;
using HsWin.Core.Logging;

namespace HsWin.Core.Http;

public sealed class SystemHttpService : IHttpService
{
    // Each request owns its timeout (options.TimeoutMs); the client-wide 100 s default would
    // otherwise cancel longer requests first and look like a disposal, so the callback never ran.
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly IRuntimeLogger _logger;
    private readonly HttpClient _client;

    public SystemHttpService(IRuntimeLogger logger)
        : this(logger, SharedClient)
    {
    }

    internal SystemHttpService(IRuntimeLogger logger, HttpClient client)
    {
        _logger = logger;
        _client = client;
    }

    public IDisposable Send(HttpRequestOptions options, Action<HttpResponseSnapshot> callback)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        var request = new HttpRequestTask(_client, options, callback, _logger);
        request.Start();
        return request;
    }

    private sealed class HttpRequestTask : IDisposable
    {
        private readonly HttpClient _client;
        private readonly HttpRequestOptions _options;
        private readonly Action<HttpResponseSnapshot> _callback;
        private readonly IRuntimeLogger _logger;
        private readonly CancellationTokenSource _cancellation = new();
        private int _disposed;

        public HttpRequestTask(HttpClient client, HttpRequestOptions options, Action<HttpResponseSnapshot> callback, IRuntimeLogger logger)
        {
            _client = client;
            _options = options;
            _callback = callback;
            _logger = logger;
        }

        public void Start()
        {
            _ = Task.Run(SendAsync);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // The request task owns the token source's lifetime; it may already be finished.
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task SendAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.TimeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token, timeout.Token);
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(_options.Method), BuildUri(_options));
                using var content = CreateContent(_options);
                if (content is not null)
                {
                    request.Content = content;
                }

                AddHeaders(request, _options.Headers);

                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token).ConfigureAwait(false);
                var bodyBytes = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
                var body = SystemHttpService.ReadBody(_options.ResponseType, bodyBytes, response.Content.Headers.ContentType?.CharSet);
                var headers = ReadHeaders(response);
                _logger.Info($"HTTP request completed method='{_options.Method}' url={LogSanitizer.DescribeUrl(_options.Url)} statusCode={(int)response.StatusCode}.");
                InvokeCallback(new HttpResponseSnapshot(
                    response.IsSuccessStatusCode,
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? response.StatusCode.ToString(),
                    headers,
                    body,
                    TimedOut: false,
                    Error: null));
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
            {
                InvokeCallback(new HttpResponseSnapshot(false, null, "timeout", new Dictionary<string, string>(), string.Empty, TimedOut: true, "HTTP request timed out."));
            }
            catch (OperationCanceledException)
            {
                _logger.Info($"HTTP request canceled method='{_options.Method}' url={LogSanitizer.DescribeUrl(_options.Url)}.");
            }
            catch (Exception exception)
            {
                _logger.Error("HTTP request failed.", exception);
                InvokeCallback(new HttpResponseSnapshot(false, null, "error", new Dictionary<string, string>(), string.Empty, TimedOut: false, exception.Message));
            }
            finally
            {
                _cancellation.Dispose();
            }
        }

        private void InvokeCallback(HttpResponseSnapshot response)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _callback(response);
            }
        }

        private static Uri BuildUri(HttpRequestOptions options)
        {
            var builder = new UriBuilder(options.Url);
            if (options.Query.Count == 0)
            {
                return builder.Uri;
            }

            var query = new StringBuilder(builder.Query.TrimStart('?'));
            foreach (var item in options.Query)
            {
                if (query.Length > 0)
                {
                    query.Append('&');
                }

                query
                    .Append(Uri.EscapeDataString(item.Key))
                    .Append('=')
                    .Append(Uri.EscapeDataString(item.Value));
            }

            builder.Query = query.ToString();
            return builder.Uri;
        }

        private static HttpContent? CreateContent(HttpRequestOptions options)
        {
            if (options.Multipart.Count > 0)
            {
                return CreateMultipartContent(options.Multipart);
            }

            if (options.Form.Count > 0)
            {
                return new FormUrlEncodedContent(options.Form);
            }

            if (options.Body is not null)
            {
                var content = new StringContent(options.Body, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(options.ContentType))
                {
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);
                }

                return content;
            }

            return null;
        }

        /// <summary>Builds the multipart body; if any file cannot be opened, every file opened so far is closed.</summary>
        private static MultipartFormDataContent CreateMultipartContent(IReadOnlyList<HttpMultipartPart> parts)
        {
            var multipart = new MultipartFormDataContent();
            try
            {
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part.Path))
                    {
                        multipart.Add(new StringContent(part.Value ?? string.Empty), part.Name);
                        continue;
                    }

                    var filePath = Environment.ExpandEnvironmentVariables(part.Path);
                    var fileContent = new StreamContent(File.OpenRead(filePath));
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(part.ContentType))
                        {
                            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(part.ContentType);
                        }

                        multipart.Add(fileContent, part.Name, part.FileName ?? Path.GetFileName(filePath));
                    }
                    catch
                    {
                        fileContent.Dispose();
                        throw;
                    }
                }

                return multipart;
            }
            catch
            {
                multipart.Dispose();
                throw;
            }
        }

        private static void AddHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
        {
            foreach (var header in headers)
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }


        private static IReadOnlyDictionary<string, string> ReadHeaders(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            foreach (var header in response.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            return headers;
        }
    }

    internal static string ReadBody(string responseType, byte[] bodyBytes, string? charset)
    {
        if (responseType.Equals("base64", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToBase64String(bodyBytes);
        }

        return ResolveEncoding(charset).GetString(bodyBytes);
    }

    /// <summary>The response charset's encoding, or UTF-8 when it is missing or unknown.</summary>
    internal static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"'));
        }
        catch (ArgumentException)
        {
            // A server's unrecognized charset label must not turn a good response into an error.
            return Encoding.UTF8;
        }
    }
}
