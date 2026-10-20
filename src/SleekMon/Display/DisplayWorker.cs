using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SkiaSharp;

namespace SleekMon.Display;

internal enum ConnectionState {
    Disconnected,
    Connecting,
    Connected
}

internal sealed record DisplayWorkerOptions {
    /// Null: found from the registry.
    public string? ForcedPort { get; init; }
    public RevAOptions Serial { get; init; } = new();
    public Orientation Orientation { get; init; } = Orientation.Landscape;
    /// 16×16 with rectangles shrunk to the changed pixels: ~22 KB per update on simulated values, partial redraws barely
    /// visible on the screen; larger tiles showed more (RESEARCH § 8).
    public int TileWidth { get; init; } = 16;
    public int TileHeight { get; init; } = 16;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan StatisticsPeriod { get; init; } = TimeSpan.FromMinutes(1);
}

/// Single owner of the screen. Callers set the wanted state (frame, brightness, power) without waiting, and a dedicated
/// thread brings the screen to it, sending only the changed parts of the frame. A frame submitted during a transfer
/// replaces the pending one. An I/O error closes the port; the next connection resends everything.
internal sealed class DisplayWorker : IDisposable {
    public const int Width = 480;
    public const int Height = 320;
    private const int FrameBytes = Width * Height * 2;

    private readonly DisplayWorkerOptions _options;
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);

    // Wanted state, guarded by _gate
    private readonly Lock _gate = new();
    private byte[] _pending = new byte[FrameBytes];
    private bool _hasPending;
    private bool _wantedOn = true;
    private int _wantedBrightness = 30;
    private bool _reconnectRequested;
    private bool _stopping;

    // Display thread only
    private RevADisplay? _display;
    private byte[] _latest = new byte[FrameBytes];
    private bool _hasLatest;
    private readonly byte[] _shown = new byte[FrameBytes];
    private bool _shownKnown;
    private bool? _sentOn;
    private int? _sentBrightness;
    private readonly byte[] _rectBuffer = new byte[FrameBytes];
    private readonly TransferStatistics _statistics = new();
    private string? _lastProblem;

    private volatile ConnectionState _state = ConnectionState.Disconnected;

    public DisplayWorker(DisplayWorkerOptions options) {
        _options = options;
        _thread = new Thread(Run) { IsBackground = true, Name = "Display" };
    }

    public ConnectionState State => _state;

    /// Raised on the display thread.
    public event Action<ConnectionState>? StateChanged;

    public void Start() => _thread.Start();

    /// The bitmap is converted before returning: the caller may reuse it.
    public void SubmitFrame(SKBitmap frame) {
        lock (_gate) {
            Rgb565.Convert(frame, _pending);
            _hasPending = true;
        }
        _wake.Set();
    }

    public void SetBrightness(int percent) {
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        lock (_gate) _wantedBrightness = percent;
        _wake.Set();
    }

    /// Frames keep being accepted while off; the screen keeps its image and gets the changes just before lighting up.
    public void SetPower(bool on) {
        lock (_gate) _wantedOn = on;
        _wake.Set();
    }

    /// After the PC resumes: the port handle may be stale.
    public void RequestReconnect() {
        lock (_gate) _reconnectRequested = true;
        _wake.Set();
    }

    public void Dispose() {
        lock (_gate) _stopping = true;
        _wake.Set();
        // A transfer ends within the write timeout
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(15))) Log.Warn("Display thread did not stop");
        _wake.Dispose();
    }

    private void Run() {
        while (true) {
            bool stopping, reconnect;
            lock (_gate) {
                stopping = _stopping;
                reconnect = _reconnectRequested;
                _reconnectRequested = false;
            }
            if (stopping) break;
            if (reconnect) Disconnect();

            if (_display == null && !TryConnect()) {
                _wake.WaitOne(_options.RetryDelay);
                continue;
            }

            try {
                Apply(_display);
            } catch (Exception e) when (IsConnectionError(e)) {
                Log.Warn($"Display error: {e.Message}");
                Disconnect();
                continue;
            }
            _statistics.LogIfDue(_options.StatisticsPeriod);
            _wake.WaitOne();
        }

        Disconnect();
        _statistics.Log();
    }

    /// Brings the screen to the wanted state.
    private void Apply(RevADisplay display) {
        bool on;
        int brightness;
        lock (_gate) {
            on = _wantedOn;
            brightness = _wantedBrightness;
            if (_hasPending) {
                (_pending, _latest) = (_latest, _pending);
                _hasPending = false;
                _hasLatest = true;
            }
        }

        if (!on) {
            if (_sentOn != false) display.ScreenOff();
            _sentOn = false;
            return;
        }

        // Lighting up: draw while still dark, so the screen shows the current image at once instead of catching up in
        // sight. After connecting, the screen state is unknown (white after plugging in): hide the whole redraw.
        if (_sentOn != true) {
            if (!_hasLatest) return;
            if (_sentOn == null) display.ScreenOff();
            _sentOn = false;
            SendFrame(display);
            display.ScreenOn();
            _sentOn = true;
        } else if (_hasLatest) {
            SendFrame(display);
        }

        if (_sentBrightness != brightness) {
            display.SetBrightness(brightness);
            _sentBrightness = brightness;
        }
    }

    private void SendFrame(RevADisplay display) {
        var stopwatch = Stopwatch.StartNew();
        var rects = _shownKnown
            ? FrameDiff.ChangedRects(_shown, _latest, Width, Height, _options.TileWidth, _options.TileHeight)
            : [new PixelRect(0, 0, Width, Height)];
        if (rects.Count == 0) return;

        var bytes = 0;
        foreach (var rect in rects) {
            var length = rect.Width * rect.Height * 2;
            FrameDiff.CopyRect(_latest, Width, rect, _rectBuffer);
            display.DisplayBitmap(rect.X, rect.Y, rect.Width, rect.Height, _rectBuffer.AsSpan(0, length));
            bytes += length;
        }

        // Only now does the screen show this frame
        _latest.CopyTo(_shown, 0);
        _shownKnown = true;
        _statistics.Add(bytes, rects.Count, stopwatch.Elapsed);
    }

    [MemberNotNullWhen(true, nameof(_display))]
    private bool TryConnect() {
        var port = PortLocator.Find(_options.ForcedPort);
        if (port == null) {
            ReportProblem(_options.ForcedPort != null ? $"{_options.ForcedPort} is not present" : "Screen not connected");
            SetState(ConnectionState.Disconnected);
            return false;
        }

        SetState(ConnectionState.Connecting);
        try {
            _display = RevADisplay.Open(port, _options.Serial);
            _display.SetOrientation(_options.Orientation);
        } catch (Exception e) when (IsConnectionError(e)) {
            ReportProblem($"Cannot open {port}: {e.Message}");
            _display?.Dispose();
            _display = null;
            SetState(ConnectionState.Disconnected);
            return false;
        }

        // Screen state unknown: everything is sent again
        _shownKnown = false;
        _sentOn = null;
        _sentBrightness = null;
        _lastProblem = null;
        Log.Info($"Screen connected on {port}");
        SetState(ConnectionState.Connected);
        return true;
    }

    private void Disconnect() {
        if (_display == null) return;

        _display.Dispose();
        _display = null;
        _shownKnown = false;
        _sentOn = null;
        _sentBrightness = null;
        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state) {
        if (_state == state) return;

        _state = state;
        Log.Info($"Display {state}");
        StateChanged?.Invoke(state);
    }

    /// Logged once until the problem changes or a connection succeeds: retries happen every few seconds.
    private void ReportProblem(string message) {
        if (message == _lastProblem) return;

        _lastProblem = message;
        Log.Warn(message);
    }

    private static bool IsConnectionError(Exception e) =>
        e is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException;

    /// Frame transfers, logged periodically: the USB link is the bottleneck (RESEARCH § 3).
    private sealed class TransferStatistics {
        private readonly List<double> _durations = [];
        private readonly Stopwatch _period = Stopwatch.StartNew();
        private long _bytes;
        private int _rects;

        public void Add(int bytes, int rects, TimeSpan duration) {
            _bytes += bytes;
            _rects += rects;
            _durations.Add(duration.TotalMilliseconds);
        }

        public void LogIfDue(TimeSpan period) {
            if (_period.Elapsed >= period) Log();
        }

        public void Log() {
            var count = _durations.Count;
            if (count > 0) {
                var last = _durations[^1];
                _durations.Sort();
                SleekMon.Log.Info(string.Create(CultureInfo.InvariantCulture,
                    $"Display: {count} updates in {_period.Elapsed.TotalSeconds:F0} s, per update {_bytes / count} bytes in " +
                    $"{(double)_rects / count:F1} rects; send average {_durations.Average():F1} ms, " +
                    $"p95 {_durations[(int)Math.Ceiling(0.95 * count) - 1]:F1} ms, max {_durations[^1]:F1} ms, last {last:F1} ms"));
            }
            _durations.Clear();
            _bytes = 0;
            _rects = 0;
            _period.Restart();
        }
    }
}
