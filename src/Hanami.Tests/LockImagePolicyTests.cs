using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

public class LockImagePolicyTests
{
    private static string Sig(string id = "w", bool dark = false) => LockImagePolicy.Signature(id, dark);

    [Fact]
    public void Off_NeverCaptures()
    {
        Assert.False(LockImagePolicy.ShouldCapture(false, LockTrigger.ScreensaverStart, null, Sig(), "video"));
        Assert.False(LockImagePolicy.ShouldCapture(false, LockTrigger.SettingsChange, null, Sig(), "image"));
    }

    [Theory]
    [InlineData("video")]
    [InlineData("builtin")]
    public void ScreensaverStart_AlwaysRefreshesMovingWallpapers(string type) =>
        Assert.True(LockImagePolicy.ShouldCapture(true, LockTrigger.ScreensaverStart, Sig(), Sig(), type));

    [Fact]
    public void ScreensaverStart_RefreshesAnImageOnlyWhenItChanged()
    {
        Assert.False(LockImagePolicy.ShouldCapture(true, LockTrigger.ScreensaverStart, Sig(), Sig(), "image"));
        Assert.True(LockImagePolicy.ShouldCapture(true, LockTrigger.ScreensaverStart, Sig("a"), Sig("b"), "image"));
        Assert.True(LockImagePolicy.ShouldCapture(true, LockTrigger.ScreensaverStart, null, Sig(), "image"));   // never captured yet
    }

    [Fact]
    public void SettingsChange_RefreshesOnlyWhenTheSignatureChanged()
    {
        Assert.False(LockImagePolicy.ShouldCapture(true, LockTrigger.SettingsChange, Sig(), Sig(), "video"));
        Assert.True(LockImagePolicy.ShouldCapture(true, LockTrigger.SettingsChange, Sig("a"), Sig("b"), "video"));
        Assert.True(LockImagePolicy.ShouldCapture(true, LockTrigger.SettingsChange, Sig(dark: false), Sig(dark: true), "image"));
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
