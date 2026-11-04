using System.Security.Cryptography;

namespace Hanami.Core;

/// <summary>
/// The pure, testable parts of installing an update: reading the checksum file, checking a download, deciding which addresses may be
/// fetched, and swapping the exe in safely. No windows or prompts. See docs/CONTRACTS.md, "Updates".
/// </summary>
public static class UpdateInstall
{
    /// <summary>
    /// The hash from a <c>Hanami.exe.sha256</c> file: <c>&lt;64 hex&gt; *Hanami.exe</c> (the format <c>sha256sum</c> writes; frozen).
    /// A line with only a hash is accepted; a line naming another file is not. Returns lowercase hex, or null.
    /// </summary>
    public static string? ParseChecksum(string text, string assetName)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
            var hash = parts[0];
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) continue;
            if (parts.Length == 2)
            {
                var name = parts[1].Trim().TrimStart('*');
                if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) continue;
            }
            return hash.ToLowerInvariant();
        }
        return null;
    }

    public static string Sha256Hex(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Null when the file is what the release promised, else a plain message. A size of 0 or less means "not known".</summary>
    public static string? Verify(string path, long expectedSize, string expectedSha256)
    {
        var actualSize = new FileInfo(path).Length;
        if (expectedSize > 0 && actualSize != expectedSize)
            return FormattableString.Invariant($"The download is {actualSize} bytes but the release says {expectedSize}. It was cut short or changed.");
        if (!string.Equals(Sha256Hex(path), expectedSha256, StringComparison.OrdinalIgnoreCase))
            return "The download doesn't match its checksum, so it was thrown away.";
        return null;
    }

    /// <summary>
    /// May this address be fetched? <c>https</c> always; <c>http</c>, <c>file</c> and plain local paths only for a test feed
    /// (<paramref name="testFeed"/>). Everything else (ftp, javascript, ...) never.
    /// </summary>
    public static bool IsAllowedUrl(string? url, bool testFeed)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)   // a "C:\..." path parses as scheme "c"
            return uri.Scheme == Uri.UriSchemeHttps || (testFeed && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeFile));
        return testFeed && Path.IsPathFullyQualified(url);
    }

    /// <summary>The local file a <c>file://</c> URI or a plain path points at, or null for a network address.</summary>
    public static string? LocalPath(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)
            return uri.IsFile ? uri.LocalPath : null;
        return Path.IsPathFullyQualified(url) ? url : null;
    }

    /// <summary>Is the address one a test feed may be given outside the dev copy: a local file or a loopback server.</summary>
    public static bool IsLocalFeed(string? feed)
    {
        if (string.IsNullOrWhiteSpace(feed)) return false;
        if (Uri.TryCreate(feed, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)
            return uri.IsFile || (uri.Scheme is "http" or "https" && uri.IsLoopback);
        return Path.IsPathFullyQualified(feed);
    }

    /// <summary>File name for a downloaded version, safe whatever the tag contained.</summary>
    public static string DownloadName(string version)
    {
        var safe = new string(version.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' ? c : '_').ToArray());
        return $"Hanami-{safe}.exe";
    }

    /// <summary>
    /// Puts <paramref name="source"/> in place of <paramref name="target"/> without ever leaving a half-written target: copy to
    /// <c>target.new</c> beside it, then move that over the target. If anything fails the target is untouched and the copy is removed.
    /// Retries while the old process still holds the file (an IOException).
    /// </summary>
    public static void Replace(string source, string target, int attempts = 20, TimeSpan? delay = null)
    {
        var staged = target + ".new";
        var wait = delay ?? TimeSpan.FromMilliseconds(250);
        try
        {
            File.Copy(source, staged, overwrite: true);
            for (var attempt = 1; ; attempt++)
            {
                try { File.Move(staged, target, overwrite: true); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < attempts) { Thread.Sleep(wait); }   // a running exe says "access denied"
            }
        }
        catch
        {
            try { File.Delete(staged); } catch (Exception) { /* leftover .new is overwritten next time */ }
            throw;
        }
    }

    /// <summary>Best effort: removes the update folder (leftovers of a finished or failed update).</summary>
    public static void DeleteFolder(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Could not remove {dir}: {ex.Message}"); }
    }
}
