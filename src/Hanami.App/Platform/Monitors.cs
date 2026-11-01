using System.Runtime.InteropServices;
using Hanami.Native;

namespace Hanami.Platform;

public sealed record MonitorInfo(string Key, string Name, bool IsPrimary, int Width, int Height, double Scale)
{
    internal IntPtr Handle { get; init; }
    internal Win32.RECT Bounds { get; init; }
}

public static class Monitors
{
    /// <summary>All active monitors, bounds in physical (virtual-screen) pixels.</summary>
    public static List<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var mi = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (!Win32.GetMonitorInfo(hMon, ref mi)) return true;
            var scale = Win32.GetDpiForMonitor(hMon, Win32.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;
            var (key, name) = Identify(mi.szDevice);
            result.Add(new MonitorInfo(key, name, (mi.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0,
                mi.rcMonitor.Width, mi.rcMonitor.Height, scale)
            {
                Handle = hMon,
                Bounds = mi.rcMonitor,
            });
            return true;
        }, IntPtr.Zero);

        // Same physical monitor model twice → keys would collide; disambiguate by position.
        foreach (var dup in result.GroupBy(m => m.Key).Where(g => g.Count() > 1))
        {
            var i = 0;
            foreach (var m in dup.OrderBy(m => m.Bounds.Left))
            {
                var idx = result.IndexOf(m);
                result[idx] = m with { Key = $"{m.Key}#{++i}" };
            }
        }
        return result.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ToList();
    }

    /// <summary>
    /// A key that stays the same for the same physical monitor across reboots and replugging
    /// (\\.\DISPLAYn numbers do not). Built from the monitor's device-interface id, which embeds
    /// its EDID manufacturer/product code.
    /// </summary>
    private static (string key, string name) Identify(string adapterDevice)
    {
        var dd = new Win32.DISPLAY_DEVICE { cb = Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
        if (Win32.EnumDisplayDevices(adapterDevice, 0, ref dd, Win32.EDD_GET_DEVICE_INTERFACE_NAME) && !string.IsNullOrEmpty(dd.DeviceID))
        {
            // e.g. \\?\DISPLAY#DELA1E4#5&1a2b3c&0&UID4352#{e6f07b5f-…} → "DELA1E4#UID4352"
            var parts = dd.DeviceID.Split('#');
            var product = parts.Length > 1 ? parts[1] : dd.DeviceID;
            var uid = parts.Length > 2 ? parts[2].Split('&').LastOrDefault() ?? "" : "";
            var name = string.IsNullOrWhiteSpace(dd.DeviceString) || dd.DeviceString.StartsWith("Generic", StringComparison.OrdinalIgnoreCase)
                ? FriendlyName(product)
                : dd.DeviceString;
            return ($"{product}#{uid}", name);
        }
        return (adapterDevice, adapterDevice.TrimStart('\\', '.'));
    }

    private static string FriendlyName(string product) =>
        product.Length >= 3 && product[..3] is var mfr && InternalPanels.Contains(mfr)
            ? "Built-in display"
            : $"Display {product}";

    // Common laptop-panel manufacturer codes; good enough for a label.
    private static readonly HashSet<string> InternalPanels = ["BOE", "AUO", "LGD", "SHP", "SDC", "CMN", "IVO", "CSO", "NCP", "TMA"];
}
