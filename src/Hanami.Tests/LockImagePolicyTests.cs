using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public class LockImagePolicyTests
{
    private static string Sig(string id = "w", bool dark = false, long still = 1) => LockImagePolicy.Signature(id, dark, still);

    [Fact]
    public void Off_NeverWrites()
    {
        Assert.False(LockImagePolicy.ShouldWrite(false, null, Sig()));
        Assert.False(LockImagePolicy.ShouldWrite(false, Sig("a"), Sig("b")));
    }

    [Fact]
    public void Unchanged_IsNotRewritten() =>
        Assert.False(LockImagePolicy.ShouldWrite(true, Sig(), Sig()));

    [Fact]
    public void NeverWritten_Writes() =>
        Assert.True(LockImagePolicy.ShouldWrite(true, null, Sig()));

    [Fact]
    public void ChangedWallpaper_ThemeOrStill_Rewrites()
    {
        Assert.True(LockImagePolicy.ShouldWrite(true, Sig("a"), Sig("b")));
        Assert.True(LockImagePolicy.ShouldWrite(true, Sig(dark: false), Sig(dark: true)));
        Assert.True(LockImagePolicy.ShouldWrite(true, Sig(still: 1), Sig(still: 2)));   // a newly chosen frame
    }

    [Fact]
    public void Setting_DefaultsOff_AndSurvivesSaveAndReload()
    {
        Assert.False(new ScreensaverSettings().SyncLockScreen);
        var path = Path.Combine(Path.GetTempPath(), "hanami-lock-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new SettingsStore(path);
            store.Patch(System.Text.Json.Nodes.JsonNode.Parse("""{"screensaver":{"syncLockScreen":true}}""")!.AsObject());
            Assert.True(SettingsStore.Load(path).Screensaver.SyncLockScreen);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }
}
