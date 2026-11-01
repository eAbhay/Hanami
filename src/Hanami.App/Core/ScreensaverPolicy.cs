namespace Hanami.Core;

public sealed class ScreensaverSettings
{
    public bool Enabled { get; set; } = true;
    public int IdleMinutes { get; set; } = 2;
    /// <summary>null = each monitor shows its own desktop wallpaper and effect.</summary>
    public string? WallpaperId { get; set; }
    /// <summary>Before settings version 3 the screensaver effect lived here; now in each wallpaper's look. Read only to migrate, never written back.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("effectPreset")]
    public string? LegacyEffectPreset { get; set; }
    public bool RequireSignIn { get; set; }
    /// <summary>0 = never stop on its own.</summary>
    public double StopAfterHours { get; set; }

    /// <summary>Before settings version 3 the clock lived here; now in each wallpaper's look. Read only to migrate, never written back.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("clock")]
    public ClockSettings? LegacyClock { get; set; }
}

/// <summary>Where and how the screensaver clock is drawn (a wallpaper's own, as a screensaver background). See docs/CONTRACTS.md.</summary>
public sealed class ClockSettings
{
    public const double DefaultX = 0.5, DefaultY = 0.31;

    /// <summary>The time's centre as a fraction of the monitor.</summary>
    public double X { get; set; } = DefaultX;
    public double Y { get; set; } = DefaultY;
    /// <summary>0.5 – 1.6.</summary>
    public double Size { get; set; } = 1;
    /// <summary>null = the bundled Tilt Warp; "system:&lt;family&gt;"; "library:&lt;file&gt;" (Library/fonts/).</summary>
    public string? Font { get; set; }
    /// <summary>"system" (Segoe UI) or "same" (the clock's font).</summary>
    public string DateFont { get; set; } = "system";
    public bool ShowDate { get; set; } = true;
}

public readonly record struct ScreensaverInputs(
    bool OnBattery, bool SessionLocked, bool FullscreenApp, bool OtherAppKeepsDisplayOn, TimeSpan Idle);

/// <summary>When the idle screensaver may start, and the status line the settings UI shows.</summary>
public static class ScreensaverPolicy
{
    public static (bool Start, string Status) Evaluate(ScreensaverSettings s, ScreensaverInputs i)
    {
        if (!s.Enabled) return (false, "Off");
        if (i.OnBattery) return (false, "Off on battery — Windows' own screen and sleep timers apply");
        if (i.SessionLocked) return (false, "Waiting — Windows is locked");
        if (i.FullscreenApp) return (false, "Waiting — a fullscreen app is open");
        if (i.OtherAppKeepsDisplayOn) return (false, "Waiting — another app is keeping the screen on");
        if (i.Idle >= TimeSpan.FromMinutes(s.IdleMinutes)) return (true, "Showing");
        return (false, $"Starts after {s.IdleMinutes} min idle");
    }

    /// <summary>Whether a showing screensaver must end for a reason other than user input.</summary>
    public static string? EndReason(ScreensaverSettings s, bool onBattery, bool sessionLocked, TimeSpan shownFor, bool preview)
    {
        if (sessionLocked) return "Windows locked";
        if (preview) return null;
        if (!s.Enabled) return "Turned off";
        if (onBattery) return "Unplugged";
        if (s.StopAfterHours > 0 && shownFor >= TimeSpan.FromHours(s.StopAfterHours)) return $"Stopped after {s.StopAfterHours:0.#} h";
        return null;
    }
}
