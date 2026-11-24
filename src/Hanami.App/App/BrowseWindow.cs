using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hanami.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hanami.App;

/// <summary>
/// What a browse window needs to know about a website: where to start, which hosts it may navigate to, which of its
/// pages is an item the user can take, and how to take it. Petdex is the first; a wallpaper site can plug in the same way.
/// </summary>
public sealed class BrowseSource
{
    public required string Title { get; init; }
    public required string StartUrl { get; init; }
    /// <summary>The label of the primary button (for example "Get pet").</summary>
    public required string ActionLabel { get; init; }
    /// <summary>Whether an https address may be navigated to inside the window; anything else opens in the default browser.</summary>
    public required Func<Uri, bool> AllowUrl { get; init; }
    /// <summary>The item a page address stands for (a pet's slug), or null when the page isn't an item.</summary>
    public required Func<string, string?> ItemFromUrl { get; init; }
    /// <summary>The item a file download of the site belongs to, or null when it isn't one of its items.</summary>
    public Func<string, Task<string?>>? ItemFromDownload { get; init; }
    /// <summary>Whether the library already has this item (the button then says "Added").</summary>
    public Func<string, bool>? IsAdded { get; init; }
    /// <summary>
    /// For a site that offers an item through an API: downloads and adds it. Reports short progress text; returns the message
    /// to show when done; throws a readable error. A source has this <i>or</i> <see cref="DownloadScript"/>.
    /// </summary>
    public Func<string, IProgress<string>, Task<string>>? GetAsync { get; init; }

    /// <summary>
    /// For a site whose own Download button is the only way to a file (it may ask for a human check first): a script that
    /// presses that button. The file the page then downloads is saved to a temporary file instead of the user's folder, and
    /// handed to <see cref="ImportDownloadAsync"/>. The same happens when the user presses the site's own button.
    /// </summary>
    public string? DownloadScript { get; init; }
    /// <summary>Whether a download the page starts may be saved (by its address).</summary>
    public Func<Uri, bool>? AllowDownload { get; init; }
    public long MaxDownloadBytes { get; init; } = 40L * 1024 * 1024;
    /// <summary>Adds the downloaded file (item, temporary file, the page, progress); returns the message to show. The file is deleted afterwards.</summary>
    public Func<string, string, IBrowsePage, IProgress<string>, Task<string>>? ImportDownloadAsync { get; init; }
}

/// <summary>What a source may ask of the page it is browsing.</summary>
public interface IBrowsePage
{
    /// <summary>Runs a script in the page and returns its result as text (empty when it fails).</summary>
    Task<string> ScriptAsync(string script);
}

/// <summary>
/// A window around a real website (the browse window): a toolbar with Back, Forward, Reload, the page title and a primary
/// button, above a WebView2. <b>It shows a third-party site, so it is locked down:</b> it never gets the settings bridge, the
/// app's virtual hosts or the library; it has its own browser profile, apart from Hanami's; it may navigate only to the
/// source's hosts; links elsewhere and new windows open in the default browser; the site's own downloads are cancelled
/// (a download that belongs to an item is handed to the same "get" as the button); and every permission is denied.
/// </summary>
public sealed class BrowseWindow : Window, IBrowsePage
{
    private static CoreWebView2Environment? _env;

    private readonly BrowseSource _source;
    private readonly bool _dev;
    private readonly WebView2 _web = new();
    private readonly Button _back = Tool("◀", "Back"), _forward = Tool("▶", "Forward"), _reload = Tool("⟳", "Reload");
    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.White, Margin = new Thickness(10, 0, 10, 0) };
    private readonly Button _get = new() { Padding = new Thickness(16, 5, 16, 5), MinWidth = 96, FontWeight = FontWeights.SemiBold, IsEnabled = false };
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xcf, 0xc8, 0xe0)), Margin = new Thickness(10, 5, 10, 5), TextTrimming = TextTrimming.CharacterEllipsis };
    private bool _busy;
    /// <summary>Set while the Get button waits for the page to start its download (see <see cref="BrowseSource.DownloadScript"/>).</summary>
    private TaskCompletionSource<bool>? _downloadWanted;

    public BrowseWindow(BrowseSource source, bool dev)
    {
        _source = source;
        _dev = dev;
        Title = $"{source.Title} — Hanami";
        Width = 1100;
        Height = 760;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1a, 0x22));
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hanami.ico"));
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1c, 0x1a, 0x22);

        _get.Content = source.ActionLabel;
        _back.Click += (_, _) => { if (_web.CanGoBack) _web.GoBack(); };
        _forward.Click += (_, _) => { if (_web.CanGoForward) _web.GoForward(); };
        _reload.Click += (_, _) => _web.Reload();
        _get.Click += async (_, _) => await GetCurrentAsync();

        var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 6), LastChildFill = true };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(_back); left.Children.Add(_forward); left.Children.Add(_reload);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(_get, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(_get);
        bar.Children.Add(_title);

        var root = new DockPanel();
        var top = new Border { Child = bar, Background = new SolidColorBrush(Color.FromRgb(0x25, 0x22, 0x2d)) };
        var bottom = new Border { Child = _status, Background = new SolidColorBrush(Color.FromRgb(0x25, 0x22, 0x2d)) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_web);
        Content = root;

        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _web.Dispose();
    }

    private static Button Tool(string glyph, string tip) => new() { Content = glyph, ToolTip = tip, Width = 34, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0, 4, 0, 4), IsEnabled = false };

    private static async Task<CoreWebView2Environment> EnvAsync() =>
        _env ??= await CoreWebView2Environment.CreateAsync(null, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hanami", "WebView2-Browse"));

    private async Task StartAsync()
    {
        try
        {
            await _web.EnsureCoreWebView2Async(await EnvAsync());
        }
        catch (Exception ex)
        {
            Log.Warn($"Browse window could not start: {ex.Message}");
            _status.Text = "The browser couldn't start. Is the WebView2 runtime installed?";
            return;
        }

        var core = _web.CoreWebView2;
        // No ConfigureCore, no WebMessageReceived, no virtual hosts: this page gets nothing of Hanami's.
        var s = core.Settings;
        s.IsWebMessageEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.AreDevToolsEnabled = _dev;
        s.AreBrowserAcceleratorKeysEnabled = _dev;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;

        core.NavigationStarting += (_, e) =>
        {
            if (IsAllowed(e.Uri, frame: false)) return;
            e.Cancel = true;
            OpenInDefaultBrowser(e.Uri);
        };
        core.FrameNavigationStarting += (_, e) => { if (!IsAllowed(e.Uri, frame: true)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenInDefaultBrowser(e.Uri); };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) =>
        {
            if (_source.ImportDownloadAsync is not null && CaptureDownload(e)) return;
            e.Cancel = true;
            e.Handled = true;
            _ = HandleSiteDownloadAsync(e.DownloadOperation.Uri);
        };
        core.DocumentTitleChanged += (_, _) => RefreshUi();
        core.HistoryChanged += (_, _) => RefreshUi();
        core.SourceChanged += (_, _) => RefreshUi();
        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess && e.WebErrorStatus is CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.HostNameNotResolved or CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.ConnectionAborted or CoreWebView2WebErrorStatus.Timeout)
                _status.Text = $"Can't reach {_source.Title}. Check your connection, then press Reload.";
            RefreshUi();
        };
        core.ProcessFailed += (_, e) => Log.Warn($"Browse window web process failed: {e.ProcessFailedKind}");
        core.Navigate(_source.StartUrl);
    }

    /// <summary>Whether the window may go there: https on an allowed host. Frames may also use about:, blob: and data: (challenge widgets).</summary>
    private bool IsAllowed(string uri, bool frame)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        if (u.Scheme == Uri.UriSchemeHttps) return _source.AllowUrl(u);
        return frame ? u.Scheme is "about" or "blob" or "data" : uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenInDefaultBrowser(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Could not open {u.Host} in the default browser: {ex.Message}"); }
    }

    private string? CurrentItem() => _web.CoreWebView2 is { } core ? _source.ItemFromUrl(core.Source) : null;

    private void RefreshUi()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        _back.IsEnabled = core.CanGoBack;
        _forward.IsEnabled = core.CanGoForward;
        _reload.IsEnabled = true;
        _title.Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? _source.Title : core.DocumentTitle;
        var item = CurrentItem();
        var added = item is not null && _source.IsAdded?.Invoke(item) == true;
        _get.Content = _busy ? "Getting…" : added ? "Added" : _source.ActionLabel;
        _get.IsEnabled = item is not null && !_busy && !added;
        if (_busy && _capturing) _get.Content = "Getting…";
    }

    private async Task GetCurrentAsync()
    {
        if (CurrentItem() is not { } item) return;
        if (_source.GetAsync is not null) await GetAsync(item);
        else if (_source.DownloadScript is not null) await RequestPageDownloadAsync();
    }

    public async Task<string> ScriptAsync(string script)
    {
        if (_web.CoreWebView2 is null) return "";
        try
        {
            var json = await _web.CoreWebView2.ExecuteScriptAsync(script);
            return System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "";
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or COMException) { return ""; }
    }

    /// <summary>The Get button of a site that only has its own Download button: presses it, then waits for the page to start the download.</summary>
    private async Task RequestPageDownloadAsync()
    {
        if (_busy || _source.DownloadScript is null) return;
        _busy = true;
        RefreshUi();
        _status.Text = "Asking the site for the file… If it asks you to verify you're human, do that in the window.";
        var wanted = _downloadWanted = new TaskCompletionSource<bool>();
        try
        {
            if (await ScriptAsync(_source.DownloadScript) == "no-button")
            {
                _status.Text = "This page has no Download button.";
                return;
            }
            // CaptureDownload completes the wait when the page starts a download; the rest of the work follows from there.
            var started = await Task.WhenAny(wanted.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            if (started != wanted.Task) _status.Text = "The site didn't start a download. Try its own Download button.";
        }
        finally
        {
            _downloadWanted = null;
            if (_status.Text.StartsWith("Asking", StringComparison.Ordinal)) _status.Text = "";
            _busy = _capturing;
            RefreshUi();
        }
    }

    private bool _capturing;

    /// <summary>
    /// A download the page starts on an item page (by the Get button or the site's own button): saved to a temporary file, with
    /// no download UI, and added through <see cref="BrowseSource.ImportDownloadAsync"/>. Returns false to leave it to the
    /// default handling (cancelled).
    /// </summary>
    private bool CaptureDownload(CoreWebView2DownloadStartingEventArgs e)
    {
        var op = e.DownloadOperation;
        if (CurrentItem() is not { } item || _capturing || !Uri.TryCreate(op.Uri, UriKind.Absolute, out var uri) || _source.AllowDownload?.Invoke(uri) != true) return false;
        var file = Path.Combine(Path.GetTempPath(), $"hanami-download-{Guid.NewGuid():N}.zip");
        e.ResultFilePath = file;
        e.Handled = true;   // no download bubble: the file goes to the temporary path above
        _capturing = true;
        _busy = true;
        _downloadWanted?.TrySetResult(true);
        RefreshUi();
        _status.Text = "Downloading…";

        void Cleanup() { try { File.Delete(file); } catch { } _capturing = false; _busy = false; RefreshUi(); }
        op.BytesReceivedChanged += (_, _) =>
        {
            long total = op.TotalBytesToReceive is { } t ? (long)t : 0, received = (long)op.BytesReceived;
            if (received > _source.MaxDownloadBytes || total > _source.MaxDownloadBytes)
            {
                op.Cancel();
                return;
            }
            _status.Text = total > 0 ? $"Downloading… {(int)(received * 100 / total)}%" : $"Downloading… {received / 1024} KB";
        };
        op.StateChanged += async (_, _) =>
        {
            if (op.State == CoreWebView2DownloadState.InProgress) return;
            if (op.State != CoreWebView2DownloadState.Completed)
            {
                _status.Text = (long)op.BytesReceived > _source.MaxDownloadBytes ? $"The download is larger than {_source.MaxDownloadBytes / 1024 / 1024} MB, so it was stopped." : "The download didn't finish. Check your connection and try again.";
                Cleanup();
                return;
            }
            try
            {
                _status.Text = await _source.ImportDownloadAsync!(item, file, this, new Progress<string>(t => _status.Text = t));
            }
            catch (Exception ex)
            {
                Log.Warn($"{_source.Title}: {ex.Message}");
                _status.Text = ex.Message;
            }
            finally { Cleanup(); }
        };
        return true;
    }

    /// <summary>A download the site started itself: cancelled, and, when it is one of the source's items, taken like the button would.</summary>
    private async Task HandleSiteDownloadAsync(string uri)
    {
        if (_source.ItemFromDownload is null || _busy) return;
        try
        {
            if (await _source.ItemFromDownload(uri) is { } item) await GetAsync(item);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private async Task GetAsync(string item)
    {
        if (_busy) return;
        _busy = true;
        RefreshUi();
        _status.Text = "Getting…";
        try
        {
            _status.Text = await _source.GetAsync!(item, new Progress<string>(t => _status.Text = t));
        }
        catch (Exception ex)
        {
            Log.Warn($"{_source.Title}: {ex.Message}");
            _status.Text = ex is HttpRequestException or TaskCanceledException ? $"Couldn't reach {_source.Title}: check your connection and try again." : ex.Message;
        }
        finally
        {
            _busy = false;
            RefreshUi();
        }
    }
}
