using System.IO.Compression;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class DropClassifierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public DropClassifierTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string File_(string name, byte[]? data = null)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data ?? [1, 2, 3]);
        return path;
    }

    private string Zip(string name, params string[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var e in entries) zip.CreateEntry(e).Open().Dispose();
        return path;
    }

    [Theory]
    [InlineData("a.jpg")]
    [InlineData("a.JPEG")]
    [InlineData("a.png")]
    [InlineData("a.webp")]
    [InlineData("a.gif")]
    public void Photos(string name) => Assert.Equal(DropKind.Photo, DropClassifier.Classify(File_(name)).Kind);

    [Theory]
    [InlineData("a.mp4")]
    [InlineData("a.WEBM")]
    [InlineData("a.mov")]
    public void Videos(string name) => Assert.Equal(DropKind.Video, DropClassifier.Classify(File_(name)).Kind);

    [Fact]
    public void AZipWithAPetdexPetOrShimejiImages_IsAPetPack()
    {
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(Zip("petdex.zip", "pet.json", "spritesheet.webp")).Kind);
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(Zip("nested.zip", "Nezuko/pet.json", "Nezuko/spritesheet.webp")).Kind);
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(Zip("shime.zip", "img/Mia/shime1.png", "img/Mia/shime2.png")).Kind);
    }

    [Fact]
    public void AZipOfPhotosOrJunk_IsRejectedWithAReason()
    {
        var item = DropClassifier.Classify(Zip("photos.zip", "a.jpg", "b.jpg", "shime47.png", "readme.txt"));
        Assert.Equal(DropKind.Rejected, item.Kind);
        Assert.Contains("no Petdex pet", item.Reason);
    }

    [Fact]
    public void ABrokenZip_IsRejected_NotAnException()
    {
        var item = DropClassifier.Classify(File_("broken.zip", [0, 1, 2, 3]));
        Assert.Equal(DropKind.Rejected, item.Kind);
        Assert.NotNull(item.Reason);
    }

    [Fact]
    public void Folders()
    {
        File_("petdex/pet.json");
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(Path.Combine(_dir, "petdex")).Kind);

        File_("shimeji/img/shime1.png");
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(Path.Combine(_dir, "shimeji")).Kind);

        File_("several/a/pet.json");
        File_("several/b/pet.json");
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(Path.Combine(_dir, "several")).Kind);

        File_("photos/a.jpg");
        var item = DropClassifier.Classify(Path.Combine(_dir, "photos"));
        Assert.Equal(DropKind.Rejected, item.Kind);
        Assert.Contains("folder", item.Reason);
    }

    [Fact]
    public void AnythingElse_AndMissingFiles_AreRejected()
    {
        Assert.Contains(".exe", DropClassifier.Classify(File_("tool.exe")).Reason);
        Assert.Contains("isn't a photo", DropClassifier.Classify(File_("README")).Reason);
        Assert.Contains("found", DropClassifier.Classify(Path.Combine(_dir, "gone.png")).Reason);
    }

    [Fact]
    public void ManyAtOnce_KeepTheirOrderAndNames()
    {
        var items = DropClassifier.Classify([File_("one.png"), File_("two.mp4"), File_("three.txt")]);
        Assert.Equal([DropKind.Photo, DropKind.Video, DropKind.Rejected], items.Select(i => i.Kind));
        Assert.Equal(["one.png", "two.mp4", "three.txt"], items.Select(i => i.Name));
    }
}
