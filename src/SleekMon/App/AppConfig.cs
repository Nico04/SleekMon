using SleekMon.Sensors;

namespace SleekMon.App;

/// Power saving: the screen sleeps once CPU, GPU and RAM have all been quiet for DelaySeconds, and wakes once one of
/// them has been busy for WakeAfterSeconds (a one-second spike does not light it up).
internal sealed record IdleConfig {
    /// %
    public int CpuBelow { get; init; } = 10;
    /// %
    public int GpuBelow { get; init; } = 5;
    /// %: RAM at or above this keeps the screen on.
    public int RamAbove { get; init; } = 90;
    public int DelaySeconds { get; init; } = 30;
    public int WakeAfterSeconds { get; init; } = 2;
}

/// Settings, stored in %AppData%\SleekMon\config.json (example: config.example.json at the repository root).
internal sealed record AppConfig {
    public static readonly int[] DefaultBrightnessLevels = [10, 30, 60, 100];

    /// Off: screen off, port released, sensors stopped.
    public bool Enabled { get; init; } = true;
    public bool PowerSaving { get; init; } = true;
    /// %
    public int Brightness { get; init; } = 60;
    /// % choices of the tray menu.
    public int[] BrightnessLevels { get; init; } = DefaultBrightnessLevels;
    public IdleConfig Idle { get; init; } = new();
    /// %: when the busiest program uses less CPU, the card shows "--".
    public int TopProcessMinCpu { get; init; } = 2;
    public FpsSettings Fps { get; init; } = new();
    /// Motherboard sensors: their names depend on the board (see --dump-sensors), so none by default.
    public SensorSelection Sensors { get; init; } = new();
    /// Forced COM port; null: found from the registry.
    public string? Port { get; init; }

    /// Values a hand-edited file may get wrong, brought back in range.
    public AppConfig Normalized() {
        // JSON null for an object or array
        var idle = Idle ?? new IdleConfig();
        var fps = Fps ?? new FpsSettings();
        var levels = (BrightnessLevels ?? []).Where(level => level is >= 0 and <= 100).Distinct().Order().ToArray();
        return this with {
            Brightness = Math.Clamp(Brightness, 0, 100),
            BrightnessLevels = levels.Length > 0 ? levels : DefaultBrightnessLevels,
            TopProcessMinCpu = Math.Clamp(TopProcessMinCpu, 0, 100),
            Idle = idle with {
                CpuBelow = Math.Clamp(idle.CpuBelow, 0, 100),
                GpuBelow = Math.Clamp(idle.GpuBelow, 0, 100),
                RamAbove = Math.Clamp(idle.RamAbove, 0, 101),
                DelaySeconds = Math.Max(1, idle.DelaySeconds),
                WakeAfterSeconds = Math.Max(0, idle.WakeAfterSeconds)
            },
            Fps = fps with {
                MinFps = Math.Max(0, fps.MinFps),
                // At least one second: the rule needs a reading to change its mind
                MinSeconds = Math.Max(1, fps.MinSeconds),
                Always = Names(fps.Always),
                Never = Names(fps.Never)
            },
            Sensors = Sensors ?? new SensorSelection()
        };
    }

    // JSON null for the array or for an entry
    private static string[] Names(string?[]? names) =>
        (names ?? []).OfType<string>().Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
}
