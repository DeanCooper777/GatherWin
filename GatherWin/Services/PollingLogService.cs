using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GatherWin.Models;

namespace GatherWin.Services;

/// <summary>
/// Manages a rotating polling log: writes timestamped entries to disk and exposes
/// an ObservableCollection for the Log tab UI.
///
/// Thread safety: entries are queued from any thread and flushed to the
/// ObservableCollection in a single batch on the UI thread, avoiding the
/// WPF ItemContainerGenerator desync that occurs with rapid individual adds.
/// </summary>
public class PollingLogService
{
    private const int MaxLogFiles = 10;
    private const string LogPrefix = "PollingLog_";
    private const string LogExtension = ".log";

    private readonly string _logDirectory;
    private readonly object _lock = new();
    private readonly List<PollingLogEntry> _pendingEntries = new();
    private bool _flushScheduled;

    public ObservableCollection<PollingLogEntry> Entries { get; } = new();

    /// <summary>Raised on the UI thread after a batch of entries has been added to <see cref="Entries"/>.</summary>
    public event EventHandler? EntriesFlushed;

    /// <summary>Maximum log file size in kilobytes. Updated from settings.</summary>
    public int MaxLogSizeKB { get; set; } = 256;

    /// <summary>Maximum in-memory entries to keep (prevents unbounded memory growth).</summary>
    private const int MaxInMemoryEntries = 2000;

    public PollingLogService()
    {
        _logDirectory = AppDomain.CurrentDomain.BaseDirectory;
    }

    /// <summary>
    /// Load the current log file (PollingLog_01.log) into memory for display on startup.
    /// Must be called from the UI thread (or before any UI binding is active).
    /// </summary>
    public void LoadExistingLog()
    {
        var path = GetLogFilePath(1);
        if (!File.Exists(path)) return;

        try
        {
            var lines = File.ReadAllLines(path);
            // Add directly — called during init before polling starts
            foreach (var line in lines)
            {
                var entry = ParseLogLine(line);
                if (entry is not null)
                    Entries.Add(entry);
            }
            AppLogger.Log("PollingLog", $"Loaded {Entries.Count} existing log entries");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("PollingLog: failed to load existing log", ex);
        }
    }

    /// <summary>
    /// Write a timestamped entry to the log file and queue it for UI display.
    /// Safe to call from any thread.
    /// </summary>
    public void WriteEntry(string text, LogEntryType type = LogEntryType.None)
    {
        var entry = new PollingLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Message = text,
            EntryType = type
        };

        // Queue for UI update (batched)
        lock (_pendingEntries)
        {
            _pendingEntries.Add(entry);

            if (!_flushScheduled)
            {
                _flushScheduled = true;
                Application.Current?.Dispatcher?.BeginInvoke(FlushPendingEntries,
                    DispatcherPriority.Background);
            }
        }

        // Write to disk immediately
        WriteToDisk(entry);
    }

    /// <summary>
    /// Flush all queued entries into the ObservableCollection in one batch.
    /// Runs on the UI thread via Dispatcher.BeginInvoke.
    /// </summary>
    private void FlushPendingEntries()
    {
        List<PollingLogEntry> batch;
        lock (_pendingEntries)
        {
            batch = new List<PollingLogEntry>(_pendingEntries);
            _pendingEntries.Clear();
            _flushScheduled = false;
        }

        foreach (var entry in batch)
            Entries.Add(entry);

        // Trim oldest entries if we exceed the cap
        while (Entries.Count > MaxInMemoryEntries)
            Entries.RemoveAt(0);

        EntriesFlushed?.Invoke(this, EventArgs.Empty);
    }

    private void WriteToDisk(PollingLogEntry entry)
    {
        lock (_lock)
        {
            try
            {
                var path = GetLogFilePath(1);

                // Check if rotation is needed before writing
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Length >= MaxLogSizeKB * 1024L)
                        RotateLogFiles();
                }

                var line = FormatLogLine(entry);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("PollingLog: failed to write entry", ex);
            }
        }
    }

    /// <summary>
    /// Rotate log files: shift 01→02, 02→03, ..., delete anything beyond MaxLogFiles.
    /// </summary>
    private void RotateLogFiles()
    {
        try
        {
            // Delete the oldest file if it exists
            var oldest = GetLogFilePath(MaxLogFiles);
            if (File.Exists(oldest))
                File.Delete(oldest);

            // Shift files up: 09→10, 08→09, ..., 01→02
            for (int i = MaxLogFiles - 1; i >= 1; i--)
            {
                var src = GetLogFilePath(i);
                var dst = GetLogFilePath(i + 1);
                if (File.Exists(src))
                    File.Move(src, dst);
            }

            AppLogger.Log("PollingLog", "Log files rotated");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("PollingLog: rotation failed", ex);
        }
    }

    private string GetLogFilePath(int index) =>
        Path.Combine(_logDirectory, $"{LogPrefix}{index:D2}{LogExtension}");

    private static string FormatLogLine(PollingLogEntry entry) =>
        $"[{entry.Timestamp.ToLocalTime():HH:mm:ss}] [{entry.EntryType}] {entry.Message}";

    private static PollingLogEntry? ParseLogLine(string line)
    {
        // Expected format: [HH:mm:ss] [Type] Message
        if (line.Length < 12 || line[0] != '[') return null;

        try
        {
            var timeEnd = line.IndexOf(']');
            if (timeEnd < 0) return null;

            var timeStr = line[1..timeEnd];
            var rest = line[(timeEnd + 2)..]; // skip "] "

            var entryType = LogEntryType.None;
            var message = rest;

            if (rest.StartsWith('['))
            {
                var typeEnd = rest.IndexOf(']');
                if (typeEnd > 0)
                {
                    var typeStr = rest[1..typeEnd];
                    if (Enum.TryParse<LogEntryType>(typeStr, out var parsed))
                        entryType = parsed;
                    message = rest[(typeEnd + 2)..]; // skip "] "
                }
            }

            // Parse time — use today's date as base
            if (TimeSpan.TryParse(timeStr, out var timeOfDay))
            {
                var timestamp = new DateTimeOffset(
                    DateTimeOffset.Now.Date + timeOfDay,
                    DateTimeOffset.Now.Offset);

                return new PollingLogEntry
                {
                    Timestamp = timestamp,
                    Message = message,
                    EntryType = entryType
                };
            }
        }
        catch
        {
            // Ignore malformed lines
        }

        return null;
    }
}
