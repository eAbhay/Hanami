using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hanami.Core;
using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

public sealed class PetPackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Library _library;
    private readonly PetLibrary _pets;

    public PetPackTests()
    {
        Directory.CreateDirectory(_dir);
        _library = new Library(Path.Combine(_dir, "Library"));
        _pets = new PetLibrary(_library);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static byte[] Png(int size = 32)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static readonly int[] AllRequired = ShimejiFrames.Required;

    /// <summary>Writes shime&lt;N&gt;.png files into a folder. Frames default to the required ones plus climb, ceiling, sit and sleep.</summary>
    private static string MakeSet(string folder, IEnumerable<int>? frames = null, int size = 32)
    {
        Directory.CreateDirectory(folder);
        foreach (var n in frames ?? Enumerable.Range(1, ShimejiFrames.NumFrames))
            File.WriteAllBytes(Path.Combine(folder, $"shime{n}.png"), Png(size));
        return folder;
    }

    private string Zip(string name, params (string path, byte[] data)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, data) in entries)
        {
            using var s = zip.CreateEntry(p).Open();
            s.Write(data);
        }
        return path;
    }

    [Theory]
    [InlineData("flat")]               // the chosen folder holds the images
    [InlineData("flat/img")]           // Name/img/…
    [InlineData("shimeji-ee/img/Yuki")] // shimeji-ee's own layout, the root chosen is shimeji-ee
    public void FolderLayouts_AreFound(string layout)
    {
        var root = Path.Combine(_dir, "src", layout.Split('/')[0]);
        MakeSet(Path.Combine(_dir, "src", layout));
        var result = PetPackReader.ImportFolder(_pets, root);
        Assert.Empty(result.Rejected);
        var pack = Assert.Single(result.Imported);
        Assert.Equal(ShimejiFrames.NumFrames, pack.Frames.Count);
        Assert.Equal((32, 32), (pack.FrameWidth, pack.FrameHeight));
    }

    [Fact]
    public void NamesComeFromTheFolder_NotFromImg()
    {
        MakeSet(Path.Combine(_dir, "src", "Yuki", "img"));
        var result = PetPackReader.ImportFolder(_pets, Path.Combine(_dir, "src"));
        Assert.Equal("Yuki", Assert.Single(result.Imported).Name);
    }

    [Fact]
    public void SeveralCharactersInOneFolder_BecomeSeparatePacks()
    {
        MakeSet(Path.Combine(_dir, "src", "A"), AllRequired);
        MakeSet(Path.Combine(_dir, "src", "B"), AllRequired);
        var result = PetPackReader.ImportFolder(_pets, Path.Combine(_dir, "src"));
        Assert.Equal(["A", "B"], result.Imported.Select(m => m.Name).Order());
    }

    [Fact]
    public void ZipWithTwoCharacters_ImportsEach_NamedAfterItsFolder()
    {
        var entries = AllRequired.SelectMany(n => new[] { ($"Pack/img/Mia/shime{n}.png", Png()), ($"Pack/img/Rin/shime{n}.png", Png()) }).ToArray();
        var result = PetPackReader.ImportZip(_pets, Zip("two.zip", entries));
        Assert.Empty(result.Rejected);
        Assert.Equal(["Mia", "Rin"], result.Imported.Select(m => m.Name).Order());
        Assert.Equal(2, _pets.List().Count);
    }

    [Fact]
    public void MissingRequiredFrame_IsRejectedWithAReadableReason()
    {
        MakeSet(Path.Combine(_dir, "src", "Broken"), AllRequired.Where(n => n != 4));
        var result = PetPackReader.ImportFolder(_pets, Path.Combine(_dir, "src", "Broken"));
        Assert.Empty(result.Imported);
        var reason = Assert.Single(result.Rejected);
        Assert.Contains("shime4", reason);
        Assert.Contains("Broken", reason);
        Assert.Empty(_pets.List());
    }

    [Fact]
    public void FolderWithoutImages_SaysSo()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));
        File.WriteAllText(Path.Combine(_dir, "empty", "readme.txt"), "hi");
        var result = PetPackReader.ImportFolder(_pets, Path.Combine(_dir, "empty"));
        Assert.Empty(result.Imported);
        Assert.Contains("No Shimeji images", Assert.Single(result.Rejected));
    }

    [Fact]
    public void FakePng_IsRejected()
    {
        var folder = MakeSet(Path.Combine(_dir, "src", "Fake"), AllRequired);
        File.WriteAllText(Path.Combine(folder, "shime3.png"), "this is not an image");
        var result = PetPackReader.ImportFolder(_pets, folder);
        Assert.Empty(result.Imported);
        Assert.Contains("shime3.png isn't a valid PNG", Assert.Single(result.Rejected));
    }

    [Fact]
    public void MixedFrameSizes_AreRejected()
    {
        var folder = MakeSet(Path.Combine(_dir, "src", "Mixed"), AllRequired);
        File.WriteAllBytes(Path.Combine(folder, "shime2.png"), Png(48));
        var result = PetPackReader.ImportFolder(_pets, folder);
        Assert.Empty(result.Imported);
        Assert.Contains("same size", Assert.Single(result.Rejected));
    }

    [Fact]
    public void OversizePng_IsRejected()
    {
        var folder = MakeSet(Path.Combine(_dir, "src", "Huge"), AllRequired);
        File.WriteAllBytes(Path.Combine(folder, "shime5.png"), new byte[PetPackReader.MaxPngBytes + 1]);
        var result = PetPackReader.ImportFolder(_pets, folder);
        Assert.Empty(result.Imported);
        Assert.Contains("shime5.png is larger", Assert.Single(result.Rejected));
    }

    [Fact]
    public void NamesOutsideShime1To46_AreIgnored()
    {
        var folder = MakeSet(Path.Combine(_dir, "src", "Extra"), AllRequired);
        File.WriteAllBytes(Path.Combine(folder, "shime47.png"), Png());
        File.WriteAllBytes(Path.Combine(folder, "shime0.png"), Png());
        File.WriteAllBytes(Path.Combine(folder, "other.png"), Png());
        var pack = Assert.Single(PetPackReader.ImportFolder(_pets, folder).Imported);
        Assert.Equal(AllRequired.Order(), pack.Frames);
    }

    [Fact]
    public void ZipSlipEntries_AreNeverWritten_AndNeverUsedForPaths()
    {
        var outside = Path.Combine(_dir, "evil");
        var entries = AllRequired.Select(n => ($"Pack/shime{n}.png", Png())).ToList();
        entries.Add(("../../evil/shime1.png", Png()));
        entries.Add(("..\\..\\evil\\shime2.png", Png()));
        entries.Add(("/abs/shime3.png", Png()));
        var result = PetPackReader.ImportZip(_pets, Zip("slip.zip", entries.ToArray()));
        Assert.Single(result.Imported);
        Assert.False(Directory.Exists(outside));
        Assert.All(Directory.EnumerateFiles(_pets.Dir, "*", SearchOption.AllDirectories), f => Assert.StartsWith(_pets.Dir, f));
    }

    [Fact]
    public void NotAZip_SaysSo()
    {
        var path = Path.Combine(_dir, "bad.zip");
        File.WriteAllText(path, "nope");
        var result = PetPackReader.ImportZip(_pets, path);
        Assert.Contains("isn't a readable zip", Assert.Single(result.Rejected));
    }

    [Fact]
    public void Manifest_RoundTrips_AndCapabilitiesFollowTheFrames()
    {
        var full = Assert.Single(PetPackReader.ImportFolder(_pets, MakeSet(Path.Combine(_dir, "src", "Full"))).Imported);
        var basic = Assert.Single(PetPackReader.ImportFolder(_pets, MakeSet(Path.Combine(_dir, "src", "Basic"), AllRequired)).Imported);

        var info = _pets.List().ToDictionary(p => p.Name);
        Assert.Equal(new PetCapabilities(true, true, true, true), info["Full"].Capabilities);
        Assert.Equal(new PetCapabilities(false, false, false, false), info["Basic"].Capabilities);
        Assert.Equal($"https://library.hanami/pets/{full.Id}/img/shime1.png", info["Full"].Preview);
        Assert.True(info["Full"].SizeBytes > info["Basic"].SizeBytes);

        var read = _pets.Read(basic.Id)!;
        Assert.Equal(basic.Name, read.Name);
        Assert.Equal(AllRequired.Order(), read.Frames);
        Assert.True(File.Exists(_pets.FramePath(basic.Id, 1)));
    }

    [Fact]
    public void Rename_And_Delete()
    {
        var pack = Assert.Single(PetPackReader.ImportFolder(_pets, MakeSet(Path.Combine(_dir, "src", "Old"), AllRequired)).Imported);
        _pets.Rename(pack.Id, "  New name ");
        Assert.Equal("New name", _pets.Read(pack.Id)!.Name);
        _pets.Rename(pack.Id, "   ");
        Assert.Equal("New name", _pets.Read(pack.Id)!.Name);
        _pets.Delete(pack.Id);
        Assert.False(_pets.Exists(pack.Id));
    }

    [Theory]
    [InlineData("pet-1234abcd", true)]
    [InlineData("../pet-1", false)]
    [InlineData("pet-a/b", false)]
    [InlineData("other", false)]
    [InlineData("", false)]
    public void PackIds_AreValidatedBeforeTouchingTheDisk(string id, bool valid)
    {
        Assert.Equal(valid, PetLibrary.IsValidId(id));
        if (!valid) Assert.Throws<ArgumentException>(() => _pets.DirOf(id));
    }

    // ---- settings --------------------------------------------------------------------------------

    private string SettingsPath => Path.Combine(_dir, "config", "settings.json");

    [Fact]
    public void Settings_ClampScale_CapItems_DropBadPacks_FixDuplicateIds()
    {
        var store = new SettingsStore(SettingsPath);
        var items = new JsonArray();
        for (var i = 0; i < 12; i++) items.Add(new JsonObject { ["id"] = "same", ["packId"] = "pet-0000000" + (i % 10) });
        items.Add(new JsonObject { ["id"] = "x", ["packId"] = "../evil" });
        store.Patch(new JsonObject { ["pets"] = new JsonObject { ["scale"] = 9, ["activity"] = "frantic", ["items"] = items } });

        var pets = store.Current.Pets;
        Assert.Equal(2, pets.Scale);
        Assert.Equal("normal", pets.Activity);
        Assert.Equal(PetSettings.MaxPets, pets.Items.Count);
        Assert.Equal(PetSettings.MaxPets, pets.Items.Select(i => i.Id).Distinct().Count());
        Assert.DoesNotContain(pets.Items, i => i.PackId.Contains(".."));

        store.Patch(new JsonObject { ["pets"] = new JsonObject { ["scale"] = 0.1 } });
        Assert.Equal(0.5, store.Current.Pets.Scale);
    }

    [Fact]
    public void AddPet_AllowsTheSamePackTwice_StopsAtTheCap_AndRemoveWorks()
    {
        var store = new SettingsStore(SettingsPath);
        var a = store.AddPet("pet-aaaa1111");
        var b = store.AddPet("pet-aaaa1111");
        Assert.NotEqual(a.Id, b.Id);
        for (var i = 2; i < PetSettings.MaxPets; i++) store.AddPet("pet-aaaa1111");
        Assert.Throws<InvalidOperationException>(() => store.AddPet("pet-aaaa1111"));
        Assert.Throws<ArgumentException>(() => store.AddPet("nope"));

        store.RemovePet(a.Id);
        Assert.Equal(PetSettings.MaxPets - 1, new SettingsStore(SettingsPath).Current.Pets.Items.Count);
        store.RemovePack("pet-aaaa1111");
        Assert.Empty(store.Current.Pets.Items);
    }

    [Fact]
    public void PrunePets_DropsItemsWhosePackIsGone()
    {
        var store = new SettingsStore(SettingsPath);
        store.AddPet("pet-aaaa1111");
        store.AddPet("pet-bbbb2222");
        store.PrunePets(id => id == "pet-bbbb2222");
        Assert.Equal(["pet-bbbb2222"], store.Current.Pets.Items.Select(i => i.PackId));
    }

    // ---- .hanamipack -----------------------------------------------------------------------------

    [Fact]
    public async Task Pack_RoundTrip_CarriesPetPacksAndTheirSettings()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var sourcePets = new PetLibrary(source);
        var pack = Assert.Single(PetPackReader.ImportFolder(sourcePets, MakeSet(Path.Combine(_dir, "src", "Mia"))).Imported);
        var settings = new AppSettings { SettingsVersion = AppSettings.CurrentVersion };
        settings.Pets.Items.Add(new PetItem { Id = "p1", PackId = pack.Id });
        settings.Pets.Scale = 1.5;

        Assert.Contains(PackService.Items(source), i => i.Kind == "pet" && i.Id == pack.Id && i.Name == "Mia");

        var file = Path.Combine(_dir, "out" + PackService.Extension);
        await PackService.ExportAsync(source, settings, [pack.Id, "settings"], file, "test", null, null, TestContext.Current.CancellationToken);

        var target = new Library(Path.Combine(_dir, "B"));
        var (result, imported) = await PackService.ImportAsync(target, file, null, TestContext.Current.CancellationToken);
        var targetPets = new PetLibrary(target);

        Assert.Equal(1, result.Pets);
        Assert.True(targetPets.Exists(pack.Id));
        Assert.Equal("Mia", targetPets.Read(pack.Id)!.Name);
        Assert.True(File.Exists(targetPets.FramePath(pack.Id, 46)));
        Assert.Equal(1.5, imported!.Pets.Scale);
        Assert.Equal(pack.Id, Assert.Single(imported.Pets.Items).PackId);

        // Importing again keeps what is there.
        var (again, _) = await PackService.ImportAsync(target, file, null, TestContext.Current.CancellationToken);
        Assert.Equal(0, again.Pets);
    }

    [Fact]
    public async Task Pack_WithABrokenPet_IsSkippedNotImported()
    {
        var file = Path.Combine(_dir, "evil" + PackService.Extension);
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("pack.json").Open())) w.Write("""{"format":1}""");
            using (var w = new StreamWriter(zip.CreateEntry("pets/pet-deadbeef/manifest.json").Open()))
                w.Write("""{"id":"pet-deadbeef","name":"x","frameWidth":32,"frameHeight":32,"frames":[1]}""");
            using var s = zip.CreateEntry("pets/pet-deadbeef/img/shime1.png").Open();
            s.Write("not a png"u8);
        }
        var target = new Library(Path.Combine(_dir, "B"));
        var (result, _) = await PackService.ImportAsync(target, file, null, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.Pets);
        Assert.False(new PetLibrary(target).Exists("pet-deadbeef"));
        Assert.False(Directory.Exists(new PetLibrary(target).DirOf("pet-deadbeef") + ".importing"));
    }
}
