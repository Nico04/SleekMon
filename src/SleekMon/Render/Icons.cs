using SkiaSharp;

namespace SleekMon.Render;

/// Stroked icon drawn in a 24×24 box, like the mockup's SVGs (viewBox 0 0 24 24, no fill, round caps).
internal sealed record IconShape(SKPath Path, float StrokeWidth, bool RoundJoin);

/// The dashboard icons (DESIGN.md "Icônes"), built once.
internal sealed class Icons : IDisposable {
    public IconShape Cpu { get; } = Build(1.7f, false, builder => {
        builder.AddRoundRect(SKRect.Create(6, 6, 12, 12), 2, 2);
        builder.AddRect(SKRect.Create(9.5f, 9.5f, 5, 5));
    }, "M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3");

    public IconShape Gpu { get; } = Build(1.7f, false, builder => {
        builder.AddRoundRect(SKRect.Create(2, 6, 20, 11), 1.5f, 1.5f);
        builder.AddCircle(15, 11.5f, 3);
    }, "M5 9.5h4M5 13h4M5 17v3M9 17v2");

    public IconShape Ram { get; } = Build(1.7f, false, builder => {
        builder.AddRoundRect(SKRect.Create(2, 7, 20, 9), 1, 1);
    }, "M6 10v3M10 10v3M14 10v3M18 10v3M5 16v2M9 16v2M15 16v2M19 16v2");

    public IconShape Case { get; } = Build(1.7f, false, builder => {
        builder.AddRoundRect(SKRect.Create(6, 2, 12, 20), 1.5f, 1.5f);
        builder.AddCircle(12, 15.5f, 2.5f);
    }, "M9 6h6M9 9h6");

    public IconShape Process { get; } = Build(1.7f, true, builder => {
        builder.AddRoundRect(SKRect.Create(3, 4, 18, 16), 2, 2);
    }, "M3 8h18M7 12l2.5 2.5L7 17M12 17h5");

    public IconShape Fan { get; } = Build(2, true, builder => {
        builder.AddCircle(12, 12, 2);
    }, "M12 10c0-4 1-7 4-7 2 0 3 2 2 4-1 2-3 3-6 3z" +
       "M13.7 13c3.5 2 5.4 4.5 3.9 7-1 1.7-3.3 1.6-4.5-.2-1.2-1.9-1.1-4.1.6-6.8z" +
       "M10.3 13c-3.5 2-6.6 2.4-8.1-.2-1-1.7.1-3.7 2.3-3.8 2.2-.1 4.1 1 5.8 4z");

    public void Dispose() {
        foreach (var icon in new[] { Cpu, Gpu, Ram, Case, Process, Fan }) icon.Path.Dispose();
    }

    private static IconShape Build(float strokeWidth, bool roundJoin, Action<SKPathBuilder> shapes, string svgPath) {
        using var builder = new SKPathBuilder();
        shapes(builder);
        using var lines = SKPath.ParseSvgPathData(svgPath);
        builder.AddPath(lines);
        return new IconShape(builder.Detach(), strokeWidth, roundJoin);
    }
}
