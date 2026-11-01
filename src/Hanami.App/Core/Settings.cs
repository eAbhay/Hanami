using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hanami.Core;

/// <summary>Render level of a wallpaper. Ordered from most to least restrictive.</summary>
public enum Mode { Paused = 0, Reduced = 1, Full = 2 }

public sealed class AppSettings
{
    public bool SameOnAllMonitors { get; set; } = true;

    /// <summary>"*" is the default for every monitor; other keys are per-monitor overrides.</summary>
    public Dictionary<string, Assignment> Assignments { get; set; } = new() { ["*"] = new Assignment() };

    public PowerSettings Power { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public ScreensaverSettings Screensaver { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public bool Paused { get; set; }

    public Assignment AssignmentFor(string monitorKey) =>
        !SameOnAllMonitors && Assignments.TryGetValue(monitorKey, out var a) ? a : Assignments["*"];
}

public sealed class Assignment
{
    public string? WallpaperId { get; set; } = "builtin:twilight";

    /// <summary>Passed through to the web side untouched; see EffectConfig in docs/CONTRACTS.md.</summary>
    public JsonObject Effect { get; set; } = Effects.Default();
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

public static class Effects
{
    public static JsonObject Default() => new()
    {
        ["preset"] = "sakura",
        ["density"] = 1.0,
        ["speed"] = 1.0,
        ["size"] = 1.0,
        ["opacity"] = 1.0,
        ["wind"] = 0.0,
        ["interaction"] = "repel",
        ["interactionStrength"] = 1.0,
        ["interactionRadius"] = 160,
        ["clickBurst"] = true,
    };
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

    public void Save() => Json.WriteFileAtomic(_path, JsonSerializer.Serialize(Current, Json.FileOptions));

    /// <summary>Fills gaps left by older or hand-edited files so the rest of the app can trust the shape.</summary>
    internal static AppSettings Normalize(AppSettings? s)
    {
        s ??= new AppSettings();
        s.Assignments ??= new();
        if (!s.Assignments.ContainsKey("*")) s.Assignments["*"] = new Assignment();
        foreach (var a in s.Assignments.Values)
        {
            var effect = Effects.Default();
            if (a.Effect is not null) Json.DeepMerge(effect, a.Effect);
            a.Effect = effect;
        }
        s.Power ??= new();
        s.Performance ??= new();
        s.Screensaver ??= new();
        s.Screensaver.IdleMinutes = Math.Clamp(s.Screensaver.IdleMinutes, 1, 120);
        s.Screensaver.StopAfterHours = Math.Clamp(s.Screensaver.StopAfterHours, 0, 48);
        s.Power.LowBatteryThreshold = Math.Clamp(s.Power.LowBatteryThreshold, 0, 100);
        s.Performance.FpsCap = Math.Clamp(s.Performance.FpsCap, 5, 240);
        s.Performance.BatteryFpsCap = Math.Clamp(s.Performance.BatteryFpsCap, 5, 240);
        return s;
    }
}
