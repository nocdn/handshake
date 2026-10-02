using System.Globalization;
using HsWin.Core.Audio;
using HsWin.Core.Logging;
using Microsoft.ClearScript;

namespace HsWin.Core.Scripting;

public sealed class AudioCaptureScriptApi
{
    private readonly IAudioCaptureService _audioCapture;
    private readonly IRuntimeLogger _logger;
    private readonly IScriptCallbackScheduler _callbackScheduler;
    private readonly ScriptCallbackInvoker _callbacks;
    private readonly Action<IDisposable> _trackResource;

    internal AudioCaptureScriptApi(
        IAudioCaptureService audioCapture,
        IRuntimeLogger logger,
        IScriptCallbackScheduler callbackScheduler,
        ScriptCallbackInvoker callbacks,
        Action<IDisposable> trackResource)
    {
        _audioCapture = audioCapture;
        _logger = logger;
        _callbackScheduler = callbackScheduler;
        _callbacks = callbacks;
        _trackResource = trackResource;
    }

    public AudioRecordingScriptHandle Record(object? options, object? callback)
    {
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Audio record callback must be a JavaScript function.", nameof(callback));
        }

        var parsedOptions = ParseRecordingOptions(options);
        var relay = new ScriptCallbackRelay(scriptFunction, _callbackScheduler, _callbacks);
        var session = _audioCapture.Record(parsedOptions, audioEvent => Deliver(relay, audioEvent));
        var handle = relay.AttachHandle(new AudioRecordingScriptHandle(session, relay));
        _trackResource(handle);
        _logger.Info(
            $"Script hs.audio.record() started format='{parsedOptions.Format.ToFileExtension()}' " +
            $"levelIntervalMs={parsedOptions.LevelIntervalMs.ToString(CultureInfo.InvariantCulture)}.");
        return handle;
    }

    public ScriptResourceHandle WatchLevels(object? options, object? callback)
    {
        if (callback is not ScriptObject scriptFunction)
        {
            throw new ArgumentException("Audio level callback must be a JavaScript function.", nameof(callback));
        }

        var parsedOptions = ParseLevelWatchOptions(options);
        var relay = new ScriptCallbackRelay(scriptFunction, _callbackScheduler, _callbacks);
        var watch = _audioCapture.WatchLevels(parsedOptions, audioEvent => relay.Post(ScriptJson.Serialize(audioEvent)));
        var handle = relay.Attach(watch);
        _trackResource(handle);
        _logger.Info($"Script hs.audio.levels() started intervalMs={parsedOptions.IntervalMs.ToString(CultureInfo.InvariantCulture)}.");
        return handle;
    }

    private static void Deliver(ScriptCallbackRelay relay, AudioCaptureEvent audioEvent)
    {
        var eventJson = ScriptJson.Serialize(audioEvent);
        if (audioEvent.IsTerminal)
        {
            relay.PostFinal(eventJson);
        }
        else
        {
            relay.Post(eventJson);
        }
    }

    public static AudioRecordingOptions ParseRecordingOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return AudioRecordingOptions.Default;
        }

        if (!ScriptArgumentReader.IsOptionsObject(value))
        {
            var pathText = ScriptArgumentReader.OptionalString(value);
            return AudioRecordingOptions.Default with
            {
                Path = pathText,
                Format = InferFormat(pathText, null)
            };
        }

        var path = ScriptArgumentReader.OptionalString(
            ScriptArgumentReader.GetPropertyValue(value, "path", "file", "filename", "output"));
        var formatText = ScriptArgumentReader.OptionalString(
            ScriptArgumentReader.GetPropertyValue(value, "format", "type", "container"));
        var levelIntervalValue = ScriptArgumentReader.GetPropertyValue(value, "levelIntervalMs", "levelInterval", "meterIntervalMs");
        var maxDurationValue = ScriptArgumentReader.GetPropertyValue(value, "maxDurationMs", "durationMs", "stopAfterMs");
        var bitrateValue = ScriptArgumentReader.GetPropertyValue(value, "bitrateKbps", "bitrate");
        var quality = ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "quality"));

        return new AudioRecordingOptions(
            ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "deviceId", "device", "inputDeviceId")),
            path,
            InferFormat(path, formatText),
            ScriptArgumentReader.OptionalBoolean(
                ScriptArgumentReader.GetPropertyValue(value, "overwrite", "replace"),
                "overwrite",
                AudioRecordingOptions.Default.Overwrite),
            ConvertBitrateKbps(bitrateValue, quality),
            ConvertLevelInterval(levelIntervalValue, AudioRecordingOptions.DefaultLevelIntervalMs),
            ConvertOptionalPositiveInt(maxDurationValue, "maxDurationMs"));
    }

    public static AudioLevelWatchOptions ParseLevelWatchOptions(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return AudioLevelWatchOptions.Default;
        }

        if (!ScriptArgumentReader.IsOptionsObject(value))
        {
            return AudioLevelWatchOptions.Default with
            {
                DeviceId = ScriptArgumentReader.OptionalString(value)
            };
        }

        var intervalValue = ScriptArgumentReader.GetPropertyValue(value, "intervalMs", "interval", "levelIntervalMs");
        return new AudioLevelWatchOptions(
            ScriptArgumentReader.OptionalString(ScriptArgumentReader.GetPropertyValue(value, "deviceId", "device", "inputDeviceId")),
            ConvertLevelWatchInterval(intervalValue));
    }

    private static AudioRecordingFormat InferFormat(string? path, string? formatText)
    {
        var normalized = string.IsNullOrWhiteSpace(formatText)
            ? Path.GetExtension(path ?? string.Empty).TrimStart('.')
            : formatText;

        return normalized.Trim().ToLowerInvariant() switch
        {
            "" => AudioRecordingOptions.Default.Format,
            "wave" or "wav" => AudioRecordingFormat.Wav,
            "mp3" => AudioRecordingFormat.Mp3,
            "m4a" or "aac" => AudioRecordingFormat.Aac,
            _ => throw new ArgumentException("Audio recording format must be wav, mp3, m4a, or aac.", nameof(formatText))
        };
    }

    private static int ConvertBitrateKbps(object? value, string? quality)
    {
        if (!ScriptArgumentReader.IsMissing(value))
        {
            var bitrate = ScriptArgumentReader.RequireInt32(value, "bitrateKbps", "a bitrate in kbps");
            if (bitrate is < AudioRecordingOptions.MinimumBitrateKbps or > AudioRecordingOptions.MaximumBitrateKbps)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"bitrateKbps must be between {AudioRecordingOptions.MinimumBitrateKbps} and {AudioRecordingOptions.MaximumBitrateKbps}.");
            }

            return bitrate;
        }

        return quality?.Trim().ToLowerInvariant() switch
        {
            null or "" => AudioRecordingOptions.DefaultBitrateKbps,
            "low" => 96,
            "medium" or "normal" => 160,
            "high" => 256,
            _ => throw new ArgumentException("Audio recording quality must be low, medium, or high.", nameof(quality))
        };
    }

    private static int ConvertLevelInterval(object? value, int defaultValue)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return defaultValue;
        }

        var interval = ScriptArgumentReader.RequireInt32(value, "levelIntervalMs", "a number of milliseconds");
        if (interval == 0)
        {
            return 0;
        }

        if (interval is < AudioRecordingOptions.MinimumLevelIntervalMs or > AudioRecordingOptions.MaximumLevelIntervalMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"levelIntervalMs must be 0 or between {AudioRecordingOptions.MinimumLevelIntervalMs} and {AudioRecordingOptions.MaximumLevelIntervalMs}.");
        }

        return interval;
    }

    private static int ConvertLevelWatchInterval(object? value)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return AudioLevelWatchOptions.DefaultIntervalMs;
        }

        var interval = ScriptArgumentReader.RequireInt32(value, "intervalMs", "a number of milliseconds");
        if (interval is < AudioLevelWatchOptions.MinimumIntervalMs or > AudioLevelWatchOptions.MaximumIntervalMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"intervalMs must be between {AudioLevelWatchOptions.MinimumIntervalMs} and {AudioLevelWatchOptions.MaximumIntervalMs}.");
        }

        return interval;
    }

    private static int? ConvertOptionalPositiveInt(object? value, string argumentName)
    {
        if (ScriptArgumentReader.IsMissing(value))
        {
            return null;
        }

        var result = ScriptArgumentReader.RequireInt32(value, argumentName, "a positive integer");
        if (result < 1)
        {
            throw new ArgumentOutOfRangeException(argumentName, $"{argumentName} must be at least 1.");
        }

        return result;
    }

}
