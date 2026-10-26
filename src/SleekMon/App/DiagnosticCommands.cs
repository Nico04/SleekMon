using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using SkiaSharp;
using SleekMon.Display;
using SleekMon.Sensors;

namespace SleekMon.App;

/// Hardware session commands (PLAN phase 2). Results go to the log, sensors to a file.
/// Common display options: --port COMx, --orientation 2|3, --rtscts on|off, --dtr-rts on|off, --chunk BYTES (0: whole frame),
/// --gap MS (pause after a frame), --brightness PERCENT.
internal static class DiagnosticCommands {
    private const int DefaultBrightness = 60;

    private sealed record DisplaySettings(string? ForcedPort, RevAOptions Options, Orientation Orientation, int BrightnessPercent) {
        public string Describe() => string.Create(CultureInfo.InvariantCulture,
            $"orientation {(int)Orientation} · rtscts {OnOff(Options.RtsCts)} · dtr/rts {OnOff(Options.DtrRts)} · " +
            $"chunk {(Options.ChunkSize > 0 ? Options.ChunkSize.ToString(CultureInfo.InvariantCulture) : "whole")} · " +
            $"gap {Options.FrameGapMs} ms · brightness {BrightnessPercent} %");

        private static string OnOff(bool value) => value ? "on" : "off";
    }

    /// --find-port: registry value, present ports and the chosen one (also to test with the screen unplugged).
    public static int FindPort(CommandArgs args) {
        var forcedPort = args.String("port");
        args.RejectUnknown();

        Log.Info($"Registered port: {PortLocator.ReadRegisteredPort() ?? "none"}");
        Log.Info($"Present ports: [{string.Join(", ", SerialPort.GetPortNames())}]");
        var port = PortLocator.Find(forcedPort);
        Log.Info(port != null ? $"Screen port: {port}" : "Screen not connected");
        return port != null ? 0 : 1;
    }

    /// --test-pattern [--repeat N] [--save PNG]: --save writes the pattern to a file instead of the screen.
    public static int TestPattern(CommandArgs args) {
        var settings = ReadDisplaySettings(args, landscapeOnly: true);
        var repeat = args.Int("repeat", 1, 1, 1000);
        var savePath = args.String("save");
        args.RejectUnknown();

        using var bitmap = Render.TestPattern.Draw(settings.Describe());
        if (savePath == null) return ShowBitmap(settings, bitmap, repeat);

        SavePng(bitmap, Path.GetFullPath(savePath));
        Log.Info($"Test pattern written to {Path.GetFullPath(savePath)}");
        return 0;
    }

    /// --show PNG [--repeat N]: a 480×320 image, e.g. the v3 mockups.
    public static int Show(CommandArgs args) {
        var path = args.Positional(0) ?? throw new UsageException("--show needs a PNG path");
        var settings = ReadDisplaySettings(args, landscapeOnly: true);
        var repeat = args.Int("repeat", 1, 1, 1000);
        args.RejectUnknown();

        using var bitmap = SKBitmap.Decode(Path.GetFullPath(path));
        if (bitmap == null) {
            Log.Error($"Cannot decode {Path.GetFullPath(path)}");
            return 1;
        }
        if (bitmap.Width != Render.TestPattern.Width || bitmap.Height != Render.TestPattern.Height) {
            Log.Error($"Image is {bitmap.Width}×{bitmap.Height}, expected {Render.TestPattern.Width}×{Render.TestPattern.Height}");
            return 1;
        }
        return ShowBitmap(settings, bitmap, repeat);
    }

    /// --screen on|off [--dim-first]: --dim-first sets brightness 0 before turning off (issue #907).
    public static int Screen(CommandArgs args) {
        var state = args.Positional(0);
        var dimFirst = args.Flag("dim-first");
        var settings = ReadDisplaySettings(args, landscapeOnly: false);
        args.RejectUnknown();
        if (state is not ("on" or "off")) throw new UsageException("--screen needs on or off");

        using var display = OpenDisplay(settings);
        if (display == null) return 1;

        if (state == "on") {
            display.ScreenOn();
            display.SetBrightness(settings.BrightnessPercent);
            Log.Info($"Screen on, brightness {settings.BrightnessPercent} %");
        } else {
            if (dimFirst) display.SetBrightness(0);
            display.ScreenOff();
            Log.Info(dimFirst ? "Brightness 0, then screen off" : "Screen off");
        }
        return 0;
    }

    /// --brightness PERCENT
    public static int Brightness(CommandArgs args) {
        var text = args.Positional(0);
        var settings = ReadDisplaySettings(args, landscapeOnly: false);
        args.RejectUnknown();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) || percent > 100) {
            throw new UsageException("--brightness needs a value between 0 and 100");
        }

        using var display = OpenDisplay(settings);
        if (display == null) return 1;

        display.SetBrightness(percent);
        Log.Info($"Brightness {percent} %");
        return 0;
    }

    /// --clear [--portrait-first]: the Python library switches to portrait before clearing ("bug").
    public static int Clear(CommandArgs args) {
        var portraitFirst = args.Flag("portrait-first");
        var settings = ReadDisplaySettings(args, landscapeOnly: false);
        args.RejectUnknown();

        using var display = OpenDisplay(settings);
        if (display == null) return 1;

        if (portraitFirst) display.SetOrientation(Orientation.Portrait);
        display.Clear();
        if (portraitFirst) display.SetOrientation(settings.Orientation);
        Log.Info(portraitFirst ? $"Cleared in portrait, back to orientation {(int)settings.Orientation}" : "Cleared");
        return 0;
    }

    /// --reset: restarts the screen and follows its port for 20 s (it may come back under another name).
    public static int Reset(CommandArgs args) {
        var settings = ReadDisplaySettings(args, landscapeOnly: false);
        args.RejectUnknown();

        using (var display = OpenDisplay(settings)) {
            if (display == null) return 1;
            display.Reset();
            Log.Info($"Reset sent on {display.PortName}");
        }

        var stopwatch = Stopwatch.StartNew();
        var lastState = "";
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(20)) {
            var state = $"registered {PortLocator.ReadRegisteredPort() ?? "none"}, present [{string.Join(", ", SerialPort.GetPortNames())}]";
            if (state != lastState) {
                Log.Info($"+{stopwatch.ElapsedMilliseconds} ms: {state}");
                lastState = state;
            }
            Thread.Sleep(100);
        }
        return 0;
    }

    /// --render SCENARIO|all OUTPUT [--compare MOCKUPS_DIR]: OUTPUT is a PNG file, or a folder for "all".
    /// --compare also writes NAME-compare.png (mockup v3-NAME.png | render | amplified difference) next to each render.
    public static int RenderScenarios(CommandArgs args) {
        var names = string.Join(", ", FakeScenarios.All.Select(scenario => scenario.Name));
        var requested = args.Positional(0) ?? throw new UsageException($"--render needs a scenario ({names}) or all");
        var output = args.Positional(1) ?? throw new UsageException("--render needs an output path");
        var mockups = args.String("compare");
        args.RejectUnknown();

        var all = requested == "all";
        var scenarios = FakeScenarios.All.Where(scenario => all || scenario.Name == requested).ToList();
        if (scenarios.Count == 0) throw new UsageException($"Unknown scenario {requested} ({names})");

        using var renderer = new Render.DashboardRenderer();
        foreach (var (name, snapshot) in scenarios) {
            var path = Path.GetFullPath(all ? Path.Combine(output, $"{name}.png") : output);
            var stopwatch = Stopwatch.StartNew();
            var bitmap = renderer.Render(snapshot);
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            SavePng(bitmap, path);
            Log.Info(string.Create(CultureInfo.InvariantCulture, $"Rendered {name} in {elapsed:F1} ms: {path}"));

            if (mockups == null) continue;
            var mockupPath = Path.Combine(Path.GetFullPath(mockups), $"v3-{name}.png");
            using var mockup = SKBitmap.Decode(mockupPath);
            if (mockup == null) {
                Log.Error($"Cannot decode {mockupPath}");
                return 1;
            }
            using var comparison = Render.MockupComparison.Compose(mockup, bitmap, out var meanDifference);
            var comparisonPath = Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}-compare.png");
            SavePng(comparison, comparisonPath);
            Log.Info(string.Create(CultureInfo.InvariantCulture, $"Compared {name}: mean difference {meanDifference:F2}/255, {comparisonPath}"));
        }
        return 0;
    }

    /// --run-fake [--tile WxH] [--duration MIN] [--power-cycle SEC]: the real 1 Hz loop (render, diff, send) on simulated
    /// values. --power-cycle turns the screen off and on every SEC seconds (does it keep its image?). Statistics in the log.
    public static int RunFake(CommandArgs args) {
        var settings = ReadDisplaySettings(args, landscapeOnly: true);
        var (tileWidth, tileHeight) = ParseTile(args.String("tile") ?? "16x16");
        var duration = TimeSpan.FromMinutes(args.Int("duration", 2, 1, 24 * 60));
        var powerCycle = args.Int("power-cycle", 0, 0, 3600);
        args.RejectUnknown();

        Log.Info($"Fake run: {duration.TotalMinutes} min, tiles {tileWidth}×{tileHeight}, power cycle {powerCycle} s, {settings.Describe()}");
        var random = new Random(1);
        var snapshot = FakeScenarios.All[0].Snapshot;
        var on = true;
        RunLoop(settings, duration, tileWidth, tileHeight, (tick, worker) => {
            if (powerCycle > 0 && tick > 0 && tick % powerCycle == 0) {
                on = !on;
                worker.SetPower(on);
                Log.Info(on ? "Power on" : "Power off");
            }
            snapshot = FakeScenarios.Wander(snapshot, random);
            return snapshot;
        });
        return 0;
    }

    /// --run [--duration MIN]: real sensors on the screen, motherboard sensors from AppConfig. Run as administrator for
    /// every sensor. The displayed values go to the log every 10 s, to compare with other tools.
    public static int Run(CommandArgs args) {
        var settings = ReadDisplaySettings(args, landscapeOnly: true);
        var duration = TimeSpan.FromMinutes(args.Int("duration", 10, 1, 24 * 60));
        args.RejectUnknown();

        var config = AppConfig.Load();
        Log.Info($"Run: {duration.TotalMinutes} min, {config.Sensors}, {settings.Describe()}");
        using var sensors = new LhmSensorProvider(config.Sensors);
        sensors.Start();
        RunLoop(settings, duration, 16, 16, (tick, _) => {
            // The clock shows the render time, not the reading time
            var snapshot = sensors.Latest with { Timestamp = DateTime.Now };
            if (tick % 10 == 0) Log.Info(Describe(snapshot));
            return snapshot;
        });
        return 0;
    }

    /// The real 1 Hz loop: render, then hand the frame to the display thread. nextSnapshot runs once per tick.
    private static void RunLoop(DisplaySettings settings, TimeSpan duration, int tileWidth, int tileHeight,
        Func<int, DisplayWorker, SensorSnapshot> nextSnapshot) {
        var options = new DisplayWorkerOptions {
            ForcedPort = settings.ForcedPort,
            Serial = settings.Options,
            Orientation = settings.Orientation,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            // Short runs get statistics too
            StatisticsPeriod = TimeSpan.FromSeconds(15)
        };
        using var renderer = new Render.DashboardRenderer();
        using var worker = new DisplayWorker(options);
        worker.SetBrightness(settings.BrightnessPercent);
        worker.Start();

        var start = DateTime.UtcNow;
        var lastTick = start;
        for (var tick = 0; DateTime.UtcNow - start < duration; tick++) {
            // A long gap between ticks means the PC slept: the port handle may be stale
            var now = DateTime.UtcNow;
            if (now - lastTick > TimeSpan.FromSeconds(5)) {
                Log.Info($"Time gap of {(now - lastTick).TotalSeconds:F0} s, reconnecting");
                worker.RequestReconnect();
            }
            lastTick = now;

            worker.SubmitFrame(renderer.Render(nextSnapshot(tick, worker)));

            // Next whole second, so that the clock changes on time
            Thread.Sleep(1000 - DateTime.Now.Millisecond);
        }
    }

    /// The values as displayed (rounded, same formats).
    private static string Describe(SensorSnapshot snapshot) {
        string Value(double? value) => Render.Formatter.Number(Render.Formatter.Round(value));
        return $"CPU {Value(snapshot.CpuLoad)}% {Value(snapshot.CpuTemp)}° {Value(snapshot.CpuFanRpm)} RPM · " +
            $"GPU {Value(snapshot.GpuLoad)}% {Value(snapshot.GpuTemp)}° {Value(snapshot.GpuFanRpm)} RPM · " +
            $"VRAM {Value(snapshot.VramLoad)}% {Value(snapshot.VramUsedGb)}{Render.Formatter.OfTotal(snapshot.VramTotalGb)} · " +
            $"RAM {Value(snapshot.RamLoad)}% {Value(snapshot.RamUsedGb)}{Render.Formatter.OfTotal(snapshot.RamTotalGb)} · " +
            $"case {Value(snapshot.CaseTemp)}° {Value(snapshot.CaseFanRpm)} RPM · limited {snapshot.SensorsLimited}";
    }

    /// --bench-diff [--frames N]: without the screen, what the tile diff would send on simulated frames, per tile size.
    public static int BenchDiff(CommandArgs args) {
        var frames = args.Int("frames", 120, 2, 10_000);
        args.RejectUnknown();

        // Same simulated frames for every tile size
        using var renderer = new Render.DashboardRenderer();
        var random = new Random(1);
        var snapshot = FakeScenarios.All[0].Snapshot;
        var pixels = new List<byte[]>();
        for (var i = 0; i < frames; i++) {
            snapshot = FakeScenarios.Wander(snapshot, random);
            pixels.Add(Rgb565.FromBitmap(renderer.Render(snapshot)));
        }

        foreach (var (tileWidth, tileHeight) in new[] { (8, 8), (16, 16), (32, 32), (48, 32), (64, 32) }) {
            long bytes = 0, rects = 0;
            var stopwatch = Stopwatch.StartNew();
            for (var i = 1; i < frames; i++) {
                foreach (var rect in FrameDiff.ChangedRects(pixels[i - 1], pixels[i], DisplayWorker.Width, DisplayWorker.Height, tileWidth, tileHeight)) {
                    bytes += rect.Width * rect.Height * 2;
                    rects++;
                }
            }
            var updates = frames - 1;
            Log.Info(string.Create(CultureInfo.InvariantCulture,
                $"Tiles {tileWidth}×{tileHeight}: per update {bytes / updates} bytes in {(double)rects / updates:F1} rects, " +
                $"diff {stopwatch.Elapsed.TotalMilliseconds / updates:F2} ms"));
        }
        return 0;
    }

    private static (int Width, int Height) ParseTile(string text) {
        var parts = text.Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width is > 0 and <= DisplayWorker.Width &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height) && height is > 0 and <= DisplayWorker.Height) {
            return (width, height);
        }
        throw new UsageException("--tile must be WIDTHxHEIGHT, e.g. 48x32");
    }

    /// --bench-sensors [--cycles N]: update cost of each piece of hardware, in the log. Run as administrator.
    public static int BenchSensors(CommandArgs args) {
        var cycles = args.Int("cycles", 20, 1, 1000);
        args.RejectUnknown();

        SensorBenchmark.Run(cycles);
        return 0;
    }

    /// --dump-sensors [PATH]: defaults to sensors.txt in the current directory. Run as administrator.
    public static int DumpSensors(CommandArgs args) {
        var path = Path.GetFullPath(args.Positional(0) ?? "sensors.txt");
        args.RejectUnknown();

        Log.Info($"Administrator: {SensorDump.IsAdministrator()}");
        SensorDump.Write(path);
        Log.Info($"Sensors written to {path}");
        return 0;
    }

    private static void SavePng(SKBitmap bitmap, string path) {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
        using var file = File.Create(path);
        bitmap.Encode(file, SKEncodedImageFormat.Png, 100);
    }

    private static DisplaySettings ReadDisplaySettings(CommandArgs args, bool landscapeOnly) {
        var orientation = (Orientation)args.Int("orientation", (int)Orientation.Landscape, 0, 3);
        if (landscapeOnly && orientation is not (Orientation.Landscape or Orientation.ReverseLandscape)) {
            throw new UsageException("--orientation must be 2 or 3 for a 480×320 image");
        }

        var options = new RevAOptions {
            RtsCts = args.OnOff("rtscts", false),
            DtrRts = args.OnOff("dtr-rts", true),
            ChunkSize = args.Int("chunk", 0, 0, 1_000_000),
            FrameGapMs = args.Int("gap", 0, 0, 1000)
        };
        return new DisplaySettings(args.String("port"), options, orientation, args.Int("brightness", DefaultBrightness, 0, 100));
    }

    private static RevADisplay? OpenDisplay(DisplaySettings settings) {
        var port = PortLocator.Find(settings.ForcedPort);
        if (port == null) {
            Log.Error(settings.ForcedPort != null ? $"{settings.ForcedPort} is not present" : "Screen not connected");
            return null;
        }

        Log.Info($"Opening {port}: {settings.Describe()}");
        return RevADisplay.Open(port, settings.Options);
    }

    private static int ShowBitmap(DisplaySettings settings, SKBitmap bitmap, int repeat) {
        var stopwatch = Stopwatch.StartNew();
        var pixels = Rgb565.FromBitmap(bitmap);
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"RGB565 conversion: {stopwatch.Elapsed.TotalMilliseconds:F2} ms"));

        using var display = OpenDisplay(settings);
        if (display == null) return 1;

        display.ScreenOn();
        display.SetBrightness(settings.BrightnessPercent);
        display.SetOrientation(settings.Orientation);

        // Includes the --gap pause; the last write may return before the USB transfer ends
        var durations = new double[repeat];
        for (var i = 0; i < repeat; i++) {
            stopwatch.Restart();
            display.DisplayBitmap(0, 0, bitmap.Width, bitmap.Height, pixels);
            durations[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(durations);
        var p95 = durations[(int)Math.Ceiling(0.95 * repeat) - 1];
        Log.Info(string.Create(CultureInfo.InvariantCulture,
            $"{repeat} frame(s) of {pixels.Length} bytes: min {durations[0]:F1} ms, average {durations.Average():F1} ms, p95 {p95:F1} ms, max {durations[^1]:F1} ms"));
        return 0;
    }
}
