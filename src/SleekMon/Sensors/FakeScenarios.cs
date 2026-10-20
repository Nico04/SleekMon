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
}
