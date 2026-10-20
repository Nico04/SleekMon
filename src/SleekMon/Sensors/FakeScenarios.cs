namespace SleekMon.Sensors;

/// The mockup scenarios (DESIGN.md), to render without hardware.
internal static class FakeScenarios {
    public static readonly IReadOnlyList<(string Name, SensorSnapshot Snapshot)> All = [
        ("gaming", new SensorSnapshot {
            CpuLoad = 46, CpuTemp = 76, CpuFanRpm = 1240,
            GpuLoad = 97, GpuTemp = 81, GpuFanRpm = 1850,
            VramLoad = 62, VramUsedGb = 7.4, VramTotalGb = 12,
            Fps = 144,
            RamLoad = 61, RamUsedGb = 19.5, RamTotalGb = 32,
            CaseTemp = 38, CaseFanRpm = 890,
            TopProcessName = "Cyberpunk2077", TopProcessCpu = 18, TopProcessMemGb = 7.8,
            Timestamp = new DateTime(2026, 10, 19, 21, 47, 0)
        }),
        ("desktop", new SensorSnapshot {
            CpuLoad = 9, CpuTemp = 48, CpuFanRpm = 780,
            GpuLoad = 1, GpuTemp = 39, GpuFanRpm = 0,
            VramLoad = 18, VramUsedGb = 2.2, VramTotalGb = 12,
            RamLoad = 60, RamUsedGb = 19.2, RamTotalGb = 32,
            CaseTemp = 33, CaseFanRpm = 720,
            TopProcessName = "firefox", TopProcessCpu = 3, TopProcessMemGb = 2.1,
            Timestamp = new DateTime(2026, 10, 19, 10, 52, 0)
        }),
        ("no-admin", new SensorSnapshot {
            CpuLoad = 14,
            GpuLoad = 1, GpuTemp = 43, GpuFanRpm = 0,
            VramLoad = 21, VramUsedGb = 2.5, VramTotalGb = 12,
            RamLoad = 63, RamUsedGb = 20.2, RamTotalGb = 32,
            TopProcessName = "Code", TopProcessCpu = 6, TopProcessMemGb = 1.3,
            SensorsLimited = true,
            Timestamp = new DateTime(2026, 10, 19, 15, 12, 0)
        }),
        ("stress", new SensorSnapshot {
            CpuLoad = 83, CpuTemp = 88, CpuFanRpm = 1980,
            GpuLoad = 97, GpuTemp = 79, GpuFanRpm = 2200,
            VramLoad = 91, VramUsedGb = 10.9, VramTotalGb = 12,
            Fps = 58,
            RamLoad = 92, RamUsedGb = 29.4, RamTotalGb = 32,
            CaseTemp = 47, CaseFanRpm = 1450,
            TopProcessName = "Blender", TopProcessCpu = 78, TopProcessMemGb = 9.6,
            Timestamp = new DateTime(2026, 10, 19, 23, 5, 0)
        })
    ];

    /// Random walk around a snapshot, timestamped now, so that values change every second like real sensors.
    public static SensorSnapshot Wander(SensorSnapshot snapshot, Random random) {
        var vramLoad = Step(random, snapshot.VramLoad, 1, 5, 100);
        var ramLoad = Step(random, snapshot.RamLoad, 0.5, 20, 100);
        return snapshot with {
            CpuLoad = Step(random, snapshot.CpuLoad, 8, 0, 100),
            CpuTemp = Step(random, snapshot.CpuTemp, 1.5, 30, 95),
            CpuFanRpm = Step(random, snapshot.CpuFanRpm, 40, 600, 2400),
            GpuLoad = Step(random, snapshot.GpuLoad, 6, 0, 100),
            GpuTemp = Step(random, snapshot.GpuTemp, 1.5, 30, 95),
            GpuFanRpm = Step(random, snapshot.GpuFanRpm, 40, 600, 3000),
            VramLoad = vramLoad,
            VramUsedGb = vramLoad / 100 * snapshot.VramTotalGb,
            Fps = Step(random, snapshot.Fps, 6, 20, 240),
            RamLoad = ramLoad,
            RamUsedGb = ramLoad / 100 * snapshot.RamTotalGb,
            CaseTemp = Step(random, snapshot.CaseTemp, 0.3, 25, 60),
            CaseFanRpm = Step(random, snapshot.CaseFanRpm, 20, 400, 1800),
            TopProcessCpu = Step(random, snapshot.TopProcessCpu, 4, 0, 100),
            Timestamp = DateTime.Now
        };
    }

    private static double? Step(Random random, double? value, double amplitude, double min, double max) =>
        value is { } v ? Math.Clamp(v + (random.NextDouble() * 2 - 1) * amplitude, min, max) : null;
}
