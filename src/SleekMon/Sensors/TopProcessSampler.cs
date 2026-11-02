using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SleekMon.Sensors;

internal sealed record TopProcess(string Name, double CpuPercent, double MemoryGb);

/// The busiest program, like Task Manager: processes grouped by name (all "firefox.exe" add up), CPU as a share of the
/// whole processor, memory as private working set. One NtQuerySystemInformation call returns every process.
/// Not thread-safe: owned by the sensor thread.
internal sealed class TopProcessSampler(double minCpuPercent) : IDisposable {
    private const int SystemProcessInformation = 5;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    // SYSTEM_PROCESS_INFORMATION on x64 (phnt ntexapi.h, checked on 02/11/2026)
    private const int NextEntryOffsetField = 0;
    private const int WorkingSetPrivateSizeField = 8;
    private const int CreateTimeField = 32;
    private const int UserTimeField = 40;
    private const int KernelTimeField = 48;
    private const int ImageNameLengthField = 56;
    private const int ImageNameBufferField = 64;
    private const int ProcessIdField = 80;

    /// Another process takes the place only if it beats the shown one by this much: no flicker between close values.
    private const double SwitchMargin = 1;

    private static readonly double ProcessorCount = Environment.ProcessorCount;

    private nint _buffer = Marshal.AllocHGlobal(1024 * 1024);
    private int _bufferSize = 1024 * 1024;
    private Dictionary<(long Id, long CreateTime), long> _previousTimes = [];
    private long _previousTimestamp;
    private string? _shown;

    /// Null on the first call (CPU needs two samples) and when nothing reaches the minimum CPU.
    public TopProcess? Sample() {
        Query();
        var timestamp = Stopwatch.GetTimestamp();
        var elapsed100Ns = Stopwatch.GetElapsedTime(_previousTimestamp, timestamp).Ticks;
        var firstSample = _previousTimestamp == 0;

        var times = new Dictionary<(long Id, long CreateTime), long>(_previousTimes.Count);
        var byName = new Dictionary<string, (long Cpu, long Memory)>(StringComparer.OrdinalIgnoreCase);
        for (var entry = _buffer; ; ) {
            var id = Marshal.ReadInt64(entry, ProcessIdField);
            // PID 0 is the idle time, not a program
            if (id != 0) {
                var key = (id, Marshal.ReadInt64(entry, CreateTimeField));
                var cpuTime = Marshal.ReadInt64(entry, UserTimeField) + Marshal.ReadInt64(entry, KernelTimeField);
                times[key] = cpuTime;
                // A process started since the last sample spent all its CPU time within the interval
                var delta = cpuTime - _previousTimes.GetValueOrDefault(key);

                var name = ReadName(entry);
                var total = byName.GetValueOrDefault(name);
                byName[name] = (total.Cpu + delta, total.Memory + Marshal.ReadInt64(entry, WorkingSetPrivateSizeField));
            }

            var next = Marshal.ReadInt32(entry, NextEntryOffsetField);
            if (next == 0) break;
            entry += next;
        }
        _previousTimes = times;
        _previousTimestamp = timestamp;
        if (firstSample || elapsed100Ns <= 0) return null;

        double Percent(long cpu) => cpu * 100.0 / (elapsed100Ns * ProcessorCount);
        var top = byName.MaxBy(pair => pair.Value.Cpu);
        if (_shown != null && byName.TryGetValue(_shown, out var shown) && Percent(top.Value.Cpu) - Percent(shown.Cpu) < SwitchMargin) {
            top = new KeyValuePair<string, (long Cpu, long Memory)>(_shown, shown);
        }

        if (Percent(top.Value.Cpu) < minCpuPercent) {
            _shown = null;
            return null;
        }
        _shown = top.Key;
        return new TopProcess(top.Key, Percent(top.Value.Cpu), top.Value.Memory / (1024.0 * 1024 * 1024));
    }

    public void Dispose() {
        Marshal.FreeHGlobal(_buffer);
        _buffer = 0;
    }

    /// Fills the buffer with every process, growing it when Windows says it is too small.
    private void Query() {
        while (true) {
            var status = NtQuerySystemInformation(SystemProcessInformation, _buffer, _bufferSize, out var needed);
            if (status == 0) return;
            if (status != StatusInfoLengthMismatch) throw new InvalidOperationException($"NtQuerySystemInformation failed: 0x{status:X8}");

            Marshal.FreeHGlobal(_buffer);
            // Processes may start in between: some room to spare
            _bufferSize = needed + 64 * 1024;
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }
    }

    /// "firefox.exe" → "firefox"
    private static string ReadName(nint entry) {
        var length = (ushort)Marshal.ReadInt16(entry, ImageNameLengthField);
        var buffer = Marshal.ReadIntPtr(entry, ImageNameBufferField);
        var name = buffer == 0 ? "System" : Marshal.PtrToStringUni(buffer, length / 2);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int systemInformationClass, nint systemInformation, int length, out int returnLength);
}
