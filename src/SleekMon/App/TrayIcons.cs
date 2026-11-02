using System.Drawing;
using System.Windows.Forms;
using SkiaSharp;
using SleekMon.Render;

namespace SleekMon.App;

/// Tray icons drawn at startup, one per state: a small screen, filled when connected (green) or connecting (orange),
/// outlined when sleeping or disconnected, crossed out when disabled. No image file to ship.
internal sealed class TrayIcons : IDisposable {
    // Every size Windows may pick for the tray, up to 200 % scaling
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48];

    private readonly Dictionary<DashboardState, Icon> _icons =
        Enum.GetValues<DashboardState>().ToDictionary(state => state, Create);

    public Icon For(DashboardState state) => _icons[state];

    public void Dispose() {
        foreach (var icon in _icons.Values) icon.Dispose();
    }

    private static Icon Create(DashboardState state) {
        var images = Sizes.Select(size => Draw(state, size)).ToArray();

        // ICO file holding PNG images
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)images.Length);
            var offset = 6 + 16 * images.Length;
            for (var i = 0; i < images.Length; i++) {
                writer.Write((byte)Sizes[i]);
                writer.Write((byte)Sizes[i]);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) writer.Write(image);
        }
        stream.Position = 0;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    /// Drawn on a 16-unit grid, scaled to the size.
    private static byte[] Draw(DashboardState state, int size) {
        using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(size / 16f);

            var color = state switch {
                DashboardState.Connected => Theme.Ok,
                DashboardState.Connecting => Theme.Warm,
                DashboardState.Sleeping => Theme.Muted,
                _ => Theme.Dim
            };
            using var paint = new SKPaint();
            paint.IsAntialias = true;
            paint.Color = color;

            // Screen
            if (state is DashboardState.Connected or DashboardState.Connecting) {
                canvas.DrawRoundRect(new SKRect(1, 2, 15, 12), 1.5f, 1.5f, paint);
            } else {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                canvas.DrawRoundRect(new SKRect(1.75f, 2.75f, 14.25f, 11.25f), 1.25f, 1.25f, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            // Stand
            canvas.DrawRect(new SKRect(7, 12, 9, 14), paint);
            canvas.DrawRoundRect(new SKRect(4.5f, 13.5f, 11.5f, 15), 0.75f, 0.75f, paint);

            if (state == DashboardState.Disabled) {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                paint.StrokeCap = SKStrokeCap.Round;
                canvas.DrawLine(2, 14, 14, 1, paint);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
        return stream.ToArray();
    }
}
