using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Hanami.Core;
using Hanami.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Hanami.App;

/// <summary>Owns the running app: settings, library, wallpapers, tray and the settings window.</summary>
public sealed class HanamiHost
{
    private readonly AppPaths _paths;
    private readonly bool _openSettingsOnStart;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private SettingsStore _store = null!;
    private Library _library = null!;
    private CoreWebView2Environment _env = null!;
    private WallpaperManager _wallpapers = null!;
    private ScreensaverManager _screensaver = null!;
    private TrayIcon _tray = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _displayDebounce;
    private bool _quitting;

    public HanamiHost(AppPaths paths, bool openSettingsOnStart)
    {
        _paths = paths;
        _openSettingsOnStart = openSettingsOnStart;
    }

    public async Task StartAsync()
    {
        Log.Info($"Hanami {AppPaths.Version} starting from {_paths.InstallDir}{(_paths.IsDev ? " (dev)" : "")}");
        _paths.ExtractWebAssets();
        _store = new SettingsStore(_paths.ConfigPath);
        _library = new Library(_paths.LibraryDir);
        if (!File.Exists(_paths.ConfigPath)) _store.Save();
        SyncStartupRegistration();

        var browserArgs = "--autoplay-policy=no-user-gesture-required";
        // Dev only: lets tools/devtools scripts drive the pages over the Chrome DevTools Protocol.
        if (_paths.IsDev && Environment.GetEnvironmentVariable("HANAMI_DEBUG_PORT") is { Length: > 0 } port)
            browserArgs += $" --remote-debugging-port={port}";
        var options = new CoreWebView2EnvironmentOptions(browserArgs);
        _env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir, options);

        _tray = new TrayIcon(OpenSettings, () => _ = SetPausedAsync(!_store.Current.Paused), Quit);
        _wallpapers = new WallpaperManager(_env, _paths, () => _store.Current, _library);
        _wallpapers.StatusChanged += OnStatusChanged;
        await _wallpapers.StartAsync();
        _screensaver = new ScreensaverManager(_env, _paths, () => _store.Current, _library,
            () => _wallpapers.Power, () => _wallpapers.SessionLocked);
        _screensaver.Changed += OnScreensaverChanged;
        _screensaver.Start();
        OnStatusChanged();

        SystemEvents.DisplaySettingsChanged += (_, _) => _ui.BeginInvoke(ScheduleRebuild);
        SystemEvents.SessionSwitch += (_, e) => _ui.BeginInvoke(() => OnSessionSwitch(e.Reason));
        SystemEvents.PowerModeChanged += (_, e) => _ui.BeginInvoke(() => OnPowerModeChanged(e.Mode));

        if (_openSettingsOnStart) OpenSettings();
    }

    // ---- lifecycle -------------------------------------------------------------------------

    public void OpenSettings()
    {
        if (_settingsWindow is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_env, _paths, DispatchAsync);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        Log.Info("Quitting");
        _settingsWindow?.Close();
        _screensaver?.Dispose();
        _wallpapers?.Dispose();
        _tray?.Dispose();
        Application.Current.Shutdown();
    }

    private void ScheduleRebuild()
    {
        _displayDebounce ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) =>
        {
            _displayDebounce!.Stop();
            Log.Info("Display configuration changed");
            await _wallpapers.RebuildAsync(force: true);
            BroadcastState();
        }, _ui);
        _displayDebounce.Stop();
        _displayDebounce.Start();
    }

    private async void OnSessionSwitch(SessionSwitchReason reason)
    {
        var locked = reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => false,
            _ => _wallpapers.SessionLocked,
        };
        if (locked == _wallpapers.SessionLocked) return;
        _wallpapers.SessionLocked = locked;
        if (locked) _screensaver.End("Windows locked");
        await _wallpapers.ApplyModesAsync();
    }

    private void OnPowerModeChanged(PowerModes mode)
    {
        if (mode == PowerModes.Suspend)
        {
            _screensaver.End("going to sleep");
        }
        else if (mode == PowerModes.Resume)
        {
            _screensaver.ResetIdle();
            ScheduleRebuild();
        }
    }

    private async void OnScreensaverChanged()
    {
        if (_wallpapers.ScreensaverShowing != _screensaver.Showing || _wallpapers.IdleFrozen != _screensaver.IdleFrozen)
        {
            _wallpapers.ScreensaverShowing = _screensaver.Showing;
            _wallpapers.IdleFrozen = _screensaver.IdleFrozen;
            await _wallpapers.ApplyModesAsync();
        }
        BroadcastState();
    }

    private void OnStatusChanged()
    {
        var (_, reason) = SummaryMode();
        _tray.Update(_store.Current.Paused, reason);
        BroadcastState();
    }

    private (Mode mode, string reason) SummaryMode()
    {
        var modes = _wallpapers.Modes.Values.ToList();
        if (modes.Count == 0) return (Mode.Paused, "No desktop found");
        var best = modes.MaxBy(m => m.Mode);
        return best.Mode == Mode.Full ? (Mode.Full, "Running") : (best.Mode, $"{(best.Mode == Mode.Reduced ? "Reduced" : "Paused")} — {best.Reason}");
    }

    private void SyncStartupRegistration()
    {
        if (_paths.IsDev) return;
        try { Registration.SetStartWithWindows(_store.Current.StartWithWindows, _paths.ExePath); }
        catch (Exception ex) { Log.Warn($"Startup registration failed: {ex.Message}"); }
    }

    private async Task SetPausedAsync(bool paused)
    {
        _store.Patch(new JsonObject { ["paused"] = paused });
        await _wallpapers.ApplyModesAsync();
        OnStatusChanged();
    }

    // ---- settings bridge -------------------------------------------------------------------

    private void BroadcastState() => _settingsWindow?.PostEvent("stateChanged", BuildState());

    private void Progress(string task, long done, long total, string label) =>
        _settingsWindow?.PostEvent("progress", new JsonObject { ["task"] = task, ["done"] = done, ["total"] = total, ["label"] = label });

    private JsonObject BuildState() => new()
    {
        ["version"] = AppPaths.Version,
        ["installDir"] = _paths.InstallDir,
        ["libraryDir"] = _paths.LibraryDir,
        ["settings"] = Json.ToNode(_store.Current),
        ["monitors"] = Json.ToNode(_wallpapers.CurrentMonitors),
        ["modes"] = new JsonObject(_wallpapers.Modes.Select(kv => KeyValuePair.Create(kv.Key,
            (JsonNode?)new JsonObject { ["mode"] = WallpaperView.ModeName(kv.Value.Mode), ["reason"] = kv.Value.Reason }))),
        ["power"] = new JsonObject
        {
            ["onBattery"] = _wallpapers.Power.OnBattery,
            ["batteryPercent"] = _wallpapers.Power.BatteryPercent,
            ["energySaver"] = _wallpapers.Power.EnergySaver,
        },
        ["wallpapers"] = Json.ToNode(_library.List()),
        ["screensaver"] = ScreensaverState(),
    };

    private JsonObject ScreensaverState()
    {
        var (displayOff, sleep) = Native.Win32.DisplayTimeouts(onBattery: false);
        return new JsonObject
        {
            ["active"] = _screensaver.Showing,
            ["status"] = _screensaver.Showing ? "Showing" : _screensaver.Status,
            ["windowsDisplayOffMinutes"] = displayOff,
            ["windowsSleepMinutes"] = sleep,
        };
    }

    private async Task<JsonNode?> DispatchAsync(string method, JsonNode? p)
    {
        switch (method)
        {
            case "getState":
                return BuildState();

            case "updateSettings":
            {
                var before = _store.Current.StartWithWindows;
                _store.Patch(p as JsonObject ?? new JsonObject());
                if (_store.Current.StartWithWindows != before) SyncStartupRegistration();
                await AfterSettingsChangedAsync();
                return BuildState();
            }

            case "setAssignment":
            {
                var key = (string?)p?["monitorKey"] ?? "*";
                var entry = new JsonObject();
                if (p?["wallpaperId"] is { } wid) entry["wallpaperId"] = wid.DeepClone();
                if (p?["effectPreset"] is { } preset) entry["effectPreset"] = preset.DeepClone();
                if (key != "*" && !_store.Current.Assignments.ContainsKey(key))
                {
                    // First override for this monitor starts from the shared assignment.
                    var seed = (JsonObject)Json.ToNode(_store.Current.Assignments["*"])!;
                    Json.DeepMerge(seed, entry);
                    entry = seed;
                }
                _store.Patch(new JsonObject { ["assignments"] = new JsonObject { [key] = entry } });
                await AfterSettingsChangedAsync();
                return BuildState();
            }

            case "setEffectTuning":
                _store.SetEffectTuning(Required(p, "preset"), p?["tuning"] as JsonObject);
                await AfterSettingsChangedAsync();
                return BuildState();

            case "importMedia":
                return await ImportMediaAsync();

            case "renameWallpaper":
                _library.Rename(Required(p, "id"), Required(p, "name"));
                _wallpapers.PushConfigAll();
                return BuildState();

            case "deleteWallpaper":
                await DeleteWallpaperAsync(Required(p, "id"));
                return BuildState();

            case "saveThumbnail":
                _library.SaveThumbnail(Required(p, "id"), Required(p, "dataUrl"),
                    (int?)p?["width"], (int?)p?["height"], (double?)p?["durationSec"]);
                return new JsonObject { ["ok"] = true };

            case "setPaused":
                await SetPausedAsync((bool?)p?["paused"] ?? false);
                return BuildState();

            case "openLibraryFolder":
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.LibraryDir}\"") { UseShellExecute = true });
                return new JsonObject { ["ok"] = true };

            case "getExportItems":
                return Json.ToNode(PackService.Items(_library));

            case "exportPack":
                return await ExportPackAsync(p);

            case "importPack":
                return await ImportPackAsync();

            case "previewScreensaver":
                await _screensaver.PreviewAsync();
                return new JsonObject { ["ok"] = true };

            case "quit":
                _ = _ui.BeginInvoke(Quit);
                return null;

            default:
                throw new InvalidOperationException($"Unknown method: {method}");
        }
    }

    private async Task AfterSettingsChangedAsync()
    {
        _wallpapers.PushConfigAll();
        await _wallpapers.ApplyModesAsync();
        OnStatusChanged();
    }

    private async Task<JsonNode> ImportMediaAsync()
    {
        var exts = string.Join(";", Library.VideoExtensions.Concat(Library.ImageExtensions).Select(e => "*" + e));
        var dlg = new OpenFileDialog
        {
            Title = "Import wallpapers",
            Multiselect = true,
            Filter = $"Videos and images|{exts}|Videos|{string.Join(";", Library.VideoExtensions.Select(e => "*" + e))}|Images|{string.Join(";", Library.ImageExtensions.Select(e => "*" + e))}",
        };
        if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };

        var imported = new JsonArray();
        var files = dlg.FileNames;
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        long before = 0;
        var lastReport = DateTime.MinValue;
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var progress = new Progress<(long done, long total)>(x =>
            {
                if (DateTime.UtcNow - lastReport < TimeSpan.FromMilliseconds(100) && x.done < x.total) return;
                lastReport = DateTime.UtcNow;
                Progress("import", before + x.done, totalBytes, name);
            });
            var info = await Task.Run(() => _library.ImportAsync(file, progress));
            before += new FileInfo(file).Length;
            imported.Add(Json.ToNode(info));
        }
        Progress("import", totalBytes, totalBytes, "Done");
        BroadcastState();
        return new JsonObject { ["imported"] = imported };
    }

    private async Task DeleteWallpaperAsync(string id)
    {
        // Move anything showing it to a built-in first so the page lets go of the file.
        var patch = new JsonObject();
        foreach (var (key, a) in _store.Current.Assignments)
            if (a.WallpaperId == id) patch[key] = new JsonObject { ["wallpaperId"] = "builtin:twilight" };
        if (patch.Count > 0)
        {
            _store.Patch(new JsonObject { ["assignments"] = patch });
            _wallpapers.PushConfigAll();
            await Task.Delay(900);
        }
        for (var attempt = 0; ; attempt++)
        {
            try { _library.Delete(id); break; }
            catch (IOException) when (attempt < 4) { await Task.Delay(500); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(500); }
        }
        BroadcastState();
    }

    private async Task<JsonNode> ExportPackAsync(JsonNode? p)
    {
        var ids = (p?["ids"] as JsonArray)?.Select(x => (string)x!).ToList() ?? new List<string>();
        var includeSettings = (bool?)p?["includeSettings"] ?? false;
        var dlg = new SaveFileDialog
        {
            Title = "Export Hanami pack",
            FileName = $"Hanami-{DateTime.Now:yyyy-MM-dd}{PackService.Extension}",
            Filter = $"Hanami pack|*{PackService.Extension}",
            DefaultExt = PackService.Extension,
        };
        if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };

        var progress = ThrottledProgress("export");
        var settings = includeSettings ? _store.Current : null;
        await Task.Run(() => PackService.ExportAsync(_library, settings, ids, dlg.FileName, AppPaths.Version, progress));
        return new JsonObject { ["path"] = dlg.FileName };
    }

    private async Task<JsonNode> ImportPackAsync()
    {
        var dlg = new OpenFileDialog { Title = "Import Hanami pack", Filter = $"Hanami pack|*{PackService.Extension}" };
        if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };

        var progress = ThrottledProgress("importPack");
        var (result, settings) = await Task.Run(() => PackService.ImportAsync(_library, dlg.FileName, progress));
        if (settings is not null)
        {
            // Keep this PC's own run state; take everything else from the pack.
            settings.Paused = _store.Current.Paused;
            foreach (var a in settings.Assignments.Values)
                if (a.WallpaperId is { } wid && !_library.Exists(wid)) a.WallpaperId = "builtin:twilight";
            _store.Replace(settings);
            SyncStartupRegistration();
        }
        await AfterSettingsChangedAsync();
        return new JsonObject { ["imported"] = result.Wallpapers, ["skipped"] = result.Skipped, ["settings"] = result.Settings };
    }

    private IProgress<(long done, long total, string label)> ThrottledProgress(string task)
    {
        var last = DateTime.MinValue;
        return new Progress<(long done, long total, string label)>(x =>
        {
            if (DateTime.UtcNow - last < TimeSpan.FromMilliseconds(100) && x.done < x.total) return;
            last = DateTime.UtcNow;
            Progress(task, x.done, x.total, x.label);
        });
    }

    private static string Required(JsonNode? p, string name) =>
        (string?)p?[name] ?? throw new ArgumentException($"Missing parameter: {name}");
}
