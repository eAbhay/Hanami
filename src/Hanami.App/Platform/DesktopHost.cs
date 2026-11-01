using Hanami.Core;
using Hanami.Native;

namespace Hanami.Platform;

/// <summary>
/// Finds the window that sits between the desktop wallpaper and the desktop icons.
///
/// Sending Progman the undocumented 0x052C message makes Explorer create a WorkerW window
/// for animated wallpaper transitions. Where it lives depends on the Windows build:
///  • before 24H2: a top-level WorkerW right after the one holding SHELLDLL_DefView (the icons);
///    children of it draw behind the icons.
///  • 24H2 and later: Progman itself hosts SHELLDLL_DefView and a child WorkerW. Our window
///    becomes a child of Progman placed directly below SHELLDLL_DefView (above that WorkerW,
///    which draws the static wallpaper).
/// </summary>
public sealed class DesktopHost
{
    public enum Layout { Legacy, Raised }

    public IntPtr Parent { get; }
    public IntPtr DefView { get; }
    public Layout Kind { get; }
    /// <summary>Virtual-screen position of Parent's client origin (child coordinates are relative to it).</summary>
    public Win32.POINT Origin { get; }

    private DesktopHost(IntPtr parent, IntPtr defView, Layout kind)
    {
        Parent = parent;
        DefView = defView;
        Kind = kind;
        Win32.GetWindowRect(parent, out var r);
        Origin = new Win32.POINT { X = r.Left, Y = r.Top };
    }

    public bool IsAlive => Win32.IsWindow(Parent) && Win32.IsWindow(DefView);

    public static DesktopHost? Find()
    {
        var progman = Win32.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            Log.Warn("Progman not found (Explorer not running?)");
            return null;
        }

        Win32.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), Win32.SMTO_NORMAL, 1000, out _);
        Win32.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, Win32.SMTO_NORMAL, 1000, out _);

        var defView = Win32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            Log.Info("Desktop layout: raised (24H2+), parenting to Progman");
            return new DesktopHost(progman, defView, Layout.Raised);
        }

        IntPtr workerW = IntPtr.Zero, iconsHost = IntPtr.Zero;
        Win32.EnumWindows((top, _) =>
        {
            var dv = Win32.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero)
            {
                iconsHost = dv;
                workerW = Win32.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        if (workerW == IntPtr.Zero)
        {
            Log.Warn("Legacy WorkerW not found");
            return null;
        }
        Log.Info("Desktop layout: legacy, parenting to WorkerW");
        return new DesktopHost(workerW, iconsHost, Layout.Legacy);
    }

    /// <summary>Makes <paramref name="hwnd"/> a child of the desktop layer covering <paramref name="screenRect"/>.</summary>
    public void Attach(IntPtr hwnd, Win32.RECT screenRect)
    {
        var style = Win32.GetStyle(hwnd);
        style = (style & ~Win32.WS_POPUP) | Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS;
        Win32.SetStyle(hwnd, style);

        Win32.SetExStyle(hwnd, Win32.GetExStyle(hwnd) | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        Win32.SetParent(hwnd, Parent);
        Place(hwnd, screenRect);
    }

    public void Place(IntPtr hwnd, Win32.RECT screenRect)
    {
        var x = screenRect.Left - Origin.X;
        var y = screenRect.Top - Origin.Y;
        // Raised layout: insert below the icons (SetWindowPos puts hwnd *after* DefView in z-order).
        var after = Kind == Layout.Raised ? DefView : IntPtr.Zero;
        var flags = Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW | Win32.SWP_FRAMECHANGED;
        if (after == IntPtr.Zero) flags |= Win32.SWP_NOZORDER;
        Win32.SetWindowPos(hwnd, after, x, y, screenRect.Width, screenRect.Height, flags);
    }

    /// <summary>Asks Explorer to repaint the static wallpaper (clears our last frame after we exit).</summary>
    public static void RefreshStaticWallpaper()
    {
        var sb = new System.Text.StringBuilder(520);
        if (Win32.SystemParametersInfo(Win32.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            Win32.SystemParametersInfo(Win32.SPI_SETDESKWALLPAPER, 0, sb.ToString(), Win32.SPIF_UPDATEINIFILE);
    }

    /// <summary>True when the window under the cursor is part of the desktop (icons view or background).</summary>
    public static bool IsDesktopSurface(IntPtr hwnd, IntPtr ownWindowsRoot)
    {
        for (var h = hwnd; h != IntPtr.Zero; h = Win32.GetParent(h))
        {
            var cls = Win32.ClassName(h);
            if (cls is "SysListView32" or "SHELLDLL_DefView" or "WorkerW" or "Progman") return true;
            if (h == ownWindowsRoot) return true;
        }
        return false;
    }
}
