namespace SleekMon.Sensors;

/// One reading of every displayed value, already in display units. Null: unavailable.
internal sealed record SensorSnapshot {
    /// %
    public double? CpuLoad { get; init; }
    /// °C
    public double? CpuTemp { get; init; }
    public double? CpuFanRpm { get; init; }

    /// %
    public double? GpuLoad { get; init; }
    /// °C
    public double? GpuTemp { get; init; }
    public double? GpuFanRpm { get; init; }

    /// %
    public double? VramLoad { get; init; }
    public double? VramUsedGb { get; init; }
    public double? VramTotalGb { get; init; }

    /// Null when no game is running.
    public double? Fps { get; init; }

    /// %
    public double? RamLoad { get; init; }
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }

    /// °C
    public double? CaseTemp { get; init; }
    public double? CaseFanRpm { get; init; }

    public string? TopProcessName { get; init; }
    /// %
    public double? TopProcessCpu { get; init; }
    public double? TopProcessMemGb { get; init; }

    /// Not administrator, or PawnIO missing: some sensors cannot be read.
    public bool SensorsLimited { get; init; }

    /// Local time shown in the header.
    public DateTime Timestamp { get; init; }
}
