using System.Globalization;

namespace SleekMon.Render;

/// Display formats (DESIGN.md "Formats"): invariant culture, no thousands separator, decimal point.
internal static class Formatter {
    public const string Missing = "--";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// Displayed values are whole numbers; thresholds apply to them, so a displayed 90% is always critical.
    public static int? Round(double? value) =>
        value is { } v ? (int)Math.Round(v, MidpointRounding.AwayFromZero) : null;

    public static string Number(int? value) => value?.ToString(Invariant) ?? Missing;

    public static string Percent(int? value) => $"{Number(value)}%";

    /// "76°", or "--" without the degree sign.
    public static string Temperature(int? value) => value is { } v ? $"{v.ToString(Invariant)}°" : Missing;

    public static string Rpm(int? value) => $"{Number(value)} RPM";

    /// One decimal: "7.8".
    public static string Gigabytes(double? value) => value?.ToString("0.0", Invariant) ?? Missing;

    /// "7.4/12": the total is rounded to a whole number (the OS reports a bit less than the installed capacity).
    public static string UsedOfTotal(double? used, double? total) =>
        $"{Gigabytes(used)}/{total?.ToString("0", Invariant) ?? Missing}";

    public static string Time(DateTime time) => time.ToString("HH:mm", Invariant);

    /// "Mon, Oct 19"
    public static string Date(DateTime time) => time.ToString("ddd, MMM d", Invariant);
}
