using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hanami.Core;
using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>Pets stand about the same height whatever the size of their art.</summary>
public sealed class PetSizeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public PetSizeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A PNG of <paramref name="size"/>×<paramref name="size"/> with an opaque box of the given height standing on its bottom edge.</summary>
    private static byte[] Png(int size, int boxHeight)
    {
        var px = new byte[size * size * 4];
        for (var y = size - boxHeight; y < size; y++)
            for (var x = size / 4; x < size * 3 / 4; x++)
            {
                var o = (y * size + x) * 4;
                px[o] = 200; px[o + 3] = 255;
            }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ---- the scale --------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 1, 1, 1)]        // already 100 px tall: unchanged
    [InlineData(200, 1, 1, 0.5)]      // twice as tall: drawn at half
    [InlineData(50, 1, 1, 2)]
    [InlineData(200, 2, 1, 1)]        // the slider still means what it did
    [InlineData(200, 0.5, 1, 0.25)]
    [InlineData(200, 1, 1.25, 0.625)] // and so does the display's scale
    public void ScaleFor_MakesEveryPetStandTheSameHeight(int standHeight, double user, double dpi, double expected) =>
        Assert.Equal(expected, PetSize.ScaleFor(standHeight, user, dpi), 6);

    [Fact]
    public void ScaleFor_ClampsTinyAndHugeArt_AndKeepsTheOldSizeWhenUnknown()
    {
        Assert.Equal(PetSize.MaxFactor, PetSize.ScaleFor(5, 1, 1));
        Assert.Equal(PetSize.MinFactor, PetSize.ScaleFor(2000, 1, 1));
        Assert.Equal(1.5 * 1.25, PetSize.ScaleFor(0, 1.5, 1.25), 6);   // unknown: image pixels, as before
    }

    [Fact]
    public void TheSameHeightOnScreen_ForDifferentArt()
    {
        // A 128 px Shimeji frame whose character is 100 px tall, and a Petdex cell whose character is 180 px tall.
        var shimeji = 100 * PetSize.ScaleFor(100, 1, 1);
        var petdex = 180 * PetSize.ScaleFor(180, 1, 1);
        Assert.Equal(shimeji, petdex, 6);
        Assert.Equal(PetSize.TargetHeight, petdex, 6);
    }

    // ---- measuring --------------------------------------------------------------------------------

    [Fact]
    public void VisibleHeight_IgnoresFaintPixelsAndEmptyRows()
    {
        var px = new byte[20 * 40 * 4];
        void Set(int x, int y, byte alpha) => px[(y * 20 + x) * 4 + 3] = alpha;
        Set(5, 10, 255); Set(6, 29, 255);               // rows 10 to 29: 20 tall
        Set(5, 2, PetdexLayout.AlphaThreshold);         // a faint halo above doesn't count
        Assert.Equal(20, PetSize.VisibleHeight(px, 20, 40));
        Assert.Equal(0, PetSize.VisibleHeight(new byte[20 * 40 * 4], 20, 40));
    }

    [Fact]
    public void VisibleHeightOf_ReadsAPng_AndReturnsZeroForJunk()
    {
        Assert.Equal(70, PetSize.VisibleHeightOf(Png(128, 70)));
        Assert.Equal(0, PetSize.VisibleHeightOf([1, 2, 3]));
    }

    [Fact]
    public void StandHeight_IsTheTallestOfTheStandFrames()
    {
        var frames = new Dictionary<int, byte[]> { [1] = Png(128, 90), [2] = Png(128, 96) };
        var anims = PetAnimations.FromMapping(new PetMapping
        {
            ImagePixels = true,
            Animations = new() { ["stand"] = [new PoseDef { Frame = 1 }, new PoseDef { Frame = 2 }] },
        }, 128, 128);
        Assert.Equal(96, PetSize.StandHeight(anims, n => frames.GetValueOrDefault(n)));
        Assert.Equal(0, PetSize.StandHeight(anims, _ => null));
    }

    // ---- in the library ---------------------------------------------------------------------------

    private PetLibrary NewLibrary() => new(new Library(Path.Combine(_dir, "Library")));

    private static Dictionary<int, byte[]> ShimejiFrames_(int box) =>
        Enumerable.Range(1, ShimejiFrames.NumFrames).ToDictionary(n => n, _ => Png(128, box));

    [Fact]
    public void ImportingAPack_MeasuresItsStandingHeight()
    {
        var pets = NewLibrary();
        var m = pets.Add("Tall", ShimejiFrames_(110), 128, 128);
        Assert.Equal(110, m.StandHeight);
        Assert.Equal(110, pets.Read(m.Id)!.StandHeight);
    }

    [Fact]
    public void AnOlderPack_IsMeasuredOnceAndSaved()
    {
        var pets = NewLibrary();
        var m = pets.Add("Old", ShimejiFrames_(80), 128, 128);

        // Make it look like a pack imported before sizes were normalised.
        var path = Path.Combine(pets.DirOf(m.Id), "manifest.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"standHeight\": 80", "\"standHeight\": 0"));
        var old = pets.Read(m.Id)!;
        Assert.Equal(0, old.StandHeight);

        Assert.Equal(80, pets.EnsureStandHeight(old).StandHeight);
        Assert.Equal(80, pets.Read(m.Id)!.StandHeight);          // saved, so it is not measured again
    }

    [Fact]
    public void ManifestsWithoutTheFieldLoadAsUnknown()
    {
        var m = JsonSerializer.Deserialize<PetPackManifest>("""{"id":"pet-12345678","name":"Old","frameWidth":64,"frameHeight":64,"frames":[1]}""", Json.Options)!;
        Assert.Equal(0, m.StandHeight);
    }

    [Fact]
    public void APackWhoseFramesAreMissing_StaysUnknown_NotAnError()
    {
        var pets = NewLibrary();
        var m = pets.Add("Odd", ShimejiFrames_(60), 128, 128);
        File.Delete(pets.FramePath(m.Id, 1));
        var path = Path.Combine(pets.DirOf(m.Id), "manifest.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"standHeight\": 60", "\"standHeight\": 0"));
        Assert.Equal(0, pets.EnsureStandHeight(pets.Read(m.Id)!).StandHeight);
    }
}
