using System.Runtime.InteropServices;

namespace Hanami.Native;

public static partial class Win32
{
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint ES_DISPLAY_REQUIRED = 0x2;
    private const int SystemExecutionState = 16;
    private const int PowerRequestDisplayRequired = 0;
    private const int PowerRequestSystemRequired = 1;
    private const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

    private static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoPowerdownTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SleepSubgroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyTimeout = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct REASON_CONTEXT { public uint Version; public uint Flags; public string SimpleReasonString; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerSetRequest(IntPtr handle, int type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool PowerClearRequest(IntPtr handle, int type);
    [DllImport("powrprof.dll")] private static extern uint CallNtPowerInformation(int level, IntPtr inBuf, uint inLen, out uint outBuf, uint outLen);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr activeScheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool LockWorkStation();

    /// <summary>Tick count (ms, wraps) of the last keyboard/mouse input in this session.</summary>
    public static uint LastInputTick()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref lii) ? lii.dwTime : (uint)Environment.TickCount;
    }

    /// <summary>True when any process (a video player, a call) currently asks Windows to keep the display on.</summary>
    public static bool AnyDisplayRequest() =>
        CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out var state, sizeof(uint)) == 0
        && (state & ES_DISPLAY_REQUIRED) != 0;

    /// <summary>Windows' plugged-in "turn off screen" and "sleep" timeouts in minutes (null = never / unknown).</summary>
    public static (double? displayOff, double? sleep) PluggedInTimeouts()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) != 0) return (null, null);
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
            double? Read(Guid subgroup, Guid setting) =>
                PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out var seconds) == 0 && seconds > 0
                    ? Math.Round(seconds / 60.0, 1)
                    : null;
            return (Read(VideoSubgroup, VideoPowerdownTimeout), Read(SleepSubgroup, StandbyTimeout));
        }
        finally
        {
            LocalFree(schemePtr);
        }
    }

    /// <summary>
    /// A named Windows power request ("Hanami screensaver" in `powercfg /requests`). It only blocks
    /// idle screen-off and idle sleep; lid close, power button and Start → Sleep still work, and
    /// Windows drops it automatically if Hanami exits.
    /// </summary>
    public sealed class KeepAwake : IDisposable
    {
        private readonly IntPtr _handle;
        public bool Active { get; private set; }

        public KeepAwake(string reason)
        {
            var ctx = new REASON_CONTEXT { Version = 0, Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING, SimpleReasonString = reason };
            _handle = PowerCreateRequest(ref ctx);
        }

        public void Set(bool on)
        {
            if (on == Active || _handle == IntPtr.Zero || _handle == new IntPtr(-1)) return;
            if (on)
            {
                PowerSetRequest(_handle, PowerRequestDisplayRequired);
                PowerSetRequest(_handle, PowerRequestSystemRequired);
            }
            else
            {
                PowerClearRequest(_handle, PowerRequestDisplayRequired);
                PowerClearRequest(_handle, PowerRequestSystemRequired);
            }
            Active = on;
        }

        public void Dispose() => Set(false);
    }

    /// <summary>Takes the foreground from another process (allowed only via the input-attach dance).</summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        var fg = GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        var me = GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }
}
