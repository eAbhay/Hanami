using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Hanami.Core;

namespace Hanami.App;

/// <summary>
/// Looks for a newer release on GitHub and keeps what it found for the tray and the settings page. It only ever asks the releases
/// list (<c>If-None-Match</c> makes an unchanged answer free) and sends nothing about the user. Downloading and installing come later and
/// always need a click. See docs/CONTRACTS.md, "Updates".
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string ReleasesUrl = "https://api.github.com/repos/eAbhay/Hanami/releases?per_page=30";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Tick = TimeSpan.FromHours(1);   // a tick, not a timer to the minute: sleep doesn't matter
    private const int MaxNotesChars = 4000;

    private readonly AppPaths _paths;
    private readonly SettingsStore _store;
    private readonly string _feed;
    private readonly bool _customFeed;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _timer;
    // The release download is big: no overall timeout (a stall is caught per read), and the headers a releases check sends aren't wanted.
    private readonly HttpClient _download = new() { Timeout = Timeout.InfiniteTimeSpan };
    private bool _checking, _installing, _disposed;
    private string? _etag;
    private List<ReleaseInfo> _lastReleases = [];

    public string Status { get; private set; } = "idle";   // idle | checking | upToDate | available | downloading | failed
    public UpdateOffer? Offer { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }

    /// <summary>Raised when anything the page or tray shows changed.</summary>
    public event Action? Changed;

    /// <summary>Raised when a check first finds a version (never a skipped one), and once more as its reminder about 3 days later if it is still waiting.</summary>
    public event Action<UpdateOffer>? Announce;

    /// <param name="feed"><c>--update-feed</c>: an http(s) URL or a file path with the releases list, instead of GitHub. The dev copy takes any;
    /// an installed copy only a local file or a loopback address (so an update can be rehearsed in a temp install).</param>
    public UpdateService(AppPaths paths, SettingsStore store, string? feed = null)
    {
        _paths = paths;
        _store = store;
        _customFeed = !string.IsNullOrWhiteSpace(feed) && (paths.IsDev || UpdateInstall.IsLocalFeed(feed));
        _feed = _customFeed ? feed! : ReleasesUrl;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hanami", AppPaths.Version));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _download.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hanami", AppPaths.Version));
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            var first = _timer.Interval == FirstCheckDelay;
            _timer.Interval = Tick;
            // The first check also runs when the last one was recent: the answer isn't kept across restarts, and an unchanged one costs nothing.
            if (_store.Current.Updates.AutoCheck && (first || UpdatePolicy.ShouldCheck(true, _store.Current.Updates.LastCheckUtc, DateTime.UtcNow)))
                await CheckAsync(manual: false);
            RemindIfDue();   // every tick, not only after a check, so it isn't up to a day late
        };
    }

    private UpdateSettings Settings => _store.Current.Updates;

    /// <summary>Automatic checks never run in the dev copy unless it was given a test feed.</summary>
    public void Start()
    {
        CleanLeftovers();
        if (_paths.IsDev && !_customFeed) { Log.Info("Updates: no automatic checks in the dev copy."); return; }
        _timer.Start();
    }

    private static SemVer? CurrentVersion() => SemVer.Parse(AppPaths.Version);

    public async Task CheckAsync(bool manual)
    {
        if (_checking || Status == "downloading") return;
        if (CurrentVersion() is not { } current)
        {
            if (manual) Fail($"This copy's version ({AppPaths.Version}) isn't one Hanami can compare.");
            else Log.Warn($"Updates: this copy's version ({AppPaths.Version}) isn't comparable; not checking.");
            return;
        }
        _checking = true;
        var before = Status;
        if (manual) { Status = "checking"; Error = null; Changed?.Invoke(); }
        try
        {
            var releases = await FetchAsync();
            _store.Patch(new JsonObject { ["updates"] = new JsonObject { ["lastCheckUtc"] = DateTime.UtcNow } });
            if (Status == "downloading") return;   // the install was clicked during an automatic check: don't overwrite its status
            var offer = UpdatePolicy.PickRelease(releases, current, Settings.Channel);
            Log.Info(offer is null ? "Updates: up to date." : $"Updates: {offer.Version} is available.");
            Apply(offer);
        }
        catch (Exception ex)
        {
            Log.Warn($"Updates: check failed: {ex.Message}");
            if (manual && Offer is null) Fail(Describe(ex));
            else if (manual)
            {
                // The version found earlier is still on offer: its tray and tab dots stay until the user updates or skips.
                Status = "available";
                Error = Describe(ex);
                Changed?.Invoke();
            }
            else if (Status == "checking") { Status = before; Changed?.Invoke(); }
        }
        finally { _checking = false; }
    }

    /// <summary>GitHub's list, or the cached one when the server says nothing changed (304).</summary>
    private async Task<List<ReleaseInfo>> FetchAsync()
    {
        if (_customFeed && !_feed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return UpdatePolicy.ParseReleases(await File.ReadAllTextAsync(_feed));

        using var request = new HttpRequestMessage(HttpMethod.Get, _feed);
        if (_etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", _etag);
        using var response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotModified) return _lastReleases;
        response.EnsureSuccessStatusCode();
        var releases = UpdatePolicy.ParseReleases(await response.Content.ReadAsStringAsync());
        _etag = response.Headers.ETag?.ToString();
        _lastReleases = releases;
        return releases;
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "GitHub didn't answer in time.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => "GitHub is limiting requests right now. Try again later.",
        HttpRequestException { StatusCode: { } code } => $"GitHub answered {(int)code}.",
        HttpRequestException => "Couldn't reach GitHub. Check the connection.",
        System.Text.Json.JsonException => "GitHub's answer wasn't a list of releases.",
        _ => ex.Message,
    };

    private void Apply(UpdateOffer? offer)
    {
        if (offer is not null && UpdatePolicy.SkipIsStale(offer.Version, Settings.SkippedVersion))
            _store.Patch(new JsonObject { ["updates"] = new JsonObject { ["skippedVersion"] = null } });   // a newer version clears a skip

        if (offer is null || UpdatePolicy.IsSkipped(offer.Version, Settings.SkippedVersion))
        {
            Offer = null;
            Status = "upToDate";
            Error = null;
            Changed?.Invoke();
            return;
        }
        var trimmed = offer.Notes.Length > MaxNotesChars ? offer.Notes[..MaxNotesChars] + "…" : offer.Notes;
        Offer = offer with { Notes = trimmed };
        Status = "available";
        Error = null;
        if (UpdatePolicy.ShouldNotify(offer.Version, Settings.SkippedVersion, Settings.NotifiedVersion))
        {
            _store.Patch(new JsonObject { ["updates"] = new JsonObject { ["notifiedVersion"] = offer.Version, ["remindAtUtc"] = DateTime.UtcNow + UpdatePolicy.RemindAfter } });
            Announce?.Invoke(Offer);
        }
        Changed?.Invoke();
    }

    /// <summary>The one reminder for a version still waiting. Not during a download or after a failed install: those say so themselves.</summary>
    private void RemindIfDue()
    {
        if (Status != "available" || Offer is not { } offer) return;
        if (!UpdatePolicy.ShouldRemind(offer.Version, Settings.NotifiedVersion, Settings.RemindAtUtc, Settings.AutoCheck, DateTime.UtcNow)) return;
        _store.Patch(new JsonObject { ["updates"] = new JsonObject { ["remindAtUtc"] = null } });
        Log.Info($"Updates: reminding about {offer.Version}.");
        Announce?.Invoke(offer);
    }

    private void Fail(string message)
    {
        Status = "failed";
        Error = message;
        Changed?.Invoke();
    }

    /// <summary>The user chose not to be offered this version again (the next one is).</summary>
    public void Skip()
    {
        if (Offer is not { } offer) return;
        _store.Patch(new JsonObject { ["updates"] = new JsonObject { ["skippedVersion"] = offer.Version } });
        Offer = null;
        Status = "upToDate";
        Error = null;
        Changed?.Invoke();
    }

    private string UpdateDir => Path.Combine(_paths.InstallDir, "update");

    /// <summary>
    /// Removes the update folder left by an earlier update (its exe may still be closing right after a restart, so a few tries).
    /// </summary>
    private void CleanLeftovers()
    {
        if (!Directory.Exists(UpdateDir)) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 5 && Directory.Exists(UpdateDir) && !_disposed; i++)
            {
                UpdateInstall.DeleteFolder(UpdateDir);
                if (Directory.Exists(UpdateDir)) await Task.Delay(TimeSpan.FromSeconds(3));
            }
        });
    }

    /// <summary>
    /// Downloads the offered version, checks it and starts it with <c>--apply-update</c>, which swaps it in and restarts Hanami. Only after a click.
    /// Returns once the download is under way (the status is <c>downloading</c> by then); progress and failures come through <see cref="Changed"/>.
    /// Any failure deletes what was downloaded, keeps this copy running and ends in <c>failed</c>.
    /// </summary>
    public async Task InstallAsync()
    {
        if (_installing || Offer is not { } offer || Status is not ("available" or "failed")) return;
        _installing = true;
        string? partFile = null, finalFile = null;
        try
        {
            if (!UpdateInstall.IsAllowedUrl(offer.ExeUrl, _customFeed) || !UpdateInstall.IsAllowedUrl(offer.ChecksumUrl, _customFeed))
                throw new InvalidDataException("The release's download address isn't a secure (https) one.");
            Status = "downloading";
            Progress = 0;
            Error = null;
            Changed?.Invoke();

            Directory.CreateDirectory(UpdateDir);
            var name = UpdateInstall.DownloadName(offer.Version);
            finalFile = Path.Combine(UpdateDir, name);
            partFile = finalFile + ".part";
            foreach (var f in new[] { partFile, finalFile }) File.Delete(f);

            var checksumText = Encoding.UTF8.GetString(await ReadAllBytesAsync(offer.ChecksumUrl));
            var expected = UpdateInstall.ParseChecksum(checksumText, UpdatePolicy.ExeAsset)
                ?? throw new InvalidDataException("The release's checksum file isn't in the expected format.");

            await DownloadAsync(offer.ExeUrl, partFile, offer.SizeBytes);
            var problem = await Task.Run(() => UpdateInstall.Verify(partFile, offer.SizeBytes, expected));
            if (problem is not null) throw new InvalidDataException(problem);
            File.Move(partFile, finalFile, overwrite: true);
            Log.Info($"Updates: downloaded and verified {name}.");

            var psi = new ProcessStartInfo(finalFile) { UseShellExecute = false, WorkingDirectory = UpdateDir };
            psi.ArgumentList.Add("--apply-update");
            var process = Process.Start(psi) ?? throw new InvalidOperationException("The new version didn't start.");
            Progress = 1;
            Changed?.Invoke();
            _ = WatchHandoverAsync(process, finalFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Updates: install failed: {ex}");
            foreach (var f in new[] { partFile, finalFile }) { if (f is not null) try { File.Delete(f); } catch (Exception) { } }
            Fail(DescribeInstall(ex));
        }
        finally { _installing = false; }
    }

    /// <summary>
    /// After the handover this copy is asked to quit by the new one. If the new one ends while this copy is still here, it didn't work
    /// (blocked, crashed, or the old copy refused to leave): say so instead of sitting on "installing" forever.
    /// </summary>
    private async Task WatchHandoverAsync(Process process, string exe)
    {
        try
        {
            int code;
            using (process) { await process.WaitForExitAsync(); code = process.ExitCode; }
            if (_disposed || Status != "downloading") return;
            Log.Warn($"Updates: the new version exited with code {code} and this copy is still running.");
            try { File.Delete(exe); } catch (Exception) { }
            Fail("The new version couldn't be started. Use Open download page to install it by hand.");
        }
        catch (Exception ex) { Log.Warn($"Updates: handover watch failed: {ex.Message}"); }
    }

    private static string DescribeInstall(Exception ex) => ex switch
    {
        System.ComponentModel.Win32Exception => "Windows wouldn't start the new version (" + ex.Message.TrimEnd('.') + "). Use Open download page to install it by hand.",
        InvalidDataException => ex.Message,
        OperationCanceledException => "The download stalled and was stopped.",
        HttpRequestException { StatusCode: { } code } => $"The download failed (GitHub answered {(int)code}).",
        HttpRequestException => "The download failed. Check the connection.",
        IOException => "The download couldn't be saved: " + ex.Message,
        _ => ex.Message,
    };

    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Reads a small file (the checksum) from a network address or a local file (test feeds).</summary>
    private async Task<byte[]> ReadAllBytesAsync(string url)
    {
        if (UpdateInstall.LocalPath(url) is { } local) return await File.ReadAllBytesAsync(local);
        using var cts = new CancellationTokenSource(StallTimeout);
        using var response = await _download.GetAsync(url, cts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cts.Token);
    }

    /// <summary>Streams the exe to <paramref name="target"/>, reporting progress. No data for 30 s counts as stalled.</summary>
    private async Task DownloadAsync(string url, string target, long expectedSize)
    {
        using var cts = new CancellationTokenSource(StallTimeout);
        HttpResponseMessage? response = null;
        Stream source;
        long total = expectedSize;
        if (UpdateInstall.LocalPath(url) is { } local)
            source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        else
        {
            response = await _download.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            total = response.Content.Headers.ContentLength ?? expectedSize;
            source = await response.Content.ReadAsStreamAsync(cts.Token);
        }
        using (response)
        await using (source)
        await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            long done = 0;
            var lastReport = Stopwatch.StartNew();
            int read;
            while ((read = await source.ReadAsync(buffer, cts.Token)) > 0)
            {
                cts.CancelAfter(StallTimeout);   // data is flowing: restart the stall clock
                await output.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                done += read;
                if (total > 0 && lastReport.ElapsedMilliseconds > 250)
                {
                    lastReport.Restart();
                    Progress = Math.Min(1, (double)done / total);
                    Changed?.Invoke();
                }
            }
        }
    }

    /// <summary>Opens the release's page in the browser (only an https link; any http link for a dev test feed).</summary>
    public void OpenPage()
    {
        var url = Offer?.PageUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == Uri.UriSchemeHttps || (_customFeed && uri.Scheme == Uri.UriSchemeHttp)))
            throw new InvalidOperationException("There is no release page to open.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public JsonObject ToState() => new()
    {
        ["status"] = Status,
        ["version"] = Offer?.Version,
        ["notes"] = Offer?.Notes,
        ["sizeBytes"] = Offer?.SizeBytes,
        ["url"] = Offer?.PageUrl,
        ["progress"] = Status == "downloading" ? Progress : null,
        ["error"] = Error,
        ["lastCheck"] = Settings.LastCheckUtc?.ToString("o"),
    };

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _download.Dispose();
        _http.Dispose();
    }
}
