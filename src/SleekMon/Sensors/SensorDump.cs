using System.Globalization;
using System.Security.Principal;
using System.Text;
using LibreHardwareMonitor.Hardware;
using PawnIoDriver = LibreHardwareMonitor.PawnIo.PawnIo;

namespace SleekMon.Sensors;

/// Writes every sensor LibreHardwareMonitor exposes on this PC, to choose sensors by name and type.
internal static class SensorDump {
    public static void Write(string path) {
        var computer = new Computer {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        computer.Open();
        try {
            // Loads are computed between two updates
            foreach (var hardware in computer.Hardware) Update(hardware);
            Thread.Sleep(1000);
            foreach (var hardware in computer.Hardware) Update(hardware);

            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            text.AppendLine(CultureInfo.InvariantCulture, $"Administrator: {IsAdministrator()}");
            text.AppendLine(CultureInfo.InvariantCulture, $"PawnIO installed: {PawnIoDriver.IsInstalled} (version {PawnIoDriver.Version?.ToString() ?? "none"})");
            text.AppendLine();
            text.AppendLine("Identifier | Name | Type | Value (raw, unit depends on type)");
            foreach (var hardware in computer.Hardware) AppendHardware(text, hardware, 0);

            text.AppendLine();
            text.AppendLine("==== LibreHardwareMonitor report ====");
            text.Append(computer.GetReport());

            File.WriteAllText(path, text.ToString());
        } finally {
            computer.Close();
        }
    }

    public static bool IsAdministrator() {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void Update(IHardware hardware) {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware) Update(subHardware);
    }

    private static void AppendHardware(StringBuilder text, IHardware hardware, int depth) {
        var indent = new string(' ', depth * 4);
        text.AppendLine(CultureInfo.InvariantCulture, $"{indent}[{hardware.HardwareType}] {hardware.Name} ({hardware.Identifier})");
        foreach (var sensor in hardware.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Index)) {
            var value = sensor.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";
            text.AppendLine(CultureInfo.InvariantCulture, $"{indent}    {sensor.Identifier} | {sensor.Name} | {sensor.SensorType} | {value}");
        }
        foreach (var subHardware in hardware.SubHardware) AppendHardware(text, subHardware, depth + 1);
    }
}
