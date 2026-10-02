using HsWin.Core.Http;
using HsWin.Core.Scripting;

namespace HsWin.Core.Tests;

public sealed class HttpScriptRuntimeTests
{
    [Fact]
    public void ReloadExposesHttpRequestWithMultipartFileUpload()
    {
        var presenter = new CapturingAlertPresenter();
        var callbacks = new QueuedScriptCallbackScheduler();
        var http = new CapturingHttpService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Alerts = presenter,
            Http = http,
            CallbackScheduler = callbacks
        });

        runtime.Reload("""
            hs.http.post("https://api.example.test/transcribe", {
              headers: { Authorization: "Bearer test-token" },
              multipart: [
                { name: "file", path: "C:\\Temp\\clip.wav", fileName: "clip.wav", contentType: "audio/wav" },
                { name: "model", value: "scribe-v1" }
              ],
              timeoutMs: 1234
            }, result => {
              hs.alert.show(`${result.success}:${result.statusCode}:${result.json.text}`, "normal", 1);
            });
            """);

        var request = Assert.Single(http.Requests);
        Assert.Equal("POST", request.Options.Method);
        Assert.Equal("https://api.example.test/transcribe", request.Options.Url);
        Assert.Equal("Bearer test-token", request.Options.Headers["Authorization"]);
        Assert.Equal(1234, request.Options.TimeoutMs);
        Assert.Collection(
            request.Options.Multipart,
            part =>
            {
                Assert.Equal("file", part.Name);
                Assert.Equal(@"C:\Temp\clip.wav", part.Path);
                Assert.Equal("clip.wav", part.FileName);
                Assert.Equal("audio/wav", part.ContentType);
            },
            part =>
            {
                Assert.Equal("model", part.Name);
                Assert.Equal("scribe-v1", part.Value);
            });

        request.Emit(new HttpResponseSnapshot(
            Success: true,
            StatusCode: 200,
            Status: "OK",
            new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body: """{"text":"hello world"}""",
            TimedOut: false,
            Error: null));
        callbacks.RunNext();

        var alert = Assert.Single(presenter.Requests);
        Assert.Equal("true:200:hello world", alert.Text);
    }

    [Fact]
    public void ReloadExposesHttpRequestWithFilesArrayUpload()
    {
        var http = new CapturingHttpService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Http = http
        });

        runtime.Reload("""
            hs.http.post("https://api.example.test/upload", {
              files: [
                { name: "clip", path: "C:\\Temp\\clip.wav", fileName: "clip.wav", contentType: "audio/wav" },
                { name: "metadata", value: "not-a-file" }
              ]
            }, () => {});
            """);

        var request = Assert.Single(http.Requests);
        Assert.Collection(
            request.Options.Multipart,
            part =>
            {
                Assert.Equal("clip", part.Name);
                Assert.Equal(@"C:\Temp\clip.wav", part.Path);
                Assert.Equal("clip.wav", part.FileName);
                Assert.Equal("audio/wav", part.ContentType);
            },
            part =>
            {
                Assert.Equal("metadata", part.Name);
                Assert.Equal("not-a-file", part.Value);
            });
    }

    [Fact]
    public void ReloadExposesHttpRequestWithFilesObjectMapUpload()
    {
        var http = new CapturingHttpService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Http = http
        });

        runtime.Reload("""
            hs.http.post("https://api.example.test/upload", {
              files: { clip: "C:\\Temp\\clip.wav" }
            }, () => {});
            """);

        var request = Assert.Single(http.Requests);
        var part = Assert.Single(request.Options.Multipart);
        Assert.Equal("clip", part.Name);
        Assert.Equal(@"C:\Temp\clip.wav", part.Path);
    }

    [Fact]
    public void ReloadHttpGetLogsRedactedUrlWithoutQueryValues()
    {
        var logger = new CapturingRuntimeLogger();
        var http = new CapturingHttpService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Logger = logger,
            Http = http
        });

        runtime.Reload("""
            hs.http.get("https://api.example.test/v1/data?token=secret-query-value&model=test", () => {});
            """);

        var log = Assert.Single(logger.Infos, message => message.Contains("hs.http.request() started", StringComparison.Ordinal));
        Assert.Contains("https://api.example.test/v1/data?keys=token,model", log);
        Assert.DoesNotContain("secret-query-value", log);
    }

    [Fact]
    public void ReloadDisposesOutstandingHttpRequests()
    {
        var http = new CapturingHttpService();
        using var runtime = new ScriptRuntime(new ScriptRuntimeServices
        {
            Http = http
        });

        runtime.Reload("""hs.http.get("https://example.test", () => {});""");
        var request = Assert.Single(http.Requests);
        runtime.Reload("""console.log("reloaded");""");

        Assert.True(request.IsDisposed);
    }
}
