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

    private static int RunApp() {
        Log.Info("Tray app not implemented yet");
        return 0;
    }
}
