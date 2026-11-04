using System.Text.Json;
using Hanami.Core;
using Xunit;

namespace Hanami.Tests;

/// <summary>Version order, which release counts, and when to check or notify (docs/CONTRACTS.md, "Updates").</summary>
public class UpdatePolicyTests
{
    private static SemVer V(string s) => SemVer.Parse(s) ?? throw new InvalidOperationException(s);

    [Theory]
    [InlineData("0.1.0-rc.2", "0.1.0")]
    [InlineData("0.1.0-rc.2", "0.1.0-rc.10")]
    [InlineData("0.1.0-rc.1", "0.1.0-rc.2")]
    [InlineData("0.1.0-alpha", "0.1.0-beta")]
    [InlineData("0.1.0-rc", "0.1.0-rc.1")]
    [InlineData("0.1.0-1", "0.1.0-alpha")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("0.1.0", "1.0.0")]
    [InlineData("v0.1.0", "v0.1.1")]
    public void Older_IsBelowNewer(string older, string newer)
    {
        Assert.True(V(older) < V(newer));
        Assert.False(V(newer) < V(older));
    }

    [Theory]
    [InlineData("v0.1.0", "0.1.0")]
    [InlineData("0.1.0+abc123", "0.1.0")]
    [InlineData("0.1.0-rc.1+abc", "v0.1.0-rc.1")]
    public void SameVersion_IsEqual(string a, string b) => Assert.Equal(V(a), V(b));

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("latest")]
    public void NotAVersion_DoesNotParse(string text) => Assert.Null(SemVer.Parse(text));

    [Fact]
    public void ToString_IsCanonical() => Assert.Equal("0.1.0-rc.2", V("v0.1.0-rc.2+sha").ToString());

    private static ReleaseInfo Rel(string tag, bool prerelease = false, bool draft = false, bool exe = true, bool sum = true)
    {
        var assets = new List<ReleaseAsset>();
        if (exe) assets.Add(new ReleaseAsset("Hanami.exe", 80_000_000, $"https://example.test/{tag}/Hanami.exe"));
        if (sum) assets.Add(new ReleaseAsset("Hanami.exe.sha256", 80, $"https://example.test/{tag}/Hanami.exe.sha256"));
        return new ReleaseInfo(tag, draft, prerelease, $"notes for {tag}", $"https://example.test/{tag}", assets);
    }

    [Fact]
    public void Stable_SkipsPrereleases_AndPicksTheNewestNormalRelease()
    {
        var releases = new[] { Rel("v0.3.0-beta.1", prerelease: true), Rel("v0.2.1"), Rel("v0.2.0"), Rel("v0.1.0") };
        var offer = UpdatePolicy.PickRelease(releases, V("0.1.0"), "stable");
        Assert.Equal("0.2.1", offer?.Version);
        Assert.Equal("https://example.test/v0.2.1/Hanami.exe", offer?.ExeUrl);
        Assert.Equal("https://example.test/v0.2.1/Hanami.exe.sha256", offer?.ChecksumUrl);
        Assert.Equal(80_000_000, offer?.SizeBytes);
    }

    [Fact]
    public void Beta_SeesPrereleasesToo()
    {
        var releases = new[] { Rel("v0.3.0-beta.1", prerelease: true), Rel("v0.2.1"), Rel("v0.3.0-beta.2", prerelease: true) };
        Assert.Equal("0.3.0-beta.2", UpdatePolicy.PickRelease(releases, V("0.2.0"), "beta")?.Version);
    }

    [Fact]
    public void ATagWithADash_CountsAsPrerelease_EvenIfGitHubDidNotFlagIt()
    {
        Assert.Null(UpdatePolicy.PickRelease([Rel("v0.2.0-rc.1")], V("0.1.0"), "stable"));
    }

    [Fact]
    public void NobodyIsOfferedALowerOrEqualVersion()
    {
        var releases = new[] { Rel("v0.1.0"), Rel("v0.0.9") };
        Assert.Null(UpdatePolicy.PickRelease(releases, V("0.1.0"), "stable"));
        Assert.Null(UpdatePolicy.PickRelease(releases, V("0.1.0"), "beta"));
        // Moving from Beta to Stable stays put until Stable passes the beta.
        Assert.Null(UpdatePolicy.PickRelease([Rel("v0.2.0")], V("0.3.0-beta.1"), "stable"));
        Assert.Equal("0.3.0", UpdatePolicy.PickRelease([Rel("v0.3.0")], V("0.3.0-beta.1"), "stable")?.Version);
    }

    [Fact]
    public void DraftsAndReleasesMissingAnAsset_AreIgnored()
    {
        var releases = new[] { Rel("v0.5.0", draft: true), Rel("v0.4.0", exe: false), Rel("v0.3.0", sum: false), Rel("v0.2.0"), Rel("not-a-version") };
        Assert.Equal("0.2.0", UpdatePolicy.PickRelease(releases, V("0.1.0"), "stable")?.Version);
    }

    [Fact]
    public void NoReleases_MeansUpToDate() => Assert.Null(UpdatePolicy.PickRelease([], V("0.1.0"), "stable"));

    [Fact]
    public void ParseReleases_ReadsTheGitHubShape()
    {
        const string json = """
        [
          {"tag_name":"v0.2.0","draft":false,"prerelease":true,"body":"hello","html_url":"https://github.com/eAbhay/Hanami/releases/tag/v0.2.0",
           "assets":[{"name":"Hanami.exe","size":123,"browser_download_url":"https://github.com/x/Hanami.exe"},
                     {"name":"Hanami.exe.sha256","size":80,"browser_download_url":"https://github.com/x/Hanami.exe.sha256"},
                     {"name":"broken"}]},
          {"draft":false},
          7
        ]
        """;
        var list = UpdatePolicy.ParseReleases(json);
        var r = Assert.Single(list);
        Assert.Equal("v0.2.0", r.Tag);
        Assert.True(r.Prerelease);
        Assert.False(r.Draft);
        Assert.Equal("hello", r.Notes);
        Assert.Equal(2, r.Assets.Count);
        Assert.Equal(123, r.Assets[0].Size);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    public void ParseReleases_RejectsAnythingElse(string json) => Assert.ThrowsAny<JsonException>(() => UpdatePolicy.ParseReleases(json));

    [Fact]
    public void ShouldCheck_OnlyWhenOn_AndADayHasPassed()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(UpdatePolicy.ShouldCheck(false, null, now));
        Assert.True(UpdatePolicy.ShouldCheck(true, null, now));
        Assert.False(UpdatePolicy.ShouldCheck(true, now.AddHours(-23), now));
        Assert.True(UpdatePolicy.ShouldCheck(true, now.AddHours(-24), now));
        Assert.True(UpdatePolicy.ShouldCheck(true, now.AddHours(1), now));   // a clock that went backwards
    }

    [Fact]
    public void Notify_OncePerVersion_NeverForTheSkippedOne()
    {
        Assert.True(UpdatePolicy.ShouldNotify("0.2.0", null, null));
        Assert.False(UpdatePolicy.ShouldNotify("0.2.0", null, "0.2.0"));
        Assert.True(UpdatePolicy.ShouldNotify("0.2.1", null, "0.2.0"));
        Assert.False(UpdatePolicy.ShouldNotify("0.2.0", "0.2.0", null));
        Assert.True(UpdatePolicy.ShouldNotify("0.2.1", "0.2.0", "0.2.0"));
    }

    [Fact]
    public void Reminder_IsDueOnce_ForTheAnnouncedVersion()
    {
        var due = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeSpan.FromDays(3), UpdatePolicy.RemindAfter);
        Assert.False(UpdatePolicy.ShouldRemind("0.2.0", "0.2.0", due, true, due.AddMinutes(-1)));   // not yet
        Assert.True(UpdatePolicy.ShouldRemind("0.2.0", "0.2.0", due, true, due));
        Assert.True(UpdatePolicy.ShouldRemind("0.2.0", "0.2.0", due, true, due.AddDays(9)));        // the PC was off: late, but still once
        Assert.False(UpdatePolicy.ShouldRemind("0.2.0", "0.2.0", null, true, due));                 // already shown, or announced before reminders
        Assert.False(UpdatePolicy.ShouldRemind("0.2.1", "0.2.0", due, true, due));                  // a different version gets its own notification
        Assert.False(UpdatePolicy.ShouldRemind("0.2.0", null, due, true, due));
        Assert.False(UpdatePolicy.ShouldRemind("0.2.0", "0.2.0", due, false, due));                 // "Tell me when updates are available" is off
    }

    [Fact]
    public void ANewerVersion_ClearsASkip()
    {
        Assert.True(UpdatePolicy.IsSkipped("0.2.0", "v0.2.0"));
        Assert.False(UpdatePolicy.SkipIsStale("0.2.0", "0.2.0"));
        Assert.True(UpdatePolicy.SkipIsStale("0.2.1", "0.2.0"));
        Assert.False(UpdatePolicy.IsSkipped("0.2.1", "0.2.0"));
        Assert.False(UpdatePolicy.SkipIsStale("0.2.1", null));
    }

    [Fact]
    public void OldSettingsFiles_GetUpdateDefaults()
    {
        var s = SettingsStore.Normalize(JsonSerializer.Deserialize<AppSettings>("""{"startWithWindows":false}""", Json.Options));
        Assert.True(s.Updates.AutoCheck);
        Assert.Equal("stable", s.Updates.Channel);
        Assert.Null(s.Updates.SkippedVersion);
        Assert.Null(s.Updates.LastCheckUtc);
    }

    [Fact]
    public void UpdateSettings_AreCleanedUp()
    {
        var s = SettingsStore.Normalize(JsonSerializer.Deserialize<AppSettings>(
            """{"updates":{"channel":"Nightly","skippedVersion":" ","lastCheckUtc":"2026-11-04T10:00:00+02:00"}}""", Json.Options));
        Assert.Equal("stable", s.Updates.Channel);
        Assert.Null(s.Updates.SkippedVersion);
        Assert.Equal(DateTimeKind.Utc, s.Updates.LastCheckUtc!.Value.Kind);
        Assert.Equal(8, s.Updates.LastCheckUtc.Value.Hour);
        Assert.Equal("beta", SettingsStore.Normalize(JsonSerializer.Deserialize<AppSettings>("""{"updates":{"channel":"BETA"}}""", Json.Options)).Updates.Channel);
    }
}
