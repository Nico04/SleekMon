using System.Text.Json;

namespace SleekMon.App;

/// Reads and writes the JSON settings. A missing file is created with the defaults; an invalid one is left untouched
/// (overwriting it would lose the user's edits): defaults are used and changes are not saved.
internal sealed class ConfigFile {
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SleekMon", "config.json");

    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _path;
    private bool _saveAllowed = true;

    public ConfigFile(string? path = null) {
        _path = path ?? DefaultPath;
    }

    public AppConfig Load() {
        if (!File.Exists(_path)) {
            var defaults = new AppConfig();
            Save(defaults);
            Log.Info($"Config created with defaults: {_path}");
            return defaults;
        }

        try {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), Options) ?? new AppConfig();
            Log.Info($"Config loaded: {_path}");
            return config.Normalized();
        } catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) {
            _saveAllowed = false;
            Log.Error($"Invalid config {_path}: defaults used, changes will not be saved. {e.Message}");
            return new AppConfig();
        }
    }

    public void Save(AppConfig config) {
        if (!_saveAllowed) {
            Log.Warn("Config not saved: the file is invalid");
            return;
        }

        try {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } directory) Directory.CreateDirectory(directory);
            // Written aside then swapped: a crash cannot leave half a file
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, Options));
            File.Move(temporaryPath, _path, overwrite: true);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            Log.Error($"Cannot save config {_path}: {e.Message}");
        }
    }
}
