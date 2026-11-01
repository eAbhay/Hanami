using System.Text.Json.Nodes;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class LooksTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public LooksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_dir, "config", "settings.json");

    private SettingsStore NewStore() => new(SettingsPath);

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void V2File_MigratesWithoutChangingWhatIsShown()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            {
              "settingsVersion": 2,
              "sameOnAllMonitors": false,
              "assignments": {
                "*":   { "wallpaperId": "A", "effectPreset": "snow" },
                "MON": { "wallpaperId": "B", "effectPreset": "embers" }
              },
              "effects": { "snow": { "density": 2 }, "embers": { "opacity": 1.5 } },
              "screensaver": { "wallpaperId": "B", "effectPreset": "rain",
                               "clock": { "x": 0.2, "y": 0.7, "size": 0.8, "showDate": false } }
            }
            """);
        var before = JsonNode.Parse("""{"snow":2}""");   // keeps the fixture honest: desktop A shows snow at 2x
        Assert.NotNull(before);

        var s = SettingsStore.Load(SettingsPath);

        Assert.Equal(AppSettings.CurrentVersion, s.SettingsVersion);
        Assert.Equal("snow", (string?)s.DesktopEffectFor("OTHER")["preset"]);
        Assert.Equal(2.0, (double?)s.DesktopEffectFor("OTHER")["density"]);
        Assert.Equal("embers", (string?)s.DesktopEffectFor("MON")["preset"]);
        Assert.Equal(1.5, (double?)s.DesktopEffectFor("MON")["opacity"]);
        // The screensaver background is B: its effect and clock came from the old screensaver settings.
        Assert.Equal("rain", (string?)s.ScreensaverEffectFor("MON")["preset"]);
        var clock = s.ScreensaverClockFor("MON");
        Assert.Equal((0.2, 0.7, 0.8, false), (clock.X, clock.Y, clock.Size, clock.ShowDate));
        // A, not used as background, keeps the default clock.
        Assert.Equal(ClockSettings.DefaultX, s.LookFor("A").Screensaver.Clock.X);
    }

    [Fact]
    public void V2File_WithoutScreensaverBackground_GivesEveryDesktopWallpaperTheClock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "settingsVersion": 2, "assignments": { "*": { "wallpaperId": "A", "effectPreset": "leaves" } },
              "screensaver": { "effectPreset": "sparkles", "clock": { "x": 0.1 } } }
            """);
        var s = SettingsStore.Load(SettingsPath);
        Assert.Equal(0.1, s.ScreensaverClockFor("*").X);
        Assert.Equal("sparkles", (string?)s.ScreensaverEffectFor("*")["preset"]);
        Assert.Equal("leaves", (string?)s.DesktopEffectFor("*")["preset"]);
    }

    [Fact]
    public void LegacyFields_AreNeverWrittenBack()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """
            { "settingsVersion": 2, "assignments": { "*": { "effectPreset": "snow" } },
              "screensaver": { "effectPreset": "rain", "clock": { "x": 0.1 } } }
            """);
        var store = NewStore();
        store.Save();
        var written = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Null(written["assignments"]!["*"]!["effectPreset"]);
        Assert.Null(written["screensaver"]!["effectPreset"]);
        Assert.Null(written["screensaver"]!["clock"]);
    }

    [Fact]
    public void SelectingANewWallpaper_CopiesTheLookOfTheOneItReplaces()
    {
        var store = NewStore();
        store.SetAssignment("*", "A");                                   // A starts from the default look
        store.SetLook("A", Obj("""{"desktop":{"effectPreset":"embers"},"screensaver":{"clock":{"x":0.2}}}"""));
        store.SetEffectTuning("embers", Obj("""{"density":2}"""), "A");

        store.SetAssignment("*", "B");
        var b = store.Current.Looks["B"];
        Assert.Equal("embers", b.Desktop.EffectPreset);
        Assert.Equal(2.0, (double?)b.Desktop.Tuning["embers"]["density"]);
        Assert.Equal(0.2, b.Screensaver.Clock.X);

        // From then on B has its own: changing it leaves A alone, and re-selecting A keeps A's.
        store.SetLook("B", Obj("""{"desktop":{"effectPreset":"rain"}}"""));
        store.SetAssignment("*", "A");
        Assert.Equal("embers", store.Current.Looks["A"].Desktop.EffectPreset);
        Assert.Equal("rain", store.Current.Looks["B"].Desktop.EffectPreset);
    }

    [Fact]
    public void FirstSelectionOnAMonitor_StartsFromTheSharedAssignment()
    {
        var store = NewStore();
        store.SetAssignment("MON", "C");
        Assert.Equal("C", store.Current.Assignments["MON"].WallpaperId);
        Assert.Equal("builtin:twilight", store.Current.Assignments["*"].WallpaperId);
    }

    [Fact]
    public void ScreensaverBackground_WithoutALook_GetsThePreviousBackgroundsScreensaverLook()
    {
        var store = NewStore();
        store.SetLook("builtin:twilight", Obj("""{"desktop":{"effectPreset":"snow"},"screensaver":{"effectPreset":"rain","clock":{"y":0.8}}}"""));
        store.Patch(Obj("""{"screensaver":{"wallpaperId":"Z"}}"""));
        store.EnsureScreensaverLook("builtin:twilight");

        var z = store.Current.Looks["Z"];
        Assert.Equal("rain", z.Screensaver.EffectPreset);
        Assert.Equal(0.8, z.Screensaver.Clock.Y);
        Assert.Equal("sakura", z.Desktop.EffectPreset);           // the desktop part is not copied
    }

    [Fact]
    public void SameAsDesktop_SharesTuning_OwnScreensaverEffectHasItsOwn()
    {
        var store = NewStore();
        store.SetAssignment("*", "A");
        store.SetLook("A", Obj("""{"desktop":{"effectPreset":"embers"}}"""));

        // Screensaver effect unset: editing "the screensaver's" tuning lands in the desktop's.
        store.SetEffectTuning("embers", Obj("""{"density":2}"""), "A", "screensaver");
        Assert.Equal(2.0, (double?)store.Current.Looks["A"].Desktop.Tuning["embers"]["density"]);
        Assert.Equal(2.0, (double?)store.Current.ScreensaverEffectFor("*")["density"]);

        store.SetLook("A", Obj("""{"screensaver":{"effectPreset":"rain"}}"""));
        store.SetEffectTuning("rain", Obj("""{"speed":2.5}"""), "A", "screensaver");
        Assert.Equal(2.5, (double?)store.Current.ScreensaverEffectFor("*")["speed"]);
        Assert.False(store.Current.DesktopEffectFor("*").ContainsKey("speed"));
        Assert.Equal(2.0, (double?)store.Current.DesktopEffectFor("*")["density"]);
    }

    [Fact]
    public void Tuning_LayersDefaultsThenEffectDefaultsThenTheLook()
    {
        var s = new AppSettings();
        s.Effects["snow"] = new JsonObject { ["density"] = 1.5, ["speed"] = 2.0 };
        s.Looks["A"] = new LookSettings { Desktop = { EffectPreset = "snow", Tuning = { ["snow"] = new JsonObject { ["density"] = 0.5 } } } };
        s.Assignments["*"].WallpaperId = "A";

        var effect = s.DesktopEffectFor("*");
        Assert.Equal(0.5, (double?)effect["density"]);      // the look wins
        Assert.Equal(2.0, (double?)effect["speed"]);        // effect defaults fill the rest
        Assert.False(effect.ContainsKey("size"));            // preset defaults stay on the page
    }

    [Fact]
    public void Tuning_IsKeptPerPresetInsideALook()
    {
        var store = NewStore();
        store.SetAssignment("*", "A");
        store.SetLook("A", Obj("""{"desktop":{"effectPreset":"sakura"}}"""));
        store.SetEffectTuning("sakura", Obj("""{"density":2}"""), "A");
        store.SetLook("A", Obj("""{"desktop":{"effectPreset":"snow"}}"""));
        store.SetEffectTuning("snow", Obj("""{"density":0.5}"""), "A");
        store.SetLook("A", Obj("""{"desktop":{"effectPreset":"sakura"}}"""));

        Assert.Equal(2.0, (double?)store.Current.DesktopEffectFor("*")["density"]);
    }

    [Fact]
    public void NullTuning_ResetsOnlyThatLevel()
    {
        var store = NewStore();
        store.SetEffectTuning("snow", Obj("""{"density":2}"""));                    // effect defaults
        store.SetEffectTuning("snow", Obj("""{"density":0.5}"""), "A");              // A's own
        store.SetEffectTuning("snow", null, "A");
        Assert.False(store.Current.Looks["A"].Desktop.Tuning.ContainsKey("snow"));
        Assert.Equal(2.0, (double?)store.Current.Effects["snow"]["density"]);
    }

    [Fact]
    public void Looks_SurviveReload_AndBadValuesAreRepaired()
    {
        var store = NewStore();
        store.SetLook("A", Obj("""{"desktop":{"effectPreset":"lasers"},"screensaver":{"clock":{"size":9,"x":-1}}}"""));
        var reloaded = new SettingsStore(SettingsPath).Current;
        Assert.Equal("sakura", reloaded.Looks["A"].Desktop.EffectPreset);
        Assert.Equal(1.6, reloaded.Looks["A"].Screensaver.Clock.Size);
        Assert.Equal(0, reloaded.Looks["A"].Screensaver.Clock.X);
    }

    [Fact]
    public void RemovingAndPruning_DropLooksOfMissingWallpapers()
    {
        var store = NewStore();
        store.SetLook("gone", Obj("""{"desktop":{"effectPreset":"snow"}}"""));
        store.SetLook("kept", Obj("""{"desktop":{"effectPreset":"snow"}}"""));
        store.SetLook("builtin:night", Obj("""{"desktop":{"effectPreset":"snow"}}"""));
        store.PruneLooks(id => id == "kept");
        Assert.Equal(["builtin:night", "builtin:twilight", "kept"], store.Current.Looks.Keys.Order());

        store.RemoveLook("kept");
        Assert.False(store.Current.Looks.ContainsKey("kept"));
    }

    [Fact]
    public async Task Pack_CarriesAWallpapersLook_EvenWithoutSettings()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var file = Path.Combine(_dir, "w.mp4");
        File.WriteAllBytes(file, new byte[2000]);
        var info = await source.ImportAsync(file, null);
        var looks = new Dictionary<string, LookSettings>
        {
            [info.Id] = new LookSettings { Desktop = { EffectPreset = "embers" }, Screensaver = { Clock = { X = 0.3 } } },
            ["other"] = new LookSettings(),
        };

        var pack = Path.Combine(_dir, "p" + PackService.Extension);
        await PackService.ExportAsync(source, null, [info.Id], pack, "test", null, looks, TestContext.Current.CancellationToken);

        var (result, settings) = await PackService.ImportAsync(new Library(Path.Combine(_dir, "B")), pack, null, TestContext.Current.CancellationToken);
        Assert.Null(settings);
        Assert.Equal([info.Id], result.Looks.Keys);
        Assert.Equal("embers", result.Looks[info.Id].Desktop.EffectPreset);
        Assert.Equal(0.3, result.Looks[info.Id].Screensaver.Clock.X);
    }
}
