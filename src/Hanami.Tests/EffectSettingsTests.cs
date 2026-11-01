using System.Text.Json.Nodes;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class EffectSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public EffectSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private const string OldShape = """
        {
          "assignments": {
            "*":   { "wallpaperId": "builtin:night", "effect": { "preset": "snow", "density": 2, "interaction": "wind", "clickBurst": false } },
            "MON": { "wallpaperId": "builtin:dawn",  "effect": { "preset": "rain", "wind": 0.5 } }
          },
          "screensaver": { "idleMinutes": 3 }
        }
        """;

    [Fact]
    public void OldAssignmentEffect_MigratesToPresetAndTuning()
    {
        var s = SettingsStore.Load(Write(OldShape));

        Assert.Equal("snow", s.Looks["builtin:night"].Desktop.EffectPreset);
        Assert.Equal("builtin:night", s.Assignments["*"].WallpaperId);
        Assert.Equal(2.0, (double?)s.Effects["snow"]["density"]);
        Assert.Equal("wind", (string?)s.Effects["snow"]["interaction"]);
        Assert.False(s.Effects["snow"].ContainsKey("preset"));
        Assert.Equal("rain", s.Looks["builtin:dawn"].Desktop.EffectPreset);
        Assert.Equal(0.5, (double?)s.Effects["rain"]["wind"]);
        Assert.Equal(3, s.Screensaver.IdleMinutes);
        Assert.Null(s.Looks["builtin:night"].Screensaver.EffectPreset);
    }

    [Fact]
    public void Migration_NeverWritesTheLegacyEffectBack_AndKeepsExistingTuning()
    {
        var path = Write("""
            {
              "assignments": { "*": { "effect": { "preset": "snow", "density": 2 } } },
              "effects": { "snow": { "density": 0.5 } }
            }
            """);
        var store = new SettingsStore(path);
        store.Save();

        Assert.Equal(0.5, (double?)store.Current.Effects["snow"]["density"]);   // existing overrides win
        var written = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Null(written["assignments"]!["*"]!["effect"]);
        Assert.Equal("snow", (string?)written["looks"]!["builtin:twilight"]!["desktop"]!["effectPreset"]);
    }

    [Fact]
    public void UnknownPresets_FallBack()
    {
        var s = SettingsStore.Load(Write("""
            { "assignments": { "*": { "effectPreset": "lasers" } }, "effects": { "lasers": { "density": 3 } },
              "screensaver": { "effectPreset": "lasers" } }
            """));
        Assert.Equal("sakura", s.Looks["builtin:twilight"].Desktop.EffectPreset);
        Assert.Empty(s.Effects);
        Assert.Null(s.Looks["builtin:twilight"].Screensaver.EffectPreset);
    }

    [Fact]
    public void ComposeEffect_IsPresetPlusOverrides_NothingElse()
    {
        var s = new AppSettings();
        var plain = s.ComposeEffect("embers");
        Assert.Equal(["preset"], plain.Select(kv => kv.Key));
        Assert.Equal("embers", (string?)plain["preset"]);

        s.Effects["embers"] = new JsonObject { ["density"] = 1.5, ["preset"] = "rain" };
        var composed = s.ComposeEffect("embers");
        Assert.Equal("embers", (string?)composed["preset"]);
        Assert.Equal(1.5, (double?)composed["density"]);
        composed["density"] = 9;                                          // a copy: the settings stay untouched
        Assert.Equal(1.5, (double?)s.Effects["embers"]["density"]);
    }

    [Fact]
    public void Screensaver_UsesOwnPreset_OrTheBackgroundsDesktopOne()
    {
        var s = new AppSettings { SameOnAllMonitors = false };
        s.Assignments["*"].WallpaperId = "A";
        s.Assignments["MON"] = new Assignment { WallpaperId = "B" };
        s.Looks["A"] = new LookSettings { Desktop = { EffectPreset = "snow" } };
        s.Looks["B"] = new LookSettings { Desktop = { EffectPreset = "leaves" } };
        s.Effects["leaves"] = new JsonObject { ["speed"] = 2.0 };

        Assert.Equal("leaves", (string?)s.DesktopEffectFor("MON")["preset"]);
        Assert.Equal("leaves", (string?)s.ScreensaverEffectFor("MON")["preset"]);
        Assert.Equal("snow", (string?)s.ScreensaverEffectFor("OTHER")["preset"]);

        s.Looks["B"].Screensaver.EffectPreset = "embers";
        Assert.Equal("embers", (string?)s.ScreensaverEffectFor("MON")["preset"]);
        Assert.False(s.ScreensaverEffectFor("MON").ContainsKey("speed"));   // tuning belongs to the preset
        Assert.Equal("leaves", (string?)s.DesktopEffectFor("MON")["preset"]);
    }

    [Fact]
    public void SetEffectTuning_Merges_Persists_AndNullResets()
    {
        var path = Path.Combine(_dir, "config", "settings.json");
        var store = new SettingsStore(path);

        store.SetEffectTuning("embers", new JsonObject { ["density"] = 2.0 });
        store.SetEffectTuning("embers", new JsonObject { ["wind"] = 0.3 });
        var reloaded = new SettingsStore(path).Current;
        Assert.Equal(2.0, (double?)reloaded.Effects["embers"]["density"]);
        Assert.Equal(0.3, (double?)reloaded.Effects["embers"]["wind"]);

        store.SetEffectTuning("embers", null);
        Assert.False(new SettingsStore(path).Current.Effects.ContainsKey("embers"));
        Assert.Throws<ArgumentException>(() => store.SetEffectTuning("lasers", new JsonObject()));
    }

    [Fact]
    public void V1File_DropsStoredClickBurst_AndIsMigratedToCurrent()
    {
        var path = Write("""
            { "effects": { "snow": { "density": 2, "clickBurst": true }, "rain": { "clickBurst": false } } }
            """);
        var store = new SettingsStore(path);

        Assert.Equal(AppSettings.CurrentVersion, store.Current.SettingsVersion);
        Assert.False(store.Current.Effects["snow"].ContainsKey("clickBurst"));
        Assert.Equal(2.0, (double?)store.Current.Effects["snow"]["density"]);
        Assert.False(store.Current.Effects["rain"].ContainsKey("clickBurst"));

        store.Save();
        var written = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(AppSettings.CurrentVersion, (int?)written["settingsVersion"]);
        Assert.Null(written["effects"]!["snow"]!["clickBurst"]);
    }

    [Fact]
    public void V2File_KeepsClickBurst()
    {
        var s = SettingsStore.Load(Write("""
            { "settingsVersion": 2, "effects": { "snow": { "clickBurst": true } } }
            """));
        Assert.True((bool?)s.Effects["snow"]["clickBurst"]);
        Assert.Equal(AppSettings.CurrentVersion, s.SettingsVersion);
    }

    [Fact]
    public void Override_WithFlicker_SurvivesRoundTrip()
    {
        var path = Path.Combine(_dir, "config", "settings.json");
        var store = new SettingsStore(path);
        store.SetEffectTuning("fireflies", new JsonObject { ["flicker"] = 0.25, ["opacity"] = 1.6 });
        var reloaded = new SettingsStore(path).Current;
        Assert.Equal(0.25, (double?)reloaded.Effects["fireflies"]["flicker"]);
        Assert.Equal(1.6, (double?)reloaded.Effects["fireflies"]["opacity"]);
        Assert.Equal(0.25, (double?)reloaded.ComposeEffect("fireflies")["flicker"]);
    }

    [Fact]
    public void SetLook_CanSetAndClearScreensaverEffect()
    {
        var store = new SettingsStore(Path.Combine(_dir, "config", "settings.json"));
        store.SetLook("builtin:twilight", JsonNode.Parse("""{"screensaver":{"effectPreset":"embers"}}""")!.AsObject());
        Assert.Equal("embers", store.Current.Looks["builtin:twilight"].Screensaver.EffectPreset);
        store.SetLook("builtin:twilight", JsonNode.Parse("""{"screensaver":{"effectPreset":null}}""")!.AsObject());
        Assert.Null(store.Current.Looks["builtin:twilight"].Screensaver.EffectPreset);
    }
}
