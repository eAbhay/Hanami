using System.Text.RegularExpressions;

namespace Hanami.Core.Pets;

/// <summary>
/// The pure parts of browsing shimeji.org (a community library of Shimeji): which pages and hosts are the site's, and how an
/// upload's page address maps to its id. The art belongs to the people who made and uploaded it, so Hanami only takes a
/// pet when the user asks for it.
/// </summary>
public static class ShimejiOrgSource
{
    public const string Site = "shimeji.org";
    public const string StartUrl = "https://shimeji.org/";
    /// <summary>The biggest download taken (the site's uploads are mostly 1 to 10 MB).</summary>
    public const long MaxDownloadBytes = 40L * 1024 * 1024;

    private static readonly Regex IdPattern = new(@"^[a-z0-9]{4,24}$", RegexOptions.CultureInvariant);

    /// <summary>Hosts the browse window may navigate to: the site itself and Cloudflare's human check. Anything else opens in the default browser.</summary>
    public static bool IsAllowedHost(string? host) =>
        !string.IsNullOrEmpty(host) && (host.Equals("shimeji.org", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".shimeji.org", StringComparison.OrdinalIgnoreCase)
            || host.Equals("challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase));

    public static bool IsAllowedUrl(Uri? uri) => uri is { Scheme: "https" } && IsAllowedHost(uri.Host);

    /// <summary>An upload's page, <c>https://shimeji.org/u/&lt;id&gt;</c> (query, fragment and a trailing slash ignored): the id, else null.</summary>
    public static string? IdFromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!uri.Host.Equals("shimeji.org", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("www.shimeji.org", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("u", StringComparison.OrdinalIgnoreCase)) return null;
        var id = Uri.UnescapeDataString(parts[1]).ToLowerInvariant();
        return IdPattern.IsMatch(id) ? id : null;
    }

    /// <summary>
    /// Whether a download the page starts may be saved: https only. The site decides where its files come from (it may use
    /// a storage host of its own), so the host is not checked; what is saved goes to a temporary file, is capped at
    /// <see cref="MaxDownloadBytes"/> and only ever read by the zip importers, which treat it as untrusted.
    /// </summary>
    public static bool IsAllowedDownload(Uri? uri) => uri is { Scheme: "https" };

    /// <summary>
    /// The script that presses the page's own Download button (it returns "clicked", or "no-button"). The site then asks
    /// "Ready to download?" in a dialog with a second Download button, which is pressed too (once, within about 3 seconds),
    /// so one click on Get pet is enough. The dialog's button is the last Download button on the page.
    /// </summary>
    public const string ClickDownloadScript =
        "(() => { const isDownload = x => /^\\s*download\\s*$/i.test(x.textContent); const all = () => [...document.querySelectorAll('button')].filter(isDownload); " +
        "const first = all()[0]; if (!first) return 'no-button'; first.click(); let confirmed = false; " +
        "for (const ms of [500, 1200, 2500]) setTimeout(() => { const buttons = all(); if (!confirmed && buttons.length > 1) { confirmed = true; buttons[buttons.length - 1].click(); } }, ms); return 'clicked'; })()";

    /// <summary>The script that reads the artist's name from an upload's page ("ARTIST:" followed by the name), or "".</summary>
    public const string ArtistScript =
        "(() => ((document.querySelector('main')?.innerText || '').match(/ARTIST:\\s*([^\\n]+)/) || [])[1] || '')()";
}
