using System.IO;

namespace GatherWin.Services;

/// <summary>
/// Simple file logger for debugging. Writes to GatherWin.log next to the executable.
/// </summary>
public static class AppLogger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "GatherWin.log");

    private static readonly object Lock = new();

    static AppLogger()
    {
        // Start fresh each run
        try { File.WriteAllText(LogPath, $"=== GatherWin started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n"); }
        catch { /* ignore */ }
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (Lock)
        {
            try { File.AppendAllText(LogPath, line + "\n"); }
            catch { /* ignore */ }
        }
    }

    public static void Log(string category, string message) =>
        Log($"[{category}] {message}");

    public static void LogError(string message, Exception? ex = null)
    {
        var line = ex is not null
            ? $"[{DateTime.Now:HH:mm:ss.fff}] ERROR: {message} — {ex.GetType().Name}: {ex.Message}"
            : $"[{DateTime.Now:HH:mm:ss.fff}] ERROR: {message}";
        lock (Lock)
        {
            try { File.AppendAllText(LogPath, line + "\n"); }
            catch { /* ignore */ }
        }
    }
}
