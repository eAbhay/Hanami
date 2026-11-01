namespace Hanami.Core;

public readonly record struct PowerState(bool OnBattery, int? BatteryPercent, bool EnergySaver);

public readonly record struct Coverage(bool Maximized, bool Fullscreen);

public readonly record struct ResolvedMode(Mode Mode, string Reason, bool Hidden);

/// <summary>
/// Decides how hard a monitor's wallpaper may work. Every rule proposes a mode and the most
/// restrictive one wins; the reason of the winning rule is shown in the UI.
/// </summary>
public static class ModeResolver
{
    public static ResolvedMode Resolve(AppSettings settings, PowerState power, Coverage coverage, bool sessionLocked,
        bool screensaverShowing = false, bool idleFrozen = false)
    {
        var mode = Mode.Full;
        var reason = "Running";

        void Apply(Mode proposed, string why)
        {
            if (proposed < mode) { mode = proposed; reason = why; }
        }

        // The screensaver covers every monitor and renders its own copy; the desktop one sleeps.
        if (screensaverShowing) return new ResolvedMode(Mode.Paused, "Screensaver showing", Hidden: true);
        if (sessionLocked) Apply(Mode.Paused, "Screen locked");
        if (settings.Paused) Apply(Mode.Paused, "Paused");
        if (coverage.Fullscreen) Apply(settings.Power.WhenFullscreen, "Fullscreen app");
        if (coverage.Maximized) Apply(settings.Power.WhenMaximized, "Maximized window");

        if (power.OnBattery)
        {
            var low = power.BatteryPercent is int pct && pct <= settings.Power.LowBatteryThreshold;
            if (low) Apply(settings.Power.LowBattery, $"Low battery ({power.BatteryPercent}%)");
            else Apply(settings.Power.OnBattery, "On battery");
        }
        else
        {
            Apply(settings.Power.OnAC, "Plugged in");
        }

        if (power.EnergySaver) Apply(settings.Power.EnergySaver, "Energy Saver");

        // Idle: the video freezes (it would hold a display request that blocks the screensaver and
        // Windows' screen-off) but particles keep moving, which takes no power request.
        if (idleFrozen) Apply(Mode.Reduced, "Idle");

        // Only the screensaver hides the wallpaper. A paused wallpaper, covered or not, stays
        // visible with its frozen frame, so Win+D never flashes the stock wallpaper while a
        // suspended page wakes up.
        return new ResolvedMode(mode, reason, Hidden: false);
    }

    public static int FpsCap(AppSettings settings, PowerState power) =>
        power.OnBattery ? settings.Performance.BatteryFpsCap : settings.Performance.FpsCap;
}
