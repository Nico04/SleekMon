using SkiaSharp;

namespace SleekMon.Render;

internal enum Weight {
    Medium,
    SemiBold,
    Bold
}

/// Vertical extent of a CSS line box around its baseline.
internal readonly record struct LineBox(float Above, float Below) {
    public float Height => Above + Below;
}

/// Saira Semi Condensed fonts, and text measuring and drawing the way the mockup's browser lays it out.
internal sealed class Typography : IDisposable {
    private readonly Dictionary<Weight, SKTypeface> _typefaces;
    private readonly Dictionary<(Weight, float), SKFont> _fonts = [];

    public Typography() {
        _typefaces = new Dictionary<Weight, SKTypeface> {
            [Weight.Medium] = LoadTypeface("SairaSemiCondensed-Medium.ttf"),
            [Weight.SemiBold] = LoadTypeface("SairaSemiCondensed-SemiBold.ttf"),
            [Weight.Bold] = LoadTypeface("SairaSemiCondensed-Bold.ttf")
        };
    }

    public SKFont Font(Weight weight, float size) {
        if (_fonts.TryGetValue((weight, size), out var font)) return font;

        // Grayscale antialiasing: the screen's subpixel order is unknown
        font = new SKFont(_typefaces[weight], size) { Edging = SKFontEdging.Antialias, Subpixel = true };
        _fonts[(weight, size)] = font;
        return font;
    }

    /// Like a browser: ascent, descent and line gap rounded to whole pixels; "normal" line height when null,
    /// otherwise the extra space (half-leading) split above and below.
    public static LineBox Box(SKFont font, float? lineHeight = null) {
        var metrics = font.Metrics;
        var ascent = MathF.Round(-metrics.Ascent);
        var descent = MathF.Round(metrics.Descent);
        var halfLeading = lineHeight is { } height ? (height - ascent - descent) / 2 : MathF.Round(metrics.Leading) / 2;
        return new LineBox(ascent + halfLeading, descent + halfLeading);
    }

    /// Advance width; CSS letter spacing is added after every character, the last one included.
    public static float Measure(string text, SKFont font, float letterSpacing = 0) =>
        letterSpacing == 0 ? font.MeasureText(text) : font.GetGlyphWidths(text).Sum() + letterSpacing * text.Length;

    /// Returns the advance width.
    public static float Draw(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint,
        float letterSpacing = 0) {
        if (text.Length == 0) return 0;

        // Whole-pixel baseline, halves rounded down the page like the browser (Skia alone would round 176.5 to 176)
        baseline = MathF.Floor(baseline + 0.5f);
        if (letterSpacing == 0) {
            canvas.DrawText(text, x, baseline, SKTextAlign.Left, font, paint);
            return font.MeasureText(text);
        }

        var widths = font.GetGlyphWidths(text);
        var positions = new float[widths.Length];
        var advance = 0f;
        for (var i = 0; i < widths.Length; i++) {
            positions[i] = advance;
            advance += widths[i] + letterSpacing;
        }
        using var blob = SKTextBlob.CreateHorizontal(text, font, positions, 0);
        canvas.DrawText(blob, x, baseline, paint);
        return advance;
    }

    /// Cuts the text with "…" to fit the width, like CSS text-overflow: ellipsis.
    public static string Ellipsize(string text, SKFont font, float maxWidth) {
        if (Measure(text, font) <= maxWidth) return text;

        for (var length = text.Length - 1; length > 0; length--) {
            var candidate = text[..length] + "…";
            if (Measure(candidate, font) <= maxWidth) return candidate;
        }
        return "…";
    }

    public void Dispose() {
        foreach (var font in _fonts.Values) font.Dispose();
        foreach (var typeface in _typefaces.Values) typeface.Dispose();
    }

    private static SKTypeface LoadTypeface(string fileName) {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", fileName);
        return SKTypeface.FromFile(path) ?? throw new FileNotFoundException("Cannot load font", path);
    }
}
