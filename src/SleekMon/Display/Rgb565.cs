using System.Runtime.InteropServices;
using SkiaSharp;

namespace SleekMon.Display;

/// The screen's pixel format: RGB565 little-endian, (r << 11) | (g << 5) | b.
/// Skia's Rgb565 has the same layout and byte order on x64 (checked on 20/10/2026), rounds to the nearest value
/// instead of truncating, and converts a 480×320 frame in about 0.5 ms.
internal static class Rgb565 {
    public static byte[] FromBitmap(SKBitmap source) {
        var pixels = new byte[source.Width * source.Height * 2];
        Convert(source, pixels);
        return pixels;
    }

    public static void Convert(SKBitmap source, byte[] destination) {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgb565, SKAlphaType.Opaque);
        if (destination.Length != info.BytesSize) {
            throw new ArgumentException($"Expected {info.BytesSize} bytes, got {destination.Length}", nameof(destination));
        }

        var handle = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try {
            using var pixmap = source.PeekPixels();
            if (!pixmap.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes)) {
                throw new InvalidOperationException($"Cannot convert {source.ColorType} pixels to RGB565");
            }
        } finally {
            handle.Free();
        }
    }
}
