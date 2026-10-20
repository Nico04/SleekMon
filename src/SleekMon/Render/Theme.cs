using SkiaSharp;
using SleekMon.Sensors;

namespace SleekMon.Render;

internal enum Level {
    Unavailable,
    Idle,
    Normal,
    Warm,
    Hot
}

internal readonly record struct Status(string Text, SKColor Color);

/// Colors and state rules of DESIGN.md. Thresholds take the displayed (rounded) values.
internal static class Theme {
    public static readonly SKColor Background = SKColor.Parse("#0E0F11");
    public static readonly SKColor Card = SKColor.Parse("#1B1C1F");
    public static readonly SKColor Text = SKColor.Parse("#F4F4F5");
    public static readonly SKColor Muted = SKColor.Parse("#A1A1AA");
    public static readonly SKColor Dim = SKColor.Parse("#71717A");
    public static readonly SKColor IdleBar = SKColor.Parse("#52525B");
    public static readonly SKColor SegmentOn = SKColor.Parse("#D4D4D8");
    public static readonly SKColor SegmentOff = SKColor.Parse("#2C2D31");
    public static readonly SKColor Warm = SKColor.Parse("#F5A524");
    public static readonly SKColor Hot = SKColor.Parse("#F2555A");
    public static readonly SKColor Ok = SKColor.Parse("#3DD68C");

    /// CPU, GPU and process load.
    public static Level LoadLevel(int? percent) => percent switch {
        null => Level.Unavailable,
        < 2 => Level.Idle,
        >= 90 => Level.Hot,
        >= 75 => Level.Warm,
        _ => Level.Normal
    };

    /// RAM and VRAM: no idle state.
    public static Level MemoryLevel(int? percent) => Thresholds(percent, 80, 90);

    /// CPU and GPU temperature.
    public static Level TemperatureLevel(int? degrees) => Thresholds(degrees, 75, 85);

    public static Level CaseTemperatureLevel(int? degrees) => Thresholds(degrees, 45, 55);

    public static SKColor ValueColor(Level level) => level switch {
        Level.Unavailable or Level.Idle => Dim,
        Level.Warm => Warm,
        Level.Hot => Hot,
        _ => Text
    };

    /// The "%" after a value: muted when normal, otherwise like the value.
    public static SKColor UnitColor(Level level) => level == Level.Normal ? Muted : ValueColor(level);

    /// Lit bar segments.
    public static SKColor SegmentColor(Level level) => level switch {
        Level.Idle => IdleBar,
        Level.Warm => Warm,
        Level.Hot => Hot,
        _ => SegmentOn
    };

    /// A stopped fan is normal (idle fan stop): green.
    public static SKColor FanColor(int? rpm) => rpm switch {
        null => Dim,
        0 => Ok,
        _ => Muted
    };

    /// FPS pill background.
    public static SKColor FpsColor(int fps) => fps < 30 ? Hot : fps < 60 ? Warm : Text;

    /// Header line, first matching rule wins.
    public static Status StatusOf(SensorSnapshot snapshot) {
        var cpuTemp = Formatter.Round(snapshot.CpuTemp);
        var gpuTemp = Formatter.Round(snapshot.GpuTemp);
        var ram = Formatter.Round(snapshot.RamLoad);
        var cpu = Formatter.Round(snapshot.CpuLoad);
        var gpu = Formatter.Round(snapshot.GpuLoad);

        if (cpuTemp >= 85) return new Status($"Overheating · CPU {Formatter.Temperature(cpuTemp)}", Hot);
        if (gpuTemp >= 85) return new Status($"Overheating · GPU {Formatter.Temperature(gpuTemp)}", Hot);
        if (ram >= 90) return new Status($"Memory almost full · {Formatter.Percent(ram)}", Hot);
        if (cpu >= 90 || gpu >= 90) {
            var busiest = (gpu ?? -1) >= (cpu ?? -1) ? $"GPU {Formatter.Percent(gpu)}" : $"CPU {Formatter.Percent(cpu)}";
            return new Status($"High load · {busiest}", Warm);
        }
        if (snapshot.SensorsLimited) return new Status("Limited sensors · run as admin", Muted);
        return new Status("All systems normal", Ok);
    }

    private static Level Thresholds(int? value, int warm, int hot) => value switch {
        null => Level.Unavailable,
        _ when value >= hot => Level.Hot,
        _ when value >= warm => Level.Warm,
        _ => Level.Normal
    };
}
