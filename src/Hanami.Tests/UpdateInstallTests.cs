using System.Security.Cryptography;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

/// <summary>Checksum parsing, download checks, which addresses may be fetched and the safe exe swap (docs/CONTRACTS.md, "Updates").</summary>
public sealed class UpdateInstallTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-updtest-" + Guid.NewGuid().ToString("N")[..8]);
    private const string Hash = "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12";

    public UpdateInstallTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string File_(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    private static string Sha(string content) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    // ---- checksum file --------------------------------------------------------------------

    [Theory]
    [InlineData(Hash + " *Hanami.exe\n")]              // what build.ps1 / sha256sum write
    [InlineData(Hash + " *Hanami.exe\r\n")]
    [InlineData(Hash + "  Hanami.exe")]                // text mode
    [InlineData(Hash)]                                 // hash only
    [InlineData("\n" + Hash + " *Hanami.exe\n\n")]
    public void ParseChecksum_ReadsTheFrozenFormat(string text) =>
        Assert.Equal(Hash, UpdateInstall.ParseChecksum(text, "Hanami.exe"));

    [Fact]
    public void ParseChecksum_LowercasesTheHash() =>
        Assert.Equal(Hash, UpdateInstall.ParseChecksum(Hash.ToUpperInvariant() + " *Hanami.exe", "Hanami.exe"));

    [Theory]
    [InlineData("")]
    [InlineData("not a hash *Hanami.exe")]
    [InlineData("ab12 *Hanami.exe")]                                   // too short
    [InlineData(Hash + "00 *Hanami.exe")]                              // too long
    [InlineData("zz12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12 *Hanami.exe")]   // not hex
    [InlineData(Hash + " *Other.exe")]                                 // somebody else's hash
    public void ParseChecksum_RejectsAnythingElse(string text) =>
        Assert.Null(UpdateInstall.ParseChecksum(text, "Hanami.exe"));

    [Fact]
    public void ParseChecksum_SkipsOtherFilesLines() =>
        Assert.Equal(Hash, UpdateInstall.ParseChecksum(new string('0', 64) + " *Other.exe\n" + Hash + " *Hanami.exe\n", "Hanami.exe"));

    // ---- the downloaded file --------------------------------------------------------------

    [Fact]
    public void Verify_AcceptsTheRightFile()
    {
        var p = File_("a.exe", "hello exe");
        Assert.Null(UpdateInstall.Verify(p, new FileInfo(p).Length, Sha("hello exe")));
    }

    [Fact]
    public void Verify_AcceptsUnknownSize_ButStillChecksTheHash()
    {
        var p = File_("a.exe", "hello exe");
        Assert.Null(UpdateInstall.Verify(p, 0, Sha("hello exe")));
        Assert.NotNull(UpdateInstall.Verify(p, 0, Sha("other")));
    }

    [Fact]
    public void Verify_CatchesACutDownload()
    {
        var p = File_("a.exe", "hello");   // the release has 9 bytes
        Assert.Contains("cut short", UpdateInstall.Verify(p, 9, Sha("hello exe")));
    }

    [Fact]
    public void Verify_CatchesAChangedFile()
    {
        var p = File_("a.exe", "hello EXE");   // same length, other content
        Assert.Contains("checksum", UpdateInstall.Verify(p, 9, Sha("hello exe")));
    }

    [Fact]
    public void Verify_ComparesHashesIgnoringCase()
    {
        var p = File_("a.exe", "hello exe");
        Assert.Null(UpdateInstall.Verify(p, 9, Sha("hello exe").ToUpperInvariant()));
    }

    // ---- addresses ------------------------------------------------------------------------

    [Theory]
    [InlineData("https://github.com/eAbhay/Hanami/releases/download/v1/Hanami.exe", false, true)]
    [InlineData("http://github.com/x/Hanami.exe", false, false)]
    [InlineData("http://127.0.0.1:8000/Hanami.exe", true, true)]
    [InlineData("http://127.0.0.1:8000/Hanami.exe", false, false)]
    [InlineData(@"C:\feed\Hanami.exe", true, true)]
    [InlineData(@"C:\feed\Hanami.exe", false, false)]
    [InlineData("file:///C:/feed/Hanami.exe", true, true)]
    [InlineData("file:///C:/feed/Hanami.exe", false, false)]
    [InlineData("ftp://example.com/Hanami.exe", true, false)]
    [InlineData("javascript:alert(1)", true, false)]
    [InlineData("Hanami.exe", true, false)]             // relative: no
    [InlineData("", true, false)]
    [InlineData(null, true, false)]
    public void IsAllowedUrl(string? url, bool testFeed, bool expected) =>
        Assert.Equal(expected, UpdateInstall.IsAllowedUrl(url, testFeed));

    [Theory]
    [InlineData(@"C:\feed\releases.json", true)]
    [InlineData("file:///C:/feed/releases.json", true)]
    [InlineData("http://127.0.0.1:8765/releases.json", true)]
    [InlineData("http://localhost:8765/releases.json", true)]
    [InlineData("https://api.github.com/repos/x/y/releases", false)]
    [InlineData("http://evil.example/releases.json", false)]
    [InlineData("", false)]
    public void IsLocalFeed(string feed, bool expected) => Assert.Equal(expected, UpdateInstall.IsLocalFeed(feed));

    [Fact]
    public void LocalPath_OnlyForLocalAddresses()
    {
        Assert.Equal(@"C:\feed\Hanami.exe", UpdateInstall.LocalPath(@"C:\feed\Hanami.exe"));
        Assert.Equal(@"C:\feed\Hanami.exe", UpdateInstall.LocalPath("file:///C:/feed/Hanami.exe"));
        Assert.Null(UpdateInstall.LocalPath("https://github.com/x/Hanami.exe"));
    }

    [Theory]
    [InlineData("0.2.0", "Hanami-0.2.0.exe")]
    [InlineData("0.3.0-beta.1", "Hanami-0.3.0-beta.1.exe")]
    [InlineData(@"..\..\evil", "Hanami-.._.._evil.exe")]   // can't climb out of update/
    [InlineData("1.0/x:y", "Hanami-1.0_x_y.exe")]
    public void DownloadName_IsSafe(string version, string expected) => Assert.Equal(expected, UpdateInstall.DownloadName(version));

    // ---- the swap -------------------------------------------------------------------------

    [Fact]
    public void Replace_PutsTheNewFileInPlace_AndLeavesNoStagingCopy()
    {
        var src = File_("new.exe", "NEW");
        var dst = File_("Hanami.exe", "OLD");
        UpdateInstall.Replace(src, dst);
        Assert.Equal("NEW", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ".new"));
        Assert.Equal("NEW", File.ReadAllText(src));   // the source is only copied
    }

    [Fact]
    public void Replace_WorksWhenThereIsNoTargetYet()
    {
        var src = File_("new.exe", "NEW");
        var dst = Path.Combine(_dir, "Hanami.exe");
        UpdateInstall.Replace(src, dst);
        Assert.Equal("NEW", File.ReadAllText(dst));
    }

    [Fact]
    public void Replace_WithAMissingSource_LeavesTheOldFileAlone()
    {
        var dst = File_("Hanami.exe", "OLD");
        Assert.ThrowsAny<IOException>(() => UpdateInstall.Replace(Path.Combine(_dir, "missing.exe"), dst, attempts: 2, delay: TimeSpan.Zero));
        Assert.Equal("OLD", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ".new"));
    }

    [Fact]
    public void Replace_WhenTheTargetIsLocked_FailsCleanly_AndKeepsTheOldFile()
    {
        var src = File_("new.exe", "NEW");
        var dst = File_("Hanami.exe", "OLD");
        using (new FileStream(dst, FileMode.Open, FileAccess.Read, FileShare.None))   // like a running exe
        {
            Assert.ThrowsAny<Exception>(() => UpdateInstall.Replace(src, dst, attempts: 3, delay: TimeSpan.FromMilliseconds(10)));
        }
        Assert.Equal("OLD", File.ReadAllText(dst));       // never half written
        Assert.False(File.Exists(dst + ".new"));          // and the staged copy is cleaned up
    }

    [Fact]
    public async Task Replace_RetriesUntilTheTargetIsReleased()
    {
        var src = File_("new.exe", "NEW");
        var dst = File_("Hanami.exe", "OLD");
        var holder = new FileStream(dst, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(() => { Thread.Sleep(300); holder.Dispose(); }, TestContext.Current.CancellationToken);
        UpdateInstall.Replace(src, dst, attempts: 40, delay: TimeSpan.FromMilliseconds(50));
        await release;
        Assert.Equal("NEW", File.ReadAllText(dst));
    }

    [Fact]
    public void DeleteFolder_RemovesItsContents_AndIgnoresAMissingOne()
    {
        var folder = Path.Combine(_dir, "update");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Hanami-0.2.0.exe"), "x");
        UpdateInstall.DeleteFolder(folder);
        Assert.False(Directory.Exists(folder));
        UpdateInstall.DeleteFolder(folder);   // no throw
    }
}
