using Hanami.App;
using Hanami.Core;
using Hanami.Platform;
using Xunit;

namespace Hanami.Tests;

/// <summary>Putting the previous lock screen picture back (docs/CONTRACTS.md, "Lock screen"), against a fake Windows.</summary>
public class LockScreenSyncTests : IDisposable
{
    private sealed class FakeLockScreen : ILockScreenImage
    {
        public readonly List<string> Set = [];
        public string? Error;
        public Task<string?> SetAsync(string path) { Set.Add(path); return Task.FromResult(Error); }
        public Task<bool> BackupAsync(string path) => Task.FromResult(false);
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-locksync-" + Guid.NewGuid().ToString("N"));
    private readonly FakeLockScreen _windows = new();
    private readonly AppSettings _settings = new();
    private readonly AppPaths _paths;

    public LockScreenSyncTests()
    {
        _paths = AppPaths.Installed(_dir);
        Directory.CreateDirectory(_paths.LockScreenDir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Previous => Path.Combine(_paths.LockScreenDir, "previous.jpg");
    private string Signature => Path.Combine(_paths.LockScreenDir, "signature.txt");

    /// <summary>A state as after Hanami wrote its picture: the previous one kept, the signature saved.</summary>
    private void HanamiPictureInPlace(bool withBackup = true)
    {
        if (withBackup) File.WriteAllBytes(Previous, [1, 2, 3]);
        File.WriteAllText(Signature, "w|light|1");
    }

    private LockScreenSync Create(bool optionOn)
    {
        _settings.Screensaver.SyncLockScreen = optionOn;
        return new LockScreenSync(_paths, () => _settings, () => false, new Library(Path.Combine(_dir, "Library")), _windows);
    }

    [Fact]
    public async Task Quitting_PutsThePreviousPictureBack_EvenWithTheOptionOn()
    {
        HanamiPictureInPlace();
        var sync = Create(optionOn: true);
        Assert.True(sync.OwnsPicture);

        await sync.RevertOnExitAsync();

        Assert.Equal([Previous], _windows.Set);
        Assert.False(sync.OwnsPicture);
        Assert.False(File.Exists(Signature));   // so the next start with the option on writes once
    }

    [Fact]
    public async Task TurningTheOptionOff_PutsThePreviousPictureBack()
    {
        HanamiPictureInPlace();
        var sync = Create(optionOn: false);   // as after the setting changed (or the start after a crash)

        sync.RequestRefresh();
        await Task.Delay(100);

        Assert.Equal([Previous], _windows.Set);
        Assert.False(sync.OwnsPicture);
        Assert.Equal("off", sync.Status);
    }

    [Fact]
    public async Task NothingWritten_NothingIsTouched()
    {
        File.WriteAllBytes(Previous, [1]);   // a backup from long ago, but no signature: Hanami's picture isn't in place
        var sync = Create(optionOn: false);

        sync.RequestRefresh();
        await sync.RevertOnExitAsync();

        Assert.Empty(_windows.Set);
    }

    [Fact]
    public async Task NoPreviousPicture_LeavesTheLockScreen_AndForgetsTheSignature()
    {
        HanamiPictureInPlace(withBackup: false);   // Windows gave nothing to keep (e.g. Spotlight)
        var sync = Create(optionOn: false);

        await sync.RevertOnExitAsync();

        Assert.Empty(_windows.Set);
        Assert.False(sync.OwnsPicture);
        Assert.False(File.Exists(Signature));
    }

    [Fact]
    public async Task WindowsRefusing_KeepsTheSignature_SoItIsTriedAgain()
    {
        HanamiPictureInPlace();
        _windows.Error = "refused";
        var sync = Create(optionOn: false);

        await sync.RevertOnExitAsync();

        Assert.Equal("failed", sync.Status);
        Assert.True(sync.OwnsPicture);
        Assert.True(File.Exists(Signature));
    }

    [Fact]
    public void TheSignatureSurvivesARestart()
    {
        HanamiPictureInPlace();
        Assert.True(Create(optionOn: true).OwnsPicture);
        Assert.Empty(_windows.Set);   // an app start with the option on doesn't touch the picture
    }
}
