using SkiaSharp;

namespace SleekMon.Render;

/// Mockup, render and their difference side by side, enlarged without smoothing, to check a render pixel by pixel.
internal static class MockupComparison {
    private const int Scale = 2;
    private const int Gap = 8;
    private const int DifferenceGain = 4;

    /// Also returns the mean absolute difference per channel (0–255).
    public static SKBitmap Compose(SKBitmap mockup, SKBitmap render, out double meanDifference) {
        if (mockup.Width != render.Width || mockup.Height != render.Height) {
            throw new ArgumentException($"Mockup is {mockup.Width}×{mockup.Height}, render is {render.Width}×{render.Height}");
        }

        using var expected = mockup.Copy(SKColorType.Bgra8888);
        using var actual = render.Copy(SKColorType.Bgra8888);
        using var difference = new SKBitmap(render.Width, render.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var expectedPixels = expected.GetPixelSpan();
        var actualPixels = actual.GetPixelSpan();
        var differencePixels = difference.GetPixelSpan();
        long total = 0;
        for (var i = 0; i < differencePixels.Length; i += 4) {
            for (var channel = 0; channel < 3; channel++) {
                var delta = Math.Abs(expectedPixels[i + channel] - actualPixels[i + channel]);
                total += delta;
                differencePixels[i + channel] = (byte)Math.Min(255, delta * DifferenceGain);
            }
            differencePixels[i + 3] = 255;
        }
        meanDifference = total / (render.Width * render.Height * 3.0);

        var width = render.Width * Scale;
        var height = render.Height * Scale;
        var result = new SKBitmap(3 * width + 2 * Gap, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Magenta);
        var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
        SKBitmap[] panels = [expected, actual, difference];
        for (var i = 0; i < panels.Length; i++) {
            using var image = SKImage.FromBitmap(panels[i]);
            canvas.DrawImage(image, SKRect.Create(i * (width + Gap), 0, width, height), sampling);
        }
        return result;
    }
}
