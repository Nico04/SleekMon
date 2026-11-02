using System.Windows.Forms;
using SleekMon.App;

namespace SleekMon;

internal static class Program {
    [STAThread]
    private static int Main(string[] args) {
        try {
            if (args.Length == 0) return RunApp();

            Log.Info($"Command: {string.Join(' ', args)}");
            var options = new CommandArgs(args[1..]);
            switch (args[0]) {
                case "--find-port": return DiagnosticCommands.FindPort(options);
                case "--test-pattern": return DiagnosticCommands.TestPattern(options);
                case "--show": return DiagnosticCommands.Show(options);
                case "--screen": return DiagnosticCommands.Screen(options);
                case "--brightness": return DiagnosticCommands.Brightness(options);
                case "--clear": return DiagnosticCommands.Clear(options);
                case "--reset": return DiagnosticCommands.Reset(options);
                case "--render": return DiagnosticCommands.RenderScenarios(options);
                case "--run-fake": return DiagnosticCommands.RunFake(options);
                case "--run": return DiagnosticCommands.Run(options);
                case "--bench-diff": return DiagnosticCommands.BenchDiff(options);
                case "--dump-sensors": return DiagnosticCommands.DumpSensors(options);
                case "--bench-sensors": return DiagnosticCommands.BenchSensors(options);
                default:
                    Log.Error($"Unknown command: {args[0]}");
                    return 2;
            }
        } catch (UsageException e) {
            Log.Error(e.Message);
            return 2;
        } catch (Exception e) {
            Log.Error("Unhandled exception", e);
            return 1;
        }
    }

    /// The tray app, single instance: two would fight for the screen. A new instance (restart as administrator) waits
    /// for the previous one to release the screen and exit.
    private static int RunApp() {
        using var instance = new Mutex(false, @"Local\SleekMon");
        try {
            if (!instance.WaitOne(TimeSpan.FromSeconds(10))) {
                Log.Warn("Already running");
                return 0;
            }
        } catch (AbandonedMutexException) {
            // The previous instance died without releasing it: it is ours now
        }

        try {
            Log.Info($"App started, administrator {Sensors.SensorDump.IsAdministrator()}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            Application.ThreadException += (_, e) => Log.Error("UI thread exception", e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var app = new TrayApp();
            Application.Run(app);
            Log.Info("App stopped");
            return 0;
        } finally {
            instance.ReleaseMutex();
        }
    }
}
