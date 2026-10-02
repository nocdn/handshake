using System.Net;
using System.Reflection;
using System.Text;
using HsWin.Core.Http;
using HsHttpRequestOptions = HsWin.Core.Http.HttpRequestOptions;

namespace HsWin.Core.Tests;

public sealed class SystemHttpServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly CapturingRuntimeLogger _logger = new();

    private static HsHttpRequestOptions Options(
        string method = "GET",
        string url = "https://api.example.test/items",
        int timeoutMs = HsHttpRequestOptions.DefaultTimeoutMs,
        string responseType = "text",
        Dictionary<string, string>? headers = null,
        Dictionary<string, string>? query = null,
        Dictionary<string, string>? form = null,
        string? body = null,
        string? contentType = null) =>
        new(
            method,
            url,
            headers ?? [],
            query ?? [],
            body,
            contentType,
            form ?? [],
            [],
            timeoutMs,
            responseType);

    private HttpResponseSnapshot Send(HttpMessageHandler handler, HsHttpRequestOptions options)
    {
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = new SystemHttpService(_logger, client);
        using var done = new ManualResetEventSlim(false);
        HttpResponseSnapshot? snapshot = null;

        using var request = service.Send(options, response =>
        {
            snapshot = response;
            done.Set();
        });

        Assert.True(done.Wait(Patience), "The HTTP callback never ran.");
        return snapshot!;
    }

    [Fact]
    public void ASuccessfulResponseCarriesStatusHeadersAndBody()
    {
        var handler = StubHandler.Returns(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("hello", Encoding.UTF8, "text/plain") };
            response.Headers.Add("X-Request-Id", "abc123");
            return response;
        });

        var snapshot = Send(handler, Options());

        Assert.True(snapshot.Success);
        Assert.Equal(200, snapshot.StatusCode);
        Assert.Equal("hello", snapshot.Body);
        Assert.Equal("abc123", snapshot.Headers["x-request-id"]);
        Assert.False(snapshot.TimedOut);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void ANonSuccessStatusIsReportedWithoutBeingAnError()
    {
        var handler = StubHandler.Returns(() => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("nope") });

        var snapshot = Send(handler, Options());

        Assert.False(snapshot.Success);
        Assert.Equal(404, snapshot.StatusCode);
        Assert.Equal("nope", snapshot.Body);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void QueryValuesAreEscapedAndAppendedToTheUrl()
    {
        var handler = StubHandler.Returns(() => new HttpResponseMessage(HttpStatusCode.OK));

        Send(handler, Options(
            url: "https://api.example.test/search?fixed=1",
            query: new() { ["q"] = "a b&c", ["lang"] = "en" }));

        Assert.Equal("https://api.example.test/search?fixed=1&q=a%20b%26c&lang=en", handler.LastRequestUri);
    }

    [Fact]
    public void FormAndBodyContentAreSentWithTheirContentTypes()
    {
        var handler = StubHandler.Returns(() => new HttpResponseMessage(HttpStatusCode.OK));

        Send(handler, Options("POST", form: new() { ["name"] = "Ada", ["role"] = "dev" }));
        Assert.Equal("name=Ada&role=dev", handler.LastBody);
        Assert.Equal("application/x-www-form-urlencoded", handler.LastContentType);

        Send(handler, Options("POST", body: "{\"a\":1}", contentType: "application/json"));
        Assert.Equal("{\"a\":1}", handler.LastBody);
        Assert.StartsWith("application/json", handler.LastContentType, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomHeadersAreSentIncludingContentHeaders()
    {
        var handler = StubHandler.Returns(() => new HttpResponseMessage(HttpStatusCode.OK));

        Send(handler, Options("POST", body: "x", headers: new() { ["Authorization"] = "Bearer t", ["Content-Language"] = "en" }));

        Assert.Equal("Bearer t", handler.LastHeaders["Authorization"]);
        Assert.Equal("en", handler.LastHeaders["Content-Language"]);
    }

    [Fact]
    public void Base64ResponseTypeReturnsTheRawBytes()
    {
        var bytes = new byte[] { 0, 1, 2, 250, 255 };
        var handler = StubHandler.Returns(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var snapshot = Send(handler, Options(responseType: "base64"));

        Assert.Equal(Convert.ToBase64String(bytes), snapshot.Body);
    }

    [Fact]
    public void ARequestThatOutlastsItsTimeoutReportsTimedOut()
    {
        var handler = new StubHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var snapshot = Send(handler, Options(timeoutMs: 100));

        Assert.True(snapshot.TimedOut);
        Assert.False(snapshot.Success);
        Assert.Null(snapshot.StatusCode);
        Assert.Equal("HTTP request timed out.", snapshot.Error);
    }

    [Fact]
    public void ANetworkFailureBecomesAnErrorSnapshot()
    {
        var handler = StubHandler.Returns(() => throw new HttpRequestException("name resolution failed"));

        var snapshot = Send(handler, Options());

        Assert.False(snapshot.Success);
        Assert.False(snapshot.TimedOut);
        Assert.Equal("name resolution failed", snapshot.Error);
        Assert.Contains(_logger.Errors, error => error.Contains("HTTP request failed", StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingARequestCancelsItAndNeverCallsBack()
    {
        var entered = new ManualResetEventSlim(false);
        var cancelled = new ManualResetEventSlim(false);
        var handler = new StubHandler(async token =>
        {
            entered.Set();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.Set();
                throw;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var service = new SystemHttpService(_logger, client);
        var callbacks = 0;

        var request = service.Send(Options(), _ => Interlocked.Increment(ref callbacks));
        Assert.True(entered.Wait(Patience));
        request.Dispose();
        request.Dispose();

        Assert.True(cancelled.Wait(Patience), "Disposing the request did not cancel it.");
        Assert.Equal(0, Volatile.Read(ref callbacks));
    }

    [Fact]
    public void TheSharedClientHasNoClientWideTimeoutSoLongRequestsAreNotCutAtOneHundredSeconds()
    {
        var shared = (HttpClient)typeof(SystemHttpService)
            .GetField("SharedClient", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.Equal(Timeout.InfiniteTimeSpan, shared.Timeout);
    }

    [Theory]
    [InlineData("utf-8", "héllo")]
    [InlineData("\"iso-8859-1\"", "héllo")]
    [InlineData("not-a-real-charset", "héllo")]
    [InlineData(null, "héllo")]
    public void ResponseTextIsDecodedWithItsCharsetAndFallsBackToUtf8(string? charset, string expected)
    {
        var bytes = charset?.Contains("8859", StringComparison.Ordinal) == true
            ? Encoding.Latin1.GetBytes("héllo")
            : Encoding.UTF8.GetBytes("héllo");

        Assert.Equal(expected, SystemHttpService.ReadBody("text", bytes, charset));
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public static StubHandler Returns(Func<HttpResponseMessage> respond) =>
            new(_ => Task.FromResult(respond()));

        public string? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        public string? LastContentType { get; private set; }

        public Dictionary<string, string> LastHeaders { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.AbsoluteUri;
            LastContentType = request.Content?.Headers.ContentType?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            LastHeaders.Clear();
            var contentHeaders = request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>();
            foreach (var header in request.Headers.Concat(contentHeaders))
            {
                LastHeaders[header.Key] = string.Join(", ", header.Value);
            }

            return await respond(cancellationToken);
        }
    }
}
