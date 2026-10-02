using HsWin.Core.Applications;
using HsWin.Core.Http;
using HsWin.Core.Scripting;
using HsWin.Core.Shell;
using HsHttpRequestOptions = HsWin.Core.Http.HttpRequestOptions;

namespace HsWin.Core.Tests;

internal sealed class CapturingApplicationProvider : IApplicationProvider
{
    private readonly IReadOnlyList<ApplicationSnapshot> _applications;

    public CapturingApplicationProvider(params ApplicationSnapshot[] applications)
    {
        _applications = applications;
    }

    public bool IsRunning(string processName)
    {
        var normalizedName = ProcessNameMatcher.Normalize(processName);
        return _applications.Any(application =>
            string.Equals(application.ProcessName, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public bool? LastIncludeDetails { get; private set; }

    public IReadOnlyList<ApplicationSnapshot> GetRunningApplications(bool includeDetails)
    {
        LastIncludeDetails = includeDetails;
        return _applications;
    }
}

internal sealed class CapturingShellService : IShellService
{
    public List<CapturingExecution> Executions { get; } = [];

    public List<CapturingLaunch> Launches { get; } = [];

    public ShellExecutionResult ExecuteResult { get; init; } =
        new("command", true, 0, string.Empty, string.Empty, TimedOut: false);

    public LaunchResult LaunchResult { get; init; } =
        new("target", true, 123, null);

    public ShellExecutionResult Execute(string command, ShellExecutionOptions options, CancellationToken cancellationToken = default)
    {
        Executions.Add(new CapturingExecution(command, options));
        return ExecuteResult;
    }

    public LaunchResult Launch(string target, LaunchOptions options)
    {
        Launches.Add(new CapturingLaunch(target, options));
        return LaunchResult;
    }
}

internal sealed record CapturingExecution(string Command, ShellExecutionOptions Options);

internal sealed record CapturingLaunch(string Target, LaunchOptions Options);

internal sealed class CapturingHttpService : IHttpService
{
    public List<CapturingHttpRequest> Requests { get; } = [];

    public IDisposable Send(HsHttpRequestOptions options, Action<HttpResponseSnapshot> callback)
    {
        var request = new CapturingHttpRequest(options, callback);
        Requests.Add(request);
        return request;
    }
}

internal sealed class CapturingHttpRequest : IDisposable
{
    private readonly Action<HttpResponseSnapshot> _callback;

    public CapturingHttpRequest(HsHttpRequestOptions options, Action<HttpResponseSnapshot> callback)
    {
        Options = options;
        _callback = callback;
    }

    public HsHttpRequestOptions Options { get; }

    public bool IsDisposed { get; private set; }

    public void Emit(HttpResponseSnapshot response)
    {
        _callback(response);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}

internal sealed class QueuedScriptCallbackScheduler : IScriptCallbackScheduler
{
    private readonly Queue<Action> _callbacks = [];
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _hasCallback = new();

    public void Schedule(Action callback)
    {
        lock (_gate)
        {
            _callbacks.Enqueue(callback);
            _hasCallback.Set();
        }
    }

    public bool WaitForCallback(TimeSpan timeout)
    {
        return _hasCallback.Wait(timeout);
    }

    public void RunNext()
    {
        Action callback;
        lock (_gate)
        {
            callback = _callbacks.Dequeue();
            if (_callbacks.Count == 0)
            {
                _hasCallback.Reset();
            }
        }

        callback();
    }
}
