using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hanami.Core;
using Hanami.Core.Pets;
using Hanami.Native;
using Hanami.Platform;

namespace Hanami.App;

/// <summary>A pack's frames, decoded once and shared by every pet that uses the pack.</summary>
internal sealed class PetFrames
{
    public required Dictionary<int, BitmapSource> Images { get; init; }
    public required HashSet<int> Set { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required PetCapabilities Caps { get; init; }
    public required PetAnimations Anims { get; init; }

    public static PetFrames? Load(PetLibrary library, string packId)
    {
        var manifest = library.Read(packId);
        if (manifest is null) return null;
        var images = new Dictionary<int, BitmapSource>();
        foreach (var n in manifest.Frames)
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                img.UriSource = new Uri(library.FramePath(packId, n));
                img.EndInit();
                img.Freeze();
                images[n] = img;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { }
        }
        if (!images.ContainsKey(1)) return null;
        return new PetFrames { Images = images, Set = images.Keys.ToHashSet(), Width = manifest.FrameWidth, Height = manifest.FrameHeight, Caps = manifest.Animations().CapabilitiesFor(images.Keys.ToList()), Anims = manifest.Animations() };
    }
}

/// <summary>
/// Owns the pets: one <see cref="PetWindow"/> and <see cref="PetBrain"/> per settings item, one timer for all
/// of them (stopped while none is visible), the world they live in (monitors' work areas and, when enabled,
/// application windows) and the rules that hide or freeze them: a fullscreen app on their monitor, the screensaver,
/// a locked session, and each monitor's power mode. Runs on the UI thread.
/// </summary>
public sealed class PetManager : IDisposable
{
    private const double TickMs = 1000.0 / ShimejiFrames.TickRate;
    private static readonly TimeSpan ScanEvery = TimeSpan.FromMilliseconds(500);

    private sealed class Pet
    {
        public required PetItem Item { get; init; }
        public required PetFrames Frames { get; init; }
        public required PetWindow Window { get; init; }
        public PetBrain Brain { get; set; } = null!;
        public double Ps;
        public string Activity = "normal";
        public bool Pressed, Dragging, Hidden;
        public Win32.POINT PressAt;
        public readonly List<(long ms, int x, int y)> Samples = new();
    }

    private readonly PetLibrary _library;
    private readonly Func<AppSettings> _settings;
    private readonly Func<Mode> _mode;
    private readonly Action _openPetSettings;
    private readonly Dictionary<string, Pet> _pets = new();
    private readonly Dictionary<string, PetFrames?> _frames = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly uint _pid = (uint)Environment.ProcessId;
    private readonly Random _rng = new();
    private double _owed;                       // ms of simulation still to run
    private long _lastMs, _lastScanMs, _lastCoverageMs, _lastAreasMs, _tickCount;
    private List<MonitorInfo> _monitors = new();
    private World _world = World.Build([new PxRect(0, 0, 1920, 1040)]);
    private WindowChangeWatch? _watch;
    private HashSet<IntPtr> _fullscreen = new();
    private bool _windowTerrain;
    private bool _screensaverShowing, _locked, _hidden;

    public PetManager(PetLibrary library, Func<AppSettings> settings, Func<Mode> mode, Action openPetSettings)
    {
        _library = library;
        _settings = settings;
        _mode = mode;
        _openPetSettings = openPetSettings;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => Tick();
        RefreshMonitors();
    }

    /// <summary>Hidden for this session only (tray menu, pet menu); not saved.</summary>
    public bool Hidden
    {
        get => _hidden;
        set { _hidden = value; Apply(); }
    }

    public bool ScreensaverShowing { set { _screensaverShowing = value; Apply(); } }
    public bool SessionLocked { set { _locked = value; Apply(); } }
    public bool HasPets => _pets.Count > 0;
    public event Action? Changed;

    private bool ShouldShow => _settings().Pets.Enabled && !_hidden && !_screensaverShowing && !_locked && _pets.Count > 0;

    // ---- keeping up with the settings ------------------------------------------------------------

    /// <summary>Reconciles the pets with the settings (added, removed, resized, shown or hidden) without touching the others.</summary>
    public void Sync()
    {
        var s = _settings().Pets;
        if (!_timer.IsEnabled) RebuildWorld();   // new pets spawn into an up-to-date world
        var wanted = s.Items.ToDictionary(i => i.Id);
        foreach (var id in _pets.Keys.Where(id => !wanted.ContainsKey(id)).ToList()) Remove(id);
        foreach (var item in s.Items)
        {
            if (_pets.TryGetValue(item.Id, out var existing))
            {
                if (existing.Item.PackId != item.PackId) { Remove(item.Id); Add(item); }
                continue;
            }
            Add(item);
        }
        // A pack that was deleted or re-imported must be decoded again.
        foreach (var packId in _frames.Keys.Where(id => !_library.Exists(id)).ToList()) _frames.Remove(packId);
        Apply();
        Changed?.Invoke();
    }

    public void OnDisplayChanged()
    {
        RefreshMonitors();
        RebuildWorld();
    }

    private void RefreshMonitors() => _monitors = Monitors.Enumerate();

    private void Add(PetItem item)
    {
        if (!_frames.TryGetValue(item.PackId, out var frames)) _frames[item.PackId] = frames = PetFrames.Load(_library, item.PackId);
        if (frames is null) { Log.Warn($"Pet pack {item.PackId} could not be loaded"); return; }

        var pet = new Pet { Item = item, Frames = frames, Window = new PetWindow() };
        pet.Window.Pressed += p => OnPressed(pet, p);
        pet.Window.Moved += p => OnMoved(pet, p);
        pet.Window.Released += p => OnReleased(pet, p);
        pet.Window.RightClicked += () => ShowMenu(pet);
        _pets[item.Id] = pet;
        Reset(pet);
    }

    private void Remove(string id)
    {
        if (!_pets.Remove(id, out var pet)) return;
        pet.Window.Close();
    }

    /// <summary>Starts a pet over: a new brain for the current size and monitor, falling from above the primary monitor.</summary>
    private void Reset(Pet pet, double? x = null, double? y = null)
    {
        var settings = _settings().Pets;
        pet.Ps = settings.Scale * ScaleAt(x ?? 0, y ?? 0);
        pet.Activity = settings.Activity;
        pet.Brain = new PetBrain(new PetProfile(pet.Frames.Caps, pet.Frames.Set, pet.Ps, settings.Activity, pet.Frames.Anims), new Random(_rng.Next()));
        var (sx, sy) = x is null ? _world.SpawnPoint(_rng) : (x.Value, y!.Value);
        pet.Brain.Spawn(sx, sy);
        pet.Hidden = false;
    }

    /// <summary>The pet's display scale (monitor DPI) at a point; the primary monitor's when it is over none.</summary>
    private double ScaleAt(double x, double y)
    {
        var m = MonitorAt(x, y);
        return m?.Scale ?? 1.0;
    }

    private MonitorInfo? MonitorAt(double x, double y) =>
        _monitors.FirstOrDefault(m => x >= m.Bounds.Left && x < m.Bounds.Right && y > m.Bounds.Top - 1 && y <= m.Bounds.Bottom)
        ?? _monitors.FirstOrDefault();

    // ---- visibility and the timer ----------------------------------------------------------------

    private void Apply()
    {
        var show = ShouldShow;
        if (show != _timer.IsEnabled)
            Log.Info(show ? $"Pets: showing {_pets.Count}" : $"Pets: hidden (enabled {_settings().Pets.Enabled}, session-hidden {_hidden}, screensaver {_screensaverShowing}, locked {_locked}, pets {_pets.Count})");
        if (show && !_timer.IsEnabled)
        {
            _lastMs = _clock.ElapsedMilliseconds;
            _owed = 0;
            UpdateWatch();
            RebuildWorld();
            _timer.Start();
        }
        else if (!show && _timer.IsEnabled)
        {
            _timer.Stop();
            StopWatch();
            foreach (var p in _pets.Values) p.Window.Conceal();
        }
        else if (show) { UpdateWatch(); RebuildWorld(); }
    }

    private void UpdateWatch()
    {
        _windowTerrain = _settings().Pets.ClimbWindows;
        if (_windowTerrain) _watch ??= new WindowChangeWatch();
        else StopWatch();
    }

    private void StopWatch()
    {
        _watch?.Dispose();
        _watch = null;
    }

    private void RebuildWorld()
    {
        var areas = WindowScan.WorkAreas(_monitors);
        var windows = _windowTerrain ? WindowScan.Read(_monitors, _pid) : null;
        _world = World.Build(areas, windows);
        _lastScanMs = _clock.ElapsedMilliseconds;
        if (_watch is not null) _watch.Changed = false;
    }

    private void Tick()
    {
        var now = _clock.ElapsedMilliseconds;
        _owed = Math.Min(_owed + (now - _lastMs), TickMs * 4);   // never try to catch up more than a few ticks
        _lastMs = now;

        // Rescan: when a hook says something moved (at most once per tick), and every 500 ms as a safety net.
        if (_windowTerrain && ((_watch?.Changed ?? false) || now - _lastScanMs >= ScanEvery.TotalMilliseconds)) RebuildWorld();
        if (now - _lastCoverageMs >= 1000) { _lastCoverageMs = now; ReadCoverage(); }
        // The taskbar moving or resizing, Explorer restarting, a monitor coming or going: the work areas change
        // without a message we listen for, so look every couple of seconds and rebuild the world when they did.
        if (now - _lastAreasMs >= 2000)
        {
            _lastAreasMs = now;
            var fresh = Monitors.Enumerate();
            if (!WindowScan.WorkAreas(fresh).SequenceEqual(WindowScan.WorkAreas(_monitors))) { _monitors = fresh; RebuildWorld(); }
        }

        var steps = 0;
        while (_owed >= TickMs)
        {
            _owed -= TickMs;
            steps++;
            foreach (var pet in _pets.Values) StepPet(pet);
        }
        if (steps == 0) return;
        _tickCount += steps;
        foreach (var pet in _pets.Values) Draw(pet);
    }

    private void ReadCoverage()
    {
        _fullscreen = CoverageProbe.Read(_monitors, _pid).Where(kv => kv.Value.Fullscreen).Select(kv => kv.Key).ToHashSet();
    }

    /// <summary>
    /// How hard pets may work: the power rules (manual pause, battery, Energy Saver) without the
    /// maximized/fullscreen rules, since pets live on top of windows. A fullscreen app hides them separately.
    /// </summary>
    private Mode ModeOf(Pet pet) => _mode();

    private void StepPet(Pet pet)
    {
        if (pet.Brain.IsHeld) { pet.Brain.Step(_world); return; }
        if (ModeOf(pet) == Mode.Paused) return;    // sits still, no animation

        var wantScale = _settings().Pets.Scale * ScaleAt(pet.Brain.X, pet.Brain.Y);
        if (Math.Abs(wantScale - pet.Ps) > 0.01 || _settings().Pets.Activity != pet.Activity) { Reset(pet, pet.Brain.X, pet.Brain.Y); return; }
        if (Win32.GetCursorPos(out var cursor)) pet.Brain.SetCursor(cursor.X, cursor.Y);
        pet.Brain.Step(_world);
    }

    private void Draw(Pet pet)
    {
        var b = pet.Brain;
        var mon = MonitorAt(b.X, b.Y);
        pet.Hidden = mon is not null && _fullscreen.Contains(mon.Handle);
        if (pet.Hidden) { pet.Window.Conceal(); return; }
        if (!b.IsHeld && ModeOf(pet) == Mode.Reduced && (_tickCount & 1) == 1) return;   // at most ~12 fps

        var f = pet.Frames;
        var frame = f.Images[b.Frame];
        var flip = b.Mirrored;
        // Default-table anchors are authored for 128 px frames; a pack's own anchors are already in image pixels.
        var (kx, ky) = f.Anims.ImagePixels ? (1.0, 1.0) : (f.Width / 128.0, f.Height / 128.0);
        var ax = b.Pose.AnchorX * kx;
        if (flip) ax = f.Width - ax;
        var ay = b.Pose.AnchorY * ky;
        var x = (int)Math.Round(b.X - ax * pet.Ps);
        var y = (int)Math.Round(b.Y - ay * pet.Ps);
        pet.Window.Present(frame, flip, x, y, (int)Math.Round(f.Width * pet.Ps), (int)Math.Round(f.Height * pet.Ps), b.ReactProgress);
    }

    // ---- mouse -----------------------------------------------------------------------------------

    private void OnPressed(Pet pet, Win32.POINT p)
    {
        if (ModeOf(pet) == Mode.Paused) return;
        pet.Pressed = true;
        pet.Dragging = false;
        pet.PressAt = p;
        pet.Samples.Clear();
        pet.Samples.Add((_clock.ElapsedMilliseconds, p.X, p.Y));
    }

    private void OnMoved(Pet pet, Win32.POINT p)
    {
        if (!pet.Pressed) return;
        var now = _clock.ElapsedMilliseconds;
        if (!pet.Dragging)
        {
            var slop = 4 * pet.Ps;
            if (Math.Abs(p.X - pet.PressAt.X) < slop && Math.Abs(p.Y - pet.PressAt.Y) < slop) return;
            pet.Dragging = true;
            pet.Brain.BeginDrag(p.X, p.Y);
        }
        pet.Brain.DragTo(p.X, p.Y);
        pet.Samples.Add((now, p.X, p.Y));
        pet.Samples.RemoveAll(s => now - s.ms > 120);
        Draw(pet);   // follow the cursor at full rate, not at 25 Hz
    }

    private void OnReleased(Pet pet, Win32.POINT p)
    {
        if (!pet.Pressed) return;
        pet.Pressed = false;
        if (!pet.Dragging) { pet.Brain.Click(); return; }
        pet.Dragging = false;
        // The cursor's velocity over the last ~80 ms, in pixels per simulation tick.
        var now = _clock.ElapsedMilliseconds;
        var recent = pet.Samples.Where(s => now - s.ms <= 80).ToList();
        double vx = 0, vy = 0;
        if (recent.Count >= 2)
        {
            var (first, last) = (recent[0], recent[^1]);
            var dt = Math.Max(1, last.ms - first.ms);
            vx = (last.x - first.x) / dt * TickMs;
            vy = (last.y - first.y) / dt * TickMs;
        }
        pet.Brain.EndDrag(vx, vy);
    }

    /// <summary>"Call here": the pet falls again from above the centre of the primary monitor.</summary>
    public void CallHere(string itemId)
    {
        if (!_pets.TryGetValue(itemId, out var pet)) return;
        var a = _world.WorkAreas[0];
        Reset(pet, a.Left + a.Width / 2.0, a.Top - 160);
        Draw(pet);
    }

    private void ShowMenu(Pet pet)
    {
        var menu = new ContextMenu();
        var sit = new MenuItem { Header = "Sit", IsEnabled = pet.Frames.Caps.Sit };
        sit.Click += (_, _) => pet.Brain.Command(PetCommand.Sit);
        var sleep = new MenuItem { Header = "Sleep", IsEnabled = pet.Frames.Caps.Sleep };
        sleep.Click += (_, _) => pet.Brain.Command(PetCommand.Sleep);
        var hide = new MenuItem { Header = "Hide pets" };
        hide.Click += (_, _) => Hidden = true;
        var settings = new MenuItem { Header = "Pet settings" };
        settings.Click += (_, _) => _openPetSettings();
        menu.Items.Add(sit);
        menu.Items.Add(sleep);
        menu.Items.Add(new Separator());
        menu.Items.Add(hide);
        menu.Items.Add(settings);
        menu.PlacementTarget = pet.Window;
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        _timer.Stop();
        StopWatch();
        foreach (var p in _pets.Values) p.Window.Close();
        _pets.Clear();
    }
}
