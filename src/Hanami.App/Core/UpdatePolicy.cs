using System.Text.Json;

namespace Hanami.Core;

/// <summary>
/// A semantic version with prerelease rules (<c>0.1.0-rc.2</c> is older than <c>0.1.0</c>, <c>rc.2</c> older than <c>rc.10</c>).
/// <see cref="Version"/> can't hold these. A leading <c>v</c> and build metadata (<c>+sha</c>) are accepted and dropped.
/// </summary>
public sealed class SemVer : IComparable<SemVer>, IEquatable<SemVer>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    /// <summary>The dot-separated prerelease identifiers; empty for a normal release.</summary>
    public IReadOnlyList<string> Pre { get; }

    public bool IsPrerelease => Pre.Count > 0;

    private SemVer(int major, int minor, int patch, string[] pre) { Major = major; Minor = minor; Patch = patch; Pre = pre; }

    public static SemVer? Parse(string? text) => TryParse(text, out var v) ? v : null;

    public static bool TryParse(string? text, out SemVer version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        var core = dash >= 0 ? s[..dash] : s;
        var pre = dash >= 0 ? s[(dash + 1)..] : "";

        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        var nums = new int[3];
        for (var i = 0; i < 3; i++)
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) || !int.TryParse(parts[i], out nums[i])) return false;

        string[] ids = [];
        if (dash >= 0)
        {
            ids = pre.Split('.');
            if (ids.Any(id => id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))) return false;
        }
        version = new SemVer(nums[0], nums[1], nums[2], ids);
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Pre.Count == 0 || other.Pre.Count == 0) return other.Pre.Count.CompareTo(Pre.Count);   // a normal release is newer than its prereleases
        for (var i = 0; i < Math.Min(Pre.Count, other.Pre.Count); i++)
        {
            c = CompareIdentifier(Pre[i], other.Pre[i]);
            if (c != 0) return c;
        }
        return Pre.Count.CompareTo(other.Pre.Count);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var an = a.All(char.IsAsciiDigit);
        var bn = b.All(char.IsAsciiDigit);
        if (an && bn)
        {
            var x = a.TrimStart('0');
            var y = b.TrimStart('0');
            return x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
        }
        if (an) return -1;   // numbers sort before words
        if (bn) return 1;
        return string.CompareOrdinal(a, b);
    }

    public bool Equals(SemVer? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVer v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Pre));
    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Pre.Count > 0 ? "-" + string.Join('.', Pre) : "")}";

    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
}

/// <summary>A release as the GitHub releases API describes it (only what the updater reads).</summary>
public sealed record ReleaseInfo(string Tag, bool Draft, bool Prerelease, string Notes, string PageUrl, IReadOnlyList<ReleaseAsset> Assets);

public sealed record ReleaseAsset(string Name, long Size, string Url);

/// <summary>A newer release the user can take. <see cref="Version"/> is the canonical text without a leading <c>v</c>.</summary>
public sealed record UpdateOffer(string Version, string Notes, long SizeBytes, string ExeUrl, string ChecksumUrl, string PageUrl);

/// <summary>When to check for updates, which release counts, and when to tell the user (docs/CONTRACTS.md, "Updates").</summary>
public static class UpdatePolicy
{
    public const string Stable = "stable", Beta = "beta";

    /// <summary>The two asset names every release must carry. Frozen: copies already installed look for exactly these.</summary>
    public const string ExeAsset = "Hanami.exe", ChecksumAsset = "Hanami.exe.sha256";

    /// <summary>How long an automatic check waits after the last one.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    public static string NormalizeChannel(string? channel) =>
        string.Equals(channel, Beta, StringComparison.OrdinalIgnoreCase) ? Beta : Stable;

    /// <summary>Reads the body of <c>GET /repos/{owner}/{repo}/releases</c>. Throws <see cref="JsonException"/> when it isn't a list of releases.</summary>
    public static List<ReleaseInfo> ParseReleases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a list of releases.");
        var list = new List<ReleaseInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var asset in a.EnumerateArray())
                    if (asset.ValueKind == JsonValueKind.Object && Str(asset, "name") is { } name && Str(asset, "browser_download_url") is { } url)
                        assets.Add(new ReleaseAsset(name, asset.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var n) ? n : 0, url));
            if (Str(r, "tag_name") is not { } tag) continue;
            list.Add(new ReleaseInfo(tag, Bool(r, "draft"), Bool(r, "prerelease"), Str(r, "body") ?? "", Str(r, "html_url") ?? "", assets));
        }
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>
    /// The newest release worth offering, or null. Skips drafts, tags that aren't versions, prereleases on Stable, releases missing
    /// either asset, and anything not newer than <paramref name="current"/> (nobody is offered a lower version).
    /// </summary>
    public static UpdateOffer? PickRelease(IEnumerable<ReleaseInfo> releases, SemVer current, string channel)
    {
        var beta = NormalizeChannel(channel) == Beta;
        UpdateOffer? best = null;
        SemVer? bestVersion = null;
        foreach (var r in releases)
        {
            if (r.Draft || !SemVer.TryParse(r.Tag, out var version)) continue;
            if ((r.Prerelease || version.IsPrerelease) && !beta) continue;
            if (!(version > current) || (bestVersion is not null && !(version > bestVersion))) continue;
            var exe = r.Assets.FirstOrDefault(x => x.Name == ExeAsset);
            var sum = r.Assets.FirstOrDefault(x => x.Name == ChecksumAsset);
            if (exe is null || sum is null) continue;
            best = new UpdateOffer(version.ToString(), r.Notes, exe.Size, exe.Url, sum.Url, r.PageUrl);
            bestVersion = version;
        }
        return best;
    }

    /// <summary>An automatic check is due when the option is on and the last check was at least <see cref="CheckInterval"/> ago (or never).</summary>
    public static bool ShouldCheck(bool autoCheck, DateTime? lastCheckUtc, DateTime nowUtc) =>
        autoCheck && (lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= CheckInterval || lastCheckUtc.Value > nowUtc);

    /// <summary>Whether the offer is the version the user chose to skip (a newer one is not).</summary>
    public static bool IsSkipped(string offerVersion, string? skippedVersion) =>
        SemVer.TryParse(skippedVersion, out var skipped) && SemVer.TryParse(offerVersion, out var offer) && offer.Equals(skipped);

    /// <summary>Tell the user (a tray notification) once per version, never for the skipped one.</summary>
    public static bool ShouldNotify(string offerVersion, string? skippedVersion, string? notifiedVersion) =>
        !IsSkipped(offerVersion, skippedVersion)
        && !(SemVer.TryParse(notifiedVersion, out var notified) && SemVer.TryParse(offerVersion, out var offer) && offer.Equals(notified));

    /// <summary>How long a version waits after its first notification before the one reminder.</summary>
    public static readonly TimeSpan RemindAfter = TimeSpan.FromDays(3);

    /// <summary>The one reminder: the version on offer is the one already announced, its reminder is due, and the user still wants to be told.</summary>
    public static bool ShouldRemind(string offerVersion, string? notifiedVersion, DateTime? remindAtUtc, bool autoCheck, DateTime nowUtc) =>
        autoCheck && remindAtUtc is { } due && nowUtc >= due
        && SemVer.TryParse(notifiedVersion, out var notified) && SemVer.TryParse(offerVersion, out var offer) && offer.Equals(notified);

    /// <summary>A skip only holds for the version it was made for: true when the offer is newer than the skipped one, so the skip can be forgotten.</summary>
    public static bool SkipIsStale(string offerVersion, string? skippedVersion) =>
        SemVer.TryParse(skippedVersion, out var skipped) && SemVer.TryParse(offerVersion, out var offer) && offer > skipped;
}
