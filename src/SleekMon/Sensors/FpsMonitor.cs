using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace SleekMon.Sensors;

/// Frames per second of the program in the foreground, counted like PresentMon from the kernel graphics events
/// (Microsoft-Windows-DxgKrnl): every API (DirectX, Vulkan, OpenGL) and every GPU, nothing injected into the game.
/// Needs administrator rights (real-time ETW session); without them Sample returns null.
/// Events arrive on the session thread; Sample is called once a second by the sensor thread, its only caller.
internal sealed class FpsMonitor : IDisposable {
    /// A crashed run leaves its session behind: the same name stops and recreates it.
    private const string SessionName = "SleekMon-Fps";
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    // Base and Present keywords; the event ID filter, applied by Windows, then delivers Present_Info only
    private const ulong DxgKrnlKeywords = 0x1 | 0x8000000;
    /// Present_Info: one per frame at the end of the kernel present, whatever the API and present path. Flip_Info and
    /// Blit_Info come in addition to it: counting them doubled the rate (RESEARCH § 5).
    private const int PresentInfoId = 184;
    private const double WindowMs = 1000;
    /// Events arrive in batches about once a second: only two samples in a row without any mean the program stalled.
    private const int StalledSamples = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private readonly FpsSettings _settings;
    private readonly HashSet<string> _always;
    private readonly HashSet<string> _never;
    private readonly WindowsGameList _games = new();
    private readonly TraceEventSession? _session;
    private readonly Thread? _thread;
    private volatile bool _running;

    // Shared with the session thread, under _gate
    private readonly Lock _gate = new();
    private readonly Queue<double> _presents = new();
    private volatile int _targetId;
    private int _eventsSinceSample;

    // The foreground program, owned by the sensor thread
    private string? _exePath;
    private string? _exeName;
    private int _samplesSinceSwitch;
    private int _emptySamples;
    private bool _otherShown;
    private int _otherStreak;
    private string? _loggedShownFor;

    public FpsMonitor(FpsSettings settings) {
        _settings = settings;
        _always = Names(settings.Always);
        _never = Names(settings.Never);
        if (!SensorDump.IsAdministrator()) {
            Log.Warn("FPS unavailable: needs administrator rights");
            return;
        }

        try {
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, DxgKrnlKeywords,
                new TraceEventProviderOptions { EventIDsToEnable = [PresentInfoId] });
            _session.Source.AllEvents += OnEvent;
        } catch (Exception e) {
            Log.Error("FPS unavailable", e);
            _session?.Dispose();
            _session = null;
            return;
        }

        _games.Read();
        _running = true;
        var session = _session;
        _thread = new Thread(() => Pump(session)) { IsBackground = true, Name = "FPS" };
        _thread.Start();
    }

    /// FPS to show, or null: rule says hidden, not measured yet after a change of program, or no session.
    public double? Sample() {
        if (!_running) return null;

        var id = ForegroundProcessId();
        if (id != _targetId) Switch(id);
        var fps = Measure();
        var (shown, reason) = Decide(fps);
        LogDecision(shown, reason);
        return shown ? fps : null;
    }

    public void Dispose() {
        _running = false;
        _session?.Dispose();
        if (_thread is { IsAlive: true } && !_thread.Join(TimeSpan.FromSeconds(5))) Log.Warn("FPS thread did not stop");
    }

    private void Pump(TraceEventSession session) {
        try {
            // Blocks until the session stops
            session.Source.Process();
        } catch (Exception e) {
            Log.Error("FPS session failed", e);
        } finally {
            // Stopped by someone else, for instance a second SleekMon with the same session name
            if (_running) Log.Warn("FPS session ended");
            _running = false;
        }
    }

    private void OnEvent(TraceEvent data) {
        var id = data.ProcessID;
        if (id == 0 || id != _targetId || (int)data.ID != PresentInfoId) return;

        var time = data.TimeStampRelativeMSec;
        lock (_gate) {
            if (id != _targetId) return;
            _presents.Enqueue(time);
            // Timestamps of the event, not of its arrival: the batches do not skew the rate
            while (_presents.Peek() <= time - WindowMs) _presents.Dequeue();
            _eventsSinceSample++;
        }
    }

    private void Switch(int id) {
        lock (_gate) {
            _presents.Clear();
            _eventsSinceSample = 0;
            _targetId = id;
        }
        _exePath = id == 0 ? null : ExePath(id);
        _exeName = _exePath == null ? null : WithoutExe(Path.GetFileName(_exePath));
        _samplesSinceSwitch = 0;
        _emptySamples = 0;
        _otherShown = false;
        _otherStreak = 0;
    }

    /// Presents in the last second of events; null during the first second after a change of program (partly covered).
    private double? Measure() {
        int count;
        bool fresh;
        lock (_gate) {
            count = _presents.Count;
            fresh = _eventsSinceSample > 0;
            _eventsSinceSample = 0;
        }
        _samplesSinceSwitch++;
        _emptySamples = fresh ? 0 : _emptySamples + 1;
        if (_emptySamples >= StalledSamples) return 0;
        return _samplesSinceSwitch < 2 ? null : count;
    }

    private (bool Shown, string Reason) Decide(double? fps) {
        if (_exePath == null || _exeName == null) return (false, "no foreground program");
        if (_never.Contains(_exeName)) return (false, "never list");
        // Even at a low rate: a game that struggles is the point of the pill
        if (_always.Contains(_exeName)) return (true, "always list");
        if (_games.Contains(_exePath)) return (true, "Windows game list");

        var above = fps >= _settings.MinFps;
        if (above == _otherShown) {
            _otherStreak = 0;
        } else if (++_otherStreak >= _settings.MinSeconds) {
            _otherShown = above;
            _otherStreak = 0;
        }
        return (_otherShown, _otherShown
            ? $"{_settings.MinFps} FPS for {_settings.MinSeconds} s"
            : $"below {_settings.MinFps} FPS");
    }

    /// One line when the pill appears, disappears or changes program.
    private void LogDecision(bool shown, string reason) {
        var shownFor = shown ? _exePath : null;
        if (shownFor == _loggedShownFor) return;
        _loggedShownFor = shownFor;
        Log.Info(shown ? $"FPS: shown for {Path.GetFileName(_exePath)} ({reason})" : $"FPS: hidden ({reason})");
    }

    private static int ForegroundProcessId() {
        var window = GetForegroundWindow();
        if (window == 0) return 0;
        GetWindowThreadProcessId(window, out var id);
        return id == Environment.ProcessId ? 0 : (int)id;
    }

    /// Full path, as Windows stores it in its game list; null if the process cannot be opened.
    private static string? ExePath(int id) {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)id);
        if (process == 0) return null;
        try {
            var buffer = new char[1024];
            var length = buffer.Length;
            return QueryFullProcessImageName(process, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
        } finally {
            CloseHandle(process);
        }
    }

    private static HashSet<string> Names(string[] names) =>
        names.Select(WithoutExe).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// "GravityMark.exe" → "GravityMark"
    private static string WithoutExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, int flags, [Out] char[] name, ref int length);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
