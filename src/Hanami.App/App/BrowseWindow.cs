using System.Net.Http;
using System.Diagnostics;
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
    /// <summary>Whether a host may be navigated to inside the window; anything else opens in the default browser.</summary>
    public required Func<string, bool> AllowHost { get; init; }
    /// <summary>The item a page address stands for (a pet's slug), or null when the page isn't an item.</summary>
    public required Func<string, string?> ItemFromUrl { get; init; }
    /// <summary>The item a file download of the site belongs to, or null when it isn't one of its items.</summary>
    public Func<string, Task<string?>>? ItemFromDownload { get; init; }
    /// <summary>Whether the library already has this item (the button then says "Added").</summary>
    public Func<string, bool>? IsAdded { get; init; }
    /// <summary>Downloads and adds an item. Reports short progress text; returns the message to show when done; throws a readable error.</summary>
    public required Func<string, IProgress<string>, Task<string>> GetAsync { get; init; }
}

/// <summary>
/// A window around a real website (the browse window): a toolbar with Back, Forward, Reload, the page title and a primary
/// button, above a WebView2. <b>It shows a third-party site, so it is locked down:</b> it never gets the settings bridge, the
/// app's virtual hosts or the library; it has its own browser profile, apart from Hanami's; it may navigate only to the
/// source's hosts; links elsewhere and new windows open in the default browser; the site's own downloads are cancelled
/// (a download that belongs to an item is handed to the same "get" as the button); and every permission is denied.
/// </summary>
public sealed class BrowseWindow : Window
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
        if (u.Scheme == Uri.UriSchemeHttps) return _source.AllowHost(u.Host);
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
    }

    private async Task GetCurrentAsync()
    {
        if (CurrentItem() is { } item) await GetAsync(item);
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
            _status.Text = await _source.GetAsync(item, new Progress<string>(t => _status.Text = t));
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
