using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Hardware;
using PawnIoDriver = LibreHardwareMonitor.PawnIo.PawnIo;

namespace SleekMon.Sensors;

/// Sensors read through LibreHardwareMonitor, which is not thread-safe: one Computer, owned by a dedicated thread that
/// updates every sensor once per second (at half seconds, so a render on the whole second gets fresh values).
/// Only reads: fans are read as Fan (RPM), never through their Control interface.
/// The busiest program is sampled on the same thread, every other second.
internal sealed class LhmSensorProvider : ISensorProvider {
    private static readonly TimeSpan ResolveRetryPeriod = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StatisticsPeriod = TimeSpan.FromMinutes(1);

    private readonly SensorSelection _selection;
    private readonly double _topProcessMinCpu;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new();
    private volatile SensorSnapshot _latest = new() { Timestamp = DateTime.Now };

    /// topProcessMinCpu: below this CPU %, no busiest program is shown.
    public LhmSensorProvider(SensorSelection selection, double topProcessMinCpu) {
        _selection = selection;
        _topProcessMinCpu = topProcessMinCpu;
        _thread = new Thread(Run) { IsBackground = true, Name = "Sensors" };
    }

    public SensorSnapshot Latest => _latest;

    public void Start() => _thread.Start();

    public void Dispose() {
        _stop.Set();
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(10))) Log.Warn("Sensor thread did not stop");
        _stop.Dispose();
    }

    private void Run() {
        var limited = !SensorDump.IsAdministrator() || !PawnIoDriver.IsInstalled;
        if (limited) Log.Warn($"Limited sensors: administrator {SensorDump.IsAdministrator()}, PawnIO installed {PawnIoDriver.IsInstalled}");

        var computer = new Computer {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        using var topProcessSampler = new TopProcessSampler(_topProcessMinCpu);
        TopProcess? topProcess = null;
        try {
            var stopwatch = Stopwatch.StartNew();
            computer.Open();
            // Some sensors only appear once read successfully: resolve after a first update
            Update(computer);
            Log.Info(string.Create(CultureInfo.InvariantCulture, $"Sensors opened in {stopwatch.Elapsed.TotalMilliseconds:F0} ms"));
            var bindings = Bindings.Resolve(computer, _selection, log: true);
            var sinceResolve = Stopwatch.StartNew();
            var durations = new List<double>();
            var sinceStatistics = Stopwatch.StartNew();
            string? lastError = null;
            var cycle = 0;

            while (!_stop.Wait(DelayToNextHalfSecond())) {
                try {
                    stopwatch.Restart();
                    Update(computer);
                    if (bindings.HasMissing && sinceResolve.Elapsed >= ResolveRetryPeriod) {
                        bindings = Bindings.Resolve(computer, _selection, log: false);
                        sinceResolve.Restart();
                    }
                    if (cycle++ % 2 == 0) topProcess = topProcessSampler.Sample();
                    _latest = bindings.Read(limited) with {
                        TopProcessName = topProcess?.Name,
                        TopProcessCpu = topProcess?.CpuPercent,
                        TopProcessMemGb = topProcess?.MemoryGb
                    };
                    durations.Add(stopwatch.Elapsed.TotalMilliseconds);
                    lastError = null;
                } catch (Exception e) {
                    // Keep going: a failing read must not stop the dashboard. Logged once until it changes
                    if (e.Message != lastError) Log.Error("Sensor update failed", e);
                    lastError = e.Message;
                }

                if (sinceStatistics.Elapsed >= StatisticsPeriod && durations.Count > 0) {
                    Log.Info(string.Create(CultureInfo.InvariantCulture,
                        $"Sensors: {durations.Count} updates, average {durations.Average():F1} ms, max {durations.Max():F1} ms"));
                    durations.Clear();
                    sinceStatistics.Restart();
                }
            }
        } catch (Exception e) {
            Log.Error("Sensors unavailable", e);
        } finally {
            computer.Close();
        }
    }

    private static void Update(IComputer computer) {
        foreach (var hardware in computer.Hardware) Update(hardware);
    }

    private static void Update(IHardware hardware) {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware) Update(subHardware);
    }

    private static TimeSpan DelayToNextHalfSecond() {
        var millisecond = DateTime.Now.Millisecond;
        return TimeSpan.FromMilliseconds(millisecond < 500 ? 500 - millisecond : 1500 - millisecond);
    }

    /// Sensors found by hardware, name and type (names checked against LibreHardwareMonitor 0.9.6, RESEARCH § 4).
    /// LibreHardwareMonitor keeps the same sensor objects afterwards: they are looked up once.
    private sealed class Bindings {
        private ISensor? _cpuLoad;
        private ISensor? _cpuTemperature;
        private ISensor? _cpuFan;
        private ISensor[] _gpuEngines = [];
        private ISensor? _gpuLoad;
        private ISensor? _gpuTemperature;
        private ISensor? _gpuFan;
        private ISensor? _vramUsed;
        private ISensor? _vramTotal;
        private ISensor? _ramUsed;
        private ISensor? _ramAvailable;
        private ISensor? _caseTemperature;
        private ISensor? _caseFan;

        public bool HasMissing { get; private set; }

        public static Bindings Resolve(IComputer computer, SensorSelection selection, bool log) {
            var cpu = computer.Hardware.FirstOrDefault(hardware => hardware.HardwareType == HardwareType.Cpu);
            // The dedicated GPU first
            var gpu = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel }
                .Select(type => computer.Hardware.FirstOrDefault(hardware => hardware.HardwareType == type))
                .FirstOrDefault(hardware => hardware != null);
            // "/vram" (virtual memory) has the same sensor names
            var ram = computer.Hardware.FirstOrDefault(hardware =>
                hardware.HardwareType == HardwareType.Memory && hardware.Identifier.ToString() == "/ram");
            var superIo = computer.Hardware
                .Where(hardware => hardware.HardwareType == HardwareType.Motherboard)
                .SelectMany(hardware => hardware.SubHardware)
                .FirstOrDefault(hardware => hardware.HardwareType == HardwareType.SuperIO);

            var bindings = new Bindings {
                _cpuLoad = Find(cpu, "CPU Total", SensorType.Load),
                // AMD Zen, then Intel
                _cpuTemperature = Find(cpu, "Core (Tctl/Tdie)", SensorType.Temperature) ?? Find(cpu, "CPU Package", SensorType.Temperature),
                _cpuFan = Find(superIo, selection.CpuFan, SensorType.Fan),
                // Like Task Manager: the busiest engine Windows sees ("D3D 3D", "D3D Copy"…). The driver's "GPU Core" load
                // reads much higher at idle (20 % against 5 %, RESEARCH § 4) and stays as a fallback
                _gpuEngines = gpu?.Sensors.Where(sensor => sensor.SensorType == SensorType.Load && sensor.Name.StartsWith("D3D ", StringComparison.Ordinal)).ToArray() ?? [],
                _gpuLoad = Find(gpu, "GPU Core", SensorType.Load),
                _gpuTemperature = Find(gpu, "GPU Core", SensorType.Temperature),
                // "GPU Fan 1" on NVIDIA, "GPU Fan" on AMD
                _gpuFan = gpu?.Sensors.Where(sensor => sensor.SensorType == SensorType.Fan).MinBy(sensor => sensor.Index),
                // MB on both NVIDIA and AMD
                _vramUsed = Find(gpu, "GPU Memory Used", SensorType.SmallData),
                _vramTotal = Find(gpu, "GPU Memory Total", SensorType.SmallData),
                // GB
                _ramUsed = Find(ram, "Memory Used", SensorType.Data),
                _ramAvailable = Find(ram, "Memory Available", SensorType.Data),
                _caseTemperature = Find(superIo, selection.CaseTemperature, SensorType.Temperature),
                _caseFan = Find(superIo, selection.CaseFan, SensorType.Fan)
            };

            (string Label, ISensor? Sensor, bool Expected)[] all = [
                ("CPU load", bindings._cpuLoad, true),
                ("CPU temperature", bindings._cpuTemperature, true),
                ("CPU fan", bindings._cpuFan, selection.CpuFan != null),
                ("GPU engines", bindings._gpuEngines.FirstOrDefault(), true),
                ("GPU load", bindings._gpuLoad, bindings._gpuEngines.Length == 0),
                ("GPU temperature", bindings._gpuTemperature, true),
                ("GPU fan", bindings._gpuFan, true),
                ("VRAM used", bindings._vramUsed, true),
                ("VRAM total", bindings._vramTotal, true),
                ("RAM used", bindings._ramUsed, true),
                ("RAM available", bindings._ramAvailable, true),
                ("Case temperature", bindings._caseTemperature, selection.CaseTemperature != null),
                ("Case fan", bindings._caseFan, selection.CaseFan != null)
            ];
            var missing = all.Where(entry => entry.Expected && entry.Sensor == null).Select(entry => entry.Label).ToList();
            bindings.HasMissing = missing.Count > 0;

            if (log) {
                Log.Info("Sensors: " + string.Join(", ", all.Where(entry => entry.Sensor != null)
                    .Select(entry => $"{entry.Label} = {entry.Sensor?.Identifier} ({entry.Sensor?.Name})")));
            }
            if (missing.Count > 0 && log) Log.Warn($"Sensors not found: {string.Join(", ", missing)}");
            else if (!bindings.HasMissing && !log) Log.Info("All sensors found");
            return bindings;
        }

        public SensorSnapshot Read(bool limited) {
            var vramUsed = Value(_vramUsed) / 1024;
            var vramTotal = Value(_vramTotal) / 1024;
            var ramUsed = Value(_ramUsed);
            var ramTotal = ramUsed + Value(_ramAvailable);
            return new SensorSnapshot {
                CpuLoad = Value(_cpuLoad),
                CpuTemp = Temperature(_cpuTemperature),
                CpuFanRpm = Value(_cpuFan),
                GpuLoad = _gpuEngines.Select(Value).Max() ?? Value(_gpuLoad),
                GpuTemp = Temperature(_gpuTemperature),
                GpuFanRpm = Value(_gpuFan),
                // Computed rather than read, so that it always matches the GB shown next to it
                VramLoad = Percent(vramUsed, vramTotal),
                VramUsedGb = vramUsed,
                VramTotalGb = vramTotal,
                RamLoad = Percent(ramUsed, ramTotal),
                RamUsedGb = ramUsed,
                RamTotalGb = ramTotal,
                CaseTemp = Temperature(_caseTemperature),
                CaseFanRpm = Value(_caseFan),
                SensorsLimited = limited,
                Timestamp = DateTime.Now
            };
        }

        private static ISensor? Find(IHardware? hardware, string? name, SensorType type) =>
            name == null ? null : hardware?.Sensors.FirstOrDefault(sensor => sensor.SensorType == type && sensor.Name == name);

        /// NaN or infinite values (failed reads) count as unavailable.
        private static double? Value(ISensor? sensor) => sensor?.Value is { } value && float.IsFinite(value) ? value : null;

        /// Without administrator rights the CPU temperature sensor exists but reads 0: no part runs at 0 °C.
        private static double? Temperature(ISensor? sensor) => Value(sensor) is > 0 and var value ? value : null;

        private static double? Percent(double? part, double? total) => total > 0 ? part / total * 100 : null;
    }
}
