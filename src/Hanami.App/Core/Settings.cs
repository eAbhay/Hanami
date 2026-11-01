using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Hanami.Core;

/// <summary>Render level of a wallpaper. Ordered from most to least restrictive.</summary>
public enum Mode { Paused = 0, Reduced = 1, Full = 2 }

public sealed class AppSettings
{
    public bool SameOnAllMonitors { get; set; } = true;

    /// <summary>"*" is the default for every monitor; other keys are per-monitor overrides.</summary>
    public Dictionary<string, Assignment> Assignments { get; set; } = new() { ["*"] = new Assignment() };

    /// <summary>
    /// The user's tuning overrides per effect preset (keys of EffectConfig minus "preset"). Missing
    /// keys fall back to that preset's defaults, which live only in web/shared/effects.js.
    /// </summary>
    public Dictionary<string, JsonObject> Effects { get; set; } = new();

    public PowerSettings Power { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public ScreensaverSettings Screensaver { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool Paused { get; set; }

    public Assignment AssignmentFor(string monitorKey) =>
        !SameOnAllMonitors && Assignments.TryGetValue(monitorKey, out var a) ? a : Assignments["*"];

    /// <summary>The EffectConfig sent to a page: the preset plus the user's overrides for it (may be partial).</summary>
    public JsonObject ComposeEffect(string preset)
    {
        var effect = new JsonObject { ["preset"] = preset };
        if (Effects.TryGetValue(preset, out var tuning))
            foreach (var (key, value) in tuning)
                if (key != "preset") effect[key] = value?.DeepClone();
        return effect;
    }

    /// <summary>The effect the desktop shows on a monitor.</summary>
    public JsonObject DesktopEffectFor(string monitorKey) => ComposeEffect(AssignmentFor(monitorKey).EffectPreset);

    /// <summary>The effect the screensaver shows on a monitor: its own preset, or the desktop's when unset.</summary>
    public JsonObject ScreensaverEffectFor(string monitorKey) =>
        ComposeEffect(Screensaver.EffectPreset ?? AssignmentFor(monitorKey).EffectPreset);
}

public sealed class Assignment
{
    public string? WallpaperId { get; set; } = "builtin:twilight";

    /// <summary>Which effect shows here; its settings come from <see cref="AppSettings.Effects"/>.</summary>
    public string EffectPreset { get; set; } = EffectPresets.Default;

    /// <summary>Older files stored the whole effect here. Read only to migrate, then cleared (never written back).</summary>
    [JsonPropertyName("effect")]
    public JsonObject? LegacyEffect { get; set; }
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

    /// <summary>Merges <paramref name="tuning"/> into a preset's overrides; null resets the preset to its defaults.</summary>
    public AppSettings SetEffectTuning(string preset, JsonObject? tuning)
    {
        if (!EffectPresets.IsValid(preset)) throw new ArgumentException($"Unknown effect '{preset}'");
        if (tuning is null) Current.Effects.Remove(preset);
        else
        {
            if (!Current.Effects.TryGetValue(preset, out var existing)) Current.Effects[preset] = existing = new JsonObject();
            Json.DeepMerge(existing, tuning);
        }
        Current = Normalize(Current);
        Save();
        return Current;
    }

    public void Save() => Json.WriteFileAtomic(_path, JsonSerializer.Serialize(Current, Json.FileOptions));

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
                // Older files: keep the preset, and its tuning unless one is already set.
                if (legacy["preset"] is JsonValue pv && pv.TryGetValue<string>(out var lp) && EffectPresets.IsValid(lp))
                {
                    a.EffectPreset = lp;
                    if (!s.Effects.ContainsKey(lp))
                    {
                        var tuning = (JsonObject)legacy.DeepClone();
                        tuning.Remove("preset");
                        s.Effects[lp] = tuning;
                    }
                }
                a.LegacyEffect = null;
            }
            if (!EffectPresets.IsValid(a.EffectPreset)) a.EffectPreset = EffectPresets.Default;
        }
        foreach (var key in s.Effects.Keys.ToList())
        {
            if (!EffectPresets.IsValid(key) || s.Effects[key] is null) { s.Effects.Remove(key); continue; }
            s.Effects[key].Remove("preset");
        }
        s.Power ??= new();
        s.Performance ??= new();
        s.Screensaver ??= new();
        if (!EffectPresets.IsValid(s.Screensaver.EffectPreset)) s.Screensaver.EffectPreset = null;
        s.Screensaver.IdleMinutes = Math.Clamp(s.Screensaver.IdleMinutes, 1, 120);
        s.Screensaver.StopAfterHours = Math.Clamp(s.Screensaver.StopAfterHours, 0, 48);
        s.Power.LowBatteryThreshold = Math.Clamp(s.Power.LowBatteryThreshold, 0, 100);
        s.Performance.FpsCap = Math.Clamp(s.Performance.FpsCap, 5, 240);
        s.Performance.BatteryFpsCap = Math.Clamp(s.Performance.BatteryFpsCap, 5, 240);
        return s;
    }
}
