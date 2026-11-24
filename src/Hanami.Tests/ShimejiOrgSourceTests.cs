using Hanami.Core;
using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>Browsing shimeji.org: which pages are uploads, which addresses the window may go to, and the credit a downloaded pet gets.</summary>
public sealed class ShimejiOrgSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));

    public ShimejiOrgSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("https://shimeji.org/u/a70vf3js", "a70vf3js")]
    [InlineData("https://shimeji.org/u/a70vf3js/", "a70vf3js")]
    [InlineData("https://shimeji.org/u/A70VF3JS?ref=x#top", "a70vf3js")]
    [InlineData("https://www.shimeji.org/u/8tgwbbok", "8tgwbbok")]
    public void IdFromUrl_FindsAnUploadPage(string url, string id) => Assert.Equal(id, ShimejiOrgSource.IdFromUrl(url));

    [Theory]
    [InlineData("https://shimeji.org/")]
    [InlineData("https://shimeji.org/about")]
    [InlineData("https://shimeji.org/u")]
    [InlineData("https://shimeji.org/u/")]
    [InlineData("https://shimeji.org/u/abc")]                 // too short to be an id
    [InlineData("https://shimeji.org/u/a70vf3js/report")]
    [InlineData("https://shimeji.org/u/not an id")]
    [InlineData("https://shimeji.org/u/..%2F..%2Fx")]
    [InlineData("http://shimeji.org/u/a70vf3js")]
    [InlineData("https://evil.example/u/a70vf3js")]
    [InlineData("https://shimeji.org.evil.example/u/a70vf3js")]
    [InlineData(null)]
    public void IdFromUrl_IgnoresEverythingElse(string? url) => Assert.Null(ShimejiOrgSource.IdFromUrl(url));

    [Theory]
    [InlineData("https://shimeji.org/", true)]
    [InlineData("https://www.shimeji.org/u/x", true)]
    [InlineData("https://challenges.cloudflare.com/turnstile/v0/api.js", true)]
    [InlineData("https://www.reddit.com/r/shimeji/", false)]
    [InlineData("https://shimeji.org.evil.com/", false)]
    [InlineData("https://notshimeji.org/", false)]
    [InlineData("http://shimeji.org/", false)]
    public void IsAllowedUrl_StaysOnTheSite(string url, bool allowed) => Assert.Equal(allowed, ShimejiOrgSource.IsAllowedUrl(new Uri(url)));

    [Theory]
    [InlineData("https://shimeji.org/api/download/x", true)]
    [InlineData("https://storage.example.com/file.zip", true)]   // the site may keep its files elsewhere
    [InlineData("http://shimeji.org/file.zip", false)]
    [InlineData("file:///C:/x.zip", false)]
    public void IsAllowedDownload_IsHttpsOnly(string url, bool allowed) => Assert.Equal(allowed, ShimejiOrgSource.IsAllowedDownload(new Uri(url)));

    [Theory]
    [InlineData("https://shimeji.org/d/a70vf3js.zip", "a70vf3js")]
    [InlineData("https://www.shimeji.org/d/8TGWBBOK.zip", "8tgwbbok")]
    public void IdFromDownloadUrl_ReadsTheUploadFromADownloadAddress(string url, string id) => Assert.Equal(id, ShimejiOrgSource.IdFromDownloadUrl(new Uri(url)));

    [Theory]
    [InlineData("https://shimeji.org/d/abc.zip")]                  // too short
    [InlineData("https://shimeji.org/u/a70vf3js")]
    [InlineData("https://shimeji.org/d/a70vf3js.exe")]
    [InlineData("https://shimeji.org/d/../a70vf3js.zip")]
    [InlineData("http://shimeji.org/d/a70vf3js.zip")]
    [InlineData("https://evil.example/d/a70vf3js.zip")]
    public void IdFromDownloadUrl_IgnoresEverythingElse(string url) => Assert.Null(ShimejiOrgSource.IdFromDownloadUrl(new Uri(url)));

    [Fact]
    public void TheListingFilter_HidesOnlyMobileCards()
    {
        Assert.Contains("Mobile shimeji", ShimejiOrgSource.ListingFilterScript);
        Assert.DoesNotContain("Desktop shimeji", ShimejiOrgSource.ListingFilterScript);
        Assert.Contains("MutationObserver", ShimejiOrgSource.ListingFilterScript);   // and cards the page adds later
    }

    [Fact]
    public void ThePageScripts_AreRegexesThatRunInAPage()
    {
        // The scripts are plain strings handed to the page: check they carry the regular expressions intact (a lost backslash would break them).
        Assert.Contains(@"/^\s*download\s*$/i", ShimejiOrgSource.ClickDownloadScript);
        Assert.Contains(@"ARTIST:\s*([^\n]+)", ShimejiOrgSource.ArtistScript);
    }

    private PetLibrary Library_() => new(new Library(Path.Combine(_dir, "Library")));

    private static PetPackManifest Add(PetLibrary lib, string name)
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var frames = Enumerable.Range(1, ShimejiFrames.NumFrames).ToDictionary(n => n, _ => png);
        return lib.Add(name, frames, 1, 1);
    }

    [Fact]
    public void ASourceGivesThePackItsCredit_ForEachSite()
    {
        var lib = Library_();
        var plain = Add(lib, "Plain");
        Assert.Null(lib.Info(plain).Credit);

        lib.SetSource(plain, new PetSource { Site = "shimeji.org", Slug = "a70vf3js", SubmittedBy = "L00ni3" });
        Assert.Equal("by L00ni3 · shimeji.org", lib.Info(lib.Read(plain.Id)!).Credit);

        var noArtist = lib.SetSource(Add(lib, "Anon"), new PetSource { Site = "shimeji.org", Slug = "8tgwbbok" });
        Assert.Equal("shimeji.org", lib.Info(noArtist).Credit);

        var petdex = lib.SetSource(Add(lib, "Pd"), new PetSource { Site = "petdex", Slug = "eve", SubmittedBy = "Michael Z." });
        Assert.Equal("by Michael Z. · Petdex", lib.Info(petdex).Credit);
    }

    [Fact]
    public void TheSource_IsSavedWithThePack_AndSurvivesARename()
    {
        var lib = Library_();
        var m = lib.SetSource(Add(lib, "Keep"), new PetSource { Site = "shimeji.org", Slug = "a70vf3js", SubmittedBy = "L00ni3" });
        lib.Rename(m.Id, "Renamed");
        var back = lib.Read(m.Id)!;
        Assert.Equal("Renamed", back.Name);
        Assert.Equal(("shimeji.org", "a70vf3js", "L00ni3"), (back.Source!.Site, back.Source.Slug, back.Source.SubmittedBy));
        Assert.Contains(lib.Manifests(), x => x.Source is { Site: ShimejiOrgSource.Site } s && s.Slug == "a70vf3js");   // what the window's "Added" looks for
    }
}
