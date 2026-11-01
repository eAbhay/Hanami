using System.Text.Json.Nodes;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class ClockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public ClockTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private const string Id = "builtin:twilight";   // the default desktop wallpaper, whose look the clock tests edit

    private string SettingsPath => Path.Combine(_dir, "config", "settings.json");

    private string MakeFont(string name, int bytes = 64)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Defaults_AreTodaysSpot()
    {
        var c = SettingsStore.Load(SettingsPath).ScreensaverClockFor("*");
        Assert.Equal(0.5, c.X);
        Assert.Equal(0.31, c.Y);
        Assert.Equal(1, c.Size);
        Assert.Null(c.Font);
        Assert.Equal("system", c.DateFont);
        Assert.True(c.ShowDate);
    }

    [Fact]
    public void Normalize_ClampsPositionAndSize_AndDropsBadValues()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetLook(Id, JsonNode.Parse("""
            {"screensaver":{"clock":{"x":-3,"y":7,"size":9,"dateFont":"comic","font":"file:///c:/evil.ttf"}}}
            """)!.AsObject());
        var c = store.Current.ScreensaverClockFor("*");
        Assert.Equal(0, c.X);
        Assert.Equal(1, c.Y);
        Assert.Equal(1.6, c.Size);
        Assert.Equal("system", c.DateFont);
        Assert.Null(c.Font);

        store.SetLook(Id, JsonNode.Parse("""{"screensaver":{"clock":{"size":0.1}}}""")!.AsObject());
        Assert.Equal(0.5, store.Current.ScreensaverClockFor("*").Size);
    }

    [Fact]
    public void Clock_SurvivesSaveAndReload()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetLook(Id, JsonNode.Parse("""
            {"screensaver":{"clock":{"x":0.2,"y":0.8,"size":0.7,"font":"system:Segoe Script","dateFont":"same","showDate":false}}}
            """)!.AsObject());
        var c = new SettingsStore(SettingsPath).Current.ScreensaverClockFor("*");
        Assert.Equal((0.2, 0.8, 0.7), (c.X, c.Y, c.Size));
        Assert.Equal("system:Segoe Script", c.Font);
        Assert.Equal("same", c.DateFont);
        Assert.False(c.ShowDate);
    }

    [Theory]
    [InlineData("..\\x.ttf")]
    [InlineData("a/b.otf")]
    [InlineData("a\\b.otf")]
    [InlineData("evil.exe")]
    [InlineData("noext")]
    [InlineData(".ttf")]
    [InlineData("..")]
    [InlineData("")]
    public void FontIds_RejectPathsAndOtherExtensions(string id)
    {
        Assert.False(FontLibrary.IsValidId(id));
        Assert.False(FontLibrary.IsValidSetting("library:" + id));
    }

    [Theory]
    [InlineData("My Font.ttf")]
    [InlineData("Lobster-Regular.OTF")]
    [InlineData("a.woff2")]
    public void FontIds_AcceptPlainNames(string id) => Assert.True(FontLibrary.IsValidId(id));

    [Fact]
    public void Import_SanitizesNames_Deduplicates_AndRejectsOtherTypes()
    {
        var fonts = new FontLibrary(new Library(Path.Combine(_dir, "Library")));
        var src = MakeFont("Cool Font!.ttf");
        var first = fonts.Import(src);
        var second = fonts.Import(src);

        Assert.Equal("Cool Font_.ttf", first.Id);
        Assert.Equal("Cool Font_-2.ttf", second.Id);
        Assert.Equal("Cool Font_", first.Name);
        Assert.Equal("https://library.hanami/fonts/Cool%20Font_.ttf", first.Src);
        Assert.Equal(2, fonts.List().Count);
        Assert.Throws<InvalidOperationException>(() => fonts.Import(MakeFont("tool.exe")));
        Assert.Throws<ArgumentException>(() => fonts.PathOf("..\\x.ttf"));
    }

    [Fact]
    public void ClockMessage_ResolvesFonts_AndFallsBackWhenMissing()
    {
        var lib = new Library(Path.Combine(_dir, "Library"));
        var fonts = new FontLibrary(lib);
        var info = fonts.Import(MakeFont("Lobster.ttf"));

        var msg = fonts.ClockMessage(new ClockSettings { Font = "library:" + info.Id, X = 0.25 });
        Assert.Equal(0.25, (double?)msg["x"]);
        Assert.Equal(info.Src, (string?)msg["font"]!["src"]);
        Assert.Equal("Lobster", (string?)msg["font"]!["family"]);

        var sys = fonts.ClockMessage(new ClockSettings { Font = "system:Segoe Script" });
        Assert.Equal("Segoe Script", (string?)sys["font"]!["family"]);
        Assert.Null(sys["font"]!["src"]);

        Assert.Null(fonts.ClockMessage(new ClockSettings { Font = "library:gone.ttf" })["font"]);
        Assert.Null(fonts.ClockMessage(new ClockSettings())["font"]);
    }

    [Fact]
    public void ResettingAFontThroughPatch_ClearsIt()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetLook(Id, JsonNode.Parse("""{"screensaver":{"clock":{"font":"library:a.ttf"}}}""")!.AsObject());
        Assert.Equal("library:a.ttf", store.Current.ScreensaverClockFor("*").Font);
        store.SetLook(Id, JsonNode.Parse("""{"screensaver":{"clock":{"font":null}}}""")!.AsObject());
        Assert.Null(store.Current.ScreensaverClockFor("*").Font);
    }

    [Fact]
    public async Task Pack_CarriesTheSelectedLibraryFont()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var info = new FontLibrary(source).Import(MakeFont("Lobster.ttf", 321));
        var other = new FontLibrary(source).Import(MakeFont("Other.ttf"));
        var settings = new AppSettings();
        settings.Looks["builtin:twilight"] = new LookSettings { Screensaver = { Clock = { Font = "library:" + info.Id } } };

        var pack = Path.Combine(_dir, "p" + PackService.Extension);
        await PackService.ExportAsync(source, settings, ["settings"], pack, "test", null, null, TestContext.Current.CancellationToken);

        var target = new Library(Path.Combine(_dir, "B"));
        var (_, imported) = await PackService.ImportAsync(target, pack, null, TestContext.Current.CancellationToken);

        var targetFonts = new FontLibrary(target);
        Assert.Equal("library:" + info.Id, imported!.ScreensaverClockFor("*").Font);
        Assert.True(targetFonts.Exists(info.Id));
        Assert.Equal(321, new FileInfo(targetFonts.PathOf(info.Id)).Length);
        Assert.False(targetFonts.Exists(other.Id));   // only the selected font travels
    }
}
