using SkiaSharp;
using SleekMon.Sensors;

namespace SleekMon.Render;

/// Draws the v3 dashboard (DESIGN.md). Layout constants first, then one method per area.
/// Vertical positions follow the mockup's CSS: each card is a flex column with space-between, and line boxes come
/// from the font metrics like in a browser, so changing a size moves the rest accordingly.
internal sealed class DashboardRenderer : IDisposable {
    public const int Width = 480;
    public const int Height = 320;

    // Grid: whole pixels keep the card edges sharp. Row 2 columns are 150.67 px in the CSS: each edge is rounded to the
    // nearest pixel, which gives 151, 150 and 151 px
    private static readonly SKRect HeaderRow = SKRect.Create(12, 8, 456, 18);
    private static readonly SKRect CpuCard = SKRect.Create(8, 32, 206, 158);
    private static readonly SKRect GpuCard = SKRect.Create(220, 32, 252, 158);
    private static readonly SKRect RamCard = SKRect.Create(8, 196, 151, 116);
    private static readonly SKRect CaseCard = SKRect.Create(165, 196, 150, 116);
    private static readonly SKRect ProcessCard = SKRect.Create(321, 196, 151, 116);

    private const float CardRadius = 12;
    private const float CardPaddingX = 12;
    private const float TitleLetterSpacing = 0.08f; // em
    private const float SegmentGap = 2;

    // Row 1 (CPU, GPU) and row 2 (RAM, CASE, TOP PROCESS) card contents
    private const float LargeTitleHeight = 36;
    private const float LargeIconSize = 20;
    private const float LargeTitleSize = 14;
    private const float SmallTitleHeight = 17;
    private const float SmallIconSize = 17;
    private const float SmallTitleSize = 13;
    private const float FooterHeight = 16;
    private const float FooterTextSize = 13;
    private const float FanIconSize = 13;
    private const float FanGap = 5;

    private readonly SKBitmap _bitmap = new(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
    private readonly Typography _type = new();
    private readonly Icons _icons = new();
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };

    /// The returned bitmap belongs to the renderer and is overwritten by the next call.
    public SKBitmap Render(SensorSnapshot snapshot) {
        using var canvas = new SKCanvas(_bitmap);
        canvas.Clear(Theme.Background);
        DrawHeader(canvas, snapshot);
        DrawCpu(canvas, snapshot);
        DrawGpu(canvas, snapshot);
        DrawRam(canvas, snapshot);
        DrawCase(canvas, snapshot);
        DrawProcess(canvas, snapshot);
        return _bitmap;
    }

    private void DrawHeader(SKCanvas canvas, SensorSnapshot snapshot) {
        // Left: status dot and text
        var status = Theme.StatusOf(snapshot);
        var statusFont = _type.Font(Weight.SemiBold, 13);
        _fill.Color = status.Color;
        canvas.DrawCircle(HeaderRow.Left + 4, HeaderRow.MidY, 4, _fill);
        DrawText(canvas, status.Text, HeaderRow.Left + 8 + 6, CenteredBaseline(HeaderRow.Top, HeaderRow.Height, Typography.Box(statusFont)),
            statusFont, status.Color);

        // Right: time and date on a common baseline
        var timeFont = _type.Font(Weight.Bold, 14);
        var dateFont = _type.Font(Weight.Medium, 12);
        var baseline = BaselineOf(HeaderRow.Top, HeaderRow.Height, Typography.Box(timeFont), Typography.Box(dateFont));
        var date = Formatter.Date(snapshot.Timestamp);
        var time = Formatter.Time(snapshot.Timestamp);
        var dateX = HeaderRow.Right - Typography.Measure(date, dateFont);
        DrawText(canvas, date, dateX, baseline, dateFont, Theme.Muted);
        DrawText(canvas, time, dateX - 6 - Typography.Measure(time, timeFont), baseline, timeFont, Theme.Text);
    }

    private void DrawCpu(SKCanvas canvas, SensorSnapshot snapshot) {
        var (inner, rows) = DrawLoadCard(canvas, CpuCard, _icons.Cpu, "CPU", snapshot.CpuLoad, snapshot.CpuTemp);
        DrawFan(canvas, snapshot.CpuFanRpm, inner.Right, rows[3], FooterHeight, alignRight: true);
    }

    private void DrawGpu(SKCanvas canvas, SensorSnapshot snapshot) {
        var (inner, rows) = DrawLoadCard(canvas, GpuCard, _icons.Gpu, "GPU", snapshot.GpuLoad, snapshot.GpuTemp);
        if (Formatter.Round(snapshot.Fps) is { } fps) DrawFpsPill(canvas, fps, inner.Right, rows[0]);

        // Footer: "62%" colored, " VRAM · " muted, used amount white, " / 12 GB" muted
        var vram = Formatter.Round(snapshot.VramLoad);
        var percentFont = _type.Font(Weight.Bold, FooterTextSize);
        var detailFont = _type.Font(Weight.Medium, FooterTextSize);
        var baseline = BaselineOf(rows[3], FooterHeight, Typography.Box(percentFont), Typography.Box(detailFont));
        var x = inner.Left + DrawText(canvas, Formatter.Percent(vram), inner.Left, baseline, percentFont,
            Theme.ValueColor(Theme.MemoryLevel(vram)));
        x += DrawText(canvas, " VRAM · ", x, baseline, detailFont, Theme.Muted);
        DrawUsedOfTotal(canvas, snapshot.VramUsedGb, snapshot.VramTotalGb, x, baseline, detailFont);

        DrawFan(canvas, snapshot.GpuFanRpm, inner.Right, rows[3], FooterHeight, alignRight: true);
    }

    /// CPU and GPU: title, load and temperature, 20-segment bar, footer row (returned for the caller).
    private (SKRect Inner, float[] Rows) DrawLoadCard(SKCanvas canvas, SKRect card, IconShape icon, string title,
        double? loadValue, double? temperatureValue) {
        var inner = DrawCard(canvas, card, paddingTop: 10, paddingBottom: 10);
        var numberFont = _type.Font(Weight.Bold, 58);
        var unitFont = _type.Font(Weight.SemiBold, 24);
        var temperatureFont = _type.Font(Weight.Bold, 38);
        var valuesBox = Union(Typography.Box(numberFont, 58), Typography.Box(unitFont), Typography.Box(temperatureFont, 38));
        var rows = SpaceBetween(inner.Top, inner.Height, LargeTitleHeight, valuesBox.Height, 8, FooterHeight);

        DrawTitle(canvas, icon, LargeIconSize, title, LargeTitleSize, inner.Left, rows[0], LargeTitleHeight);

        var load = Formatter.Round(loadValue);
        var loadLevel = Theme.LoadLevel(load);
        var baseline = rows[1] + valuesBox.Above;
        var numberWidth = DrawText(canvas, Formatter.Number(load), inner.Left, baseline, numberFont, Theme.ValueColor(loadLevel));
        DrawText(canvas, "%", inner.Left + numberWidth + 2, baseline, unitFont, Theme.UnitColor(loadLevel));

        var temperature = Formatter.Round(temperatureValue);
        var temperatureText = Formatter.Temperature(temperature);
        DrawText(canvas, temperatureText, inner.Right - Typography.Measure(temperatureText, temperatureFont), baseline,
            temperatureFont, Theme.ValueColor(Theme.TemperatureLevel(temperature)));

        DrawBar(canvas, inner.Left, rows[2], inner.Width, 8, 20, 2, load, Theme.SegmentColor(loadLevel));
        return (inner, rows);
    }

    /// Right-aligned in the GPU title row: height 36, horizontal padding 12, fully rounded.
    private void DrawFpsPill(SKCanvas canvas, int fps, float right, float top) {
        const float height = 36, padding = 12, gap = 5;
        var numberFont = _type.Font(Weight.Bold, 31);
        var labelFont = _type.Font(Weight.Bold, 14);
        var labelSpacing = 0.04f * labelFont.Size;
        var number = Formatter.Number(fps);
        var numberWidth = Typography.Measure(number, numberFont);
        var width = padding + numberWidth + gap + Typography.Measure("FPS", labelFont, labelSpacing) + padding;
        var left = right - width;

        _fill.Color = Theme.FpsColor(fps);
        canvas.DrawRoundRect(new SKRect(left, top, right, top + height), height / 2, height / 2, _fill);

        // Both centered in the pill; the label box is 14 px plus a 6 px top padding
        var numberBox = Typography.Box(numberFont, 31);
        DrawText(canvas, number, left + padding, top + (height - numberBox.Height) / 2 + numberBox.Above, numberFont, Theme.Background);
        var labelBox = Typography.Box(labelFont, 14);
        DrawText(canvas, "FPS", left + padding + numberWidth + gap, top + (height - labelBox.Height - 6) / 2 + 6 + labelBox.Above,
            labelFont, Theme.Background, labelSpacing);
    }

    private void DrawRam(SKCanvas canvas, SensorSnapshot snapshot) {
        var inner = DrawCard(canvas, RamCard, paddingTop: 9, paddingBottom: 10);
        var numberFont = _type.Font(Weight.Bold, 38);
        var unitFont = _type.Font(Weight.SemiBold, 18);
        var footerFont = _type.Font(Weight.Medium, FooterTextSize);
        var valueBox = Union(Typography.Box(numberFont, 38), Typography.Box(unitFont));
        var footerBox = Typography.Box(footerFont);
        var rows = SpaceBetween(inner.Top, inner.Height, SmallTitleHeight, valueBox.Height, 6, footerBox.Height);

        DrawTitle(canvas, _icons.Ram, SmallIconSize, "RAM", SmallTitleSize, inner.Left, rows[0], SmallTitleHeight);

        var load = Formatter.Round(snapshot.RamLoad);
        var level = Theme.MemoryLevel(load);
        var baseline = rows[1] + valueBox.Above;
        var numberWidth = DrawText(canvas, Formatter.Number(load), inner.Left, baseline, numberFont, Theme.ValueColor(level));
        DrawText(canvas, "%", inner.Left + numberWidth + 2, baseline, unitFont, Theme.UnitColor(level));

        DrawBar(canvas, inner.Left, rows[2], inner.Width, 6, 12, 1.5f, load, Theme.SegmentColor(level));
        DrawUsedOfTotal(canvas, snapshot.RamUsedGb, snapshot.RamTotalGb, inner.Left, rows[3] + footerBox.Above, footerFont);
    }

    /// "31 / 32 GB": the used amount stands out in white, the rest muted.
    private void DrawUsedOfTotal(SKCanvas canvas, double? used, double? total, float x, float baseline, SKFont font) {
        x += DrawText(canvas, Formatter.WholeGigabytes(used), x, baseline, font, used != null ? Theme.Text : Theme.Dim);
        DrawText(canvas, Formatter.OfTotal(total), x, baseline, font, Theme.Muted);
    }

    private void DrawCase(SKCanvas canvas, SensorSnapshot snapshot) {
        var inner = DrawCard(canvas, CaseCard, paddingTop: 9, paddingBottom: 10);
        var temperatureFont = _type.Font(Weight.Bold, 38);
        var temperatureBox = Typography.Box(temperatureFont, 38);
        var fanHeight = FanGroupHeight();
        var rows = SpaceBetween(inner.Top, inner.Height, SmallTitleHeight, temperatureBox.Height, 6, fanHeight);

        DrawTitle(canvas, _icons.Case, SmallIconSize, "CASE", SmallTitleSize, inner.Left, rows[0], SmallTitleHeight);

        var temperature = Formatter.Round(snapshot.CaseTemp);
        DrawText(canvas, Formatter.Temperature(temperature), inner.Left, rows[1] + temperatureBox.Above, temperatureFont,
            Theme.ValueColor(Theme.CaseTemperatureLevel(temperature)));

        DrawFan(canvas, snapshot.CaseFanRpm, inner.Left, rows[3], fanHeight, alignRight: false);
    }

    private void DrawProcess(SKCanvas canvas, SensorSnapshot snapshot) {
        var inner = DrawCard(canvas, ProcessCard, paddingTop: 9, paddingBottom: 10);
        var nameFont = _type.Font(Weight.Bold, 20);
        var nameBox = Typography.Box(nameFont, 22);
        var percentFont = _type.Font(Weight.Bold, FooterTextSize);
        var detailFont = _type.Font(Weight.Medium, FooterTextSize);
        var footerBox = Union(Typography.Box(percentFont), Typography.Box(detailFont));
        var rows = SpaceBetween(inner.Top, inner.Height, SmallTitleHeight, nameBox.Height, 6, footerBox.Height);

        DrawTitle(canvas, _icons.Process, SmallIconSize, "TOP PROCESS", SmallTitleSize, inner.Left, rows[0], SmallTitleHeight);

        // Not measured yet in real mode (PLAN phase 7): everything shows "--" in dim
        var name = snapshot.TopProcessName;
        var cpu = name != null ? Formatter.Round(snapshot.TopProcessCpu) : null;
        var level = Theme.LoadLevel(cpu);
        DrawText(canvas, Typography.Ellipsize(name ?? Formatter.Missing, nameFont, inner.Width), inner.Left,
            rows[1] + nameBox.Above, nameFont, name != null ? Theme.Text : Theme.Dim);

        DrawBar(canvas, inner.Left, rows[2], inner.Width, 6, 12, 1.5f, cpu, Theme.SegmentColor(level));

        var baseline = rows[3] + footerBox.Above;
        var memory = name != null ? Formatter.Gigabytes(snapshot.TopProcessMemGb) : Formatter.Missing;
        var x = inner.Left + DrawText(canvas, Formatter.Percent(cpu), inner.Left, baseline, percentFont, Theme.ValueColor(level));
        DrawText(canvas, $" CPU · {memory} GB", x, baseline, detailFont, name != null ? Theme.Muted : Theme.Dim);
    }

    /// Card background; returns the content area.
    private SKRect DrawCard(SKCanvas canvas, SKRect card, float paddingTop, float paddingBottom) {
        _fill.Color = Theme.Card;
        canvas.DrawRoundRect(card, CardRadius, CardRadius, _fill);
        return new SKRect(card.Left + CardPaddingX, card.Top + paddingTop, card.Right - CardPaddingX, card.Bottom - paddingBottom);
    }

    /// Icon and spaced uppercase title, vertically centered in the row, muted.
    private void DrawTitle(SKCanvas canvas, IconShape icon, float iconSize, string text, float fontSize, float left, float top,
        float height) {
        DrawIcon(canvas, icon, left, top + (height - iconSize) / 2, iconSize, Theme.Muted);
        var font = _type.Font(Weight.Bold, fontSize);
        DrawText(canvas, text, left + iconSize + 6, CenteredBaseline(top, height, Typography.Box(font)), font, Theme.Muted,
            TitleLetterSpacing * fontSize);
    }

    /// Fan icon and RPM, vertically centered in the row; x is the right edge when right-aligned.
    /// A spinning fan's number stands out in white; stopped (green) and unavailable (dim) keep one color.
    private void DrawFan(SKCanvas canvas, double? rpmValue, float x, float top, float height, bool alignRight) {
        const string unit = " RPM";
        var rpm = Formatter.Round(rpmValue);
        var color = Theme.FanColor(rpm);
        var font = _type.Font(Weight.SemiBold, FooterTextSize);
        var number = Formatter.Number(rpm);
        var left = alignRight ? x - (FanIconSize + FanGap + Typography.Measure(number + unit, font)) : x;

        var groupHeight = FanGroupHeight();
        var groupTop = top + (height - groupHeight) / 2;
        var baseline = CenteredBaseline(groupTop, groupHeight, Typography.Box(font));
        DrawIcon(canvas, _icons.Fan, left, groupTop + (groupHeight - FanIconSize) / 2, FanIconSize, color);
        var textX = left + FanIconSize + FanGap;
        textX += DrawText(canvas, number, textX, baseline, font, rpm > 0 ? Theme.Text : color);
        DrawText(canvas, unit, textX, baseline, font, color);
    }

    private float FanGroupHeight() => Math.Max(FanIconSize, Typography.Box(_type.Font(Weight.SemiBold, FooterTextSize)).Height);

    /// Lit segments: max(p > 0 ? 1 : 0, round(p / 100 × n)). Edges snapped to whole pixels for sharp segments.
    private void DrawBar(SKCanvas canvas, float left, float top, float width, float height, int count, float radius, int? percent,
        SKColor litColor) {
        var lit = percent is { } p ? Math.Clamp(Math.Max(p > 0 ? 1 : 0, (int)Math.Round(p / 100.0 * count, MidpointRounding.AwayFromZero)), 0, count) : 0;
        var segmentWidth = (width - SegmentGap * (count - 1)) / count;
        var y = MathF.Round(top);
        for (var i = 0; i < count; i++) {
            var x = left + i * (segmentWidth + SegmentGap);
            _fill.Color = i < lit ? litColor : Theme.SegmentOff;
            canvas.DrawRoundRect(new SKRect(MathF.Round(x), y, MathF.Round(x + segmentWidth), y + height), radius, radius, _fill);
        }
    }

    private void DrawIcon(SKCanvas canvas, IconShape icon, float x, float y, float size, SKColor color) {
        _stroke.Color = color;
        _stroke.StrokeWidth = icon.StrokeWidth;
        _stroke.StrokeJoin = icon.RoundJoin ? SKStrokeJoin.Round : SKStrokeJoin.Miter;
        canvas.Save();
        canvas.Translate(x, y);
        canvas.Scale(size / 24);
        canvas.DrawPath(icon.Path, _stroke);
        canvas.Restore();
    }

    private float DrawText(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKColor color, float letterSpacing = 0) {
        _fill.Color = color;
        return Typography.Draw(canvas, text, x, baseline, font, _fill, letterSpacing);
    }

    /// Tops of the rows of a flex column with justify-content: space-between, in 1/64 px like the browser's layout units
    /// (otherwise float sums give 163.99998 instead of 164 and the text lands one pixel higher).
    private static float[] SpaceBetween(float top, float height, params ReadOnlySpan<float> rowHeights) {
        var free = height;
        foreach (var rowHeight in rowHeights) free -= rowHeight;
        var gap = free / (rowHeights.Length - 1);

        var tops = new float[rowHeights.Length];
        var y = top;
        for (var i = 0; i < rowHeights.Length; i++) {
            tops[i] = MathF.Round(y * 64) / 64;
            y += rowHeights[i] + gap;
        }
        return tops;
    }

    /// Line boxes aligned on a common baseline (flex align-items: baseline).
    private static LineBox Union(params ReadOnlySpan<LineBox> boxes) {
        float above = 0, below = 0;
        foreach (var box in boxes) {
            above = Math.Max(above, box.Above);
            below = Math.Max(below, box.Below);
        }
        return new LineBox(above, below);
    }

    /// Baseline of a single line box centered in a row (align-items: center).
    private static float CenteredBaseline(float top, float height, LineBox box) => top + (height - box.Height) / 2 + box.Above;

    /// Baseline of line boxes aligned together, the group centered in a row.
    private static float BaselineOf(float top, float height, params ReadOnlySpan<LineBox> boxes) =>
        CenteredBaseline(top, height, Union(boxes));

    public void Dispose() {
        _bitmap.Dispose();
        _type.Dispose();
        _icons.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
    }
}
