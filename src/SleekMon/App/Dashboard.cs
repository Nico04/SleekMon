using SleekMon.Display;
using SleekMon.Render;
using SleekMon.Sensors;

namespace SleekMon.App;

internal enum DashboardState {
    /// Turned off by the user: screen off, port released, sensors stopped.
    Disabled,
    Disconnected,
    Connecting,
    Connected,
    /// Screen off: PC idle (power saving) or asleep.
    Sleeping
}

/// The running dashboard: sensors, render and screen once per second, power saving and PC sleep.
/// Methods are thread-safe. The state is computed in one place (RefreshState) from the parts it depends on.
internal sealed class Dashboard : IDisposable {
    private static readonly TimeSpan PowerOffTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private AppConfig _config;
    private bool _suspended;
    private Running? _running;
    private DashboardState _state = DashboardState.Disabled;

    public Dashboard(AppConfig config) {
        _config = config;
    }

    /// Raised on the thread that caused the change: subscribers marshal it to their own.
    public event Action<DashboardState>? StateChanged;

    public DashboardState State {
        get {
            lock (_gate) return _state;
        }
    }

    public string? PortName {
        get {
            lock (_gate) return _running?.Worker.PortName;
        }
    }

    public void Start() {
        lock (_gate) {
            if (_config.Enabled && _running == null) _running = new Running(this, _config);
        }
        RefreshState();
    }

    public void SetEnabled(bool enabled) {
        Running? stopped = null;
        lock (_gate) {
            _config = _config with { Enabled = enabled };
            if (enabled) {
                _running ??= new Running(this, _config);
            } else {
                stopped = _running;
                _running = null;
            }
        }
        // Outside the lock: stopping joins the dashboard thread, which takes it
        stopped?.Dispose();
        RefreshState();
    }

    public void SetPowerSaving(bool enabled) {
        lock (_gate) _config = _config with { PowerSaving = enabled };
    }

    public void SetBrightness(int percent) {
        lock (_gate) {
            _config = _config with { Brightness = percent };
            _running?.Worker.SetBrightness(percent);
        }
    }

    /// Acts like an automatic wake: the screen lights up and sleeps again after the usual quiet delay.
    public void Wake() {
        Running? running;
        lock (_gate) running = _running;
        running?.Wake();
    }

    /// The PC is going to sleep: the screen is turned off before returning (bounded wait).
    public void Suspend() {
        Running? running;
        lock (_gate) {
            _suspended = true;
            running = _running;
        }
        Log.Info("PC suspending");
        running?.TurnOff();
        RefreshState();
    }

    public void Resume() {
        Running? running;
        lock (_gate) {
            _suspended = false;
            running = _running;
        }
        Log.Info("PC resumed");
        // The port handle may be stale after sleep
        running?.Worker.RequestReconnect();
        RefreshState();
    }

    public void Dispose() {
        Running? stopped;
        lock (_gate) {
            stopped = _running;
            _running = null;
        }
        stopped?.Dispose();
    }

    /// The only place where the state is decided.
    private void RefreshState() {
        DashboardState state;
        lock (_gate) {
            state = _running == null
                ? DashboardState.Disabled
                : _running.Worker.State switch {
                    ConnectionState.Disconnected => DashboardState.Disconnected,
                    ConnectionState.Connecting => DashboardState.Connecting,
                    _ => _running.Sleeping || _suspended ? DashboardState.Sleeping : DashboardState.Connected
                };
            if (state == _state) return;
            _state = state;
        }
        Log.Info($"Dashboard {state}");
        StateChanged?.Invoke(state);
    }

    private (bool PowerSaving, bool Suspended) ReadSettings() {
        lock (_gate) return (_config.PowerSaving, _suspended);
    }

    /// Everything that runs while enabled, with its own thread: the 1 Hz loop.
    private sealed class Running : IDisposable {
        private readonly Dashboard _owner;
        private readonly LhmSensorProvider _sensors;
        private readonly DashboardRenderer _renderer = new();
        private readonly IdleDetector _idle;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _stop = new();
        private volatile bool _sleeping;
        private int _wakeRequested;

        public Running(Dashboard owner, AppConfig config) {
            _owner = owner;
            _idle = new IdleDetector(config.Idle);
            _sensors = new LhmSensorProvider(config.Sensors, config.TopProcessMinCpu, config.Fps);
            Worker = new DisplayWorker(new DisplayWorkerOptions { ForcedPort = config.Port });
            Worker.SetBrightness(config.Brightness);
            Worker.StateChanged += _ => _owner.RefreshState();

            _sensors.Start();
            Worker.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "Dashboard" };
            _thread.Start();
        }

        public DisplayWorker Worker { get; }

        /// Idle with power saving on.
        public bool Sleeping => _sleeping;

        /// Raised on any thread; the idle detector is reset by the loop, its only owner.
        public void Wake() {
            Interlocked.Exchange(ref _wakeRequested, 1);
            // Without waiting for the next tick
            Worker.SetPower(true);
        }

        public void TurnOff() {
            Worker.SetPower(false);
            if (!Worker.WaitSettled(PowerOffTimeout)) Log.Warn("Screen not turned off in time");
        }

        /// Screen off before releasing the port: otherwise it keeps showing the last image.
        public void Dispose() {
            _stop.Set();
            _thread.Join();
            TurnOff();
            Worker.Dispose();
            _sensors.Dispose();
            _renderer.Dispose();
            _stop.Dispose();
        }

        private void Loop() {
            var lastTick = DateTime.UtcNow;
            while (!_stop.Wait(DelayToNextSecond())) {
                var now = DateTime.UtcNow;
                // Fallback for a missed resume event: a long gap means the PC slept
                if (now - lastTick > TimeSpan.FromSeconds(5)) {
                    Log.Info($"Time gap of {(now - lastTick).TotalSeconds:F0} s, reconnecting");
                    Worker.RequestReconnect();
                    _idle.Reset();
                }
                lastTick = now;

                var (powerSaving, suspended) = _owner.ReadSettings();
                // The clock shows the render time, not the reading time
                var snapshot = _sensors.Latest with { Timestamp = DateTime.Now };
                var wakeRequested = Interlocked.Exchange(ref _wakeRequested, 0) == 1;
                if (!powerSaving || wakeRequested) _idle.Reset();
                _sleeping = powerSaving && _idle.Update(snapshot, now);

                // Rendered even when off: the screen lights up on a current image
                Worker.SetPower(!_sleeping && !suspended);
                Worker.SubmitFrame(_renderer.Render(snapshot));
                _owner.RefreshState();
            }
        }

        /// The next whole second, so that the clock changes on time.
        private static TimeSpan DelayToNextSecond() => TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond);
    }
}
