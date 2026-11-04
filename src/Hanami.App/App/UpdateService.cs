using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private bool _checking;
    private string? _etag;
    private List<ReleaseInfo> _lastReleases = [];

    public string Status { get; private set; } = "idle";   // idle | checking | upToDate | available | downloading | failed
    public UpdateOffer? Offer { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }

    /// <summary>Raised when anything the page or tray shows changed.</summary>
    public event Action? Changed;

    /// <summary>Raised once per version (and never for a skipped one) when an automatic or manual check finds it.</summary>
    public event Action<UpdateOffer>? Announce;

    /// <param name="feed">Dev only (<c>--update-feed</c>): an http(s) URL or a file path with the releases list, instead of GitHub.</param>
    public UpdateService(AppPaths paths, SettingsStore store, string? feed = null)
    {
        _paths = paths;
        _store = store;
        _customFeed = paths.IsDev && !string.IsNullOrWhiteSpace(feed);
        _feed = _customFeed ? feed! : ReleasesUrl;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hanami", AppPaths.Version));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _timer = new DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            var first = _timer.Interval == FirstCheckDelay;
            _timer.Interval = Tick;
            // The first check also runs when the last one was recent: the answer isn't kept across restarts, and an unchanged one costs nothing.
            if (_store.Current.Updates.AutoCheck && (first || UpdatePolicy.ShouldCheck(true, _store.Current.Updates.LastCheckUtc, DateTime.UtcNow)))
                await CheckAsync(manual: false);
        };
    }

    private UpdateSettings Settings => _store.Current.Updates;

    /// <summary>Automatic checks never run in the dev copy unless it was given a test feed.</summary>
    public void Start()
    {
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
            var offer = UpdatePolicy.PickRelease(releases, current, Settings.Channel);
            Log.Info(offer is null ? "Updates: up to date." : $"Updates: {offer.Version} is available.");
            Apply(offer);
        }
        catch (Exception ex)
        {
            Log.Warn($"Updates: check failed: {ex.Message}");
            if (manual) Fail(Describe(ex));
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
            _store.Patch(new JsonObject { ["updates"] = new JsonObject { ["notifiedVersion"] = offer.Version } });
            Announce?.Invoke(Offer);
        }
        Changed?.Invoke();
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
        Changed?.Invoke();
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
        _timer.Stop();
        _http.Dispose();
    }
}
