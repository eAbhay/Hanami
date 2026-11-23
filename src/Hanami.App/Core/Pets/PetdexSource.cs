using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hanami.Core.Pets;

/// <summary>One pet in Petdex's list: its page name, who made it and where its zip is.</summary>
public sealed record PetdexEntry(string Slug, string DisplayName, string? SubmittedBy, string ZipUrl)
{
    public PetSource ToSource() => new() { Site = "petdex", Slug = Slug, SubmittedBy = SubmittedBy };
}

/// <summary>
/// The pure parts of browsing Petdex: which pages and hosts are Petdex's, how a pet's page address maps to its slug, and how
/// the pet list is read. Petdex's art belongs to its submitters, so Hanami only ever downloads it on the user's click.
/// </summary>
public static class PetdexSource
{
    public const string Site = "petdex";
    public const string StartUrl = "https://petdex.dev/";
    /// <summary>The full pet list (a redirect to a file on <see cref="AssetsHost"/>).</summary>
    public const string ManifestUrl = "https://petdex.dev/api/manifest";
    public const string AssetsHost = "assets.petdex.dev";
    public const long MaxZipBytes = PetdexReader.MaxZipBytes, MaxManifestBytes = 12L * 1024 * 1024;

    private static readonly Regex SlugPattern = new(@"^[a-z0-9][a-z0-9_-]{0,80}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Hosts the browse window may navigate to: Petdex itself, its sign-in (Clerk and the usual identity providers) and
    /// Cloudflare's challenge. Anything else opens in the default browser. Matches the host or any subdomain of it.
    /// </summary>
    private static readonly string[] AllowedHosts =
        ["petdex.dev", "clerk.com", "clerk.accounts.dev", "challenges.cloudflare.com", "accounts.google.com", "github.com"];

    public static bool IsAllowedHost(string? host) =>
        !string.IsNullOrEmpty(host) && AllowedHosts.Any(a => host.Equals(a, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + a, StringComparison.OrdinalIgnoreCase));

    /// <summary>A pet page, <c>https://petdex.dev/pets/&lt;slug&gt;</c> (query, fragment and a trailing slash ignored): the slug, else null.</summary>
    public static string? SlugFromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!uri.Host.Equals("petdex.dev", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("www.petdex.dev", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("pets", StringComparison.OrdinalIgnoreCase)) return null;
        var slug = Uri.UnescapeDataString(parts[1]).ToLowerInvariant();
        return SlugPattern.IsMatch(slug) ? slug : null;
    }

    /// <summary>True for an address on Petdex's file host over https: the only place a pet is ever downloaded from.</summary>
    public static bool IsAssetUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals(AssetsHost, StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort;

    /// <summary>
    /// Reads the pet list (<c>{pets: [{slug, displayName, submittedBy, zipUrl}]}</c>). Entries without a valid slug, or whose zip
    /// isn't on the assets host, are dropped. A bad file gives an empty list.
    /// </summary>
    public static Dictionary<string, PetdexEntry> ParseManifest(string json)
    {
        var result = new Dictionary<string, PetdexEntry>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("pets", out var pets) || pets.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var p in pets.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var slug = Text(p, "slug")?.ToLowerInvariant();
                var zip = Text(p, "zipUrl");
                if (slug is null || !SlugPattern.IsMatch(slug) || !IsAssetUrl(zip)) continue;
                result[slug] = new PetdexEntry(slug, Text(p, "displayName")?.Trim() is { Length: > 0 } name ? name : slug, Text(p, "submittedBy"), zip!);
            }
        }
        catch (JsonException) { result.Clear(); }
        return result;
    }

    private static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Downloads Petdex's pet list and pet zips (only from petdex.dev and its file host, with size limits).</summary>
public sealed class PetdexClient
{
    private readonly HttpClient _http;
    private Dictionary<string, PetdexEntry>? _list;

    public PetdexClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Hanami");   // a plain client name, nothing about the user
    }

    /// <summary>The pet list, fetched once per client (a browse window's session).</summary>
    public async Task<IReadOnlyDictionary<string, PetdexEntry>> ListAsync(CancellationToken ct = default)
    {
        if (_list is not null) return _list;
        using var response = await _http.GetAsync(PetdexSource.ManifestUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var final = response.RequestMessage?.RequestUri;
        if (final is null || !(final.Host.Equals("petdex.dev", StringComparison.OrdinalIgnoreCase) || final.Host.Equals(PetdexSource.AssetsHost, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Petdex's pet list came from an unexpected address.");
        var bytes = await ReadCappedAsync(response, PetdexSource.MaxManifestBytes, null, ct);
        _list = PetdexSource.ParseManifest(System.Text.Encoding.UTF8.GetString(bytes));
        if (_list.Count == 0) { _list = null; throw new IOException("Petdex's pet list couldn't be read."); }
        return _list;
    }

    /// <summary>Downloads a pet's zip to <paramref name="destination"/>; refuses addresses outside the assets host and anything over 20 MB.</summary>
    public async Task DownloadZipAsync(PetdexEntry entry, string destination, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!PetdexSource.IsAssetUrl(entry.ZipUrl)) throw new IOException("That pet's download isn't on Petdex's file host.");
        using var response = await _http.GetAsync(entry.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (!PetdexSource.IsAssetUrl(response.RequestMessage?.RequestUri?.ToString())) throw new IOException("That pet's download was sent somewhere else.");
        var bytes = await ReadCappedAsync(response, PetdexSource.MaxZipBytes, progress, ct);
        await File.WriteAllBytesAsync(destination, bytes, ct);
    }

    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, long cap, IProgress<double>? progress, CancellationToken ct)
    {
        var length = response.Content.Headers.ContentLength;
        if (length > cap) throw new IOException($"The download is larger than {cap / 1024 / 1024} MB.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > cap) throw new IOException($"The download is larger than {cap / 1024 / 1024} MB.");
            if (length is > 0) progress?.Report((double)ms.Length / length.Value);
        }
        return ms.ToArray();
    }
}
