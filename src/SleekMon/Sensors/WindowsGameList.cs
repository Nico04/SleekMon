using System.Diagnostics;
using System.Globalization;
using System.Security;
using Microsoft.Win32;

namespace SleekMon.Sensors;

/// Games Windows recognised (Game Bar, Game Mode), by exe path: value MatchedExeFullPath of each subkey of
/// HKCU\System\GameConfigStore\Children. Undocumented, checked on Windows 11 (RESEARCH § 5): a missing key or a changed
/// format gives an empty list. Windows adds a game when it is first launched: an unknown program re-reads the list, at
/// most once a minute. Not thread-safe: owned by the sensor thread.
internal sealed class WindowsGameList {
    private const string ChildrenKey = @"System\GameConfigStore\Children";
    private static readonly TimeSpan RefreshPeriod = TimeSpan.FromMinutes(1);

    private readonly Stopwatch _sinceRead = new();
    private HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private int _loggedCount = -1;

    public bool Contains(string exePath) {
        if (_paths.Contains(exePath)) return true;
        if (_sinceRead.IsRunning && _sinceRead.Elapsed < RefreshPeriod) return false;
        Read();
        return _paths.Contains(exePath);
    }

    public void Read() {
        var stopwatch = Stopwatch.StartNew();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            using var children = Registry.CurrentUser.OpenSubKey(ChildrenKey);
            if (children != null) {
                foreach (var name in children.GetSubKeyNames()) {
                    using var child = children.OpenSubKey(name);
                    if (child?.GetValue("MatchedExeFullPath") is string { Length: > 0 } path) paths.Add(path);
                }
            }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) {
            Log.Warn($"Windows game list unreadable: {e.Message}");
        }
        _paths = paths;
        _sinceRead.Restart();

        // Logged when it changes only: an unknown program in front re-reads it every minute
        if (paths.Count == _loggedCount) return;
        _loggedCount = paths.Count;
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"Windows game list: {paths.Count} games, read in {stopwatch.Elapsed.TotalMilliseconds:F1} ms"));
    }
}
