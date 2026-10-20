using System.Globalization;

namespace SleekMon.App;

/// Invalid command line: logged without a stack trace, exit code 2.
internal sealed class UsageException(string message) : Exception(message);

/// Arguments after the command name: positional values, "--name value" options and "--name" flags.
/// A value is anything that does not start with "--", so flags go after the positional values.
internal sealed class CommandArgs {
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);

    public CommandArgs(IReadOnlyList<string> args) {
        for (var i = 0; i < args.Count; i++) {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) {
                _positional.Add(args[i]);
                continue;
            }

            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            _options[args[i][2..]] = hasValue ? args[++i] : null;
        }
    }

    public string? Positional(int index) => index < _positional.Count ? _positional[index] : null;

    public bool Flag(string name) {
        _read.Add(name);
        if (!_options.TryGetValue(name, out var value)) return false;
        if (value != null) throw new UsageException($"--{name} takes no value");
        return true;
    }

    public string? String(string name) {
        _read.Add(name);
        if (!_options.TryGetValue(name, out var value)) return null;
        return value ?? throw new UsageException($"--{name} needs a value");
    }

    public int Int(string name, int defaultValue, int min, int max) {
        var text = String(name);
        if (text == null) return defaultValue;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max) {
            throw new UsageException($"--{name} must be an integer between {min} and {max}");
        }
        return value;
    }

    public bool OnOff(string name, bool defaultValue) => String(name) switch {
        null => defaultValue,
        "on" => true,
        "off" => false,
        _ => throw new UsageException($"--{name} must be on or off")
    };

    /// Call once every option has been read: a typo must not silently fall back to a default.
    public void RejectUnknown() {
        var unknown = _options.Keys.Where(name => !_read.Contains(name)).ToList();
        if (unknown.Count > 0) throw new UsageException($"Unknown option(s): {string.Join(", ", unknown.Select(name => "--" + name))}");
    }
}
