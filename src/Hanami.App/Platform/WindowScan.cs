using Hanami.Core.Pets;
using Hanami.Native;

namespace Hanami.Platform;

/// <summary>
/// The application windows pets can stand on, in z-order (topmost first), and a hook that says when they changed.
/// A window counts when it is visible, not minimized, not cloaked, has a title and is not a tool window, an
/// overlay or part of the shell, belongs to another process, and is at least 120×60 px. Bounds are the visible
/// frame (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c>), without the invisible resize border.
/// </summary>
public static class WindowScan
{
    public const int MinWidth = 120, MinHeight = 60;

    public static List<WorldWindow> Read(IReadOnlyList<MonitorInfo> monitors, uint ownProcessId)
    {
        var result = new List<WorldWindow>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd) || Win32.IsCloaked(hwnd)) return true;
            var ex = Win32.GetExStyle(hwnd);
            if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return true;
            if ((ex & Win32.WS_EX_TRANSPARENT) != 0 && (ex & Win32.WS_EX_LAYERED) != 0) return true;   // click-through overlays
            if (Win32.GetWindowTextLength(hwnd) == 0) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownProcessId) return true;
            if (CoverageProbe.IgnoredClasses.Contains(Win32.ClassName(hwnd))) return true;

            var frame = Win32.FrameBounds(hwnd);
            if (frame.Width < MinWidth || frame.Height < MinHeight) return true;

            // A window covering its whole monitor (fullscreen) or maximized gives no top edge, but still hides what is behind it.
            var mon = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONULL);
            var bounds = monitors.FirstOrDefault(m => m.Handle == mon)?.Bounds;
            var maximized = Win32.IsZoomed(hwnd) || (bounds is { } b && frame.Covers(b));
            result.Add(new WorldWindow(hwnd.ToInt64(), new PxRect(frame.Left, frame.Top, frame.Right, frame.Bottom), maximized));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static List<PxRect> WorkAreas(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.Select(m => new PxRect(m.WorkArea.Left, m.WorkArea.Top, m.WorkArea.Right, m.WorkArea.Bottom)).ToList();
}

/// <summary>
/// Out-of-context WinEvent hooks for window moves, size changes, minimizing, closing and z-order changes.
/// The callback only sets a flag (<see cref="Changed"/>); the pet timer rescans at most once per tick.
/// Events of our own process (the pets themselves) are skipped by the system.
/// </summary>
public sealed class WindowChangeWatch : IDisposable
{
    private readonly Win32.WinEventProc _proc;   // kept alive for the hooks
    private readonly List<IntPtr> _hooks = new();

    /// <summary>Set by the hooks, cleared by the reader.</summary>
    public volatile bool Changed;
    /// <summary>True between the start and end of a user dragging or resizing a window.</summary>
    public bool Moving { get; private set; }

    public WindowChangeWatch()
    {
        _proc = OnEvent;
        const uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _proc, 0, 0, flags));
        _hooks.Add(Win32.SetWinEventHook(Win32.EVENT_OBJECT_DESTROY, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0, flags));
    }

    private void OnEvent(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (type == Win32.EVENT_SYSTEM_MOVESIZESTART) Moving = true;
        else if (type == Win32.EVENT_SYSTEM_MOVESIZEEND) Moving = false;
        // Only window objects (not carets, cursors, menus …).
        if (type >= Win32.EVENT_OBJECT_DESTROY && (idObject != Win32.OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)) return;
        Changed = true;
    }

    public void Dispose()
    {
        foreach (var h in _hooks) if (h != IntPtr.Zero) Win32.UnhookWinEvent(h);
        _hooks.Clear();
    }
}
