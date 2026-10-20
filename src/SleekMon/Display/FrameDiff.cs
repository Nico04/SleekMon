namespace SleekMon.Display;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// Finds what to send so that the screen shows a new frame, from the frame it currently shows.
/// Frames are RGB565 buffers, 2 bytes per pixel, rows packed.
internal static class FrameDiff {
    /// Changed tiles of a grid; neighbouring changed tiles of a tile row are merged into one rectangle, then shrunk to the
    /// bounding box of the pixels that actually changed (the USB link, ~170 KB/s, is the bottleneck).
    public static List<PixelRect> ChangedRects(ReadOnlySpan<byte> shown, ReadOnlySpan<byte> next, int width, int height,
        int tileWidth, int tileHeight) {
        var rects = new List<PixelRect>();
        var columns = (width + tileWidth - 1) / tileWidth;
        for (var y = 0; y < height; y += tileHeight) {
            var rowHeight = Math.Min(tileHeight, height - y);
            var runStart = -1;
            // One step past the last column closes a run that reaches the right edge
            for (var column = 0; column <= columns; column++) {
                var x = column * tileWidth;
                var changed = column < columns &&
                    TileDiffers(shown, next, width, x, y, Math.Min(tileWidth, width - x), rowHeight);
                if (changed && runStart < 0) {
                    runStart = x;
                } else if (!changed && runStart >= 0) {
                    rects.Add(Shrink(shown, next, width, new PixelRect(runStart, y, Math.Min(x, width) - runStart, rowHeight)));
                    runStart = -1;
                }
            }
        }
        return rects;
    }

    /// Copies a rectangle of a frame into a packed buffer, as the screen expects it.
    public static void CopyRect(ReadOnlySpan<byte> frame, int width, PixelRect rect, Span<byte> destination) {
        var rowBytes = rect.Width * 2;
        for (var row = 0; row < rect.Height; row++) {
            frame.Slice(((rect.Y + row) * width + rect.X) * 2, rowBytes).CopyTo(destination.Slice(row * rowBytes, rowBytes));
        }
    }

    /// Bounding box of the changed pixels of a rectangle that contains at least one.
    private static PixelRect Shrink(ReadOnlySpan<byte> shown, ReadOnlySpan<byte> next, int width, PixelRect rect) {
        var rowBytes = rect.Width * 2;
        int top = -1, bottom = -1, left = rect.Width, right = -1;
        for (var row = 0; row < rect.Height; row++) {
            var offset = ((rect.Y + row) * width + rect.X) * 2;
            var before = shown.Slice(offset, rowBytes);
            var after = next.Slice(offset, rowBytes);
            var firstDifference = before.CommonPrefixLength(after);
            if (firstDifference == rowBytes) continue;

            if (top < 0) top = row;
            bottom = row;
            // Byte indexes to pixel indexes
            left = Math.Min(left, firstDifference / 2);
            right = Math.Max(right, LastDifference(before, after) / 2);
        }
        return new PixelRect(rect.X + left, rect.Y + top, right - left + 1, bottom - top + 1);
    }

    private static int LastDifference(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after) {
        for (var i = before.Length - 1; i >= 0; i--) {
            if (before[i] != after[i]) return i;
        }
        return -1;
    }

    private static bool TileDiffers(ReadOnlySpan<byte> shown, ReadOnlySpan<byte> next, int width, int x, int y, int tileWidth,
        int tileHeight) {
        var rowBytes = tileWidth * 2;
        for (var row = y; row < y + tileHeight; row++) {
            var offset = (row * width + x) * 2;
            if (!shown.Slice(offset, rowBytes).SequenceEqual(next.Slice(offset, rowBytes))) return true;
        }
        return false;
    }
}
