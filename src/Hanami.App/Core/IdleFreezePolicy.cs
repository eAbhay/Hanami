namespace Hanami.Core;

/// <summary>
/// When the desktop wallpaper must freeze because the user is idle. A playing video makes
/// WebView2 hold a display-required request, which would stop both the screensaver and Windows'
/// own screen-off timer, so we let go of it shortly before the earliest idle deadline.
/// </summary>
public static class IdleFreezePolicy
{
    public static readonly TimeSpan Lead = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Idle time after which to freeze: the earlier of the screensaver start (enabled and plugged in)
    /// and Windows' display-off timeout for the current power source, minus <see cref="Lead"/>.
    /// null = no idle deadline, never freeze.
    /// </summary>
    public static TimeSpan? FreezeAfter(ScreensaverSettings s, bool onBattery, double? displayOffMinutes)
    {
        TimeSpan? deadline = null;
        if (s.Enabled && !onBattery) deadline = TimeSpan.FromMinutes(s.IdleMinutes);
        if (displayOffMinutes is double m && m > 0)
        {
            var windows = TimeSpan.FromMinutes(m);
            if (deadline is null || windows < deadline) deadline = windows;
        }
        if (deadline is not TimeSpan d) return null;
        var after = d - Lead;
        return after < TimeSpan.Zero ? TimeSpan.Zero : after;
    }
}
