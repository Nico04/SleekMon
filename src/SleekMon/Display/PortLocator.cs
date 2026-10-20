using System.IO.Ports;
using Microsoft.Win32;

namespace SleekMon.Display;

/// Finds the screen's COM port without WMI: the port name the driver stored in the registry, accepted only if that
/// exact name is currently present (the registry key outlives the device).
internal static class PortLocator {
    private const string DeviceParametersKey =
        @"SYSTEM\CurrentControlSet\Enum\USB\VID_1A86&PID_5722\USB35INCHIPSV2\Device Parameters";

    /// Port name recorded by the driver, or null if the screen was never plugged in.
    public static string? ReadRegisteredPort() {
        using var key = Registry.LocalMachine.OpenSubKey(DeviceParametersKey);
        return key?.GetValue("PortName") as string;
    }

    /// Port to open (the forced one if given, otherwise the registered one), or null if it is not present.
    public static string? Find(string? forcedPort = null) {
        var candidate = forcedPort ?? ReadRegisteredPort();
        if (candidate == null) return null;

        return SerialPort.GetPortNames().Contains(candidate, StringComparer.OrdinalIgnoreCase) ? candidate : null;
    }
}
