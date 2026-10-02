using System.Collections.Concurrent;

namespace HsWin.Core.Logging;

/// <summary>
/// Console logger for script output: one file per config reload under the configured directory.
/// Writes are queued and drained by a background worker so a console.log inside a timer or
/// hotkey callback never performs disk I/O on the calling thread (script callbacks run on the
/// UI dispatcher while holding the global script callback gate). The current log path is still
/// assigned synchronously in <see cref="BeginReload"/> so callers can surface it immediately.
/// </summary>
public sealed class ReloadScriptConsoleLogger : IScriptConsoleLogger, IDisposable
{
    private const int MaxQueuedEntries = 20_000;

    private readonly string _logDirectory;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly BlockingCollection<Entry> _entries = new(MaxQueuedEntries);
    private readonly Thread _worker;

    private string? _currentLogFilePath;
    private int _disposed;
    private int _dropped;

    public ReloadScriptConsoleLogger(string logDirectory)
        : this(logDirectory, () => DateTimeOffset.Now)
    {
    }

    public ReloadScriptConsoleLogger(string logDirectory, Func<DateTimeOffset> now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(now);
        _logDirectory = logDirectory;
        _now = now;
        _worker = new Thread(WriteQueuedEntries)
        {
            IsBackground = true,
            Name = "HsWin Script Console Logger"
        };
        _worker.Start();
    }

    public string? CurrentLogFilePath
    {
        get
        {
            lock (_gate)
            {
                return _currentLogFilePath;
            }
        }
    }

    public void BeginReload(string documentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_gate)
        {
            var logFilePath = DatedLogFileName.CreateUniquePath(_logDirectory, _now());
            _currentLogFilePath = logFilePath;
            // Create the file eagerly so its name stays claimed (uniqueness probing) and the
            // path is openable the moment BeginReload returns. All content — including the
            // rotation away from a previous file — is written by the worker in order.
            Directory.CreateDirectory(_logDirectory);
            using var _ = new FileStream(
                logFilePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite);
            Enqueue(new RotateEntry(logFilePath, $"{_now():O} [reload] Started {documentName}"));
        }
    }

    public void Write(string level, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_currentLogFilePath is null)
            {
                BeginReload("config.js");
            }

            Enqueue(new LineEntry(_currentLogFilePath!, $"{_now():O} [{level}] {message}"));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _entries.CompleteAdding();
            _worker.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Logging shutdown must not block app shutdown.
        }
        finally
        {
            _entries.Dispose();
        }
    }

    private void Enqueue(Entry entry)
    {
        try
        {
            // A flood (console.log in a tight loop) must not grow memory without bound.
            if (Volatile.Read(ref _disposed) == 0 && !_entries.TryAdd(entry))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
        catch
        {
            // Logging must never break the tray app or script reload loop.
        }
    }

    private void WriteQueuedEntries()
    {
        WriterState state = new(null, null);
        try
        {
            foreach (var entry in _entries.GetConsumingEnumerable())
            {
                state = ApplyEntry(state, entry);
                while (_entries.TryTake(out var nextEntry))
                {
                    state = ApplyEntry(state, nextEntry);
                }

                var dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0 && state.LogFilePath is { } currentPath)
                {
                    state = ApplyEntry(
                        state,
                        new LineEntry(currentPath, $"{_now():O} [warn] Dropped {dropped} console lines because logging fell behind."));
                }

                // Queue momentarily drained: flush so recently logged lines are on disk even
                // if the process exits without Dispose.
                state.Writer?.Flush();
            }
        }
        catch
        {
            // Logging must never break the tray app or script reload loop.
        }
        finally
        {
            state.Writer?.Dispose();
        }
    }

    private WriterState ApplyEntry(WriterState state, Entry entry)
    {
        var rotate = entry as RotateEntry;
        var line = rotate?.Line ?? ((LineEntry)entry).Line;
        var logFilePath = rotate?.LogFilePath ?? ((LineEntry)entry).LogFilePath;
        var writer = state.Writer;
        try
        {
            if (writer is null || rotate is not null || state.LogFilePath != logFilePath)
            {
                writer?.Flush();
                writer?.Dispose();
                writer = null;
                writer = OpenWriter(logFilePath);
            }

            writer.WriteLine(line);
            return new WriterState(writer, logFilePath);
        }
        catch
        {
            // A failed writer must not wedge the worker or leak its handle (including one that
            // opened but failed on its first write); drop it, retry the line once through a
            // one-shot append, and let the next entry recreate the writer.
            writer?.Dispose();
            try
            {
                File.AppendAllText(logFilePath, line + Environment.NewLine);
                return new WriterState(null, logFilePath);
            }
            catch
            {
                // Logging must never break the tray app or script reload loop.
                return new WriterState(null, null);
            }
        }
    }

    private sealed record WriterState(StreamWriter? Writer, string? LogFilePath);

    private StreamWriter OpenWriter(string logFilePath)
    {
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        try
        {
            return new StreamWriter(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private abstract record Entry;

    private sealed record LineEntry(string LogFilePath, string Line) : Entry;

    private sealed record RotateEntry(string LogFilePath, string Line) : Entry;
}
