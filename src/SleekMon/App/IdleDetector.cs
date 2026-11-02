using SleekMon.Sensors;

namespace SleekMon.App;

/// Power saving rule (IdleConfig). Missing sensor values are ignored: they neither keep the screen on nor wake it.
internal sealed class IdleDetector(IdleConfig config) {
    private DateTime? _quietSince;
    private DateTime? _busySince;

    public bool Sleeping { get; private set; }

    public bool Update(SensorSnapshot snapshot, DateTime now) {
        var busy = snapshot.CpuLoad >= config.CpuBelow || snapshot.GpuLoad >= config.GpuBelow || snapshot.RamLoad >= config.RamAbove;
        if (busy) {
            _quietSince = null;
            _busySince ??= now;
            if (Sleeping && now - _busySince >= TimeSpan.FromSeconds(config.WakeAfterSeconds)) Sleeping = false;
        } else {
            _busySince = null;
            _quietSince ??= now;
            if (!Sleeping && now - _quietSince >= TimeSpan.FromSeconds(config.DelaySeconds)) Sleeping = true;
        }
        return Sleeping;
    }

    /// Awake, counting from scratch (power saving turned off, PC resumed).
    public void Reset() {
        Sleeping = false;
        _quietSince = null;
        _busySince = null;
    }
}
