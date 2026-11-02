using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hanami.Core;
using Hanami.Platform;
using Microsoft.Web.WebView2.Core;

namespace Hanami.App;

/// <summary>
/// "Match the lock screen to the screensaver": photographs the primary monitor's screensaver wallpaper (without clock or
/// particles) and makes it the Windows lock screen picture. Off by default, never restores anything by itself: the first time it
/// writes it keeps a copy of the previous picture, which the user can put back. Needs the lock screen set to Picture in
/// Windows (Spotlight overrides it). Everything Windows-specific goes through <see cref="ILockScreenImage"/>.
/// See docs/CONTRACTS.md, "Lock screen".
/// </summary>
public sealed class LockScreenSync : IDisposable
{
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);

    private readonly CoreWebView2Environment _env;
    private readonly AppPaths _paths;
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _dark;
    private readonly Library _library;
    private readonly ILockScreenImage _api;
    private readonly Func<bool> _blocked;      // the screensaver shows or Windows is locked: no background refresh now
    private readonly DispatcherTimer _debounce;
    private string? _lastSignature;
    private bool _busy, _again;
    private BitmapSource? _systemImage;        // what Windows showed before Hanami, for the curtain when sync is off

    public string Status { get; private set; } = "off";
    public string? Error { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Raised when the status or the picture changed (the host broadcasts state; the curtains swap their image).</summary>
    public event Action? Changed;

    public LockScreenSync(CoreWebView2Environment env, AppPaths paths, Func<AppSettings> settings, Func<bool> dark, Library library,
        ILockScreenImage api, Func<bool> blocked)
    {
        _env = env; _paths = paths; _settings = settings; _dark = dark; _library = library; _api = api; _blocked = blocked;
        _debounce = new DispatcherTimer { Interval = RefreshDebounce };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshAsync(); };
        Status = _settings().Screensaver.SyncLockScreen ? "idle" : "off";
    }

    private string CurrentPath => Path.Combine(_paths.LockScreenDir, "current.jpg");
    private string PreviousPath => Path.Combine(_paths.LockScreenDir, "previous.jpg");
    public bool HasBackup => File.Exists(PreviousPath);
    public bool Enabled => _settings().Screensaver.SyncLockScreen;

    private MonitorInfo? Primary() => Monitors.Enumerate().FirstOrDefault(m => m.IsPrimary);

    private (string Signature, string Type) Describe(MonitorInfo m)
    {
        var s = _settings();
        var dark = _dark();
        var id = s.ScreensaverWallpaperFor(m.Key, dark);
        return (LockImagePolicy.Signature(id, dark), _library.Resolve(id)?.Type ?? "builtin");
    }

    // ---- screensaver start -----------------------------------------------------------------------

    /// <summary>Whether the screensaver about to show must hold its clock and particles back so its first frames can be photographed.</summary>
    public bool WantsCaptureOnStart(MonitorInfo primary)
    {
        if (!Enabled) return false;
        var (sig, type) = Describe(primary);
        return LockImagePolicy.ShouldCapture(true, LockTrigger.ScreensaverStart, _lastSignature, sig, type);
    }

    /// <summary>The primary screensaver page says its plain background is up: photograph it and set it. Never throws.</summary>
    public async Task CaptureFromScreensaverAsync(WallpaperView view)
    {
        try
        {
            var (sig, _) = Describe(view.Monitor);
            var temp = Path.Combine(_paths.LockScreenDir, "capture.jpg");
            await view.CaptureAsync(temp);
            await ApplyAsync(temp, sig);
        }
        catch (Exception ex) { Fail($"Capture failed: {ex.Message}"); }
    }

    // ---- refresh without a screensaver -----------------------------------------------------------

    /// <summary>The option was turned on, or the screensaver wallpaper or the theme changed: refresh the picture soon (debounced).</summary>
    public void RequestRefresh()
    {
        if (!Enabled)
        {
            _debounce.Stop();
            if (Status != "off") { Status = "off"; Error = null; Changed?.Invoke(); }
            return;
        }
        if (Status == "off") { Status = "idle"; Changed?.Invoke(); }
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RefreshAsync()
    {
        if (!Enabled) return;
        if (_busy) { _again = true; return; }
        if (_blocked()) { _debounce.Start(); return; }   // try again later; a screensaver start captures by itself
        if (Primary() is not { } monitor) return;
        var (sig, type) = Describe(monitor);
        if (!LockImagePolicy.ShouldCapture(true, LockTrigger.SettingsChange, _lastSignature, sig, type)) return;

        _busy = true;
        WallpaperView? view = null;
        try
        {
            view = await WallpaperView.CreateCaptureAsync(_env, monitor, _paths);
            var ready = new TaskCompletionSource();
            view.MessageReceived += (_, msg) => { if ((string?)msg["type"] == "readyForCapture") ready.TrySetResult(); };
            var s = _settings();
            var dark = _dark();
            view.SendConfig(_library.Resolve(s.ScreensaverWallpaperFor(monitor.Key, dark)), s.ScreensaverEffectFor(monitor.Key, dark), s.Performance.FpsCap, null, holdForCapture: false);
            await view.SetModeAsync(new ResolvedMode(Mode.Full, "Lock screen capture", false));
            if (await Task.WhenAny(ready.Task, Task.Delay(CaptureTimeout)) != ready.Task) throw new TimeoutException("The wallpaper did not finish loading.");
            var temp = Path.Combine(_paths.LockScreenDir, "capture.jpg");
            await view.CaptureAsync(temp);
            await ApplyAsync(temp, sig);
        }
        catch (Exception ex) { Fail($"Capture failed: {ex.Message}"); }
        finally
        {
            view?.Dispose();
            _busy = false;
            if (_again) { _again = false; _debounce.Start(); }
        }
    }

    // ---- writing the picture ---------------------------------------------------------------------

    private async Task ApplyAsync(string captured, string signature)
    {
        Directory.CreateDirectory(_paths.LockScreenDir);
        if (!HasBackup && !await _api.BackupAsync(PreviousPath)) Log.Info("Lock screen: no previous picture to keep (Windows gave none).");
        File.Move(captured, CurrentPath, overwrite: true);
        if (await _api.SetAsync(CurrentPath) is { } error) { Fail(error); return; }
        _lastSignature = signature;
        Status = "ok";
        Error = null;
        UpdatedAt = DateTime.Now;
        Log.Info("Lock screen picture updated");
        Changed?.Invoke();
    }

    private void Fail(string message)
    {
        Log.Warn($"Lock screen: {message}");
        Status = "failed";
        Error = message;
        Changed?.Invoke();
    }

    /// <summary>Puts the picture the lock screen had before Hanami's first write back. The option should be off first; Hanami does not do this by itself.</summary>
    public async Task<bool> RestoreAsync()
    {
        if (!HasBackup) return false;
        if (await _api.SetAsync(PreviousPath) is { } error) { Fail(error); return false; }
        _lastSignature = null;
        Status = Enabled ? "idle" : "off";
        Error = null;
        Log.Info("Lock screen: previous picture restored");
        Changed?.Invoke();
        return true;
    }

    public static void OpenWindowsSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:lockscreen") { UseShellExecute = true });

    // ---- the curtain's picture -------------------------------------------------------------------

    /// <summary>
    /// What the sign-in curtain shows under the sliding screensaver: Hanami's current picture when sync is on and one exists, else
    /// what Windows' lock screen showed (read once at start), else null (black).
    /// </summary>
    public BitmapSource? CurtainImage() => Enabled && Load(CurrentPath) is { } synced ? synced : _systemImage;

    private static BitmapSource? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(Path.GetFullPath(path));
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Reads what the Windows lock screen shows now into memory, for the curtain when sync is off. Before anything of ours wrote it.</summary>
    public async Task WarmCurtainAsync()
    {
        if (HasBackup) { _systemImage = Load(PreviousPath); return; }
        var temp = Path.Combine(_paths.LockScreenDir, "system.jpg");
        Directory.CreateDirectory(_paths.LockScreenDir);
        if (await _api.BackupAsync(temp)) { _systemImage = Load(temp); File.Delete(temp); }
    }

    public void Dispose() => _debounce.Stop();
}
