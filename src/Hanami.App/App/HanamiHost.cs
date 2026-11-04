using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Hanami.Core;
using Hanami.Core.Pets;
using Hanami.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Hanami.App;

/// <summary>Owns the running app: settings, library, wallpapers, tray and the settings window.</summary>
public sealed class HanamiHost
{
    private readonly AppPaths _paths;
    private readonly bool _openSettingsOnStart;
    private readonly string? _updateFeed;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private SettingsStore _store = null!;
    private Library _library = null!;
    private PetLibrary _petLibrary = null!;
    private CoreWebView2Environment _env = null!;
    private WallpaperManager _wallpapers = null!;
    private ScreensaverManager _screensaver = null!;
    private bool _petsSawScreensaver;
    private LockScreenSync _lockSync = null!;
    private TrayIcon _tray = null!;
    private UpdateService _updates = null!;
    private ThemeWatch _theme = null!;
    private PetManager _pets = null!;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _displayDebounce;
    private bool _quitting;

    public HanamiHost(AppPaths paths, bool openSettingsOnStart, string? updateFeed = null)
    {
        _paths = paths;
        _openSettingsOnStart = openSettingsOnStart;
        _updateFeed = updateFeed;
    }

    public async Task StartAsync()
    {
        Log.Info($"Hanami {AppPaths.Version} starting from {_paths.InstallDir}{(_paths.IsDev ? " (dev)" : "")}");
        _paths.ExtractWebAssets();
        _store = new SettingsStore(_paths.ConfigPath);
        _library = new Library(_paths.LibraryDir) { BuiltinStillDir = _paths.LockScreenDir };
        if (!File.Exists(_paths.ConfigPath)) _store.Save();
        _petLibrary = new PetLibrary(_library);
        _store.PruneLooks(_library.Exists);
        _store.PrunePets(_petLibrary.Exists);
        SyncStartupRegistration();
        if (!_paths.IsDev)
        {
            // Start menu shortcut and the Apps & features entry (with its Uninstall action); idempotent.
            try { Registration.RegisterApp(_paths.ExePath, AppPaths.Version); }
            catch (Exception ex) { Log.Warn($"App registration failed: {ex.Message}"); }
        }

        var browserArgs = "--autoplay-policy=no-user-gesture-required";
        // Dev only: lets tools/devtools scripts drive the pages over the Chrome DevTools Protocol.
        if (_paths.IsDev && Environment.GetEnvironmentVariable("HANAMI_DEBUG_PORT") is { Length: > 0 } port)
            browserArgs += $" --remote-debugging-port={port}";
        var options = new CoreWebView2EnvironmentOptions(browserArgs);
        _env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir, options);

        _theme = new ThemeWatch();
        _theme.Changed += () => _ui.BeginInvoke(OnThemeChanged);
        _tray = new TrayIcon(OpenSettings, () => OpenSettingsTab("general"), () => _ = SetPausedAsync(!_store.Current.Paused), TogglePets, SetThemeMode, Quit);
        _wallpapers = new WallpaperManager(_env, _paths, () => _store.Current, IsDark, _library);
        _wallpapers.StatusChanged += OnStatusChanged;
        await _wallpapers.StartAsync();
        _screensaver = new ScreensaverManager(_env, _paths, () => _store.Current, IsDark, _library,
            () => _wallpapers.Power, () => _wallpapers.SessionLocked);
        _screensaver.Changed += OnScreensaverChanged;
        _lockSync = new LockScreenSync(_paths, () => _store.Current, IsDark, _library, new Platform.LockScreenImage());
        _lockSync.Changed += BroadcastState;
        _screensaver.Lock = _lockSync;
        _ = _lockSync.WarmCurtainAsync();
        _lockSync.RequestRefresh();
        _screensaver.Start();
        _pets = new PetManager(_petLibrary, () => _store.Current, () => ModeResolver.Resolve(_store.Current, _wallpapers.Power, default, _wallpapers.SessionLocked).Mode, () => OpenSettingsTab("pets"));
        _pets.Changed += OnStatusChanged;
        _pets.Sync();
        _updates = new UpdateService(_paths, _store, _updateFeed);
        _updates.Changed += OnUpdateChanged;
        _updates.Announce += offer => _tray.NotifyUpdate(offer.Version);
        _updates.Start();
        OnStatusChanged();

        SystemEvents.DisplaySettingsChanged += (_, _) => _ui.BeginInvoke(ScheduleRebuild);
        SystemEvents.SessionSwitch += (_, e) => _ui.BeginInvoke(() => OnSessionSwitch(e.Reason));
        SystemEvents.PowerModeChanged += (_, e) => _ui.BeginInvoke(() => OnPowerModeChanged(e.Mode));

        if (_openSettingsOnStart) OpenSettings();
    }

    /// <summary>The active theme: forced by the settings, else Windows' apps theme.</summary>
    private bool IsDark() => _store.Current.Theme.IsDark(_theme.WindowsDark);

    private void OnThemeChanged()
    {
        _wallpapers.PushConfigAll();
        _screensaver.PushConfigAll();
        _lockSync.RequestRefresh();
        OnStatusChanged();
    }

    private void TogglePets()
    {
        _pets.Hidden = !_pets.Hidden;
        OnStatusChanged();
    }

    private void SetThemeMode(string mode)
    {
        _store.Patch(new JsonObject { ["theme"] = new JsonObject { ["mode"] = mode } });
        OnThemeChanged();
    }

    // ---- lifecycle -------------------------------------------------------------------------

    public void OpenSettings() => OpenSettingsTab(null);

    public void OpenSettingsTab(string? tab)
    {
        if (_settingsWindow is { } w)
        {
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            if (tab is not null) w.PostEvent("showTab", tab);
            return;
        }
        _settingsWindow = new SettingsWindow(_env, _paths, DispatchAsync, tab);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _ = QuitAsync();
    }

    private async Task QuitAsync()
    {
        Log.Info("Quitting");
        _settingsWindow?.Close();
        if (_lockSync is not null) await _lockSync.RevertOnExitAsync();   // the lock screen gets its previous picture back
        _pets?.Dispose();
        _theme?.Dispose();
        _updates?.Dispose();
        _lockSync?.Dispose();
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
            _pets.OnDisplayChanged();
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
        _pets.SessionLocked = locked;
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
        // While the screensaver slides away the desktop wallpaper behind it must be live, so it counts as gone for the wallpapers
        // (not for pets, which stay hidden until the slide ends).
        var coversDesktop = _screensaver.Showing && !_screensaver.Revealing;
        if (_wallpapers.ScreensaverShowing != coversDesktop || _petsSawScreensaver != _screensaver.Showing || _wallpapers.IdleFrozen != _screensaver.IdleFrozen)
        {
            _wallpapers.ScreensaverShowing = coversDesktop;
            _petsSawScreensaver = _screensaver.Showing;
            _pets.ScreensaverShowing = _screensaver.Showing;
            _wallpapers.IdleFrozen = _screensaver.IdleFrozen;
            await _wallpapers.ApplyModesAsync();
        }
        BroadcastState();
    }

    private void OnStatusChanged()
    {
        var (_, reason) = SummaryMode();
        _tray.Update(_store.Current.Paused, reason);
        _tray.UpdatePets(_pets?.HasPets ?? false, _pets?.Hidden ?? false);
        _tray.UpdateTheme(_store.Current.Theme.Split, _store.Current.Theme.Mode);
        BroadcastState();
    }

    private void OnUpdateChanged()
    {
        _tray.UpdateAvailable(_updates.Status == "available" ? _updates.Offer?.Version : null);
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
        ["petPacks"] = Json.ToNode(_petLibrary.List()),
        ["screensaver"] = ScreensaverState(),
        ["update"] = _updates?.ToState(),
        ["theme"] = new JsonObject
        {
            ["active"] = IsDark() ? ThemeSettings.Dark : ThemeSettings.Light,
            ["windows"] = _theme.WindowsDark ? ThemeSettings.Dark : ThemeSettings.Light,
        },
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
            ["lockScreen"] = new JsonObject
            {
                ["status"] = _lockSync.Status,
                ["error"] = _lockSync.Error,
                ["updatedAt"] = _lockSync.UpdatedAt?.ToString("o"),
                ["hasBackup"] = _lockSync.HasBackup,
            },
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
                var previousBackground = _store.Current.Screensaver.WallpaperId ?? _store.Current.Assignments["*"].WallpaperId;
                _store.Patch(p as JsonObject ?? new JsonObject());
                _store.EnsureScreensaverLook(previousBackground);
                if (_store.Current.StartWithWindows != before) SyncStartupRegistration();
                await AfterSettingsChangedAsync();
                return BuildState();
            }

            case "setAssignment":
                _store.SetAssignment((string?)p?["monitorKey"] ?? "*", (string?)p?["wallpaperId"], (string?)p?["theme"] == ThemeSettings.Dark);
                await AfterSettingsChangedAsync();
                return BuildState();

            case "setLook":
                _store.SetLook(Required(p, "wallpaperId"), LookPatch(p));
                await AfterSettingsChangedAsync();
                return BuildState();

            case "setEffectTuning":
                _store.SetEffectTuning(Required(p, "preset"), p?["tuning"] as JsonObject,
                    (string?)p?["wallpaperId"], (string?)p?["target"] ?? "desktop");
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

            case "importPet":
                return await ImportPetAsync((string?)p?["source"] ?? "zip");

            case "renamePet":
                _petLibrary.Rename(Required(p, "id"), Required(p, "name"));
                return BuildState();

            case "deletePet":
                _store.RemovePack(Required(p, "id"));
                _petLibrary.Delete(Required(p, "id"));
                await AfterSettingsChangedAsync();
                return BuildState();

            case "addPet":
                _store.AddPet(Required(p, "packId"));
                await AfterSettingsChangedAsync();
                return BuildState();

            case "callPet":
                _pets.CallHere(Required(p, "id"));
                return BuildState();

            case "removePet":
                _store.RemovePet(Required(p, "id"));
                await AfterSettingsChangedAsync();
                return BuildState();

            case "saveThumbnail":
                _library.SaveThumbnail(Required(p, "id"), Required(p, "dataUrl"),
                    (int?)p?["width"], (int?)p?["height"], (double?)p?["durationSec"]);
                return new JsonObject { ["ok"] = true };

            case "saveLockStill":
                _library.SaveLockStill(Required(p, "id"), Required(p, "dataUrl"), (double?)p?["frameSec"], (string?)p?["fit"]);
                _lockSync.RequestRefresh();
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

            case "getFonts":
                return await GetFontsAsync();

            case "importFont":
                return ImportFont();

            case "deleteFont":
                DeleteFont(Required(p, "id"));
                return BuildState();

            case "restoreLockScreen":
                if (!await _lockSync.RestoreAsync()) throw new InvalidOperationException(_lockSync.Error ?? "There is no earlier picture to restore.");
                return BuildState();

            case "openLockScreenSettings":
                LockScreenSync.OpenWindowsSettings();
                return new JsonObject { ["ok"] = true };

            case "previewScreensaver":
                await _screensaver.PreviewAsync();
                return new JsonObject { ["ok"] = true };

            case "uninstall":
                if (_paths.IsDev) throw new InvalidOperationException("Uninstall isn't available in the dev copy.");
                if (!Uninstaller.PromptAndStart(_settingsWindow, _paths.InstallDir)) return new JsonObject { ["cancelled"] = true };
                _ = _ui.BeginInvoke(Quit);   // the helper waits for this copy to exit, then removes it
                return new JsonObject { ["started"] = true };

            case "checkForUpdates":
                await _updates.CheckAsync(manual: true);
                return BuildState();

            case "installUpdate":
                _updates.OpenPage();   // until the download-and-install phase, the release page is the way to update
                return BuildState();

            case "skipUpdate":
                _updates.Skip();
                return BuildState();

            case "openUpdatePage":
                _updates.OpenPage();
                return new JsonObject { ["ok"] = true };

            case "quit":
                _ = _ui.BeginInvoke(Quit);
                return null;

            default:
                throw new InvalidOperationException($"Unknown method: {method}");
        }
    }

    /// <summary>The look part of a setLook request: everything except the wallpaper id.</summary>
    private static JsonObject LookPatch(JsonNode? p)
    {
        var patch = new JsonObject();
        foreach (var key in new[] { "desktop", "screensaver" })
            if (p?[key] is JsonObject part) patch[key] = part.DeepClone();
        return patch;
    }

    private async Task AfterSettingsChangedAsync()
    {
        _pets.Sync();
        _wallpapers.PushConfigAll();
        _screensaver.PushConfigAll();
        _lockSync.RequestRefresh();
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

    /// <summary>Imports Shimeji image sets from a folder or from .zip files; fails with the reasons when none was usable.</summary>
    private async Task<JsonNode> ImportPetAsync(string source)
    {
        Func<PetImportResult>? work = null;
        if (source == "folder")
        {
            var dlg = new OpenFolderDialog { Title = "Choose a folder with a Shimeji image set (shime1.png …)" };
            if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };
            var folder = dlg.FolderName;
            work = () => PetPackReader.ImportFolder(_petLibrary, folder);
        }
        else
        {
            var dlg = new OpenFileDialog { Title = "Import a Shimeji .zip", Multiselect = true, Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*" };
            if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };
            var files = dlg.FileNames;
            work = () =>
            {
                var all = new PetImportResult(new(), new());
                foreach (var f in files)
                {
                    var r = PetPackReader.ImportZip(_petLibrary, f);
                    all.Imported.AddRange(r.Imported);
                    all.Rejected.AddRange(r.Rejected);
                }
                return all;
            };
        }
        var result = await Task.Run(work);
        foreach (var reason in result.Rejected) Log.Warn($"Pet import: {reason}");
        if (result.Imported.Count == 0) throw new InvalidOperationException(string.Join(" ", result.Rejected));
        BroadcastState();
        return new JsonObject
        {
            ["imported"] = Json.ToNode(result.Imported.Select(_petLibrary.Info).ToList()),
            ["rejected"] = Json.ToNode(result.Rejected),
        };
    }

    private async Task DeleteWallpaperAsync(string id)
    {
        // Move anything showing it to a built-in first so the page lets go of the file.
        var patch = new JsonObject();
        foreach (var (key, a) in _store.Current.Assignments)
            if (a.WallpaperId == id) patch[key] = new JsonObject { ["wallpaperId"] = "builtin:twilight" };
            else if (a.DarkWallpaperId == id) patch[key] = new JsonObject { ["darkWallpaperId"] = null };
        if (patch.Count > 0)
        {
            _store.Patch(new JsonObject { ["assignments"] = patch });
            _wallpapers.PushConfigAll();
            await Task.Delay(900);
        }
        if (_store.Current.Screensaver.WallpaperId == id) _store.Patch(new JsonObject { ["screensaver"] = new JsonObject { ["wallpaperId"] = null } });
        _store.ForgetWallpaper(id);
        for (var attempt = 0; ; attempt++)
        {
            try { _library.Delete(id); break; }
            catch (IOException) when (attempt < 4) { await Task.Delay(500); }
            catch (UnauthorizedAccessException) when (attempt < 4) { await Task.Delay(500); }
        }
        BroadcastState();
    }

    private static string[]? _systemFonts;

    private async Task<JsonNode> GetFontsAsync()
    {
        // Enumerating the installed families can take a moment on a big font folder: do it once, off the UI thread.
        _systemFonts ??= await Task.Run(() =>
        {
            var en = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
            return System.Windows.Media.Fonts.SystemFontFamilies
                .Select(f => f.FamilyNames.TryGetValue(en, out var n) ? n : f.Source)
                .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith('@'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        });
        return new JsonObject
        {
            ["bundled"] = new JsonArray(new JsonObject { ["id"] = "", ["name"] = "Tilt Warp", ["src"] = "https://app.hanami/wallpaper/fonts/TiltWarp.ttf" }),
            ["system"] = new JsonArray(_systemFonts.Select(n => (JsonNode?)n).ToArray()),
            ["imported"] = Json.ToNode(new FontLibrary(_library).List()),
        };
    }

    private JsonNode ImportFont()
    {
        var exts = string.Join(";", FontLibrary.Extensions.Select(e => "*" + e));
        var dlg = new OpenFileDialog { Title = "Import a font", Filter = $"Fonts|{exts}" };
        if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };
        var info = new FontLibrary(_library).Import(dlg.FileName);
        return new JsonObject { ["imported"] = Json.ToNode(info) };
    }

    private void DeleteFont(string id)
    {
        var used = $"{FontLibrary.LibraryPrefix}{id}";
        foreach (var (wallpaperId, look) in _store.Current.Looks.ToList())
            if (look.Screensaver.Clock.Font == used)
                _store.SetLook(wallpaperId, new JsonObject { ["screensaver"] = new JsonObject { ["clock"] = new JsonObject { ["font"] = null } } });
        new FontLibrary(_library).Delete(id);
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
        var destination = PackService.NormalizeExportPath(dlg.FileName);

        var progress = ThrottledProgress("export");
        var settings = includeSettings ? _store.Current : null;
        var looks = _store.Current.Looks;
        await Task.Run(() => PackService.ExportAsync(_library, settings, ids, destination, AppPaths.Version, progress, looks));
        return new JsonObject { ["path"] = destination };
    }

    private async Task<JsonNode> ImportPackAsync()
    {
        var dlg = new OpenFileDialog { Title = "Import Hanami pack", Filter = $"Hanami pack (*{PackService.Extension})|*{PackService.Extension}|All files (*.*)|*.*" };
        if (dlg.ShowDialog(_settingsWindow) != true) return new JsonObject { ["cancelled"] = true };

        var progress = ThrottledProgress("importPack");
        var (result, settings) = await Task.Run(() => PackService.ImportAsync(_library, dlg.FileName, progress));
        if (settings is not null)
        {
            // Keep this PC's own run state; take everything else from the pack.
            settings.Paused = _store.Current.Paused;
            settings.Updates = _store.Current.Updates;   // update choices belong to this install
            foreach (var a in settings.Assignments.Values)
            {
                if (a.WallpaperId is { } wid && !_library.Exists(wid)) a.WallpaperId = "builtin:twilight";
                if (a.DarkWallpaperId is { } did && !_library.Exists(did)) a.DarkWallpaperId = null;
            }
            if (settings.Screensaver.DarkWallpaperId is { } sdid && !_library.Exists(sdid)) settings.Screensaver.DarkWallpaperId = null;
            var fonts = new FontLibrary(_library);
            foreach (var look in settings.Looks.Values)
                if (look.Screensaver.Clock.Font is { } font && font.StartsWith(FontLibrary.LibraryPrefix, StringComparison.Ordinal)
                    && !fonts.Exists(font[FontLibrary.LibraryPrefix.Length..]))
                    look.Screensaver.Clock.Font = null;
            _store.Replace(settings);
            _store.PruneLooks(_library.Exists);
            _store.PrunePets(_petLibrary.Exists);
            SyncStartupRegistration();
        }
        // A wallpaper's own look comes with it, unless this PC already has one for it.
        foreach (var (id, look) in result.Looks)
            if (!_store.Current.Looks.ContainsKey(id) && _library.Exists(id))
                _store.SetLook(id, (JsonObject)Json.ToNode(look)!);
        await AfterSettingsChangedAsync();
        return new JsonObject { ["imported"] = result.Wallpapers, ["skipped"] = result.Skipped, ["settings"] = result.Settings, ["pets"] = result.Pets };
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
