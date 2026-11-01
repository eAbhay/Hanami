using System.Windows.Threading;
using Hanami.Core;
using Hanami.Native;
using Hanami.Platform;
using Microsoft.Web.WebView2.Core;

namespace Hanami.App;

/// <summary>
/// The idle screensaver: after N minutes without input (plugged in only) it covers every monitor
/// with a topmost live wallpaper and holds a Windows power request so the screen stays on.
/// Only a key press or mouse click ends it — moving the mouse does not. The page reports those
/// (it has keyboard focus and sits under the cursor); see <see cref="WatchInput"/> for the fallback.
/// Rules live in <see cref="ScreensaverPolicy"/>; this class does the timing and the windows.
/// </summary>
public sealed class ScreensaverManager : IDisposable
{
    // Input in the first moments after showing is the tail of whatever started it (e.g. the Preview click).
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(900);

    private readonly CoreWebView2Environment _env;
    private readonly AppPaths _paths;
    private readonly Func<AppSettings> _settings;
    private readonly Library _library;
    private readonly Func<PowerState> _power;
    private readonly Func<bool> _sessionLocked;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _inputWatch;
    private readonly Win32.KeepAwake _keepAwake = new("Hanami screensaver");
    private readonly uint _pid = (uint)Environment.ProcessId;
    private readonly List<WallpaperView> _views = new();

    private bool _showing, _opening, _preview;
    private DateTime _shownAt;
    private uint _inputBaseline;          // last-input tick the current show (or wait) is measured from
    private Win32.POINT _lastCursor;
    private IntPtr _previousForeground;
    private uint _blockedUntilInputAfter;  // after an automatic end, wait for fresh input before showing again
    private bool _blocked;
    private DateTime _frozenSince;

    // Chromium needs a moment to drop its display request after the video freezes.
    private static readonly TimeSpan FreezeSettle = TimeSpan.FromSeconds(2);

    public bool Showing => _showing;
    /// <summary>
    /// True once idle time passed <see cref="IdleFreezePolicy.FreezeAfter"/>, so the desktop video
    /// has let go of its display request; stays true after an automatic end until real input.
    /// </summary>
    public bool IdleFrozen { get; private set; }
    public string Status { get; private set; } = "";

    /// <summary>Raised when Showing or Status changes.</summary>
    public event Action? Changed;

    public ScreensaverManager(CoreWebView2Environment env, AppPaths paths, Func<AppSettings> settings, Library library,
        Func<PowerState> power, Func<bool> sessionLocked)
    {
        _env = env;
        _paths = paths;
        _settings = settings;
        _library = library;
        _power = power;
        _sessionLocked = sessionLocked;
        _inputBaseline = Win32.LastInputTick();
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += async (_, _) => await TickAsync();
        _inputWatch = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
        _inputWatch.Tick += (_, _) => WatchInput();
    }

    public void Start() => _tick.Start();

    /// <summary>After waking from sleep the last input can be minutes old; count idle time from now.</summary>
    public void ResetIdle()
    {
        _inputBaseline = (uint)Environment.TickCount;
        _blocked = false;
    }

    public Task PreviewAsync() => ShowAsync(preview: true);

    public void End(string why) => Hide(why, byInput: false);

    private async Task TickAsync()
    {
        var s = _settings().Screensaver;
        var power = _power();
        var locked = _sessionLocked();

        if (_showing)
        {
            var reason = ScreensaverPolicy.EndReason(s, power.OnBattery, locked, DateTime.UtcNow - _shownAt, _preview);
            if (reason is not null) Hide(reason, byInput: false);
            else KeepOnTop();
            return;
        }
        if (_opening) return;

        var last = Win32.LastInputTick();
        if (_blocked && last != _blockedUntilInputAfter) _blocked = false;
        if (TickAfter(last, _inputBaseline)) _inputBaseline = last;
        var idle = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - _inputBaseline));

        var freezeAfter = IdleFreezePolicy.FreezeAfter(s, power.OnBattery, Win32.DisplayTimeouts(power.OnBattery).displayOff);
        // After an automatic end Windows' own timers take over; keep the video frozen until input.
        SetFrozen(_blocked || (freezeAfter is TimeSpan f && idle >= f));

        var monitors = Monitors.Enumerate();
        var fullscreen = CoverageProbe.Read(monitors, _pid).Values.Any(c => c.Fullscreen);
        var settling = IdleFrozen && DateTime.UtcNow - _frozenSince < FreezeSettle;
        var inputs = new ScreensaverInputs(power.OnBattery, locked, fullscreen, !settling && Win32.AnyDisplayRequest(), idle);
        var (start, status) = ScreensaverPolicy.Evaluate(s, inputs);
        if (start && (_blocked || settling)) (start, status) = (false, $"Starts after {s.IdleMinutes} min idle");
        SetStatus(start ? "Showing" : status);
        if (start) await ShowAsync(preview: false);
    }

    private async Task ShowAsync(bool preview)
    {
        if (_showing || _opening) return;
        _opening = true;
        try
        {
            _preview = preview;
            _previousForeground = Win32.GetForegroundWindow();
            var s = _settings();
            var fps = s.Performance.FpsCap;
            foreach (var m in Monitors.Enumerate())
            {
                var view = await WallpaperView.CreateScreensaverAsync(_env, m, _paths);
                var wallpaper = _library.Resolve(s.ScreensaverWallpaperFor(m.Key));
                view.SendConfig(wallpaper, s.ScreensaverEffectFor(m.Key), fps, new FontLibrary(_library).ClockMessage(s.ScreensaverClockFor(m.Key)));
                await view.SetModeAsync(new ResolvedMode(Mode.Full, "Screensaver", false));
                view.MessageReceived += OnPageMessage;
                _views.Add(view);
            }
            if (_views.Count == 0) return;

            _keepAwake.Set(true);
            // Focus our window so the key that wakes the screen reaches the page instead of typing
            // into the user's app.
            Win32.ForceForeground(_views[0].Hwnd);
            _views[0].FocusPage();
            Win32.GetCursorPos(out _lastCursor);
            _inputBaseline = Win32.LastInputTick();
            _shownAt = DateTime.UtcNow;
            _showing = true;
            _inputWatch.Start();
            Log.Info($"Screensaver shown{(preview ? " (preview)" : "")} on {_views.Count} monitor(s)");
            SetStatus("Showing");
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Screensaver failed to show", ex);
            DisposeViews();
            _keepAwake.Set(false);
        }
        finally
        {
            _opening = false;
        }
    }

    private void OnPageMessage(WallpaperView view, System.Text.Json.Nodes.JsonObject msg)
    {
        if ((string?)msg["type"] == "fontError")
        {
            Log.Warn($"Clock font {(string?)msg["font"]} failed to load: {(string?)msg["message"]}");
            return;
        }
        if (!_showing || (string?)msg["type"] != "dismiss") return;
        if (DateTime.UtcNow - _shownAt < Grace) return;
        Hide((string?)msg["input"] ?? "input", byInput: true);
    }

    /// <summary>
    /// Fallback for when Windows refused to give us keyboard focus: a key press then goes to the
    /// other app and the page never sees it. Input that didn't move the cursor must have been a
    /// key, click or wheel, so end on that; input that moved the cursor is ignored.
    /// </summary>
    private void WatchInput()
    {
        if (!_showing) return;
        var last = Win32.LastInputTick();
        if (last == _inputBaseline) return;
        _inputBaseline = last;
        Win32.GetCursorPos(out var pt);
        var moved = pt.X != _lastCursor.X || pt.Y != _lastCursor.Y;
        _lastCursor = pt;
        if (moved || DateTime.UtcNow - _shownAt < Grace) return;
        if (_views.Any(v => v.Hwnd == Win32.GetForegroundWindow())) return;   // the page handles it
        Hide("key (unfocused)", byInput: true);
    }

    private void Hide(string why, bool byInput)
    {
        if (!_showing) return;
        _showing = false;
        _inputWatch.Stop();
        var lockIt = byInput && !_preview && _settings().Screensaver.RequireSignIn;
        if (lockIt) Win32.LockWorkStation();   // lock first, so the desktop is never shown unlocked

        DisposeViews();
        _keepAwake.Set(false);
        if (!lockIt && _previousForeground != IntPtr.Zero && Win32.IsWindow(_previousForeground))
            Win32.SetForegroundWindow(_previousForeground);

        var last = Win32.LastInputTick();
        _inputBaseline = byInput ? last : (uint)Environment.TickCount;
        SetFrozen(!byInput);
        // Ended without the user (stop-after limit, unplugged): don't pop straight back up while still idle.
        _blocked = !byInput;
        _blockedUntilInputAfter = last;
        Log.Info($"Screensaver ended: {why}");
        SetStatus("");
        Changed?.Invoke();
    }

    private void KeepOnTop()
    {
        // Something else topmost (a toast, the taskbar) may have come forward; stay above it.
        foreach (var v in _views)
            Win32.SetWindowPos(v.Hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void SetFrozen(bool frozen)
    {
        if (frozen == IdleFrozen) return;
        IdleFrozen = frozen;
        if (frozen) _frozenSince = DateTime.UtcNow;
        Changed?.Invoke();
    }

    private void SetStatus(string status)
    {
        if (status.Length == 0 || status == Status) return;
        Status = status;
        Changed?.Invoke();
    }

    private void DisposeViews()
    {
        foreach (var v in _views) v.Dispose();
        _views.Clear();
    }

    /// <summary>tick a is later than tick b, allowing for the 49-day wrap of the millisecond counter.</summary>
    private static bool TickAfter(uint a, uint b) => unchecked((int)(a - b)) > 0;

    public void Dispose()
    {
        _tick.Stop();
        _inputWatch.Stop();
        DisposeViews();
        _keepAwake.Dispose();
    }
}
