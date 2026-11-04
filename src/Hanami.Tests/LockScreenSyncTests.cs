using Hanami.App;
using Hanami.Core;
using Hanami.Platform;
using Xunit;

namespace Hanami.Tests;

/// <summary>Putting the previous lock screen picture back (docs/CONTRACTS.md, "Lock screen"), against a fake Windows.</summary>
public class LockScreenSyncTests : IDisposable
{
    /// <summary>Windows as verified on the real lock screen: a file at a path it already knows is ignored unless its write time is newer, and the call still succeeds.</summary>
    private sealed class FakeLockScreen : ILockScreenImage
    {
        public readonly List<string> Set = [];
        public string? Error;
        public bool IgnoreWrites;                 // Spotlight-like: succeeds, shows something else
        public byte[]? Held;                      // the picture Windows shows (a "picture" is just the file's bytes)
        private readonly Dictionary<string, DateTime> _known = [];

        public Task<string?> SetAsync(string path)
        {
            Set.Add(path);
            if (Error is not null || IgnoreWrites) return Task.FromResult(Error);
            var written = File.GetLastWriteTimeUtc(path);
            if (_known.TryGetValue(path, out var seen) && written <= seen) return Task.FromResult<string?>(null);
            _known[path] = written;
            Held = File.ReadAllBytes(path);
            return Task.FromResult<string?>(null);
        }

        public Task<bool> BackupAsync(string path)
        {
            if (Held is null) return Task.FromResult(false);
            File.WriteAllBytes(path, Held);
            return Task.FromResult(true);
        }

        public Task<byte[]?> SampleAsync(string path) => Task.FromResult<byte[]?>(File.ReadAllBytes(path));
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
        return new LockScreenSync(_paths, () => _settings, () => false, new Library(Path.Combine(_dir, "Library")), _windows)
        {
            VerifyTimeout = TimeSpan.FromMilliseconds(60),
            VerifyInterval = TimeSpan.FromMilliseconds(10),
        };
    }

    private static readonly DateTime LongAgo = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A freshly copied still: its picture is <paramref name="shade"/>, and its write time is old, as File.Copy keeps the original's.</summary>
    private string Still(byte shade)
    {
        var path = Path.Combine(_paths.LockScreenDir, "still.tmp");
        File.WriteAllBytes(path, [shade, shade, shade, shade]);
        File.SetLastWriteTimeUtc(path, LongAgo);
        return path;
    }

    [Fact]
    public async Task ChangingTheWallpaper_ReallyChangesWhatWindowsShows()
    {
        var sync = Create(optionOn: true);

        await sync.ApplyAsync(Still(10), "a|light|1");
        Assert.Equal(new byte[] { 10, 10, 10, 10 }, _windows.Held);

        await sync.ApplyAsync(Still(200), "b|light|1");   // same path, older date than Windows knows: only a fresh date gets it taken
        Assert.Equal(new byte[] { 200, 200, 200, 200 }, _windows.Held);
        Assert.Equal("ok", sync.Status);
        Assert.True(sync.OwnsPicture);
    }

    [Fact]
    public async Task WindowsKeepingAnotherPicture_EndsAsFailed_WithoutASignature()
    {
        _windows.Held = [99, 99, 99, 99];
        _windows.IgnoreWrites = true;
        var sync = Create(optionOn: true);

        await sync.ApplyAsync(Still(10), "a|light|1");

        Assert.Equal("failed", sync.Status);
        Assert.Contains("Picture", sync.Error);
        Assert.False(sync.OwnsPicture);
        Assert.False(File.Exists(Signature));
    }

    [Fact]
    public async Task WindowsGivingNoPictureBack_CountsAsUnverified_NotFailed()
    {
        _windows.IgnoreWrites = true;   // Held stays null: nothing to read back
        var sync = Create(optionOn: true);

        await sync.ApplyAsync(Still(10), "a|light|1");

        Assert.Equal("ok", sync.Status);
        Assert.True(sync.OwnsPicture);
    }

    [Fact]
    public async Task PuttingTheOldPictureBack_WorksAfterWindowsKnowsThePath()
    {
        File.WriteAllBytes(Previous, [5, 5, 5, 5]);
        File.SetLastWriteTimeUtc(Previous, LongAgo);
        var sync = Create(optionOn: true);

        await sync.ApplyAsync(Still(10), "a|light|1");
        Assert.True(await sync.RestoreAsync());
        Assert.Equal(new byte[] { 5, 5, 5, 5 }, _windows.Held);

        await sync.ApplyAsync(Still(10), "a|light|2");
        Assert.True(await sync.RestoreAsync());   // previous.jpg again: its date must be fresh again
        Assert.Equal(new byte[] { 5, 5, 5, 5 }, _windows.Held);
        Assert.False(sync.OwnsPicture);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, true)]
    [InlineData(new byte[] { 10, 10, 10 }, new byte[] { 20, 20, 20 }, true)]
    [InlineData(new byte[] { 10, 10, 10 }, new byte[] { 200, 200, 200 }, false)]
    [InlineData(new byte[] { 10, 10, 10 }, new byte[] { 10, 10 }, false)]
    [InlineData(new byte[] { }, new byte[] { }, false)]
    public void SamePicture_AllowsReEncodingNoise_ButNotAnotherPicture(byte[] a, byte[] b, bool same) =>
        Assert.Equal(same, LockImagePolicy.SamePicture(a, b));

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
