using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using HsWin.Core.Logging;

namespace HsWin.App;

internal sealed class FileLogger : IRuntimeLogger, IDisposable
{
    private const int MaxQueuedEntries = 20_000;

    private readonly string _logFilePath;
    private readonly BlockingCollection<LogEntry> _entries = new(MaxQueuedEntries);
    private readonly object _fallbackGate = new();
    private readonly Thread _worker;
    private StreamWriter? _writer;
    private int _firstUnwrittenIndex;
    private int _disposed;
    private int _dropped;

    private FileLogger(string logFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        _logFilePath = logFilePath;
        EnsureLogFile();
        _worker = new Thread(WriteQueuedEntries)
        {
            IsBackground = true,
            Name = "HsWin Runtime Logger"
        };
        WriteFallback(new LogEntry(DateTimeOffset.Now, "INFO", "Runtime logger initialized.", null));
        _worker.Start();
    }

    public string LogFilePath => _logFilePath;

    public static FileLogger CreateForLaunch(string logDirectory)
    {
        return new FileLogger(DatedLogFileName.CreateUniquePath(logDirectory, DateTimeOffset.Now));
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Warning(string message)
    {
        Write("WARN", message, null);
    }

    public void Error(string message, Exception exception)
    {
        Write("ERROR", message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            // A flood (a script logging in a tight loop) must not grow memory without bound.
            if (!_entries.TryAdd(new LogEntry(DateTimeOffset.Now, level, message, exception)))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
        catch
        {
            WriteFallback(new LogEntry(DateTimeOffset.Now, "ERROR", "Runtime logger queue write failed.", null));
            // Logging must never break the tray app or script reload loop.
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
            // Bounded: if the worker is stuck mid-write on the gate, a blocking lock here
            // would hang shutdown past the Join timeout above.
            if (Monitor.TryEnter(_fallbackGate, TimeSpan.FromSeconds(2)))
            {
                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                    _writer = null;
                }
                finally
                {
                    Monitor.Exit(_fallbackGate);
                }
            }

            _entries.Dispose();
        }
    }

    private void EnsureLogFile()
    {
        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var _ = new FileStream(
            _logFilePath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite);
    }

    private void WriteQueuedEntries()
    {
        try
        {
            foreach (var entry in _entries.GetConsumingEnumerable())
            {
                WriteBatch(entry);
            }
        }
        catch (Exception exception)
        {
            WriteFallback(new LogEntry(DateTimeOffset.Now, "ERROR", "Runtime logger worker failed.", exception));
            // Logging must never break the tray app or script reload loop.
        }
    }

    private void WriteBatch(LogEntry firstEntry)
    {
        var entries = new List<LogEntry> { firstEntry };
        while (_entries.TryTake(out var nextEntry))
        {
            entries.Add(nextEntry);
        }

        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            entries.Add(new LogEntry(
                DateTimeOffset.Now,
                "WARN",
                $"Dropped {dropped} log entries because logging fell behind.",
                null));
        }

        lock (_fallbackGate)
        {
            // After Dispose, never resurrect the long-lived writer; drain via the fallback.
            if (Volatile.Read(ref _disposed) != 0)
            {
                foreach (var entry in entries)
                {
                    WriteFallback(entry);
                }

                return;
            }

            try
            {
                // One long-lived writer instead of open/flush/close per batch: under sparse
                // logging that was one file-open cycle per line. The writer is flushed when
                // the queue drains so entries reach disk without Dispose.
                _writer ??= CreateWriter();
                for (var index = 0; index < entries.Count; index++)
                {
                    WriteEntry(_writer, entries[index]);

                    // Entries before this point may already be in the writer buffer; only
                    // this and later entries go through the fallback if a write fails, so
                    // lines are never duplicated.
                    _firstUnwrittenIndex = index + 1;
                }

                _writer.Flush();
                _firstUnwrittenIndex = 0;
            }
            catch
            {
                // Drop the long-lived writer; entries not yet handed to it go through the
                // open/close fallback so nothing is lost, and the next batch recreates it.
                _writer?.Dispose();
                _writer = null;
                for (var index = _firstUnwrittenIndex; index < entries.Count; index++)
                {
                    WriteFallback(entries[index]);
                }

                _firstUnwrittenIndex = 0;
            }
        }
    }

    private StreamWriter CreateWriter()
    {
        var stream = new FileStream(
            _logFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        return new StreamWriter(stream);
    }

    private void WriteFallback(LogEntry entry)
    {
        try
        {
            lock (_fallbackGate)
            {
                using var stream = new FileStream(
                    _logFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                WriteEntry(writer, entry);
                writer.Flush();
            }
        }
        catch
        {
            // Logging must never break the tray app or script reload loop.
        }
    }

    private static void WriteEntry(TextWriter writer, LogEntry entry)
    {
        writer.Write(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        writer.Write(" [");
        writer.Write(entry.Level);
        writer.Write("] ");
        writer.WriteLine(entry.Message);
        if (entry.Exception is not null)
        {
            writer.WriteLine(entry.Exception);
        }
    }

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string Message,
        Exception? Exception);
}
