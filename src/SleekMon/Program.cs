namespace SleekMon;

internal static class Program {
    [STAThread]
    private static int Main(string[] args) {
        try {
            if (args.Length == 0) return RunApp();

            Log.Info($"Command: {string.Join(' ', args)}");
            switch (args[0]) {
                default:
                    Log.Error($"Unknown command: {args[0]}");
                    return 2;
            }
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
