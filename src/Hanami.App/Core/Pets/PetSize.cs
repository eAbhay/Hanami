using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hanami.Core.Pets;

/// <summary>
/// How big a pet is drawn. Packs come with art of very different sizes (a 128 px Shimeji frame, a 192×208 Petdex cell with
/// a taller character), so the size is set by how tall the character itself is: at the Size slider's 100% and a 100%
/// display every pet stands about <see cref="TargetHeight"/> px tall.
/// </summary>
public static class PetSize
{
    /// <summary>On-screen height in px of a pet's standing pose at Size 100% on a 100% display.</summary>
    public const double TargetHeight = 100;
    /// <summary>Bounds on the correction so tiny or huge art, or a blank stand frame, stays sane.</summary>
    public const double MinFactor = 0.25, MaxFactor = 3;

    /// <summary>
    /// The factor from image pixels to screen pixels: the user's <paramref name="userScale"/> × the display's
    /// <paramref name="dpi"/> scale × the pack's own correction, <see cref="TargetHeight"/> / <paramref name="standHeight"/>.
    /// A pack whose stand height is unknown (0) is drawn at its image size, as before.
    /// </summary>
    public static double ScaleFor(int standHeight, double userScale, double dpi) =>
        userScale * dpi * (standHeight > 0 ? Math.Clamp(TargetHeight / standHeight, MinFactor, MaxFactor) : 1);

    /// <summary>Height in px of the box around every pixel above the alpha threshold (0 when there are none).</summary>
    public static int VisibleHeight(byte[] bgra, int width, int height)
    {
        int top = int.MaxValue, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            var row = (long)y * width * 4;
            for (var x = 0; x < width; x++)
                if (bgra[row + x * 4 + 3] > PetdexLayout.AlphaThreshold)
                {
                    if (y < top) top = y;
                    bottom = y;
                    break;
                }
        }
        return bottom < 0 ? 0 : bottom - top + 1;
    }

    /// <summary>The visible height of a PNG, or 0 when it can't be decoded.</summary>
    public static int VisibleHeightOf(byte[] png)
    {
        try
        {
            using var ms = new MemoryStream(png);
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var (w, h) = (converted.PixelWidth, converted.PixelHeight);
            var pixels = new byte[w * h * 4];
            converted.CopyPixels(pixels, w * 4, 0);
            return VisibleHeight(pixels, w, h);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or IOException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The pack's standing height: the tallest visible height among the frames its stand animation shows (a pet that bobs
    /// while idle is measured at its tallest). <paramref name="readFrame"/> returns a frame's PNG bytes, or null if missing.
    /// </summary>
    public static int StandHeight(PetAnimations animations, Func<int, byte[]?> readFrame)
    {
        var best = 0;
        foreach (var frame in animations.Stand.Select(p => p.Frame).Distinct())
            if (readFrame(frame) is { } bytes) best = Math.Max(best, VisibleHeightOf(bytes));
        return best;
    }
}
