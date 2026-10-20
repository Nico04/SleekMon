using SkiaSharp;

namespace SleekMon.Render;

/// Landscape test pattern: border and numbered corners (orientation, alignment), color bars and ramps (RGB565 banding),
/// the design palette (how its dark grays come out) and 1 px lines (sharpness).
internal static class TestPattern {
    public const int Width = 480;
    public const int Height = 320;

    private static readonly SKColor Muted = SKColor.Parse("#A1A1AA");

    private static readonly SKColor[] Bars = [
        SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Cyan,
        SKColors.Magenta, SKColors.Yellow, SKColors.White, new(128, 128, 128)
    ];

    private static readonly SKColor[] Ramps = [SKColors.White, SKColors.Red, SKColors.Lime, SKColors.Blue];

    private static readonly (string Name, SKColor Color)[] Palette = [
        ("bg", SKColor.Parse("#0E0F11")),
        ("card", SKColor.Parse("#1B1C1F")),
        ("segOff", SKColor.Parse("#2C2D31")),
        ("idle", SKColor.Parse("#52525B")),
        ("dim", SKColor.Parse("#71717A")),
        ("muted", SKColor.Parse("#A1A1AA")),
        ("segOn", SKColor.Parse("#D4D4D8")),
        ("text", SKColor.Parse("#F4F4F5")),
        ("warm", SKColor.Parse("#F5A524")),
        ("hot", SKColor.Parse("#F2555A")),
        ("ok", SKColor.Parse("#3DD68C"))
    ];

    public static SKBitmap Draw(string caption) {
        var bitmap = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        using var typeface = SKTypeface.FromFile(FontPath("SairaSemiCondensed-SemiBold.ttf"));
        using var fill = new SKPaint();
        fill.IsAntialias = true;
        using var line = new SKPaint();
        line.Style = SKPaintStyle.Stroke;
        line.StrokeWidth = 1;
        line.Color = SKColors.White;

        // Border on the outermost pixels: any missing side shows a cropped or shifted image
        canvas.DrawRect(new SKRect(0.5f, 0.5f, Width - 0.5f, Height - 0.5f), line);

        DrawCorners(canvas, typeface, fill);
        DrawTopMarker(canvas, typeface, fill);

        // Color bars
        const float left = 20, right = Width - 20, barWidth = (right - left) / 8;
        for (var i = 0; i < Bars.Length; i++) {
            fill.Color = Bars[i];
            canvas.DrawRect(left + i * barWidth, 40, barWidth, 50, fill);
        }

        // Ramps from black, 10 px each
        for (var i = 0; i < Ramps.Length; i++) {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(left, 0), new SKPoint(right, 0), [SKColors.Black, Ramps[i]], SKShaderTileMode.Clamp);
            fill.Shader = shader;
            canvas.DrawRect(left, 98 + i * 10, right - left, 10, fill);
            fill.Shader = null;
        }

        // Design palette with names
        using var labelFont = new SKFont(typeface, 10);
        const float swatchWidth = 36, swatchStep = 40, swatchLeft = (Width - (11 * swatchStep - (swatchStep - swatchWidth))) / 2;
        for (var i = 0; i < Palette.Length; i++) {
            var x = swatchLeft + i * swatchStep;
            fill.Color = Palette[i].Color;
            canvas.DrawRect(x, 150, swatchWidth, 50, fill);
            fill.Color = Muted;
            canvas.DrawText(Palette[i].Name, x + swatchWidth / 2, 214, SKTextAlign.Center, labelFont, fill);
        }

        using var titleFont = new SKFont(typeface, 16);
        fill.Color = SKColors.White;
        canvas.DrawText("SleekMon test pattern", Width / 2f, 246, SKTextAlign.Center, titleFont, fill);
        using var captionFont = new SKFont(typeface, 12);
        fill.Color = Muted;
        canvas.DrawText(caption, Width / 2f, 266, SKTextAlign.Center, captionFont, fill);

        // Alternating 1 px lines
        for (var x = 40; x < Width - 40; x += 2) {
            canvas.DrawLine(x + 0.5f, 280, x + 0.5f, 300, line);
        }

        return bitmap;
    }

    /// Numbered clockwise from the top-left corner.
    private static void DrawCorners(SKCanvas canvas, SKTypeface typeface, SKPaint paint) {
        using var font = new SKFont(typeface, 22);
        paint.Color = SKColors.White;
        const float margin = 8;
        var top = margin + font.Metrics.CapHeight;
        const float bottom = Height - margin;
        canvas.DrawText("1", margin, top, SKTextAlign.Left, font, paint);
        canvas.DrawText("2", Width - margin, top, SKTextAlign.Right, font, paint);
        canvas.DrawText("3", Width - margin, bottom, SKTextAlign.Right, font, paint);
        canvas.DrawText("4", margin, bottom, SKTextAlign.Left, font, paint);
    }

    private static void DrawTopMarker(SKCanvas canvas, SKTypeface typeface, SKPaint paint) {
        using var font = new SKFont(typeface, 12);
        using var builder = new SKPathBuilder();
        builder.AddPoly([new SKPoint(Width / 2f, 6), new SKPoint(Width / 2f - 8, 16), new SKPoint(Width / 2f + 8, 16)]);
        using var arrow = builder.Detach();
        paint.Color = SKColors.Yellow;
        canvas.DrawPath(arrow, paint);
        canvas.DrawText("TOP", Width / 2f, 30, SKTextAlign.Center, font, paint);
    }

    private static string FontPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "assets", "fonts", fileName);
}
