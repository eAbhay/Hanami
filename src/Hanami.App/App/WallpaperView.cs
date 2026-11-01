using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Interop;
using Hanami.Core;
using Hanami.Native;
using Hanami.Platform;
using Microsoft.Web.WebView2.Core;

namespace Hanami.App;

/// <summary>One monitor's wallpaper: a child window behind the desktop icons hosting a WebView2.</summary>
public sealed class WallpaperView : IDisposable
{
    public const string AppOrigin = "https://app.hanami";

    private readonly HwndSource _window;
    private CoreWebView2Controller? _controller;
    private bool _ready;
    private bool _hidden;
    private Mode _mode = Mode.Full;
    private JsonObject? _pendingConfig;
    private bool _disposed;

    public MonitorInfo Monitor { get; private set; }
    public IntPtr Hwnd => _window.Handle;
    public Mode Mode => _mode;
    public bool PointerInside { get; set; }

    public event Action<WallpaperView>? Ready;
    public event Action<WallpaperView, JsonObject>? MessageReceived;

    private WallpaperView(MonitorInfo monitor, HwndSource window)
    {
        Monitor = monitor;
        _window = window;
    }

    public static async Task<WallpaperView> CreateAsync(CoreWebView2Environment env, DesktopHost desktop, MonitorInfo monitor, AppPaths paths)
    {
        var p = new HwndSourceParameters($"Hanami wallpaper {monitor.Key}")
        {
            WindowStyle = unchecked((int)(Win32.WS_POPUP | Win32.WS_CLIPCHILDREN)),
            ExtendedWindowStyle = unchecked((int)(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE)),
            PositionX = monitor.Bounds.Left,
            PositionY = monitor.Bounds.Top,
            Width = monitor.Bounds.Width,
            Height = monitor.Bounds.Height,
        };
        var source = new HwndSource(p);
        var view = new WallpaperView(monitor, source);
        desktop.Attach(source.Handle, monitor.Bounds);

        var controller = await env.CreateCoreWebView2ControllerAsync(source.Handle);
        view._controller = controller;
        controller.DefaultBackgroundColor = System.Drawing.Color.Black;
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.RasterizationScale = monitor.Scale;
        controller.Bounds = new System.Drawing.Rectangle(0, 0, monitor.Bounds.Width, monitor.Bounds.Height);

        var core = controller.CoreWebView2;
        ConfigureCore(core, paths);
        core.WebMessageReceived += view.OnWebMessage;
        core.ProcessFailed += (_, e) => Log.Warn($"Wallpaper {monitor.Key} web process failed: {e.ProcessFailedKind}");
        core.Navigate($"{AppOrigin}/wallpaper/index.html?monitor={Uri.EscapeDataString(monitor.Key)}");
        return view;
    }

    /// <summary>Shared WebView2 setup for wallpaper and settings pages.</summary>
    public static void ConfigureCore(CoreWebView2 core, AppPaths paths)
    {
        core.SetVirtualHostNameToFolderMapping("app.hanami", paths.WebDir, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping("library.hanami", paths.LibraryDir, CoreWebView2HostResourceAccessKind.Allow);
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = paths.IsDev;
        s.AreDevToolsEnabled = paths.IsDev;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = paths.IsDev;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
    }

    public void Reposition(DesktopHost desktop, MonitorInfo monitor)
    {
        Monitor = monitor;
        desktop.Place(Hwnd, monitor.Bounds);
        if (_controller is null) return;
        _controller.RasterizationScale = monitor.Scale;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, monitor.Bounds.Width, monitor.Bounds.Height);
    }

    public void SendConfig(WallpaperRef? wallpaper, JsonObject effect, int fpsCap)
    {
        var msg = new JsonObject
        {
            ["type"] = "config",
            ["wallpaper"] = Json.ToNode(wallpaper),
            ["effect"] = effect.DeepClone(),
            ["fpsCap"] = fpsCap,
            ["mode"] = ModeName(_mode),
        };
        if (_ready) Post(msg); else _pendingConfig = msg;
    }

    public async Task SetModeAsync(ResolvedMode resolved)
    {
        if (_controller is null || _disposed) return;
        if (resolved.Hidden != _hidden)
        {
            _hidden = resolved.Hidden;
            if (_hidden)
            {
                Post(new JsonObject { ["type"] = "mode", ["mode"] = "paused" });
                _controller.IsVisible = false;
                try { await _controller.CoreWebView2.TrySuspendAsync(); } catch (Exception ex) { Log.Warn($"Suspend failed: {ex.Message}"); }
            }
            else
            {
                if (_controller.CoreWebView2.IsSuspended) _controller.CoreWebView2.Resume();
                _controller.IsVisible = true;
                _mode = (Mode)(-1);   // force the mode message below
            }
        }
        if (resolved.Mode != _mode)
        {
            _mode = resolved.Mode;
            if (!_hidden) Post(new JsonObject { ["type"] = "mode", ["mode"] = ModeName(_mode) });
        }
    }

    public bool IsLive => !_hidden && _mode != Mode.Paused;

    public void Post(JsonObject msg)
    {
        if (_disposed || _controller is null || !_ready) return;
        if (_hidden && (string?)msg["type"] is "pointer" or "click") return;
        try { _controller.CoreWebView2.PostWebMessageAsJson(msg.ToJsonString()); }
        catch (Exception ex) { Log.Warn($"Post to wallpaper failed: {ex.Message}"); }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonObject? msg;
        try { msg = JsonNode.Parse(e.WebMessageAsJson) as JsonObject; }
        catch (JsonException) { return; }
        if (msg is null) return;

        if ((string?)msg["type"] == "ready")
        {
            _ready = true;
            if (_pendingConfig is not null)
            {
                _pendingConfig["mode"] = ModeName(_hidden ? Mode.Paused : _mode);
                Post(_pendingConfig);
                _pendingConfig = null;
            }
            Ready?.Invoke(this);
            return;
        }
        MessageReceived?.Invoke(this, msg);
    }

    public static string ModeName(Mode m) => m switch { Mode.Full => "full", Mode.Reduced => "reduced", _ => "paused" };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _controller?.Close(); } catch { }
        _window.Dispose();
    }
}
