using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hanami.Core.Pets;

/// <summary>
/// Many Shimeji packs draw some frames a few pixels bigger or smaller than the rest (a pack with a 128×128 stand and a
/// 128×132 frame is common). Hanami keeps all of a pack's frames one size, so such a pack is put on one canvas, the size of
/// its biggest frame: every smaller frame is placed on it bottom-centre, where the feet are (the default table's anchor is
/// the bottom centre). A pack's own anchors (in image pixels) are moved by the same amount for the frames they use.
/// </summary>
public static class PetFrameSizes
{
    /// <summary>The frames and mapping on one canvas, or null when there is nothing to do (the sizes already match, or one can't be read).</summary>
    public static (Dictionary<int, byte[]> Frames, PetMapping? Mapping, int Width, int Height)? Equalize(IReadOnlyDictionary<int, byte[]> frames, PetMapping? mapping)
    {
        var sizes = new Dictionary<int, (int w, int h)>();
        foreach (var (n, bytes) in frames)
        {
            if (!PetPackReader.TryDecodeSize(bytes, out var w, out var h)) return null;   // Validate reports it
            sizes[n] = (w, h);
        }
        if (sizes.Count == 0 || sizes.Values.Distinct().Count() == 1) return null;
        int width = sizes.Values.Max(s => s.w), height = sizes.Values.Max(s => s.h);
        if (width > PetPackReader.MaxFrameSize || height > PetPackReader.MaxFrameSize) return null;

        var result = new Dictionary<int, byte[]>();
        var offsets = new Dictionary<int, (int dx, int dy)>();
        foreach (var (n, bytes) in frames)
        {
            var (w, h) = sizes[n];
            if ((w, h) == (width, height)) { result[n] = bytes; continue; }
            var (dx, dy) = ((width - w) / 2, height - h);
            result[n] = Pad(bytes, w, h, width, height, dx, dy);
            offsets[n] = (dx, dy);
        }

        if (mapping is { ImagePixels: true, Animations: { } animations })
        {
            var moved = new PetMapping { ImagePixels = true, Swing = mapping.Swing, Animations = new() };
            foreach (var (name, poses) in animations)
                moved.Animations![name] = poses.Select(p => offsets.TryGetValue(p.Frame, out var o)
                    ? new PoseDef { Frame = p.Frame, Ticks = p.Ticks, AnchorX = p.AnchorX + o.dx, AnchorY = p.AnchorY + o.dy, Vx = p.Vx, Vy = p.Vy, Mirror = p.Mirror, Twin = p.Twin }
                    : p).ToList();
            mapping = moved;
        }
        return (result, mapping, width, height);
    }

    private static byte[] Pad(byte[] png, int w, int h, int width, int height, int dx, int dy)
    {
        using var input = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var src = new byte[w * h * 4];
        source.CopyPixels(src, w * 4, 0);

        var dst = new byte[width * height * 4];   // transparent
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(src, y * w * 4, dst, ((y + dy) * width + dx) * 4, w * 4);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, dst, width * 4)));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
