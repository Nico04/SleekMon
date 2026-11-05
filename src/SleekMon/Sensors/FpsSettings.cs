namespace SleekMon.Sensors;

/// When the FPS pill shows, for the program in the foreground: always for the games Windows knows (Game Bar list) and
/// the Always programs, never for the Never programs; any other program once it presents at least MinFps for MinSeconds
/// in a row (and hidden once below for as long).
internal sealed record FpsSettings {
    public int MinFps { get; init; } = 20;
    public int MinSeconds { get; init; } = 3;
    /// Exe names ("GravityMark.exe", ".exe" optional), case-insensitive.
    public string[] Always { get; init; } = [];
    public string[] Never { get; init; } = [];
}
