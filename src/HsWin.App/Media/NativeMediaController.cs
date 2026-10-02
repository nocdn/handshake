using System.ComponentModel;
using System.Diagnostics;
using HsWin.App.Native;
using HsWin.Core.Keyboard;
using HsWin.Core.Logging;
using HsWin.Core.Media;
using Windows.Foundation;
using Windows.Media.Control;

namespace HsWin.App.Media;

internal sealed class NativeMediaController : IMediaController
{
    private const ushort VkMediaNextTrack = 0xB0;
    private const ushort VkMediaPreviousTrack = 0xB1;
    private const ushort VkMediaPlayPause = 0xB3;

    /// <summary>A media session that has not answered by now is treated as hung; the media key is sent instead.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Players publish the new playback status shortly after a toggle, not synchronously with it.
    /// Waiting briefly reports the real result instead of the stale pre-toggle status.
    /// </summary>
    private static readonly TimeSpan StatusSettleTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(25);

    private readonly IRuntimeLogger _logger;
    private readonly object _managerGate = new();
    private Task<GlobalSystemMediaTransportControlsSessionManager>? _managerRequest;

    public NativeMediaController(IRuntimeLogger logger)
    {
        _logger = logger;
    }

    public MediaCommandResult PlayPause()
    {
        return ControlCurrentSession(
            "playPause",
            static session => session.TryTogglePlayPauseAsync(),
            fallbackVirtualKey: VkMediaPlayPause,
            waitForStatusChange: true,
            inferAction: InferPlayPauseAction);
    }

    public MediaCommandResult PreviousTrack()
    {
        return ControlCurrentSession(
            "previousTrack",
            static session => session.TrySkipPreviousAsync(),
            fallbackVirtualKey: VkMediaPreviousTrack,
            waitForStatusChange: false,
            inferAction: static (_, _) => "previousTrack");
    }

    public MediaCommandResult NextTrack()
    {
        return ControlCurrentSession(
            "nextTrack",
            static session => session.TrySkipNextAsync(),
            fallbackVirtualKey: VkMediaNextTrack,
            waitForStatusChange: false,
            inferAction: static (_, _) => "nextTrack");
    }

    /// <summary>
    /// The action a toggle performed. A status that changed is authoritative; an unchanged one
    /// means the player has not reported yet, so the action is the inverse of the old status.
    /// </summary>
    internal static string InferPlayPauseAction(string statusBefore, string statusAfter)
    {
        if (statusAfter != statusBefore)
        {
            if (statusAfter is "playing")
            {
                return "played";
            }

            if (statusAfter is "paused" or "stopped")
            {
                return "paused";
            }
        }

        return statusBefore switch
        {
            "playing" => "paused",
            "paused" or "stopped" => "played",
            _ => "toggled"
        };
    }

    private MediaCommandResult ControlCurrentSession(
        string command,
        Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> sessionAction,
        ushort fallbackVirtualKey,
        bool waitForStatusChange,
        Func<string, string, string> inferAction)
    {
        try
        {
            var session = GetSessionManager().GetCurrentSession();
            if (session is not null)
            {
                var statusBefore = GetPlaybackStatus(session);
                var success = Await(sessionAction(session).AsTask(), command);
                var statusAfter = success && waitForStatusChange
                    ? WaitForStatusChange(session, statusBefore)
                    : GetPlaybackStatus(session);
                var action = inferAction(statusBefore, statusAfter);
                return new MediaCommandResult(command, success, action, statusBefore, statusAfter, "mediaSession");
            }
        }
        catch (Exception exception)
        {
            // The cached manager may be the problem (for example after Explorer restarted).
            ForgetSessionManager();
            _logger.Warning($"Media session command '{command}' failed; sending the media key instead. {exception.Message}");
        }

        SendMediaKey(fallbackVirtualKey, command);
        return MediaCommandResult.Sent(command, "sendInput");
    }

    /// <summary>
    /// Requests the session manager once and reuses it; the first request costs tens of
    /// milliseconds. Any session failure forgets it so the next command requests a fresh one.
    /// </summary>
    private GlobalSystemMediaTransportControlsSessionManager GetSessionManager()
    {
        Task<GlobalSystemMediaTransportControlsSessionManager> request;
        lock (_managerGate)
        {
            request = _managerRequest ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        }

        return Await(request, "session manager request");
    }

    private void ForgetSessionManager()
    {
        lock (_managerGate)
        {
            _managerRequest = null;
        }
    }

    private static T Await<T>(Task<T> task, string description)
    {
        if (!task.Wait(SessionTimeout))
        {
            throw new TimeoutException(
                $"Windows media session did not answer the {description} within {SessionTimeout.TotalSeconds:0}s.");
        }

        return task.GetAwaiter().GetResult();
    }

    private static string WaitForStatusChange(GlobalSystemMediaTransportControlsSession session, string statusBefore)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(StatusSettleTimeout.TotalSeconds * Stopwatch.Frequency);
        var status = GetPlaybackStatus(session);
        while (status == statusBefore && Stopwatch.GetTimestamp() < deadline)
        {
            Thread.Sleep(StatusPollInterval);
            status = GetPlaybackStatus(session);
        }

        return status;
    }

    private static string GetPlaybackStatus(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return NormalizePlaybackStatus(session.GetPlaybackInfo().PlaybackStatus);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return "unknown";
        }
    }

    private static string NormalizePlaybackStatus(GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        return status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => "closed",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => "opened",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => "changing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "stopped",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "paused",
            _ => "unknown"
        };
    }

    private void SendMediaKey(ushort virtualKey, string command)
    {
        // Media keys are defined by their virtual key; the shell handles them without a scan code.
        var isExtended = KeyboardKeyRules.IsExtendedVirtualKey(virtualKey);
        ReadOnlySpan<NativeInput> inputs =
        [
            NativeInputSender.VirtualKey(virtualKey, keyUp: false, isExtended),
            NativeInputSender.VirtualKey(virtualKey, keyUp: true, isExtended)
        ];

        if (!NativeInputSender.TrySend(inputs, out _, out var error))
        {
            var exception = new Win32Exception(error, $"Could not send the media key for {command}.");
            _logger.Error($"Media key send failed command='{command}' vk=0x{virtualKey:X2}.", exception);
            throw exception;
        }
    }
}
