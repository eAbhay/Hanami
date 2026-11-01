using System.Text.Json.Nodes;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakeFile(string name, int bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        return path;
    }

    [Fact]
    public void Settings_Patch_DeepMerges_AndPersists()
    {
        var path = Path.Combine(_dir, "config", "settings.json");
        var store = new SettingsStore(path);
        store.Patch(JsonNode.Parse("""{"power":{"onBattery":"full"},"assignments":{"*":{"effect":{"preset":"snow"}}}}""")!.AsObject());

        var reloaded = new SettingsStore(path).Current;
        Assert.Equal(Mode.Full, reloaded.Power.OnBattery);
        Assert.Equal(Mode.Paused, reloaded.Power.LowBattery);              // untouched sibling survives
        Assert.Equal("snow", (string?)reloaded.Assignments["*"].Effect["preset"]);
        Assert.Equal(160, (int?)reloaded.Assignments["*"].Effect["interactionRadius"]);   // defaults kept
        Assert.Equal("builtin:twilight", reloaded.Assignments["*"].WallpaperId);
    }

    [Fact]
    public void Settings_BrokenFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var store = new SettingsStore(path);
        Assert.True(store.Current.SameOnAllMonitors);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void Settings_PerMonitorOverride_OnlyWhenNotSameOnAll()
    {
        var s = new AppSettings();
        s.Assignments["MON#1"] = new Assignment { WallpaperId = "builtin:night" };
        Assert.Equal("builtin:twilight", s.AssignmentFor("MON#1").WallpaperId);
        s.SameOnAllMonitors = false;
        Assert.Equal("builtin:night", s.AssignmentFor("MON#1").WallpaperId);
        Assert.Equal("builtin:twilight", s.AssignmentFor("OTHER").WallpaperId);
    }

    [Fact]
    public async Task Library_Import_CopiesFile_SoOriginalCanBeDeleted()
    {
        var lib = new Library(Path.Combine(_dir, "Library"));
        var src = MakeFile("My Clip.mp4", 4096);
        var info = await lib.ImportAsync(src, null);
        File.Delete(src);

        Assert.Equal("My Clip", info.Name);
        Assert.Equal("video", info.Type);
        Assert.Equal(4096 + new FileInfo(Path.Combine(lib.DirOf(info.Id), "manifest.json")).Length, info.SizeBytes);
        var r = lib.Resolve(info.Id);
        Assert.NotNull(r);
        Assert.Equal($"https://library.hanami/wallpapers/{info.Id}/media.mp4", r!.Src);
    }

    [Fact]
    public async Task Library_RejectsUnsupportedTypes_AndBuiltinDelete()
    {
        var lib = new Library(Path.Combine(_dir, "Library"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lib.ImportAsync(MakeFile("notes.txt", 10), null));
        Assert.Throws<InvalidOperationException>(() => lib.Delete("builtin:twilight"));
        Assert.Throws<ArgumentException>(() => lib.DirOf("..\\escape"));
    }

    [Fact]
    public async Task Library_ListsBuiltinsFirst_ThenImports()
    {
        var lib = new Library(Path.Combine(_dir, "Library"));
        await lib.ImportAsync(MakeFile("a.png", 100), null);
        var list = lib.List();
        Assert.Equal(["builtin:twilight", "builtin:dawn", "builtin:night"], list.Take(3).Select(w => w.Id));
        Assert.Equal("image", list[3].Type);
        Assert.Null(list[3].Thumbnail);
    }

    [Fact]
    public async Task Pack_RoundTrip_SelectedItemsOnly()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var keep = await source.ImportAsync(MakeFile("keep.mp4", 3000), null);
        var skip = await source.ImportAsync(MakeFile("skip.webm", 2000), null);
        source.SaveThumbnail(keep.Id, "data:image/jpeg;base64," + Convert.ToBase64String([1, 2, 3]), 1920, 1080, 12.5);

        var settings = new AppSettings();
        settings.Assignments["*"].WallpaperId = keep.Id;
        settings.Power.OnBattery = Mode.Reduced;

        var pack = Path.Combine(_dir, "out" + PackService.Extension);
        await PackService.ExportAsync(source, settings, [keep.Id, "settings"], pack, "test", null);

        var target = new Library(Path.Combine(_dir, "B"));
        var (result, imported) = await PackService.ImportAsync(target, pack, null);

        Assert.Equal(1, result.Wallpapers);
        Assert.True(target.Exists(keep.Id));
        Assert.False(target.Exists(skip.Id));
        Assert.NotNull(target.List().Single(w => w.Id == keep.Id).Thumbnail);
        Assert.Equal(1920, target.List().Single(w => w.Id == keep.Id).Width);
        Assert.Equal(Mode.Reduced, imported!.Power.OnBattery);
        Assert.Equal(keep.Id, imported.Assignments["*"].WallpaperId);

        // Importing again keeps what is there instead of duplicating it.
        var (again, _) = await PackService.ImportAsync(target, pack, null);
        Assert.Equal(0, again.Wallpapers);
        Assert.Equal(1, again.Skipped);
    }
}
