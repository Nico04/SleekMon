using System.Diagnostics;
using System.Globalization;

namespace SleekMon;

/// Minimal thread-safe file logger: one file per day in %LocalAppData%\SleekMon\logs, the most recent ones kept.
/// Several processes (tray app, diagnostic commands) may append to the same file.
internal static class Log {
    private const int KeptFiles = 7;

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SleekMon", "logs");

    private static readonly Lock Gate = new();
    private static StreamWriter? _writer;
    private static DateOnly _writerDay;

    public static void Info(string message) => Write("INF", message);

    public static void Warn(string message) => Write("WRN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERR", exception == null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message) {
        var now = DateTime.Now;
        var line = $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} [{Environment.CurrentManagedThreadId}] {message}";
        Debug.WriteLine(line);

        lock (Gate) {
            // Logging must never crash the app
            try {
                GetWriter(DateOnly.FromDateTime(now)).WriteLine(line);
            } catch (IOException e) {
                Debug.WriteLine($"Log write failed: {e.Message}");
            } catch (UnauthorizedAccessException e) {
                Debug.WriteLine($"Log write failed: {e.Message}");
            }
        }
    }

    private static StreamWriter GetWriter(DateOnly day) {
        if (_writer != null && day == _writerDay) return _writer;

        _writer?.Dispose();
        _writer = null;
        Directory.CreateDirectory(LogDirectory);
        DeleteOldFiles();

        var path = Path.Combine(LogDirectory, $"sleekmon-{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _writerDay = day;
        return _writer;
    }

    /// Keeps room for the file about to be opened.
    private static void DeleteOldFiles() {
        var oldFiles = new DirectoryInfo(LogDirectory)
            .GetFiles("sleekmon-*.log")
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(KeptFiles - 1);
        foreach (var file in oldFiles) {
            file.Delete();
        }
    }
}
