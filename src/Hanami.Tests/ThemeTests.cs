using System.Text.Json.Nodes;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public ThemeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_dir, "config", "settings.json");

    private static AppSettings Slots(bool split)
    {
        var s = new AppSettings { Theme = { Split = split } };
        s.Assignments["*"].WallpaperId = "day";
        s.Assignments["*"].DarkWallpaperId = "night";
        return s;
    }

    [Fact]
    public void SplitOff_IgnoresDarkSlots()
    {
        var s = Slots(split: false);
        Assert.Equal("day", s.DesktopWallpaperFor("*", dark: true));
        Assert.Equal("day", s.DesktopWallpaperFor("*", dark: false));
    }

    [Fact]
    public void SplitOn_PicksTheSlotForTheTheme()
    {
        var s = Slots(split: true);
        Assert.Equal("day", s.DesktopWallpaperFor("*", dark: false));
        Assert.Equal("night", s.DesktopWallpaperFor("*", dark: true));
    }

    [Fact]
    public void EmptyDarkSlot_FallsBackToTheLightWallpaper()
    {
        var s = Slots(split: true);
        s.Assignments["*"].DarkWallpaperId = null;
        Assert.Equal("day", s.DesktopWallpaperFor("*", dark: true));
    }

    [Theory]
    [InlineData("system", false, false)]
    [InlineData("system", true, true)]
    [InlineData("light", true, false)]
    [InlineData("dark", false, true)]
    [InlineData("dark", true, true)]
    public void ActiveTheme_FollowsWindowsOrIsForced(string mode, bool windowsDark, bool expectedDark) =>
        Assert.Equal(expectedDark, new ThemeSettings { Mode = mode }.IsDark(windowsDark));

    [Fact]
    public void ScreensaverSameAsDesktop_FollowsTheActiveTheme()
    {
        var s = Slots(split: true);
        Assert.Null(s.Screensaver.WallpaperId);
        Assert.Equal("night", s.ScreensaverWallpaperFor("*", dark: true));
        Assert.Equal("day", s.ScreensaverWallpaperFor("*", dark: false));
    }

    [Fact]
    public void ScreensaverOwnBackground_HasItsOwnDarkSlot()
    {
        var s = Slots(split: true);
        s.Screensaver.WallpaperId = "sun";
        Assert.Equal("sun", s.ScreensaverWallpaperFor("*", dark: true));      // dark slot empty → the light one
        s.Screensaver.DarkWallpaperId = "moon";
        Assert.Equal("moon", s.ScreensaverWallpaperFor("*", dark: true));
        Assert.Equal("sun", s.ScreensaverWallpaperFor("*", dark: false));
    }

    [Fact]
    public void LooksFollowTheShownWallpaper()
    {
        var s = Slots(split: true);
        s.Looks["day"] = new LookSettings { Desktop = { EffectPreset = "sakura" } };
        s.Looks["night"] = new LookSettings { Desktop = { EffectPreset = "fireflies" }, Screensaver = { Clock = { X = 0.8 } } };
        Assert.Equal("sakura", (string?)s.DesktopEffectFor("*", dark: false)["preset"]);
        Assert.Equal("fireflies", (string?)s.DesktopEffectFor("*", dark: true)["preset"]);
        Assert.Equal(0.8, s.ScreensaverClockFor("*", dark: true).X);
    }

    [Fact]
    public void PerMonitorOverride_StartsFromTheSharedSlots_AndDarkSlotIsWrittenOnItsOwn()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetAssignment("*", "day");
        store.SetAssignment("*", "night", dark: true);
        Assert.Equal("day", store.Current.Assignments["*"].WallpaperId);
        Assert.Equal("night", store.Current.Assignments["*"].DarkWallpaperId);

        store.SetAssignment("MON", "other");
        Assert.Equal("other", store.Current.Assignments["MON"].WallpaperId);
        Assert.Equal("night", store.Current.Assignments["MON"].DarkWallpaperId);
    }

    [Fact]
    public void FirstSelectionInTheDarkSlot_CopiesTheLightWallpapersLook()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetAssignment("*", "day");
        store.SetLook("day", JsonNode.Parse("""{"desktop":{"effectPreset":"snow"},"screensaver":{"clock":{"x":0.2}}}""")!.AsObject());
        store.SetAssignment("*", "night", dark: true);

        Assert.Equal("snow", store.Current.Looks["night"].Desktop.EffectPreset);
        Assert.Equal(0.2, store.Current.Looks["night"].Screensaver.Clock.X);
        Assert.Equal("day", store.Current.Assignments["*"].WallpaperId);   // the light slot is untouched
    }

    [Fact]
    public void DeletingAWallpaper_ClearsTheDarkSlotsUsingIt()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetAssignment("*", "night", dark: true);
        store.Patch(JsonNode.Parse("""{"screensaver":{"wallpaperId":"sun","darkWallpaperId":"night"}}""")!.AsObject());
        store.ForgetWallpaper("night");

        Assert.Null(store.Current.Assignments["*"].DarkWallpaperId);
        Assert.Null(store.Current.Screensaver.DarkWallpaperId);
        Assert.False(store.Current.Looks.ContainsKey("night"));
    }

    [Fact]
    public void ScreensaverDarkBackground_StartsFromTheLightBackgroundsLook()
    {
        var store = new SettingsStore(SettingsPath);
        store.SetLook("sun", JsonNode.Parse("""{"screensaver":{"effectPreset":"rain","clock":{"y":0.8}}}""")!.AsObject());
        store.Patch(JsonNode.Parse("""{"screensaver":{"wallpaperId":"sun","darkWallpaperId":"moon"}}""")!.AsObject());
        store.EnsureScreensaverLook("builtin:twilight");

        Assert.Equal("rain", store.Current.Looks["moon"].Screensaver.EffectPreset);
        Assert.Equal(0.8, store.Current.Looks["moon"].Screensaver.Clock.Y);
    }

    [Fact]
    public void Theme_DefaultsOff_RoundTrips_AndRepairsBadMode()
    {
        var path = SettingsPath;
        var store = new SettingsStore(path);
        Assert.False(store.Current.Theme.Split);
        Assert.Equal("system", store.Current.Theme.Mode);

        store.Patch(JsonNode.Parse("""{"theme":{"split":true,"mode":"dark"}}""")!.AsObject());
        var reloaded = new SettingsStore(path).Current;
        Assert.True(reloaded.Theme.Split);
        Assert.Equal("dark", reloaded.Theme.Mode);

        store.Patch(JsonNode.Parse("""{"theme":{"mode":"purple"}}""")!.AsObject());
        Assert.Equal("system", store.Current.Theme.Mode);
    }

    [Fact]
    public async Task Pack_KeepsThemeAndDarkSlotsWithSettings()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var settings = Slots(split: true);
        settings.SettingsVersion = AppSettings.CurrentVersion;
        settings.Theme.Mode = "light";
        settings.Screensaver.DarkWallpaperId = "moon";

        var pack = Path.Combine(_dir, "p" + PackService.Extension);
        await PackService.ExportAsync(source, settings, ["settings"], pack, "test", null, null, TestContext.Current.CancellationToken);
        var (_, imported) = await PackService.ImportAsync(new Library(Path.Combine(_dir, "B")), pack, null, TestContext.Current.CancellationToken);

        Assert.True(imported!.Theme.Split);
        Assert.Equal("light", imported.Theme.Mode);
        Assert.Equal("night", imported.Assignments["*"].DarkWallpaperId);
        Assert.Equal("moon", imported.Screensaver.DarkWallpaperId);
    }
}
