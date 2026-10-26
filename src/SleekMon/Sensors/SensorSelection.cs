namespace SleekMon.Sensors;

/// Motherboard sensors to show, by their name on the Super I/O chip ("Fan #2", "Temperature #2"…): generic names whose
/// meaning depends on the board and on what is plugged into it. Null: not shown ("--").
internal sealed record SensorSelection {
    public string? CpuFan { get; init; }
    public string? CaseFan { get; init; }
    public string? CaseTemperature { get; init; }
}
