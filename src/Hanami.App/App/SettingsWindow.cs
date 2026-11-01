using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using Hanami.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hanami.App;

/// <summary>The settings UI: a plain window around web/settings, wired to the JSON bridge.</summary>
public sealed class SettingsWindow : Window
{
    private readonly WebView2 _web = new();
    private readonly Func<string, JsonNode?, Task<JsonNode?>> _dispatch;

    public SettingsWindow(CoreWebView2Environment env, AppPaths paths, Func<string, JsonNode?, Task<JsonNode?>> dispatch, string? tab = null)
    {
        _dispatch = dispatch;
        Title = "Hanami";
        Width = 1040;
        Height = 700;
        MinWidth = 840;
        MinHeight = 580;
        // Visible in the taskbar while open; closing it leaves Hanami running in the tray.
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1a, 0x22));
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hanami.ico"));
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1c, 0x1a, 0x22);
        Content = _web;
        Loaded += async (_, _) =>
        {
            await _web.EnsureCoreWebView2Async(env);
            WallpaperView.ConfigureCore(_web.CoreWebView2, paths);
            _web.CoreWebView2.WebMessageReceived += OnMessage;
            _web.CoreWebView2.Navigate($"{WallpaperView.AppOrigin}/settings/index.html{(tab is null ? "" : "#" + tab)}");
        };
        Closed += (_, _) => _web.Dispose();
    }

    public void PostEvent(string name, JsonNode? data)
    {
        if (_web.CoreWebView2 is null) return;
        _web.CoreWebView2.PostWebMessageAsJson(new JsonObject { ["event"] = name, ["data"] = data?.DeepClone() }.ToJsonString());
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonObject? req;
        try { req = JsonNode.Parse(e.WebMessageAsJson) as JsonObject; }
        catch (JsonException) { return; }
        if (req is null || req["method"] is null) return;

        var id = req["id"]?.DeepClone();
        var method = (string)req["method"]!;
        JsonObject response;
        try
        {
            var result = await _dispatch(method, req["params"]);
            response = new JsonObject { ["id"] = id, ["result"] = result };
        }
        catch (Exception ex)
        {
            Log.Warn($"Bridge {method} failed: {ex.Message}");
            response = new JsonObject { ["id"] = id, ["error"] = ex.Message };
        }
        _web.CoreWebView2?.PostWebMessageAsJson(response.ToJsonString());
    }
}
