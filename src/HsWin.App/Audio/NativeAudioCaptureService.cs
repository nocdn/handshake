using System.Diagnostics;
using System.IO;
using HsWin.Core.Audio;
using HsWin.Core.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HsWin.App.Audio;

internal sealed class NativeAudioCaptureService : IAudioCaptureService
{
    private readonly string _recordingDirectory;
    private readonly IRuntimeLogger _logger;

    public NativeAudioCaptureService(string recordingDirectory, IRuntimeLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingDirectory);
        _recordingDirectory = recordingDirectory;
        _logger = logger;
    }

    public IAudioRecordingSession Record(AudioRecordingOptions options, Action<AudioCaptureEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        var path = ResolveRecordingPath(options);
        return StartOrDispose(new NativeAudioRecordingSession(ResolveInputDevice(options.DeviceId), path, options, callback, _logger));
    }

    public IDisposable WatchLevels(AudioLevelWatchOptions options, Action<AudioCaptureEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callback);

        return StartOrDispose(new NativeAudioLevelWatch(ResolveInputDevice(options.DeviceId), options, callback, _logger));
    }

    private static T StartOrDispose<T>(T capture) where T : NativeAudioCaptureBase
    {
        try
        {
            capture.Start();
            return capture;
        }
        catch
        {
            // A device that refuses to start must not leave the endpoint, writer or timer behind.
            capture.Dispose();
            throw;
        }
    }

    private static MMDevice ResolveInputDevice(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        return string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
    }

    private string ResolveRecordingPath(AudioRecordingOptions options)
    {
        var path = string.IsNullOrWhiteSpace(options.Path)
            ? Path.Combine(_recordingDirectory, $"recording-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{options.Format.ToFileExtension()}")
            : Environment.ExpandEnvironmentVariables(options.Path);

        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Audio recording path must include a directory.", nameof(options));
        }

        Directory.CreateDirectory(directory);
        if (options.Overwrite || !File.Exists(path))
        {
            return path;
        }

        var extension = Path.GetExtension(path);
        var stem = Path.Combine(directory, Path.GetFileNameWithoutExtension(path));
        for (var suffix = 2; suffix < 10000; suffix++)
        {
            var candidate = $"{stem}-{suffix}{extension}";
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find an unused recording path near '{path}'.");
    }

    private abstract class NativeAudioCaptureBase : IDisposable
    {
        private readonly Action<AudioCaptureEvent> _callback;
        private readonly int _levelIntervalMs;
        private AudioLevelStats _pendingLevels = AudioLevelStats.Empty;
        private long _lastLevelTick;
        private int _disposed;

        protected NativeAudioCaptureBase(
            MMDevice device,
            int levelIntervalMs,
            Action<AudioCaptureEvent> callback,
            IRuntimeLogger logger)
        {
            Device = device;
            DeviceId = device.ID;
            DeviceName = device.FriendlyName;
            _levelIntervalMs = levelIntervalMs;
            _callback = callback;
            Logger = logger;
        }

        protected MMDevice Device { get; }

        // Cached so events raised on the capture thread never touch a disposed COM endpoint.
        protected string DeviceId { get; }

        protected string DeviceName { get; }

        protected WasapiCapture? Capture { get; private set; }

        protected WaveFormat? Format { get; private set; }

        protected IRuntimeLogger Logger { get; }

        protected Stopwatch Stopwatch { get; } = new();

        protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Start()
        {
            Capture = CreateCaptureWithoutSynchronizationContext(Device);
            Format = Capture.WaveFormat;
            Capture.DataAvailable += OnDataAvailable;
            Capture.RecordingStopped += OnRecordingStopped;
            OnCaptureReady(Capture);
            Stopwatch.Start();
            _lastLevelTick = Stopwatch.ElapsedMilliseconds;
            Capture.StartRecording();
            OnStarted();
            Emit(new AudioCaptureEvent(
                AudioCaptureEvent.Started,
                DeviceId,
                DeviceName,
                SampleRate: Format.SampleRate,
                Channels: Format.Channels));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                OnDisposing();
                StopCapture();
            }
            finally
            {
                Capture?.Dispose();
                Device.Dispose();
                OnDisposed();
            }
        }

        protected void StopCapture()
        {
            try
            {
                Capture?.StopRecording();
            }
            catch (InvalidOperationException)
            {
            }
        }

        protected void Emit(AudioCaptureEvent audioEvent)
        {
            try
            {
                _callback(audioEvent);
            }
            catch (Exception exception)
            {
                Logger.Error("Audio capture callback dispatch failed.", exception);
            }
        }

        protected abstract void HandleAudioData(byte[] buffer, int bytesRecorded);

        protected virtual void OnCaptureReady(WasapiCapture capture)
        {
        }

        protected virtual void OnStarted()
        {
        }

        protected virtual void OnDisposing()
        {
        }

        protected virtual void OnDisposed()
        {
        }

        protected virtual void HandleRecordingStopped(Exception? exception)
        {
        }

        /// <summary>
        /// NAudio raises RecordingStopped on the SynchronizationContext captured at construction.
        /// Started from a hotkey that is the UI thread, which would run MP3/AAC encoding there.
        /// </summary>
        private static WasapiCapture CreateCaptureWithoutSynchronizationContext(MMDevice device)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                return new WasapiCapture(device);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
        {
            try
            {
                HandleAudioData(eventArgs.Buffer, eventArgs.BytesRecorded);
                MaybeEmitLevel(eventArgs.Buffer, eventArgs.BytesRecorded);
            }
            catch (Exception exception)
            {
                Logger.Error("Audio capture data handling failed.", exception);
                Emit(new AudioCaptureEvent(
                    AudioCaptureEvent.Error,
                    DeviceId,
                    DeviceName,
                    DurationMs: Stopwatch.Elapsed.TotalMilliseconds,
                    ErrorCode: "capture_failed",
                    Message: exception.Message));
                StopCapture();
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
        {
            Stopwatch.Stop();
            if (Capture is not null)
            {
                Capture.DataAvailable -= OnDataAvailable;
                Capture.RecordingStopped -= OnRecordingStopped;
            }

            HandleRecordingStopped(eventArgs.Exception);
        }

        private void MaybeEmitLevel(byte[] buffer, int bytesRecorded)
        {
            if (_levelIntervalMs <= 0 || Format is null)
            {
                return;
            }

            _pendingLevels = _pendingLevels.Merge(AudioLevelCalculator.Calculate(buffer, bytesRecorded, Format));
            var elapsedMs = Stopwatch.ElapsedMilliseconds;
            if (elapsedMs - _lastLevelTick < _levelIntervalMs)
            {
                return;
            }

            Emit(new AudioCaptureEvent(
                AudioCaptureEvent.Level,
                DeviceId,
                DeviceName,
                SampleRate: Format.SampleRate,
                Channels: Format.Channels,
                DurationMs: Stopwatch.Elapsed.TotalMilliseconds,
                Peak: Math.Round(_pendingLevels.Peak, 4),
                Rms: Math.Round(_pendingLevels.Rms, 4)));

            _pendingLevels = AudioLevelStats.Empty;
            _lastLevelTick = elapsedMs;
        }
    }

    private sealed class NativeAudioRecordingSession : NativeAudioCaptureBase, IAudioRecordingSession
    {
        /// <summary>A canonical PCM WAV header; a file this small never captured audio.</summary>
        private const int WavHeaderBytes = 44;

        private readonly AudioRecordingOptions _options;
        private readonly object _gate = new();
        private System.Threading.Timer? _stopTimer;
        private WaveFileWriter? _writer;
        private string? _workingPath;
        private int _stopped;
        private int _finalized;

        public NativeAudioRecordingSession(
            MMDevice device,
            string path,
            AudioRecordingOptions options,
            Action<AudioCaptureEvent> callback,
            IRuntimeLogger logger)
            : base(device, options.LevelIntervalMs, callback, logger)
        {
            Path = path;
            _options = options;
        }

        public string Path { get; }

        public bool IsRecording => Volatile.Read(ref _stopped) == 0;

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
            {
                return;
            }

            StopCapture();
        }

        protected override void OnCaptureReady(WasapiCapture capture)
        {
            _workingPath = _options.Format == AudioRecordingFormat.Wav
                ? Path
                : System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Path)!,
                    $".{System.IO.Path.GetFileNameWithoutExtension(Path)}-{Guid.NewGuid():N}.wav");
            _writer = new WaveFileWriter(_workingPath, capture.WaveFormat);
            Logger.Info($"Audio recording started format='{_options.Format.ToFileExtension()}'.");
        }

        protected override void OnStarted()
        {
            // Arm the limit only once capture really runs, so a failed start leaves no timer.
            if (_options.MaxDurationMs is { } maxDurationMs)
            {
                _stopTimer = new System.Threading.Timer(_ => Stop(), null, maxDurationMs, Timeout.Infinite);
            }
        }

        protected override void OnDisposing()
        {
            Interlocked.Exchange(ref _stopped, 1);
            _stopTimer?.Dispose();
        }

        protected override void OnDisposed()
        {
            // Disposed before capture stopped normally (or before it ever started): release the
            // file handle and drop intermediate or header-only files.
            if (Interlocked.Exchange(ref _finalized, 1) != 0)
            {
                return;
            }

            CloseWriter();
            DeleteTemporaryWorkingFile();
            DeleteIfHeaderOnly(Path);
        }

        protected override void HandleAudioData(byte[] buffer, int bytesRecorded)
        {
            lock (_gate)
            {
                _writer?.Write(buffer, 0, bytesRecorded);
            }
        }

        protected override void HandleRecordingStopped(Exception? exception)
        {
            if (Interlocked.Exchange(ref _finalized, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _stopped, 1);
            _stopTimer?.Dispose();
            var reason = "stopped";
            try
            {
                CloseWriter();
                if (exception is not null)
                {
                    reason = "error";
                    EmitError("capture_failed", exception.Message);
                }
                else if (_options.Format is AudioRecordingFormat.Mp3 or AudioRecordingFormat.Aac)
                {
                    EncodeRecording();
                }
            }
            catch (Exception finalizeException)
            {
                reason = "error";
                Logger.Error("Audio recording finalization failed.", finalizeException);
                EmitError("finalize_failed", finalizeException.Message);
            }
            finally
            {
                DeleteTemporaryWorkingFile();
                var bytes = File.Exists(Path) ? new FileInfo(Path).Length : 0;
                Logger.Info($"Audio recording stopped reason='{reason}' bytes={bytes} durationMs={Stopwatch.Elapsed.TotalMilliseconds:F3}.");
                Emit(new AudioCaptureEvent(
                    AudioCaptureEvent.Stopped,
                    DeviceId,
                    DeviceName,
                    Path,
                    _options.Format.ToFileExtension(),
                    Format?.SampleRate,
                    Format?.Channels,
                    bytes,
                    Stopwatch.Elapsed.TotalMilliseconds,
                    Reason: reason));
            }
        }

        private void EmitError(string errorCode, string message)
        {
            Emit(new AudioCaptureEvent(
                AudioCaptureEvent.Error,
                DeviceId,
                DeviceName,
                Path,
                Format: _options.Format.ToFileExtension(),
                DurationMs: Stopwatch.Elapsed.TotalMilliseconds,
                ErrorCode: errorCode,
                Message: message));
        }

        private void CloseWriter()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void EncodeRecording()
        {
            if (string.IsNullOrWhiteSpace(_workingPath))
            {
                throw new InvalidOperationException("Audio recording has no working file.");
            }

            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            using var reader = new AudioFileReader(_workingPath);
            var bitrate = _options.BitrateKbps * 1000;
            if (_options.Format == AudioRecordingFormat.Mp3)
            {
                MediaFoundationEncoder.EncodeToMp3(reader, Path, bitrate);
            }
            else
            {
                MediaFoundationEncoder.EncodeToAac(reader, Path, bitrate);
            }
        }

        private void DeleteTemporaryWorkingFile()
        {
            if (_options.Format != AudioRecordingFormat.Wav && !string.IsNullOrWhiteSpace(_workingPath))
            {
                TryDelete(_workingPath);
            }
        }

        private static void DeleteIfHeaderOnly(string path)
        {
            if (File.Exists(path) && new FileInfo(path).Length <= WavHeaderBytes)
            {
                TryDelete(path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class NativeAudioLevelWatch : NativeAudioCaptureBase
    {
        public NativeAudioLevelWatch(
            MMDevice device,
            AudioLevelWatchOptions options,
            Action<AudioCaptureEvent> callback,
            IRuntimeLogger logger)
            : base(device, options.IntervalMs, callback, logger)
        {
        }

        protected override void HandleAudioData(byte[] buffer, int bytesRecorded)
        {
        }

        protected override void HandleRecordingStopped(Exception? exception)
        {
            if (exception is not null && !IsDisposed)
            {
                Emit(new AudioCaptureEvent(
                    AudioCaptureEvent.Error,
                    DeviceId,
                    DeviceName,
                    DurationMs: Stopwatch.Elapsed.TotalMilliseconds,
                    ErrorCode: "levels_failed",
                    Message: exception.Message));
            }
        }
    }
}
