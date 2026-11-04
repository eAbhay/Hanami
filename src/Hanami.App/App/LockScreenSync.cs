using System.Diagnostics;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hanami.Core;
using Hanami.Platform;

namespace Hanami.App;

/// <summary>
/// "Match the lock screen to the screensaver": makes the lock picture saved ahead of time for the primary monitor's screensaver wallpaper
/// (<see cref="Library.LockStillPath"/>, made by the settings page) the Windows lock screen picture. It photographs nothing and opens no
/// window. Off by default, never restores anything by itself: the first time it writes it keeps a copy of the previous picture, which the
/// user can put back. Needs the lock screen set to Picture in Windows (Spotlight overrides it). Everything Windows-specific goes through
/// <see cref="ILockScreenImage"/>. See docs/CONTRACTS.md, "Lock screen".
/// </summary>
public sealed class LockScreenSync : IDisposable
{
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromSeconds(1.5);

    private readonly AppPaths _paths;
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _dark;
    private readonly Library _library;
    private readonly ILockScreenImage _api;
    private readonly DispatcherTimer _debounce;
    private string? _lastSignature;            // what current.jpg shows; persisted so an app start doesn't rewrite the picture
    private bool _busy, _again;
    private BitmapSource? _systemImage;        // what Windows showed before Hanami, for the curtain when sync is off

    public string Status { get; private set; } = "off";
    public string? Error { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Raised when the status or the picture changed (the host broadcasts state; the curtains swap their image).</summary>
    public event Action? Changed;

    public LockScreenSync(AppPaths paths, Func<AppSettings> settings, Func<bool> dark, Library library, ILockScreenImage api)
    {
        _paths = paths; _settings = settings; _dark = dark; _library = library; _api = api;
        _debounce = new DispatcherTimer { Interval = RefreshDebounce };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshAsync(); };
        Status = _settings().Screensaver.SyncLockScreen ? "idle" : "off";
        try { _lastSignature = File.Exists(SignaturePath) ? File.ReadAllText(SignaturePath) : null; }
        catch (IOException) { }
    }

    private string CurrentPath => Path.Combine(_paths.LockScreenDir, "current.jpg");
    private string PreviousPath => Path.Combine(_paths.LockScreenDir, "previous.jpg");
    private string SignaturePath => Path.Combine(_paths.LockScreenDir, "signature.txt");
    public bool HasBackup => File.Exists(PreviousPath);
    public bool Enabled => _settings().Screensaver.SyncLockScreen;

    private static MonitorInfo? Primary() => Monitors.Enumerate().FirstOrDefault(m => m.IsPrimary);

    /// <summary>The option was turned on, the screensaver wallpaper or the theme changed, or a lock picture was saved: refresh soon (debounced).</summary>
    public void RequestRefresh()
    {
        if (!Enabled)
        {
            _debounce.Stop();
            ForgetSignature();   // turning it on again must write once, whatever Windows shows by then
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
        if (Primary() is not { } monitor) return;

        _busy = true;
        try
        {
            var dark = _dark();
            var id = _settings().ScreensaverWallpaperFor(monitor.Key, dark);
            var still = id is null ? null : _library.LockStillPath(id);
            if (still is null || !File.Exists(still))
            {
                SetStatus("waiting");   // keep the current picture; the settings page is making this wallpaper's
                return;
            }
            var signature = LockImagePolicy.Signature(id, dark, File.GetLastWriteTimeUtc(still).Ticks);
            if (!LockImagePolicy.ShouldWrite(true, _lastSignature, signature))
            {
                if (Status != "ok" && File.Exists(CurrentPath)) { UpdatedAt ??= File.GetLastWriteTime(CurrentPath); SetStatus("ok"); }
                return;
            }
            Directory.CreateDirectory(_paths.LockScreenDir);
            var temp = CurrentPath + ".tmp";
            File.Copy(still, temp, overwrite: true);
            await ApplyAsync(temp, signature);
        }
        catch (Exception ex) { Fail($"Could not use the saved picture: {ex.Message}"); }
        finally
        {
            _busy = false;
            if (_again) { _again = false; _debounce.Start(); }
        }
    }

    private void SetStatus(string status)
    {
        if (Status == status && Error is null) return;
        Status = status;
        Error = null;
        Changed?.Invoke();
    }

    private void ForgetSignature()
    {
        _lastSignature = null;
        try { File.Delete(SignaturePath); } catch (IOException) { }
    }

    // ---- writing the picture ---------------------------------------------------------------------

    private async Task ApplyAsync(string captured, string signature)
    {
        Directory.CreateDirectory(_paths.LockScreenDir);
        if (!HasBackup && !await _api.BackupAsync(PreviousPath)) Log.Info("Lock screen: no previous picture to keep (Windows gave none).");
        File.Move(captured, CurrentPath, overwrite: true);
        if (await _api.SetAsync(CurrentPath) is { } error) { Fail(error); return; }
        _lastSignature = signature;
        try { File.WriteAllText(SignaturePath, signature); } catch (IOException ex) { Log.Warn($"Lock screen: could not save the signature: {ex.Message}"); }
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
        ForgetSignature();
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
