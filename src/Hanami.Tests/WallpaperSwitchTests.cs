using System.Text.Json;
using System.Text.Json.Nodes;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public sealed class WallpaperSwitchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public WallpaperSwitchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Old_files_without_the_block_load_with_the_wallpaper_on()
    {
        File.WriteAllText(SettingsPath, """{"paused":true}""");
        Assert.True(new SettingsStore(SettingsPath).Current.Wallpaper.Enabled);
    }

    [Fact]
    public void A_null_block_gets_the_defaults()
    {
        var s = SettingsStore.Normalize(JsonSerializer.Deserialize<AppSettings>("""{"wallpaper":null}""", Json.Options));
        Assert.True(s.Wallpaper.Enabled);
    }

    [Fact]
    public void Switching_off_is_saved_and_leaves_the_rest_alone()
    {
        var store = new SettingsStore(SettingsPath);
        store.Patch(new JsonObject { ["wallpaper"] = new JsonObject { ["enabled"] = false } });
        var reloaded = new SettingsStore(SettingsPath).Current;
        Assert.False(reloaded.Wallpaper.Enabled);
        Assert.True(reloaded.Pets.Enabled);
        Assert.True(reloaded.Screensaver.Enabled);
        Assert.False(reloaded.Paused);
    }
}
