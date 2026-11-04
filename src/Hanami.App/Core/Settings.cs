using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hanami.Core.Pets;

namespace Hanami.Core;

/// <summary>Render level of a wallpaper. Ordered from most to least restrictive.</summary>
public enum Mode { Paused = 0, Reduced = 1, Full = 2 }

public sealed class AppSettings
{
    /// <summary>The settings shape this build writes. Files without the key are version 1.</summary>
    public const int CurrentVersion = 3;

    /// <summary>Bumped by one-time migrations in <see cref="SettingsStore.Normalize"/>.</summary>
    public int SettingsVersion { get; set; } = 1;

    public bool SameOnAllMonitors { get; set; } = true;

    /// <summary>"*" is the default for every monitor; other keys are per-monitor overrides.</summary>
    public Dictionary<string, Assignment> Assignments { get; set; } = new() { ["*"] = new Assignment() };

    /// <summary>
    /// The effect defaults: the user's tuning per effect preset (keys of EffectConfig minus "preset"),
    /// the starting point for wallpapers that haven't tuned that effect themselves. Missing keys fall
    /// back to that preset's defaults, which live only in web/shared/effects.js.
    /// </summary>
    public Dictionary<string, JsonObject> Effects { get; set; } = new();

    /// <summary>Every wallpaper's own look (desktop effect + tuning, screensaver clock + effect), by wallpaper id.</summary>
    public Dictionary<string, LookSettings> Looks { get; set; } = new();

    /// <summary>Optional light and dark wallpapers per slot. Off (<see cref="ThemeSettings.Split"/>) by default.</summary>
    public ThemeSettings Theme { get; set; } = new();

    /// <summary>Desktop pets (Shimeji-style companions).</summary>
    public PetSettings Pets { get; set; } = new();

    public PowerSettings Power { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public ScreensaverSettings Screensaver { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool Paused { get; set; }

    /// <summary>Update checks. Missing in older files, which simply get the defaults.</summary>
    public UpdateSettings Updates { get; set; } = new();

    public Assignment AssignmentFor(string monitorKey) =>
        !SameOnAllMonitors && Assignments.TryGetValue(monitorKey, out var a) ? a : Assignments["*"];

    /// <summary>A wallpaper's look, or the built-in default look when it has none (nothing is stored).</summary>
    public LookSettings LookFor(string? wallpaperId) =>
        wallpaperId is not null && Looks.TryGetValue(wallpaperId, out var look) ? look : new LookSettings();

    /// <summary>
    /// The wallpaper the desktop shows on a monitor. With <see cref="ThemeSettings.Split"/> on and the
    /// <paramref name="dark"/> theme active, the slot's dark wallpaper, falling back to its light one.
    /// </summary>
    public string? DesktopWallpaperFor(string monitorKey, bool dark = false)
    {
        var a = AssignmentFor(monitorKey);
        return Theme.Split && dark && a.DarkWallpaperId is { } d ? d : a.WallpaperId;
    }

    /// <summary>The wallpaper the screensaver shows on a monitor: its own, or the desktop's (for the theme) when unset.</summary>
    public string? ScreensaverWallpaperFor(string monitorKey, bool dark = false) =>
        Screensaver.WallpaperId is { } own
            ? (Theme.Split && dark && Screensaver.DarkWallpaperId is { } d ? d : own)
            : DesktopWallpaperFor(monitorKey, dark);

    /// <summary>
    /// The EffectConfig sent to a page: the preset plus the user's overrides for it (may be partial).
    /// Layers, lowest first: the preset's defaults (on the page) → <see cref="Effects"/> → the look's own tuning.
    /// </summary>
    public JsonObject ComposeEffect(string preset, JsonObject? lookTuning = null)
    {
        var effect = new JsonObject { ["preset"] = preset };
        foreach (var layer in new[] { Effects.GetValueOrDefault(preset), lookTuning })
            if (layer is not null)
                foreach (var (key, value) in layer)
                    if (key != "preset") effect[key] = value?.DeepClone();
        return effect;
    }

    /// <summary>The effect the desktop shows on a monitor.</summary>
    public JsonObject DesktopEffectFor(string monitorKey, bool dark = false)
    {
        var d = LookFor(DesktopWallpaperFor(monitorKey, dark)).Desktop;
        return ComposeEffect(d.EffectPreset, d.Tuning.GetValueOrDefault(d.EffectPreset));
    }

    /// <summary>
    /// The effect the screensaver shows on a monitor: the screensaver background's own screensaver effect,
    /// or (when unset) its desktop effect with the same tuning.
    /// </summary>
    public JsonObject ScreensaverEffectFor(string monitorKey, bool dark = false)
    {
        var look = LookFor(ScreensaverWallpaperFor(monitorKey, dark));
        if (look.Screensaver.EffectPreset is { } own) return ComposeEffect(own, look.Screensaver.Tuning.GetValueOrDefault(own));
        return ComposeEffect(look.Desktop.EffectPreset, look.Desktop.Tuning.GetValueOrDefault(look.Desktop.EffectPreset));
    }

    /// <summary>The clock the screensaver shows on a monitor (its background's own).</summary>
    public ClockSettings ScreensaverClockFor(string monitorKey, bool dark = false) => LookFor(ScreensaverWallpaperFor(monitorKey, dark)).Screensaver.Clock;
}

public sealed class Assignment
{
    public string? WallpaperId { get; set; } = "builtin:twilight";

    /// <summary>Shown instead of <see cref="WallpaperId"/> in dark mode when light/dark wallpapers are on; null = use the light one.</summary>
    public string? DarkWallpaperId { get; set; }

    /// <summary>Before settings version 3 the effect lived here; it moved to <see cref="AppSettings.Looks"/>. Read only to migrate, never written back.</summary>
    [JsonPropertyName("effectPreset")]
    public string? LegacyEffectPreset { get; set; }

    /// <summary>Even older files stored the whole effect here. Read only to migrate, then cleared (never written back).</summary>
    [JsonPropertyName("effect")]
    public JsonObject? LegacyEffect { get; set; }
}

/// <summary>What one wallpaper looks like on the desktop and as a screensaver background. See docs/CONTRACTS.md.</summary>
public sealed class LookSettings
{
    public DesktopLook Desktop { get; set; } = new();
    public ScreensaverLook Screensaver { get; set; } = new();

    public LookSettings Clone() => Json.FromNode<LookSettings>(Json.ToNode(this));
}

public sealed class DesktopLook
{
    public string EffectPreset { get; set; } = EffectPresets.Default;

    /// <summary>This wallpaper's slider tuning, kept per preset so switching effects and back keeps each one's sliders.</summary>
    public Dictionary<string, JsonObject> Tuning { get; set; } = new();
}

public sealed class ScreensaverLook
{
    /// <summary>null = same as the desktop effect, sharing its tuning.</summary>
    public string? EffectPreset { get; set; }
    public Dictionary<string, JsonObject> Tuning { get; set; } = new();
    public ClockSettings Clock { get; set; } = new();
}

/// <summary>Whether wallpaper slots hold a light and a dark wallpaper, and which one is showing.</summary>
public sealed class ThemeSettings
{
    public const string System = "system", Light = "light", Dark = "dark";

    public bool Split { get; set; }

    /// <summary>"system" follows Windows' apps theme; "light" / "dark" force one.</summary>
    public string Mode { get; set; } = System;

    /// <summary>The active theme for a Windows theme: forced by <see cref="Mode"/>, else Windows'.</summary>
    public bool IsDark(bool windowsDark) => Mode switch { Dark => true, Light => false, _ => windowsDark };
}

/// <summary>Desktop pets: which ones are out and how they behave. See docs/CONTRACTS.md.</summary>
public sealed class PetSettings
{
    public const int MaxPets = 8;
    public static readonly string[] Activities = ["calm", "normal", "lively"];

    public bool Enabled { get; set; } = true;
    public List<PetItem> Items { get; set; } = new();
    /// <summary>0.5 – 2.</summary>
    public double Scale { get; set; } = 1;
    public bool ClimbWindows { get; set; } = true;
    /// <summary>Pets greet each other, sit together and follow one another.</summary>
    public bool Socialize { get; set; } = true;
    /// <summary>"calm" | "normal" | "lively".</summary>
    public string Activity { get; set; } = "normal";
}

/// <summary>One pet that is out: its own id (the same pack may be added more than once) and the pack it looks like.</summary>
public sealed class PetItem
{
    public string Id { get; set; } = "";
    public string PackId { get; set; } = "";
}

public sealed class PowerSettings
{
    public Mode OnAC { get; set; } = Mode.Full;
    public Mode OnBattery { get; set; } = Mode.Paused;
    public Mode LowBattery { get; set; } = Mode.Paused;
    public int LowBatteryThreshold { get; set; } = 20;
    public Mode EnergySaver { get; set; } = Mode.Reduced;
    public Mode WhenMaximized { get; set; } = Mode.Paused;
    public Mode WhenFullscreen { get; set; } = Mode.Paused;
}

public sealed class UpdateSettings
{
    /// <summary>Check for updates by itself (about a minute after start, then daily). <c>Check now</c> always works.</summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>"stable" (normal releases only) or "beta" (those plus prereleases).</summary>
    public string Channel { get; set; } = UpdatePolicy.Stable;

    /// <summary>The version the user chose to skip; a newer one is offered again.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>When the last successful check finished.</summary>
    public DateTime? LastCheckUtc { get; set; }

    /// <summary>The version the user was last told about by a tray notification (once per version).</summary>
    public string? NotifiedVersion { get; set; }
    /// <summary>When the notified version's one reminder is due; null once it was shown (or for a version announced before reminders existed).</summary>
    public DateTime? RemindAtUtc { get; set; }
}

public sealed class PerformanceSettings
{
    public int FpsCap { get; set; } = 60;
    public int BatteryFpsCap { get; set; } = 30;
}

public static class EffectPresets
{
    public const string Default = "sakura";

    /// <summary>Must match PRESETS in web/shared/effects.js.</summary>
    public static readonly string[] All = ["none", "sakura", "snow", "leaves", "fireflies", "rain", "sparkles", "embers"];

    public static bool IsValid(string? id) => id is not null && Array.IndexOf(All, id) >= 0;
}

/// <summary>Loads, patches and persists <see cref="AppSettings"/> as config/settings.json.</summary>
public sealed class SettingsStore
{
    private readonly string _path;
    public AppSettings Current { get; private set; }

    public SettingsStore(string path)
    {
        _path = path;
        Current = Load(path);
    }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options));
        }
        catch (Exception ex)
        {
            Log.Warn($"settings.json unreadable, using defaults: {ex.Message}");
            try { File.Copy(path, path + ".broken", overwrite: true); } catch { }
        }
        return Normalize(null);
    }

    /// <summary>Applies a partial settings object (same shape as the JSON file) and saves.</summary>
    public AppSettings Patch(JsonObject patch)
    {
        var node = (JsonObject)Json.ToNode(Current)!;
        Json.DeepMerge(node, patch);
        Current = Normalize(Json.FromNode<AppSettings>(node));
        Save();
        return Current;
    }

    public void Replace(AppSettings settings)
    {
        Current = Normalize(settings);
        Save();
    }

    /// <summary>
    /// Merges <paramref name="tuning"/> into a preset's overrides; null resets that level to its defaults.
    /// Without a wallpaper this edits the effect defaults; with one, that wallpaper's look
    /// (<paramref name="target"/> "desktop" or "screensaver"; a screensaver effect that is "same as
    /// desktop" shares the desktop tuning, so that is what gets edited).
    /// </summary>
    public AppSettings SetEffectTuning(string preset, JsonObject? tuning, string? wallpaperId = null, string target = "desktop")
    {
        if (!EffectPresets.IsValid(preset)) throw new ArgumentException($"Unknown effect '{preset}'");
        var bag = Current.Effects;
        if (wallpaperId is not null)
        {
            var look = GetOrCreateLook(wallpaperId);
            bag = target == "screensaver" && look.Screensaver.EffectPreset is not null ? look.Screensaver.Tuning : look.Desktop.Tuning;
        }
        if (tuning is null) bag.Remove(preset);
        else
        {
            if (!bag.TryGetValue(preset, out var existing)) bag[preset] = existing = new JsonObject();
            Json.DeepMerge(existing, tuning);
        }
        Current = Normalize(Current);
        Save();
        return Current;
    }

    /// <summary>
    /// Shows <paramref name="wallpaperId"/> on a monitor's desktop. A wallpaper without a look yet starts
    /// from a copy of the look of the wallpaper it replaces, so nothing suddenly changes.
    /// </summary>
    public AppSettings SetAssignment(string monitorKey, string? wallpaperId, bool dark = false)
    {
        if (monitorKey != "*" && !Current.Assignments.ContainsKey(monitorKey))
        {
            var shared = Current.Assignments["*"];
            Current.Assignments[monitorKey] = new Assignment { WallpaperId = shared.WallpaperId, DarkWallpaperId = shared.DarkWallpaperId };
        }
        var assignment = Current.Assignments[monitorKey];
        // The light wallpaper of the slot is what a dark one replaces for the purpose of copying a look.
        if (wallpaperId is not null && !Current.Looks.ContainsKey(wallpaperId))
            Current.Looks[wallpaperId] = Current.LookFor(assignment.WallpaperId).Clone();
        if (dark) assignment.DarkWallpaperId = wallpaperId;
        else assignment.WallpaperId = wallpaperId;
        Current = Normalize(Current);
        Save();
        return Current;
    }

    /// <summary>
    /// Call after the screensaver background changed from <paramref name="previousId"/>: a wallpaper
    /// without a look gets the previous background's screensaver look (clock and effect).
    /// </summary>
    public void EnsureScreensaverLook(string? previousId)
    {
        var changed = false;
        if (Current.Screensaver.WallpaperId is { } id && !Current.Looks.ContainsKey(id))
        {
            Current.Looks[id] = new LookSettings { Screensaver = Current.LookFor(previousId).Clone().Screensaver };
            changed = true;
        }
        // A dark screensaver background starts from the look of the light one (or of the desktop's).
        if (Current.Screensaver.DarkWallpaperId is { } dark && !Current.Looks.ContainsKey(dark))
        {
            Current.Looks[dark] = Current.LookFor(Current.Screensaver.WallpaperId ?? Current.Assignments["*"].WallpaperId).Clone();
            changed = true;
        }
        if (!changed) return;
        Current = Normalize(Current);
        Save();
    }

    /// <summary>Applies a partial look (same shape as the JSON; <c>screensaver.effectPreset: null</c> = same as desktop).</summary>
    public AppSettings SetLook(string wallpaperId, JsonObject patch)
    {
        var node = (JsonObject)Json.ToNode(GetOrCreateLook(wallpaperId))!;
        Json.DeepMerge(node, patch);
        // The look is written without nulls, so DeepMerge's explicit null can't clear a preset.
        if (node["screensaver"] is JsonObject ss && ss.ContainsKey("effectPreset") && ss["effectPreset"] is null)
            ss.Remove("effectPreset");
        Current.Looks[wallpaperId] = Json.FromNode<LookSettings>(node);
        Current = Normalize(Current);
        Save();
        return Current;
    }

    /// <summary>Forgets looks of wallpapers that no longer exist (built-ins always exist).</summary>
    public void PruneLooks(Func<string, bool> exists)
    {
        var gone = Current.Looks.Keys.Where(id => !Library.IsBuiltin(id) && !exists(id)).ToList();
        if (gone.Count == 0) return;
        foreach (var id in gone) Current.Looks.Remove(id);
        Save();
    }

    /// <summary>Puts another copy of an imported pet pack on the desktop (at most <see cref="PetSettings.MaxPets"/>).</summary>
    public PetItem AddPet(string packId)
    {
        if (!PetLibrary.IsValidId(packId)) throw new ArgumentException("Unknown pet pack.");
        if (Current.Pets.Items.Count >= PetSettings.MaxPets) throw new InvalidOperationException($"At most {PetSettings.MaxPets} pets can be out at once.");
        var item = new PetItem { Id = NewPetId(), PackId = packId };
        Current.Pets.Items.Add(item);
        Save();
        return item;
    }

    public void RemovePet(string id)
    {
        if (Current.Pets.Items.RemoveAll(p => p.Id == id) > 0) Save();
    }

    /// <summary>A pack was deleted (or never arrived): the pets using it go too.</summary>
    public void RemovePack(string packId)
    {
        if (Current.Pets.Items.RemoveAll(p => p.PackId == packId) > 0) Save();
    }

    /// <summary>Drops pets whose pack is gone.</summary>
    public void PrunePets(Func<string, bool> packExists)
    {
        if (Current.Pets.Items.RemoveAll(p => !packExists(p.PackId)) > 0) Save();
    }

    private static string NewPetId() => "p" + Guid.NewGuid().ToString("N")[..8];

    public void RemoveLook(string wallpaperId)
    {
        if (Current.Looks.Remove(wallpaperId)) Save();
    }

    /// <summary>A wallpaper was deleted: its look goes, and any dark slot using it falls back to the light wallpaper.</summary>
    public void ForgetWallpaper(string wallpaperId)
    {
        foreach (var a in Current.Assignments.Values)
            if (a.DarkWallpaperId == wallpaperId) a.DarkWallpaperId = null;
        if (Current.Screensaver.DarkWallpaperId == wallpaperId) Current.Screensaver.DarkWallpaperId = null;
        Current.Looks.Remove(wallpaperId);
        Save();
    }

    private LookSettings GetOrCreateLook(string wallpaperId)
    {
        if (!Current.Looks.TryGetValue(wallpaperId, out var look)) Current.Looks[wallpaperId] = look = new LookSettings();
        return look;
    }

    public void Save() => Json.WriteFileAtomic(_path, JsonSerializer.Serialize(Current, Json.FileOptions));

    private static ClockSettings NormalizeClock(ClockSettings? c)
    {
        c ??= new ClockSettings();
        c.X = Finite(c.X, ClockSettings.DefaultX, 0, 1);
        c.Y = Finite(c.Y, ClockSettings.DefaultY, 0, 1);
        c.Size = Finite(c.Size, 1, 0.5, 1.6);
        c.Opacity = Finite(c.Opacity, ClockSettings.DefaultOpacity, 0.04, 0.8);
        c.Frost = Finite(c.Frost, ClockSettings.DefaultFrost, 0, 40);
        if (c.Tint is null || !System.Text.RegularExpressions.Regex.IsMatch(c.Tint, "^#[0-9a-fA-F]{6}$")) c.Tint = ClockSettings.DefaultTint;
        if (c.DateFont is not ("system" or "same")) c.DateFont = "system";
        if (!FontLibrary.IsValidSetting(c.Font)) c.Font = null;
        return c;
    }

    private static Dictionary<string, JsonObject> NormalizeTuning(Dictionary<string, JsonObject>? tuning)
    {
        tuning ??= new();
        foreach (var key in tuning.Keys.ToList())
        {
            if (!EffectPresets.IsValid(key) || tuning[key] is null) { tuning.Remove(key); continue; }
            tuning[key].Remove("preset");
        }
        return tuning;
    }

    private static double Finite(double v, double fallback, double lo, double hi) =>
        double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;

    /// <summary>Fills gaps left by older or hand-edited files so the rest of the app can trust the shape.</summary>
    internal static AppSettings Normalize(AppSettings? s)
    {
        s ??= new AppSettings();
        s.Assignments ??= new();
        if (!s.Assignments.ContainsKey("*")) s.Assignments["*"] = new Assignment();
        s.Effects ??= new();
        foreach (var a in s.Assignments.Values)
        {
            if (a.LegacyEffect is { } legacy)
            {
                // Oldest files: keep the preset, and its tuning unless one is already set.
                if (legacy["preset"] is JsonValue pv && pv.TryGetValue<string>(out var lp) && EffectPresets.IsValid(lp))
                {
                    a.LegacyEffectPreset = lp;
                    if (!s.Effects.ContainsKey(lp))
                    {
                        var tuning = (JsonObject)legacy.DeepClone();
                        tuning.Remove("preset");
                        s.Effects[lp] = tuning;
                    }
                }
                a.LegacyEffect = null;
            }
        }
        foreach (var key in s.Effects.Keys.ToList())
        {
            if (!EffectPresets.IsValid(key) || s.Effects[key] is null) { s.Effects.Remove(key); continue; }
            s.Effects[key].Remove("preset");
        }
        if (s.SettingsVersion < 2)
        {
            // v2: click burst became off by default. Older drawers stored whole effect objects, so a
            // stored clickBurst is almost always the old default rather than a choice: drop it.
            foreach (var tuning in s.Effects.Values) tuning.Remove("clickBurst");
        }
        s.Looks ??= new();
        s.Screensaver ??= new();
        s.Theme ??= new();
        NormalizePets(s.Pets ??= new());
        if (s.Theme.Mode is not (ThemeSettings.System or ThemeSettings.Light or ThemeSettings.Dark)) s.Theme.Mode = ThemeSettings.System;
        if (s.SettingsVersion < 3) MigrateToLooks(s);
        foreach (var a in s.Assignments.Values) a.LegacyEffectPreset = null;
        s.Screensaver.LegacyEffectPreset = null;
        s.Screensaver.LegacyClock = null;
        foreach (var id in s.Looks.Keys.ToList())
        {
            var look = s.Looks[id] ??= new LookSettings();
            look.Desktop ??= new();
            look.Screensaver ??= new();
            if (!EffectPresets.IsValid(look.Desktop.EffectPreset)) look.Desktop.EffectPreset = EffectPresets.Default;
            look.Desktop.Tuning = NormalizeTuning(look.Desktop.Tuning);
            if (!EffectPresets.IsValid(look.Screensaver.EffectPreset)) look.Screensaver.EffectPreset = null;
            look.Screensaver.Tuning = NormalizeTuning(look.Screensaver.Tuning);
            look.Screensaver.Clock = NormalizeClock(look.Screensaver.Clock);
        }
        s.SettingsVersion = AppSettings.CurrentVersion;
        s.Power ??= new();
        s.Performance ??= new();
        s.Updates ??= new();
        s.Updates.Channel = UpdatePolicy.NormalizeChannel(s.Updates.Channel);
        if (string.IsNullOrWhiteSpace(s.Updates.SkippedVersion)) s.Updates.SkippedVersion = null;
        if (string.IsNullOrWhiteSpace(s.Updates.NotifiedVersion)) s.Updates.NotifiedVersion = null;
        if (s.Updates.LastCheckUtc is { } last) s.Updates.LastCheckUtc = last.ToUniversalTime();
        if (s.Updates.RemindAtUtc is { } remind) s.Updates.RemindAtUtc = remind.ToUniversalTime();
        s.Screensaver.IdleMinutes = Math.Clamp(s.Screensaver.IdleMinutes, 1, 120);
        s.Screensaver.StopAfterHours = Math.Clamp(s.Screensaver.StopAfterHours, 0, 48);
        s.Power.LowBatteryThreshold = Math.Clamp(s.Power.LowBatteryThreshold, 0, 100);
        s.Performance.FpsCap = Math.Clamp(s.Performance.FpsCap, 5, 240);
        s.Performance.BatteryFpsCap = Math.Clamp(s.Performance.BatteryFpsCap, 5, 240);
        return s;
    }

    private static void NormalizePets(PetSettings p)
    {
        p.Items ??= new();
        var seen = new HashSet<string>();
        p.Items = p.Items.Where(i => i is not null && PetLibrary.IsValidId(i.PackId)).Take(PetSettings.MaxPets).ToList();
        foreach (var item in p.Items)
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 20 || !seen.Add(item.Id))
                do item.Id = NewPetId(); while (!seen.Add(item.Id));
        p.Scale = Finite(p.Scale, 1, 0.5, 2);
        if (Array.IndexOf(PetSettings.Activities, p.Activity) < 0) p.Activity = "normal";
    }

    /// <summary>
    /// v3: the effect (per monitor slot) and the screensaver effect + clock moved into each wallpaper's
    /// look. Nothing visible changes: the global tuning in <see cref="AppSettings.Effects"/> still applies.
    /// </summary>
    private static void MigrateToLooks(AppSettings s)
    {
        foreach (var a in s.Assignments.Values)
        {
            if (a.WallpaperId is not { } w || s.Looks.ContainsKey(w)) continue;
            var preset = EffectPresets.IsValid(a.LegacyEffectPreset) ? a.LegacyEffectPreset! : EffectPresets.Default;
            s.Looks[w] = new LookSettings { Desktop = { EffectPreset = preset } };
        }
        var backgrounds = s.Screensaver.WallpaperId is { } own
            ? [own]
            : s.Assignments.Values.Select(a => a.WallpaperId).OfType<string>().Distinct().ToArray();
        foreach (var w in backgrounds)
        {
            if (!s.Looks.TryGetValue(w, out var look)) s.Looks[w] = look = new LookSettings();
            if (s.Screensaver.LegacyEffectPreset is not null) look.Screensaver.EffectPreset = s.Screensaver.LegacyEffectPreset;
            if (s.Screensaver.LegacyClock is { } clock) look.Screensaver.Clock = Json.FromNode<ClockSettings>(Json.ToNode(clock));
        }
    }
}
