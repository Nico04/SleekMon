using SleekMon.Sensors;

namespace SleekMon.App;

/// App settings.
/// TODO: load them from %AppData%\SleekMon\config.json. Until then these values fit one specific PC only: another
/// motherboard needs other sensor names (see --dump-sensors).
internal sealed record AppConfig {
    public SensorSelection Sensors { get; init; } = new() {
        CpuFan = "Fan #2",
        CaseFan = "Fan #3",
        CaseTemperature = "Temperature #2"
    };

    public static AppConfig Load() => new();
}
