using System.Text.Json.Nodes;
using System.Windows.Threading;
using Hanami.Core;
using Hanami.Native;
using Hanami.Platform;
using Microsoft.Web.WebView2.Core;

namespace Hanami.App;

/// <summary>
/// Keeps one <see cref="WallpaperView"/> per monitor attached behind the desktop icons, feeds them
/// cursor/click input, and applies the power/coverage rules. Everything runs on the UI thread.
/// </summary>
public sealed class WallpaperManager : IDisposable
{
    private readonly CoreWebView2Environment _env;
    private readonly AppPaths _paths;
    private readonly Func<AppSettings> _settings;
    private readonly Library _library;
    private readonly Dictionary<string, WallpaperView> _views = new();
    private readonly DispatcherTimer _inputTimer;
    private readonly DispatcherTimer _watchTimer;
    private readonly uint _pid = (uint)Environment.ProcessId;

    private DesktopHost? _desktop;
    private List<MonitorInfo> _monitors = new();
    private bool _rebuilding;
    private DateTime _lastRebuild = DateTime.MinValue;
    private Win32.POINT _lastCursor = new() { X = int.MinValue };
    private bool _leftDown, _rightDown;

    public PowerState Power { get; private set; }
    public bool SessionLocked { get; set; }
    /// <summary>While the screensaver covers the screens, the desktop wallpapers are hidden and suspended.</summary>
    public bool ScreensaverShowing { get; set; }
    public Dictionary<string, ResolvedMode> Modes { get; } = new();
    public IReadOnlyList<MonitorInfo> CurrentMonitors => _monitors;

    /// <summary>Raised when power state or any monitor's effective mode changed (for the UI).</summary>
    public event Action? StatusChanged;
    public event Action<string, JsonObject>? PageMessage;

    public WallpaperManager(CoreWebView2Environment env, AppPaths paths, Func<AppSettings> settings, Library library)
    {
        _env = env;
        _paths = paths;
        _settings = settings;
        _library = library;
        _inputTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _inputTimer.Tick += (_, _) => PumpInput();
        _watchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _watchTimer.Tick += async (_, _) => await WatchAsync();
    }

    public async Task StartAsync()
    {
        Power = PowerProbe.Read();
        await RebuildAsync(force: true);
        _watchTimer.Start();
        _inputTimer.Start();
    }

    /// <summary>Re-finds the desktop window and matches views to the current monitors.</summary>
    public async Task RebuildAsync(bool force = false)
    {
        if (_rebuilding) return;
        if (!force && DateTime.UtcNow - _lastRebuild < TimeSpan.FromSeconds(3)) return;
        _rebuilding = true;
        _lastRebuild = DateTime.UtcNow;
        try
        {
            var desktop = DesktopHost.Find();
            if (desktop is null) return;
            var monitors = Monitors.Enumerate();
            var desktopChanged = _desktop is null || _desktop.Parent != desktop.Parent;

            foreach (var key in _views.Keys.ToList())
            {
                if (desktopChanged || monitors.All(m => m.Key != key))
                {
                    _views[key].Dispose();
                    _views.Remove(key);
                    Modes.Remove(key);
                }
            }

            _desktop = desktop;
            _monitors = monitors;
            foreach (var m in monitors)
            {
                if (_views.TryGetValue(m.Key, out var existing))
                {
                    existing.Reposition(desktop, m);
                    continue;
                }
                try
                {
                    var view = await WallpaperView.CreateAsync(_env, desktop, m, _paths);
                    view.Ready += v => PushConfig(v);
                    view.MessageReceived += (v, msg) => OnPageMessage(v, msg);
                    _views[m.Key] = view;
                    PushConfig(view);
                    Log.Info($"Wallpaper view for {m.Key} ({m.Name}) {m.Width}x{m.Height} @{m.Scale:0.##}x at {m.Bounds}");
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not create wallpaper for {m.Key}", ex);
                }
            }
            await ApplyModesAsync();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    /// <summary>Sends every view its wallpaper/effect/fps (after settings or library changes).</summary>
    public void PushConfigAll()
    {
        foreach (var v in _views.Values) PushConfig(v);
    }

    private void PushConfig(WallpaperView view)
    {
        var s = _settings();
        var a = s.AssignmentFor(view.Monitor.Key);
        var wallpaper = _library.Resolve(a.WallpaperId);
        view.SendConfig(wallpaper, a.Effect, ModeResolver.FpsCap(s, Power));
    }

    public async Task ApplyModesAsync(Dictionary<IntPtr, Coverage>? coverage = null)
    {
        coverage ??= CoverageProbe.Read(_monitors, _pid);
        var s = _settings();
        var changed = false;
        foreach (var view in _views.Values)
        {
            var cov = coverage.GetValueOrDefault(view.Monitor.Handle);
            var resolved = ModeResolver.Resolve(s, Power, cov, SessionLocked, ScreensaverShowing);
            if (!Modes.TryGetValue(view.Monitor.Key, out var prev) || prev != resolved)
            {
                Modes[view.Monitor.Key] = resolved;
                changed = true;
                Log.Info($"{view.Monitor.Key}: {resolved.Mode} ({resolved.Reason}){(resolved.Hidden ? ", hidden" : "")}");
            }
            await view.SetModeAsync(resolved);
        }
        if (changed) StatusChanged?.Invoke();
    }

    private async Task WatchAsync()
    {
        var power = PowerProbe.Read();
        if (power != Power)
        {
            var fpsChanged = power.OnBattery != Power.OnBattery;
            Power = power;
            if (fpsChanged) PushConfigAll();
            StatusChanged?.Invoke();
        }

        // Explorer restarted, or the desktop windows were recreated (e.g. after resume).
        var broken = _desktop is null || !_desktop.IsAlive || _views.Values.Any(v => Win32.GetParent(v.Hwnd) != _desktop.Parent);
        if (broken)
        {
            Log.Warn("Desktop host lost; re-attaching");
            await RebuildAsync();
            return;
        }
        await ApplyModesAsync();
    }

    /// <summary>~60 Hz: forwards the cursor to the monitor it is on, and clicks that land on the desktop.</summary>
    private void PumpInput()
    {
        if (_views.Count == 0 || !_views.Values.Any(v => v.IsLive)) return;
        if (!Win32.GetCursorPos(out var pt)) return;

        var moved = pt.X != _lastCursor.X || pt.Y != _lastCursor.Y;
        _lastCursor = pt;
        if (moved)
        {
            foreach (var view in _views.Values)
            {
                var b = view.Monitor.Bounds;
                var inside = b.Contains(pt);
                if (!inside && !view.PointerInside) continue;
                view.PointerInside = inside;
                view.Post(new JsonObject
                {
                    ["type"] = "pointer",
                    ["x"] = Math.Round((pt.X - b.Left) / view.Monitor.Scale, 1),
                    ["y"] = Math.Round((pt.Y - b.Top) / view.Monitor.Scale, 1),
                    ["inside"] = inside,
                });
            }
        }

        CheckButton(Win32.VK_LBUTTON, ref _leftDown, "left", pt);
        CheckButton(Win32.VK_RBUTTON, ref _rightDown, "right", pt);
    }

    private void CheckButton(int vk, ref bool wasDown, string name, Win32.POINT pt)
    {
        var down = (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
        if (down && !wasDown)
        {
            var view = _views.Values.FirstOrDefault(v => v.Monitor.Bounds.Contains(pt));
            if (view is not null && DesktopHost.IsDesktopSurface(Win32.WindowFromPoint(pt), view.Hwnd))
            {
                var b = view.Monitor.Bounds;
                view.Post(new JsonObject
                {
                    ["type"] = "click",
                    ["x"] = Math.Round((pt.X - b.Left) / view.Monitor.Scale, 1),
                    ["y"] = Math.Round((pt.Y - b.Top) / view.Monitor.Scale, 1),
                    ["button"] = name,
                });
            }
        }
        wasDown = down;
    }

    private void OnPageMessage(WallpaperView view, JsonObject msg)
    {
        if ((string?)msg["type"] == "mediaError")
            Log.Warn($"{view.Monitor.Key}: media error for {msg["wallpaperId"]}: {msg["message"]}");
        PageMessage?.Invoke(view.Monitor.Key, msg);
    }

    public void Dispose()
    {
        _inputTimer.Stop();
        _watchTimer.Stop();
        foreach (var v in _views.Values) v.Dispose();
        _views.Clear();
        DesktopHost.RefreshStaticWallpaper();
    }
}
