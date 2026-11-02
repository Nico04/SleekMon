using System.Diagnostics;
using System.Globalization;
using LibreHardwareMonitor.Hardware;

namespace SleekMon.Sensors;

/// Cost of updating each piece of hardware, to update only what the dashboard reads if some of it is slow.
internal static class SensorBenchmark {
    public static void Run(int cycles) {
        var computer = new Computer {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        computer.Open();
        try {
            var hardware = computer.Hardware.SelectMany(Flatten).ToList();
            var wall = new double[hardware.Count];
            var cpu = new double[hardware.Count];
            var process = Process.GetCurrentProcess();

            for (var cycle = 0; cycle < cycles; cycle++) {
                for (var i = 0; i < hardware.Count; i++) {
                    process.Refresh();
                    var cpuBefore = process.TotalProcessorTime;
                    var stopwatch = Stopwatch.StartNew();
                    hardware[i].Update();
                    wall[i] += stopwatch.Elapsed.TotalMilliseconds;
                    process.Refresh();
                    cpu[i] += (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                }
                Thread.Sleep(1000);
            }

            Log.Info($"Sensor update cost per cycle, average over {cycles} cycles (administrator {SensorDump.IsAdministrator()}):");
            foreach (var i in Enumerable.Range(0, hardware.Count).OrderByDescending(i => wall[i])) {
                Log.Info(string.Create(CultureInfo.InvariantCulture,
                    $"  {wall[i] / cycles,7:F1} ms elapsed, {cpu[i] / cycles,6:F1} ms CPU  [{hardware[i].HardwareType}] {hardware[i].Name} ({hardware[i].Identifier})"));
            }
            Log.Info(string.Create(CultureInfo.InvariantCulture,
                $"  {wall.Sum() / cycles,7:F1} ms elapsed, {cpu.Sum() / cycles,6:F1} ms CPU  total"));

            // The busiest program, sampled every other second by the app
            using var sampler = new TopProcessSampler(0);
            sampler.Sample();
            process.Refresh();
            var samplerCpuBefore = process.TotalProcessorTime;
            var stopwatch2 = Stopwatch.StartNew();
            TopProcess? top = null;
            for (var cycle = 0; cycle < cycles; cycle++) top = sampler.Sample();
            process.Refresh();
            Log.Info(string.Create(CultureInfo.InvariantCulture,
                $"  {stopwatch2.Elapsed.TotalMilliseconds / cycles,7:F1} ms elapsed, {(process.TotalProcessorTime - samplerCpuBefore).TotalMilliseconds / cycles,6:F1} ms CPU  busiest program ({top?.Name})"));
        } finally {
            computer.Close();
        }
    }

    private static IEnumerable<IHardware> Flatten(IHardware hardware) =>
        hardware.SubHardware.SelectMany(Flatten).Prepend(hardware);
}
