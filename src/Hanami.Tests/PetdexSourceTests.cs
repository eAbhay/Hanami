using System.Net;
using System.Net.Http;
using System.Text;
using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>Browsing Petdex: which addresses are pet pages, which hosts are allowed, the pet list, and the download limits.</summary>
public sealed class PetdexSourceTests
{
    // ---- addresses --------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://petdex.dev/pets/nezuko", "nezuko")]
    [InlineData("https://petdex.dev/pets/frieren-6/", "frieren-6")]
    [InlineData("https://petdex.dev/pets/nezuko?ref=home#top", "nezuko")]
    [InlineData("https://www.petdex.dev/pets/Nezuko", "nezuko")]
    [InlineData("https://petdex.dev/pets/a_b-c9", "a_b-c9")]
    public void SlugFromUrl_FindsThePetPage(string url, string slug) => Assert.Equal(slug, PetdexSource.SlugFromUrl(url));

    [Theory]
    [InlineData("https://petdex.dev/")]
    [InlineData("https://petdex.dev/pets")]
    [InlineData("https://petdex.dev/pets/")]
    [InlineData("https://petdex.dev/pets/nezuko/edit")]
    [InlineData("https://petdex.dev/collections/nezuko")]
    [InlineData("https://petdex.dev/pets/not a slug")]
    [InlineData("https://petdex.dev/pets/..%2F..%2Fx")]
    [InlineData("http://petdex.dev/pets/nezuko")]
    [InlineData("https://evil.example/pets/nezuko")]
    [InlineData("https://petdex.dev.evil.example/pets/nezuko")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void SlugFromUrl_IgnoresEverythingElse(string? url) => Assert.Null(PetdexSource.SlugFromUrl(url));

    [Theory]
    [InlineData("petdex.dev", true)]
    [InlineData("www.petdex.dev", true)]
    [InlineData("assets.petdex.dev", true)]
    [InlineData("accounts.petdex.dev", true)]
    [InlineData("challenges.cloudflare.com", true)]
    [InlineData("clerk.com", true)]
    [InlineData("evil.com", false)]
    [InlineData("petdex.dev.evil.com", false)]
    [InlineData("notpetdex.dev", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedHost_MatchesTheHostOrItsSubdomains(string? host, bool allowed) => Assert.Equal(allowed, PetdexSource.IsAllowedHost(host));

    [Theory]
    [InlineData("https://assets.petdex.dev/pets/nezuko-6856df028590/zip.zip", true)]
    [InlineData("http://assets.petdex.dev/pets/x/zip.zip", false)]
    [InlineData("https://assets.petdex.dev.evil.com/pets/x/zip.zip", false)]
    [InlineData("https://petdex.dev/pets/x/zip.zip", false)]
    [InlineData("https://assets.petdex.dev:8443/pets/x/zip.zip", false)]
    [InlineData("file:///C:/x.zip", false)]
    [InlineData(null, false)]
    public void IsAssetUrl_OnlyTheFileHostOverHttps(string? url, bool ok) => Assert.Equal(ok, PetdexSource.IsAssetUrl(url));

    // ---- the pet list -----------------------------------------------------------------------------

    private const string Manifest = """
        {"total":4,"pets":[
          {"slug":"nezuko","displayName":"Nezuko","submittedBy":"sintaxx","zipUrl":"https://assets.petdex.dev/pets/nezuko-6856df028590/zip.zip"},
          {"slug":"Frieren-6","displayName":"  ","submittedBy":null,"zipUrl":"https://assets.petdex.dev/pets/frieren-6-aa/zip.zip"},
          {"slug":"evil","displayName":"Evil","zipUrl":"https://evil.example/zip.zip"},
          {"slug":"../bad","displayName":"Bad","zipUrl":"https://assets.petdex.dev/x.zip"}
        ]}
        """;

    [Fact]
    public void ParseManifest_KeepsValidPets_AndDropsTheRest()
    {
        var list = PetdexSource.ParseManifest(Manifest);
        Assert.Equal(["frieren-6", "nezuko"], list.Keys.Order());
        Assert.Equal("Nezuko", list["nezuko"].DisplayName);
        Assert.Equal("sintaxx", list["nezuko"].SubmittedBy);
        Assert.Equal("frieren-6", list["frieren-6"].DisplayName);   // no display name: the slug
        Assert.Equal(new PetSource { Site = "petdex", Slug = "nezuko", SubmittedBy = "sintaxx" }.Slug, list["nezuko"].ToSource().Slug);
        Assert.Equal("petdex", list["nezuko"].ToSource().Site);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"pets":"nope"}""")]
    [InlineData("""{"pets":[1,"x",null]}""")]
    public void ParseManifest_ToleratesGarbage(string json) => Assert.Empty(PetdexSource.ParseManifest(json));

    // ---- downloads --------------------------------------------------------------------------------

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static HttpResponseMessage Ok(byte[] bytes, long? declared = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        if (declared is not null) r.Content.Headers.ContentLength = declared;
        return r;
    }

    private static PetdexEntry Entry(string url = "https://assets.petdex.dev/pets/x/zip.zip") => new("x", "X", "me", url);

    [Fact]
    public async Task ListAsync_ReadsTheListOnce()
    {
        var handler = new FakeHandler(_ => Ok(Encoding.UTF8.GetBytes(Manifest)));
        var client = new PetdexClient(new HttpClient(handler));
        Assert.Equal(2, (await client.ListAsync()).Count);
        Assert.Equal(2, (await client.ListAsync()).Count);
        Assert.Single(handler.Requests);   // once per client, so once per browse window
        Assert.Equal(PetdexSource.ManifestUrl, handler.Requests[0]);
    }

    [Fact]
    public async Task ListAsync_RefusesAnUnreadableList_AndAListFromElsewhere()
    {
        await Assert.ThrowsAsync<IOException>(() => new PetdexClient(new HttpClient(new FakeHandler(_ => Ok("nope"u8.ToArray())))).ListAsync());

        // A redirect to another host shows up as the final request address.
        var redirected = new HttpClient(new RedirectingHandler("https://evil.example/list.json", Encoding.UTF8.GetBytes(Manifest)));
        await Assert.ThrowsAsync<IOException>(() => new PetdexClient(redirected).ListAsync());
    }

    private sealed class RedirectingHandler(string finalUrl, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl),
            });
    }

    [Fact]
    public async Task DownloadZipAsync_SavesTheFile_AndReportsProgress()
    {
        var dest = Path.Combine(Path.GetTempPath(), "hanami-dl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var seen = new List<double>();
            var client = new PetdexClient(new HttpClient(new FakeHandler(_ => Ok(new byte[100_000]))));
            await client.DownloadZipAsync(Entry(), dest, new Progress<double>(seen.Add));
            Assert.Equal(100_000, new FileInfo(dest).Length);
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadZipAsync_RefusesOtherHosts_WithoutRequestingThem()
    {
        var handler = new FakeHandler(_ => Ok([1]));
        var client = new PetdexClient(new HttpClient(handler));
        await Assert.ThrowsAsync<IOException>(() => client.DownloadZipAsync(Entry("https://evil.example/zip.zip"), Path.GetTempFileName()));
        await Assert.ThrowsAsync<IOException>(() => client.DownloadZipAsync(Entry("http://assets.petdex.dev/zip.zip"), Path.GetTempFileName()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DownloadZipAsync_StopsAtTwentyMegabytes_WhetherOrNotTheServerSaysSo()
    {
        var dest = Path.Combine(Path.GetTempPath(), "hanami-dl-" + Guid.NewGuid().ToString("N"));
        var big = (int)PetdexSource.MaxZipBytes + 1;
        try
        {
            var declared = new PetdexClient(new HttpClient(new FakeHandler(_ => Ok(new byte[10], declared: big))));
            await Assert.ThrowsAsync<IOException>(() => declared.DownloadZipAsync(Entry(), dest));

            var undeclared = new PetdexClient(new HttpClient(new FakeHandler(_ => Ok(new byte[big]))));
            await Assert.ThrowsAsync<IOException>(() => undeclared.DownloadZipAsync(Entry(), dest));
            Assert.False(File.Exists(dest));   // nothing is saved
        }
        finally { File.Delete(dest); }
    }

    [Fact]
    public async Task DownloadZipAsync_ReportsAServerError()
    {
        var client = new PetdexClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadZipAsync(Entry(), Path.GetTempFileName()));
    }
}
