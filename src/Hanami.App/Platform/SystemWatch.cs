using Hanami.Core;
using Hanami.Native;

namespace Hanami.Platform;

public static class PowerProbe
{
    public static PowerState Read()
    {
        if (!Win32.GetSystemPowerStatus(out var s)) return new PowerState(false, null, false);
        var hasBattery = s.BatteryFlag != 128 && s.BatteryFlag != 255;
        var onBattery = hasBattery && s.ACLineStatus == 0;
        int? pct = hasBattery && s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        return new PowerState(onBattery, pct, (s.SystemStatusFlag & 1) != 0);
    }
}

/// <summary>
/// Works out, per monitor, whether a maximized or fullscreen window is hiding the wallpaper.
/// Polled once a second; enumerating a few hundred top-level windows costs well under a millisecond.
/// </summary>
public static class CoverageProbe
{
    private static readonly HashSet<string> IgnoredClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
        "ApplicationFrameWindow_Ghost", "ForegroundStaging", "MultitaskingViewFrame", "EdgeUiInputTopWndClass",
    ];

    public static Dictionary<IntPtr, Coverage> Read(IReadOnlyList<MonitorInfo> monitors, uint ownProcessId)
    {
        var result = monitors.ToDictionary(m => m.Handle, _ => default(Coverage));
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || Win32.IsCloaked(hwnd)) return true;
            var ex = Win32.GetExStyle(hwnd);
            // Click-through overlays (GPU/game-bar overlays, our own pets later) are fullscreen but see-through.
            if ((ex & Win32.WS_EX_TRANSPARENT) != 0 && (ex & Win32.WS_EX_LAYERED) != 0) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownProcessId) return true;
            if (IgnoredClasses.Contains(Win32.ClassName(hwnd))) return true;

            var mon = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONULL);
            if (mon == IntPtr.Zero || !result.TryGetValue(mon, out var cov)) return true;
            var bounds = monitors.First(m => m.Handle == mon).Bounds;
            var frame = Win32.FrameBounds(hwnd);

            if (frame.Covers(bounds)) cov = cov with { Fullscreen = true };
            else if (Win32.IsZoomed(hwnd)) cov = cov with { Maximized = true };
            result[mon] = cov;
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
