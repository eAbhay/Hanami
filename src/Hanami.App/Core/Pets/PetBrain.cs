namespace Hanami.Core.Pets;

public enum PetState
{
    Stand, Walk, Run, Sit, Lie, Sleep, Fall, Land, Dragged, Thrown, React, GrabWall, ClimbWall, GrabCeiling, ClimbCeiling, Chase,
    Jump, Seek, Perch, Meet, FollowPet, Stroked,
}

public enum PetCommand { Sit, Sleep }

/// <summary>What is drawn over a pet: hearts rising (a click, a stroke, a meeting) or a "!" (its window went).</summary>
public enum PetMark { None, Hearts, Startled }

/// <summary>What two pets do once they have greeted each other: sit together, one follows the other, or walk off apart.</summary>
public enum MeetKind { None, Sit, Follow, Part }

public readonly record struct MeetPlan(MeetKind Kind, int Ticks);

/// <summary>
/// How a pet behaves: from the pack (what its frames allow) and the settings. <paramref name="BodyHeight"/> is the pet's
/// height in physical px (0 = 128 px at the pixel scale); it decides what is "in the way" and how far a jump reaches.
/// </summary>
public sealed record PetProfile(PetCapabilities Caps, IReadOnlySet<int> Frames, double PixelScale, string Activity = "normal",
    PetAnimations? Animations = null, double BodyHeight = 0);

/// <summary>
/// One pet's state machine and physics, in physical screen pixels, stepped at a fixed 25 Hz
/// (<see cref="ShimejiFrames.TickRate"/>) so frame durations are exact. Pure: the window only draws
/// <see cref="Pose"/> at <see cref="X"/>, <see cref="Y"/> and feeds in mouse events. Deterministic for a given
/// <see cref="Random"/>. The position is the pet's anchor (its feet on a floor, its hands on a wall or ceiling).
/// </summary>
public sealed class PetBrain
{
    private readonly PetProfile _profile;
    private readonly Random _rng;
    private readonly double _ps;
    private readonly PetAnimations _a;
    private readonly Pose[] _reactPoses;

    private Pose[] _anim = ShimejiFrames.Stand;   // replaced in the constructor
    private int _animIndex, _animTicks;
    private int _stateTicks;
    private Surface? _on;
    private PxRect? _ownerRect;
    private int _calmTicks;
    private int _climbDir = -1;          // -1 up, +1 down
    private int _climbTicks;
    private bool _climbAll;              // the next climb goes all the way (after a jump or a seek)
    private int _holdTicks;              // how long the pet has been held without moving
    private double _lastX, _smoothVx;
    private int _reactTicks;
    private int _dragTicks;              // how long the current drag has lasted
    private bool _dizzy;                 // dragged around for a long time: sprawls after landing
    private double _cursorX, _cursorY;
    private bool _hasCursor;

    // jumping: a scripted arc to a surface
    private Surface? _jumpTarget;
    private double _jumpFromX, _jumpFromY, _jumpToX, _jumpToY, _jumpOffX, _jumpOffY;
    private int _jumpN, _jumpK;

    private Surface? _seekTarget;
    private int _seekTicks;
    private int _perchInward;
    private bool _ownerGone;             // the window it stood on was closed or minimized
    private int _startled;               // ticks left of the "!" mark
    private readonly List<(double dx, double dy)> _shake = new();   // the owner window's last movements
    private int _markTicks;
    private MeetPlan _meetPlan;
    private int _followTicks;            // follow-the-cursor time left
    private bool _strokeSit;

    private const int ShakeWindow = 15;

    public PetState State { get; private set; } = PetState.Fall;
    public double X { get; private set; }
    public double Y { get; private set; }
    /// <summary>−1 = facing left (the way Shimeji frames are drawn), +1 = facing right (draw mirrored).</summary>
    public int Facing { get; private set; } = -1;
    public double Vx { get; private set; }
    public double Vy { get; private set; }
    public Pose Pose => _anim[_animIndex];
    /// <summary>The image to draw: the pose's frame, or shime1 when the pack lacks it.</summary>
    public int Frame => _profile.Frames.Contains(Pose.Frame) ? Pose.Frame : 1;
    public Surface? Surface => _on;
    /// <summary>Whether to draw the frame mirrored: facing right, unless the pose's image faces the other way.</summary>
    public bool Mirrored => (Facing > 0) ^ Pose.Mirror;
    public bool IsHeld => State == PetState.Dragged;
    public double PixelScale => _ps;
    public PetCapabilities Caps => _profile.Caps;
    /// <summary>The pet's height in physical px.</summary>
    public double Body => _profile.BodyHeight > 0 ? _profile.BodyHeight : 128 * _ps;

    /// <summary>0 → 1 while reacting to a click (hearts rise), −1 otherwise.</summary>
    public double ReactProgress => State == PetState.React ? Math.Min(1, _reactTicks / (double)_reactPoses.Sum(p => p.Ticks)) : -1;

    /// <summary>What to draw over the pet now.</summary>
    public PetMark Mark => _startled > 0 ? PetMark.Startled
        : State is PetState.React or PetState.Stroked or PetState.Meet ? PetMark.Hearts : PetMark.None;

    /// <summary>0 → 1 over the life of the mark (looping for a stroke); −1 when there is none.</summary>
    public double MarkProgress => Mark switch
    {
        PetMark.Startled => 1 - _startled / 25.0,
        PetMark.Hearts when State == PetState.React => ReactProgress,
        PetMark.Hearts when State == PetState.Meet => Math.Min(1, _markTicks / 50.0),
        PetMark.Hearts => (_markTicks % 40) / 40.0,
        _ => -1,
    };

    /// <summary>The pet stays where it is (session only): it only stands, sits, lies or sleeps, and holds on to a wall or ceiling.</summary>
    public bool Stay
    {
        get => _stay;
        set { _stay = value; if (value) _followTicks = 0; }
    }
    private bool _stay;

    /// <summary>The pet tracks the cursor along its floor for a minute, or until it is turned off or the pet is clicked.</summary>
    public bool FollowCursor => _followTicks > 0;

    /// <summary>For a pet in <see cref="PetState.FollowPet"/>: where the pet it follows stands now; null once that one has left the surface.</summary>
    public double? PetTargetX { get; set; }

    /// <summary>On a floor and not busy with anything that must not be interrupted: free to meet another pet.</summary>
    public bool IsFree => _on is { Kind: SurfaceKind.Floor } && State is PetState.Stand or PetState.Walk or PetState.Run or PetState.Sit && !Stay && !FollowCursor;

    public PetBrain(PetProfile profile, Random rng)
    {
        _profile = profile;
        _rng = rng;
        _ps = profile.PixelScale;
        _a = profile.Animations ?? PetAnimations.Default;
        var sw = _a.Swing;
        var st = _a.Stand[0];   // held and reacting poses stand where the standing pose does
        Pose At(int frame, int ticks) => new(frame, ticks, st.AnchorX, st.AnchorY, 0, 0, st.Mirror);
        _reactPoses = [At(sw[0], 5), At(sw[1], 5), At(sw[2], 5), At(sw[3], 5), At(sw[4], 5), At(sw[2], 8)];
        _anim = _a.Fall;
        SetAnim(_a.Fall);
    }

    // ---- input from the mouse / app --------------------------------------------------------------

    /// <summary>Where the cursor is (physical px), fed in every tick; pets sometimes walk or run over to it.</summary>
    public void SetCursor(double x, double y)
    {
        _cursorX = x; _cursorY = y; _hasCursor = true;
    }

    /// <summary>Starts a fall from a point (new pets and respawns).</summary>
    public void Spawn(double x, double y)
    {
        X = x; Y = y; Vx = 0; Vy = 0;
        StartFall(PetState.Fall, 0, 0);
    }

    /// <summary>The pet is picked up: the cursor holds its anchor.</summary>
    public void BeginDrag(double cursorX, double cursorY)
    {
        X = _lastX = cursorX; Y = cursorY;
        _on = null; _ownerRect = null; _smoothVx = 0; _holdTicks = 0; _calmTicks = 0; _dragTicks = 0; _dizzy = false; _startled = 0;
        State = PetState.Dragged;
        SetAnim(_a.Fall);
    }

    public void DragTo(double cursorX, double cursorY)
    {
        if (State != PetState.Dragged) return;
        X = cursorX; Y = cursorY;
    }

    /// <summary>
    /// Let go with the cursor's velocity in px per tick. A gentle release near a wall, the ceiling or a floor (a window's
    /// title bar, the taskbar) latches on or lands there, whichever is closest; anything else, or a fast release, is a throw.
    /// </summary>
    public void EndDrag(double vx, double vy, World world)
    {
        if (State != PetState.Dragged) return;
        _dizzy = _dragTicks > 5 * ShimejiFrames.TickRate;     // carried around for 5+ seconds
        if (double.Hypot(vx, vy) < 6 * _ps && TryDrop(world)) return;
        var max = 40 * _ps;
        StartFall(PetState.Thrown, Math.Clamp(vx, -max, max), Math.Clamp(vy, -max, max));
    }

    private bool TryDrop(World world)
    {
        var reach = 40 * _ps;
        var wall = _profile.Caps.Climb ? world.NearestWall(X, Y, reach) : null;
        var ceiling = _profile.Caps.Ceiling ? world.CeilingAt(X, Y, reach) : null;
        var floor = world.DropFloor(X, Y, 24 * _ps, 56 * _ps);
        var dWall = wall is null ? double.MaxValue : Math.Abs(wall.At - X);
        var dCeiling = ceiling is null ? double.MaxValue : Math.Abs(ceiling.At - Y);
        var dFloor = floor is null ? double.MaxValue : Math.Abs(floor.At - Y);
        var best = Math.Min(dFloor, Math.Min(dWall, dCeiling));
        if (best == double.MaxValue) return false;

        Vx = Vy = 0;
        var hold = 125 + _rng.Next(376);      // 5 – 20 s before it starts climbing: it stays where it was put
        if (dFloor == best) { LandOn(floor!, X, hard: false, world); return true; }
        if (dWall == best) { LatchWall(wall!, world, hold); return true; }
        LatchCeiling(ceiling!, world, hold);
        return true;
    }

    /// <summary>A click without dragging: the pet reacts (only while it is on a floor).</summary>
    public void Click()
    {
        _followTicks = 0;
        if (State is PetState.Stand or PetState.Walk or PetState.Run or PetState.Sit or PetState.Lie or PetState.Sleep or PetState.Perch)
        {
            State = PetState.React;
            _reactTicks = 0;
            _calmTicks = 0;
            Vx = 0;
            SetAnim(_reactPoses);
        }
    }

    /// <summary>The menu's Sit / Sleep (ignored when the pack can't, or the pet is not on a floor).</summary>
    public void Command(PetCommand command)
    {
        if (_on is not { Kind: SurfaceKind.Floor } || State is PetState.Fall or PetState.Thrown or PetState.Dragged or PetState.Land or PetState.Jump) return;
        if (command == PetCommand.Sit && _profile.Caps.Sit) Enter(PetState.Sit, 250 + _rng.Next(250));
        else if (command == PetCommand.Sleep && _profile.Caps.Sleep) Enter(PetState.Sleep, 1500 + _rng.Next(1500));
    }

    /// <summary>"Follow me": the pet tracks the cursor for a minute (or turn it off). Cancels Stay.</summary>
    public void SetFollowCursor(bool on)
    {
        _followTicks = on ? 60 * ShimejiFrames.TickRate : 0;
        if (on) _stay = false;
    }

    /// <summary>The cursor rests on the pet: it holds still and shows hearts (only on a floor, and not while busy). Ends by itself after 30 s.</summary>
    public void BeginStroke()
    {
        if (_on is not { Kind: SurfaceKind.Floor } || State is not (PetState.Stand or PetState.Walk or PetState.Run or PetState.Sit or PetState.Lie or PetState.Sleep or PetState.Perch))
            return;
        _strokeSit = State == PetState.Sit;
        State = PetState.Stroked;
        _stateTicks = 30 * ShimejiFrames.TickRate;
        _markTicks = 0;
        Vx = 0;
        SetAnim(_strokeSit ? _a.Sit : _a.Stand);
    }

    public void EndStroke()
    {
        if (State == PetState.Stroked) Enter(PetState.Stand, 25 + _rng.Next(50));
    }

    /// <summary>
    /// Another pet came close: face it and show hearts for about 2 s, then carry out <paramref name="plan"/>
    /// (see <see cref="PetSocial"/>). Ignored unless <see cref="IsFree"/>.
    /// </summary>
    public void Meet(double otherX, MeetPlan plan)
    {
        if (!IsFree) return;
        State = PetState.Meet;
        _stateTicks = 50;
        _markTicks = 0;
        _meetPlan = plan;
        Facing = otherX < X ? -1 : 1;
        Vx = 0;
        SetAnim(_a.Stand);
    }

    // ---- the tick --------------------------------------------------------------------------------

    public void Step(World world)
    {
        if (State == PetState.Dragged)
        {
            _dragTicks++;
            _smoothVx = _smoothVx * 0.7 + (X - _lastX) * 0.3;
            _lastX = X;
            if (Math.Abs(_smoothVx) < 0.5 * _ps) _holdTicks++; else _holdTicks = 0;
            if (_holdTicks > 20 && _anim != _a.Resist) SetAnim(_a.Resist);
            else if (_holdTicks <= 20) SetAnim(Held(_smoothVx));
            AdvanceAnim();
            return;
        }

        if (_startled > 0) _startled--;
        _markTicks++;
        ApplyModes(world);

        var inAir = State is PetState.Fall or PetState.Thrown or PetState.Land or PetState.Jump;
        if (!inAir && !FollowSurface(world))
        {
            if (_ownerGone)
            {
                // The window it stood on went (closed or minimized): a startled little hop, then down.
                _ownerGone = false;
                StartFall(PetState.Fall, 0, -6 * _ps);
                _startled = 25;
            }
            else StartFall(PetState.Fall, 0, 0);
        }

        switch (State)
        {
            case PetState.Fall:
            case PetState.Thrown: StepAir(world); break;
            case PetState.Land: if (--_stateTicks <= 0) Enter(PetState.Stand, 50 + _rng.Next(100)); break;
            case PetState.React: StepReact(world); break;
            case PetState.Stand or PetState.Sit or PetState.Lie or PetState.Sleep:
                _calmTicks++;
                if (--_stateTicks <= 0) ChooseNext(world);
                break;
            case PetState.Walk or PetState.Run: StepWalk(world); break;
            case PetState.Chase: StepChase(world); break;
            case PetState.Jump: StepJump(world); break;
            case PetState.Seek: StepSeek(world); break;
            case PetState.Perch: StepPerch(world); break;
            case PetState.Meet: StepMeet(world); break;
            case PetState.FollowPet: StepFollowPet(world); break;
            case PetState.Stroked: if (--_stateTicks <= 0) EndStroke(); break;
            case PetState.GrabWall: if (!Stay && --_stateTicks <= 0) StartClimbWall(); break;
            case PetState.ClimbWall: StepClimbWall(world); break;
            case PetState.GrabCeiling: if (!Stay && --_stateTicks <= 0) StartClimbCeiling(); break;
            case PetState.ClimbCeiling: StepClimbCeiling(world); break;
        }
        AdvanceAnim();
    }

    /// <summary>"Stay here" and "Follow me" take over whatever the pet is doing, except being held, falling or reacting.</summary>
    private void ApplyModes(World world)
    {
        if (Stay)
        {
            switch (State)
            {
                case PetState.ClimbWall: State = PetState.GrabWall; SetAnim(_a.GrabWall); break;
                case PetState.ClimbCeiling: State = PetState.GrabCeiling; SetAnim(_a.GrabCeiling); break;
                case PetState.Walk or PetState.Run or PetState.Chase or PetState.Seek or PetState.Perch or PetState.FollowPet or PetState.Meet:
                    Enter(PetState.Stand, 50 + _rng.Next(100));
                    break;
            }
            return;
        }
        if (_followTicks <= 0) return;
        _followTicks--;
        switch (State)
        {
            case PetState.GrabWall or PetState.ClimbWall:
                StartFall(PetState.Fall, (_on?.Side ?? 0) * 3 * _ps, 0);
                break;
            case PetState.GrabCeiling or PetState.ClimbCeiling:
                StartFall(PetState.Fall, 0, 0);
                break;
            case PetState.Stand or PetState.Sit or PetState.Lie or PetState.Sleep or PetState.Walk or PetState.Run
                or PetState.Perch or PetState.Seek or PetState.Meet or PetState.FollowPet:
                if (_on is { Kind: SurfaceKind.Floor }) ChooseNext(world);
                break;
        }
    }

    // ---- surfaces that move ----------------------------------------------------------------------

    /// <summary>
    /// Keeps the pet on the surface it is on: carried along when its window moves (shaken off when the window is
    /// thrown about), snapped back onto the line after a small change, and released (false) when the surface is gone
    /// (closed, minimized, covered).
    /// </summary>
    private bool FollowSurface(World world)
    {
        if (_on is null) return false;
        if (_on.Owner != 0)
        {
            if (world.RectOf(_on.Owner) is not { } rect) { _ownerGone = true; return false; }
            if (_ownerRect is not { } prev) return false;
            var (dx, dy) = ((double)(rect.Left - prev.Left), (double)(rect.Top - prev.Top));
            X += dx;
            Y += dy;
            _ownerRect = rect;
            if (Shaken(dx, dy))
            {
                _dizzy = true;
                var max = 40 * _ps;
                StartFall(PetState.Thrown, Math.Clamp(dx * 0.6, -max, max), -8 * _ps);
                return true;
            }
        }
        var along = _on.Kind == SurfaceKind.Wall ? Y : X;
        var at = _on.Kind == SurfaceKind.Wall ? X : Y;
        var found = world.Find(_on.Kind, _on.Owner, along, at, 12 * _ps + 2);
        if (found is null) return false;
        _on = found;
        if (found.Kind == SurfaceKind.Wall) X = found.At; else Y = found.At;
        return true;
    }

    /// <summary>
    /// The window under the pet is being jerked about: it reverses direction at least twice within <see cref="ShakeWindow"/>
    /// ticks at more than 25 px/tick, or moves more than 60 px in one tick. Plain dragging keeps carrying the pet.
    /// </summary>
    private bool Shaken(double dx, double dy)
    {
        _shake.Add((dx, dy));
        if (_shake.Count > ShakeWindow) _shake.RemoveAt(0);
        if (double.Hypot(dx, dy) > 60 * _ps) { _shake.Clear(); return true; }
        if (Reversals(_shake.Select(s => s.dx)) >= 2 || Reversals(_shake.Select(s => s.dy)) >= 2) { _shake.Clear(); return true; }
        return false;
    }

    private int Reversals(IEnumerable<double> deltas)
    {
        var flips = 0;
        double prev = 0;
        foreach (var d in deltas)
        {
            if (Math.Abs(d) <= 25 * _ps) continue;
            if (prev != 0 && Math.Sign(d) != Math.Sign(prev)) flips++;
            prev = d;
        }
        return flips;
    }

    private void Attach(Surface s, World world)
    {
        _on = s;
        _ownerRect = s.Owner != 0 ? world.RectOf(s.Owner) : null;
        _shake.Clear();
    }

    // ---- air -------------------------------------------------------------------------------------

    private void StartFall(PetState state, double vx, double vy)
    {
        State = state;
        Vx = vx; Vy = vy;
        _on = null; _ownerRect = null;
        SetAnim(_a.Fall);
    }

    private void StepAir(World world)
    {
        Vx -= Vx * ShimejiFrames.ResistanceX;
        Vy += ShimejiFrames.Gravity * _ps - Vy * ShimejiFrames.ResistanceY;
        var nx = X + Vx;
        var ny = Y + Vy;
        var thrown = State == PetState.Thrown;

        if (world.FirstWallCrossed(X, nx, Y - 6 * _ps) is { } wall)
        {
            if (thrown && _profile.Caps.Climb) { _dizzy = false; LatchWall(wall, world, 12 + _rng.Next(26)); return; }
            nx = wall.At; Vx = -Vx * 0.5;
        }
        if (thrown && Vy < 0 && _profile.Caps.Ceiling && world.FirstCeilingCrossed(X, Y, nx, ny) is { } ceil)
        {
            _dizzy = false;
            X = ceil.X;
            LatchCeiling(ceil.Ceiling, world, 12 + _rng.Next(26));
            return;
        }
        var b = world.Bounds;
        if (nx < b.Left) { nx = b.Left; Vx = Math.Abs(Vx) * 0.5; }
        else if (nx > b.Right) { nx = b.Right; Vx = -Math.Abs(Vx) * 0.5; }
        if (ny < b.Top - 600) { ny = b.Top - 600; Vy = Math.Max(Vy, 0); }

        if (Vy >= 0 && world.FirstFloorCrossed(X, Y, nx, ny) is { } hit)
        {
            LandOn(hit.Floor, hit.X, hard: Vy > 9 * _ps, world);
            return;
        }
        X = nx; Y = ny;

        // Fell past everything (a monitor went away): start again from above the primary monitor.
        if (Y > b.Bottom + 400) { var (sx, sy) = world.SpawnPoint(_rng); Spawn(sx, sy); }
    }

    /// <summary>Puts the pet on a floor: a hard landing bounces, a long carry ends in a dizzy sprawl, else it stands.</summary>
    private void LandOn(Surface floor, double x, bool hard, World world)
    {
        X = x; Y = floor.At;
        Attach(floor, world);
        Vx = Vy = 0;
        if (_dizzy && _profile.Caps.Sleep) { _dizzy = false; Enter(PetState.Lie, 75 + _rng.Next(75)); return; }
        _dizzy = false;
        if (hard && Present(_a.Bounce)) { State = PetState.Land; _stateTicks = 8; SetAnim(_a.Bounce); }
        else Enter(PetState.Stand, 25 + _rng.Next(75));
    }

    // ---- jumping ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts a scripted jump to a point on a surface: an arc 24 px high (at pixel scale 1) over 14 + distance / 6 ticks,
    /// exact rather than physical. The target is re-found every tick; if it is gone the pet falls.
    /// </summary>
    private void StartJump(Surface target, double tx, double ty, World world)
    {
        _jumpTarget = target;
        _jumpFromX = X; _jumpFromY = Y;
        _jumpToX = tx; _jumpToY = ty;
        if (target.Owner != 0 && world.RectOf(target.Owner) is { } rect) { _jumpOffX = tx - rect.Left; _jumpOffY = ty - rect.Top; }
        _jumpN = 14 + (int)(double.Hypot(tx - X, ty - Y) / (6 * _ps));
        _jumpK = 0;
        _on = null; _ownerRect = null;
        State = PetState.Jump;
        Vx = Vy = 0;
        if (Math.Abs(tx - X) > 1) Facing = tx < X ? -1 : 1;
        SetAnim(Present(_a.Jump) ? _a.Jump : _a.Fall);
    }

    private void StepJump(World world)
    {
        if (_jumpTarget is not { } target) { StartFall(PetState.Fall, 0, 0); return; }
        if (target.Owner != 0)
        {
            if (world.RectOf(target.Owner) is not { } rect) { StartFall(PetState.Fall, 0, 0); return; }
            _jumpToX = rect.Left + _jumpOffX;
            _jumpToY = rect.Top + _jumpOffY;
        }
        var isWall = target.Kind == SurfaceKind.Wall;
        var found = world.Find(target.Kind, target.Owner, isWall ? _jumpToY : _jumpToX, isWall ? _jumpToX : _jumpToY, 6 * _ps + 3);
        if (found is null) { StartFall(PetState.Fall, 0, 0); return; }
        _jumpTarget = found;
        if (isWall) _jumpToX = found.At; else _jumpToY = found.At;

        _jumpK++;
        var t = Math.Min(1.0, _jumpK / (double)_jumpN);
        X = _jumpFromX + (_jumpToX - _jumpFromX) * t;
        Y = _jumpFromY + (_jumpToY - _jumpFromY) * t - 4 * 24 * _ps * t * (1 - t);
        if (_jumpK < _jumpN) return;

        _jumpTarget = null;
        if (isWall)
        {
            X = found.At;
            Y = Between(_jumpToY, found.From + 1, found.To);
            GrabWall(found, world, 6);
            _climbDir = -1;      // up
            _climbAll = true;
        }
        else
        {
            X = _jumpToX; Y = found.At;
            Attach(found, world);
            Enter(PetState.Stand, 25 + _rng.Next(50));
        }
    }

    // ---- floor -----------------------------------------------------------------------------------

    /// <summary>Whether the pack has every image this animation shows.</summary>
    private bool Present(Pose[] poses) => poses.All(p => _profile.Frames.Contains(p.Frame));

    private static double Between(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Clamp(v, lo, hi);

    private void Enter(PetState state, int ticks)
    {
        State = state;
        _stateTicks = ticks;
        Vx = Vy = 0;
        SetAnim(state switch
        {
            PetState.Walk => _a.Walk,
            PetState.Run or PetState.Chase => _a.Run,
            PetState.Sit => _rng.Next(4) == 0 && Present(_a.SitLookUp) ? _a.SitLookUp : _a.Sit,
            PetState.Lie => _a.Sprawl,
            PetState.Sleep => _a.Sleep,
            _ => _a.Stand,
        });
    }

    private void ChooseNext(World world)
    {
        // "Follow me": keep to the cursor's X along this floor, standing still once next to it.
        if (FollowCursor && _hasCursor && _on is { Kind: SurfaceKind.Floor })
        {
            if (Math.Abs(_cursorX - X) > 60 * _ps) { Facing = _cursorX < X ? -1 : 1; Enter(PetState.Chase, 400); }
            else Enter(PetState.Stand, 25 + _rng.Next(25));
            return;
        }

        var calm = _profile.Activity == "calm";
        var lively = _profile.Activity == "lively";
        // Sleep only after a long untouched stretch.
        var sleepAfter = ShimejiFrames.TickRate * (calm ? 45 : lively ? 600 : 150);
        var options = new List<(PetState state, double weight, int ticks)>
        {
            (PetState.Stand, calm ? 4 : lively ? 1 : 3, 50 + _rng.Next(100)),
        };
        if (!Stay)
        {
            options.Add((PetState.Walk, calm ? 2 : 5, 75 + _rng.Next(175)));
            options.Add((PetState.Run, calm ? 0.2 : lively ? 4 : 1, 50 + _rng.Next(100)));
        }
        if (_profile.Caps.Sit) options.Add((PetState.Sit, calm ? 5 : lively ? 1 : 3, 125 + _rng.Next(375)));
        if (_profile.Caps.Sleep)
        {
            options.Add((PetState.Lie, calm ? 3 : lively ? 0.5 : 1, 100 + _rng.Next(200)));
            if (_calmTicks >= sleepAfter) options.Add((PetState.Sleep, 3, 750 + _rng.Next(1750)));
        }
        Surface? seek = null;
        (Surface Floor, double X)? hop = null;
        var hopDir = 0;
        if (!Stay && _on is { Kind: SurfaceKind.Floor } floor)
        {
            // Now and then, go over to the cursor if it is on this floor's level, near enough to be worth the walk.
            if (_hasCursor && Math.Abs(_cursorX - X) > 120 * _ps && Math.Abs(_cursorX - X) < 1500 * _ps
                && _cursorY >= Y - 500 * _ps && _cursorY <= Y + 60 * _ps)
                options.Add((PetState.Chase, calm ? 0.1 : lively ? 2 : 0.5, 400));
            // Or head for the side of a window it can climb or jump onto.
            if (_profile.Caps.Climb && world.ReachableWall(X, Y, 2 * Body, 600 * _ps) is { } wall)
            {
                seek = wall;
                options.Add((PetState.Seek, calm ? 0.3 : lively ? 2.5 : 1, 400));
            }
            // Near the edge of a window with another one within hopping distance.
            if (floor.Owner != 0 && Math.Min(X - floor.From, floor.To - X) <= 60 * _ps)
            {
                hopDir = X - floor.From < floor.To - X ? -1 : 1;
                hop = world.HopTarget(X, Y, hopDir, 3 * Body, 1.5 * Body, floor.Owner);
                if (hop is not null) options.Add((PetState.Jump, calm ? 0.3 : lively ? 2 : 1, 0));
            }
        }
        var roll = _rng.NextDouble() * options.Sum(o => o.weight);
        var pick = options[^1];
        foreach (var o in options) { if ((roll -= o.weight) < 0) { pick = o; break; } }

        if (pick.state is PetState.Walk or PetState.Run) Facing = _rng.Next(2) == 0 ? -1 : 1;
        if (pick.state == PetState.Chase) { Enter(PetState.Chase, pick.ticks); Facing = _cursorX < X ? -1 : 1; return; }
        if (pick.state == PetState.Seek) { StartSeek(seek!); return; }
        if (pick.state == PetState.Jump) { Facing = hopDir; StartJump(hop!.Value.Floor, hop.Value.X, hop.Value.Floor.At, world); return; }
        Enter(pick.state, pick.ticks);
    }

    private void StepWalk(World world)
    {
        if (--_stateTicks <= 0) { Enter(PetState.Stand, 25 + _rng.Next(50)); return; }
        MoveOnFloor(world);
    }

    /// <summary>Walks towards the cursor (running when it is far), and settles next to it.</summary>
    private void StepChase(World world)
    {
        if (--_stateTicks <= 0 || !_hasCursor) { Enter(PetState.Stand, 25 + _rng.Next(50)); return; }
        var dx = _cursorX - X;
        if (Math.Abs(dx) < 60 * _ps)
        {
            Enter(!FollowCursor && _profile.Caps.Sit && _rng.Next(2) == 0 ? PetState.Sit : PetState.Stand, 75 + _rng.Next(125));
            return;
        }
        Facing = dx < 0 ? -1 : 1;
        SetAnim(Math.Abs(dx) > 300 * _ps ? _a.Run : _a.Walk);
        MoveOnFloor(world);
    }

    private void MoveOnFloor(World world)
    {
        if (_on is null) return;
        var step = -Pose.Vx * Facing * _ps;   // Pose.Vx is for a pet facing left
        var nx = X + step;

        // Anything overlapping the pet's body, not just its feet, is in the way.
        if (world.FirstWallCrossed(X, nx, Y - Body, Y - 6 * _ps) is { } wall)
        {
            X = wall.At;
            Blocked(wall, world);
            return;
        }
        X = nx;
        if (X >= _on.From - 0.001 && X <= _on.To + 0.001) return;

        // The end of this floor: carry on if another floor continues it, else turn back, climb down or step off.
        if (world.FloorAt(X, Y) is { } next) { Attach(next, world); return; }
        X = Math.Clamp(X, _on.From, _on.To);
        if (_on.Owner != 0) WindowEdge(world);
        else if (X <= world.Bounds.Left + 1 || X >= world.Bounds.Right - 1) { if (FollowCursor) Enter(PetState.Stand, 25); else Facing = -Facing; }
        else StartFall(PetState.Fall, 0, 0);
    }

    /// <summary>A wall overlaps the pet's body: climb it if it reaches the feet, jump to it if it ends above them, else turn back.</summary>
    private void Blocked(Surface wall, World world)
    {
        if (FollowCursor) { Enter(PetState.Stand, 25); return; }
        if (wall.To >= Y - 6 * _ps)
        {
            if (_profile.Caps.Climb && _rng.NextDouble() < (wall.Owner != 0 ? 0.85 : 0.6))
            {
                Y = Math.Min(Y, wall.To);
                GrabWall(wall, world);
            }
            else Facing = -Facing;
        }
        else if (_profile.Caps.Climb && Y - wall.To <= 2 * Body) StartJump(wall, wall.At, wall.To - 2, world);
        else Facing = -Facing;
    }

    /// <summary>The end of a window's top edge: turn back, climb down its side, step off, sit with the legs dangling, or hop to another window.</summary>
    private void WindowEdge(World world)
    {
        if (_on is null) return;
        if (FollowCursor) { StartFall(PetState.Fall, 0, 0); return; }
        var dir = X <= _on.From + 1 ? -1 : 1;     // which way the edge faces
        var options = new List<(string kind, double weight)> { ("turn", 35), ("off", 30) };
        var down = _profile.Caps.Climb ? world.Find(SurfaceKind.Wall, _on.Owner, Y + 2, X, 3) : null;
        if (down is not null) options.Add(("down", 35));
        if (_profile.Caps.Sit) options.Add(("perch", 25));
        var hop = world.HopTarget(X, Y, dir, 3 * Body, 1.5 * Body, _on.Owner);
        if (hop is not null) options.Add(("hop", 25));

        var roll = _rng.NextDouble() * options.Sum(o => o.weight);
        var pick = options[^1].kind;
        foreach (var o in options) { if ((roll -= o.weight) < 0) { pick = o.kind; break; } }
        switch (pick)
        {
            case "turn": Facing = -Facing; break;
            case "down": StartClimbDown(down!, world); break;
            case "perch": StartPerch(dir); break;
            case "hop": Facing = dir; StartJump(hop!.Value.Floor, hop.Value.X, hop.Value.Floor.At, world); break;
            default: StartFall(PetState.Fall, 0, 0); break;
        }
    }

    // ---- perching, seeking -----------------------------------------------------------------------

    /// <summary>Sits at the very edge of a window with its legs dangling (the pet stepped back onto the surface, facing outward).</summary>
    private void StartPerch(int outward)
    {
        if (_on is null) return;
        X = Between(X - outward * 20 * _ps, _on.From, _on.To);
        _perchInward = -outward;
        Facing = outward;
        State = PetState.Perch;
        _stateTicks = 200 + _rng.Next(550);
        Vx = 0;
        SetAnim(Present(_a.SitDangle) ? _a.SitDangle : _a.Sit);
    }

    private void StepPerch(World world)
    {
        if (--_stateTicks > 0) return;
        Facing = _perchInward;
        ChooseNext(world);
    }

    private void StartSeek(Surface wall)
    {
        _seekTarget = wall;
        _seekTicks = 400;
        State = PetState.Seek;
        Facing = wall.At < X ? -1 : 1;
        Vx = 0;
        SetAnim(_a.Walk);
    }

    /// <summary>Walks to the side of a window and goes up it, by climbing if it reaches the floor, else by jumping.</summary>
    private void StepSeek(World world)
    {
        if (_seekTarget is not { } t || _on is not { Kind: SurfaceKind.Floor } || --_seekTicks <= 0) { Enter(PetState.Stand, 25 + _rng.Next(50)); return; }
        // Re-validate every tick by owner and side: the window may have moved, closed or been covered.
        var wall = world.Surfaces
            .Where(s => s.Kind == SurfaceKind.Wall && s.Owner == t.Owner && s.Side == t.Side && (X - s.At) * s.Side >= 0
                        && s.To >= Y - 2 * Body && s.From <= Y + 2)
            .OrderBy(s => Math.Abs(X - s.At)).FirstOrDefault();
        if (wall is null) { Enter(PetState.Stand, 25 + _rng.Next(50)); return; }
        _seekTarget = wall;

        if (Math.Abs(wall.At - X) <= 40 * _ps)
        {
            if (wall.To >= Y - 6 * _ps)
            {
                X = wall.At;
                Y = Math.Min(Y, wall.To);
                GrabWall(wall, world);
                _climbDir = -1;
                _climbAll = true;
            }
            else StartJump(wall, wall.At, wall.To - 2, world);
            return;
        }
        Facing = wall.At < X ? -1 : 1;
        SetAnim(_a.Walk);
        MoveOnFloor(world);
    }

    // ---- meeting other pets ----------------------------------------------------------------------

    private void StepMeet(World world)
    {
        if (--_stateTicks > 0) return;
        var plan = _meetPlan;
        switch (plan.Kind)
        {
            case MeetKind.Sit when _profile.Caps.Sit: Enter(PetState.Sit, plan.Ticks); break;
            case MeetKind.Follow:
                State = PetState.FollowPet;
                _stateTicks = plan.Ticks;
                SetAnim(_a.Stand);
                break;
            case MeetKind.Part:
                Facing = -Facing;      // away from the other
                Enter(PetState.Walk, 125 + _rng.Next(175));
                break;
            default: Enter(PetState.Stand, 25 + _rng.Next(50)); break;
        }
    }

    /// <summary>Follows another pet, keeping a gap of 70 px (at pixel scale 1); ends after its time or when the other leaves the surface.</summary>
    private void StepFollowPet(World world)
    {
        if (--_stateTicks <= 0 || PetTargetX is not { } tx) { Enter(PetState.Stand, 25 + _rng.Next(50)); return; }
        var dx = tx - X;
        var gap = 70 * _ps;
        Facing = dx < 0 ? -1 : 1;
        if (Math.Abs(dx) <= gap + 10 * _ps) { SetAnim(_a.Stand); return; }
        SetAnim(Math.Abs(dx) > 300 * _ps ? _a.Run : _a.Walk);
        MoveOnFloor(world);
    }

    // ---- walls and ceilings ----------------------------------------------------------------------

    /// <summary>Takes hold of a wall: looks at it, then after <paramref name="hold"/> ticks (default 12 – 37) starts to climb.</summary>
    private void GrabWall(Surface wall, World world, int hold = -1)
    {
        Attach(wall, world);
        X = wall.At;
        Facing = -wall.Side;                         // look at the wall
        State = PetState.GrabWall;
        _stateTicks = hold >= 0 ? hold : 12 + _rng.Next(25);
        _climbDir = _rng.NextDouble() < 0.7 ? -1 : +1;
        _climbTicks = 150 + _rng.Next(400);
        SetAnim(_a.GrabWall);
    }

    /// <summary>Holds on to a wall where the pet was put or thrown: Y is moved into the wall's span.</summary>
    private void LatchWall(Surface wall, World world, int hold)
    {
        Y = Between(Y, wall.From + 2, wall.To);
        Vx = Vy = 0;
        _dizzy = false;
        GrabWall(wall, world, hold);
    }

    private void LatchCeiling(Surface ceiling, World world, int hold)
    {
        Attach(ceiling, world);
        X = Between(X, ceiling.From, ceiling.To);
        Y = ceiling.At;
        Vx = Vy = 0;
        _dizzy = false;
        State = PetState.GrabCeiling;
        _stateTicks = hold;
        _climbDir = _rng.Next(2) == 0 ? -1 : +1;
        _climbTicks = 150 + _rng.Next(300);
        SetAnim(_a.GrabCeiling);
    }

    /// <summary>Over the edge of a window: takes hold of its side from the top and climbs down.</summary>
    private void StartClimbDown(Surface wall, World world)
    {
        Attach(wall, world);
        X = wall.At;
        Y = wall.From;
        Facing = -wall.Side;
        State = PetState.GrabWall;
        _stateTicks = 8;
        _climbDir = +1;
        _climbTicks = 150 + _rng.Next(400);
        Vx = 0;
        SetAnim(_a.GrabWall);
    }

    private void StartClimbWall()
    {
        if (_on is not { Kind: SurfaceKind.Wall } wall) { StartFall(PetState.Fall, 0, 0); return; }
        // Go up only if there is wall left above, down only if there is wall below.
        if (_climbDir < 0 && Y <= wall.From + 4) _climbDir = +1;
        if (_climbDir > 0 && Y >= wall.To - 4) _climbDir = -1;
        State = PetState.ClimbWall;
        // Usually climbs all the way to the end (about half a pixel per tick); sometimes lets go part-way.
        var distance = _climbDir < 0 ? Y - wall.From : wall.To - Y;
        _climbTicks = _climbAll || _rng.NextDouble() < 0.6 ? (int)(distance / (0.5 * _ps)) + 200 : 150 + _rng.Next(400);
        _climbAll = false;
        SetAnim(_climbDir < 0 ? _a.ClimbWallUp : _a.ClimbWallDown);
    }

    private void StepClimbWall(World world)
    {
        if (_on is not { Kind: SurfaceKind.Wall } wall) { StartFall(PetState.Fall, 0, 0); return; }
        if (--_climbTicks <= 0)
        {
            StartFall(PetState.Fall, wall.Side * 3 * _ps, 0);    // let go
            return;
        }
        Y += Pose.Vy * _ps;

        if (_climbDir < 0 && Y <= wall.From)
        {
            Y = wall.From;
            if (wall.Owner != 0)
            {
                // Over the top of a window side: onto its top edge.
                var floor = world.Find(SurfaceKind.Floor, wall.Owner, X, Y, 6 * _ps + 3);
                if (floor is null) { StartFall(PetState.Fall, 0, 0); return; }
                Attach(floor, world);
                Y = floor.At;
                Facing = -wall.Side;                  // walk away from the edge, onto the window
                Enter(PetState.Stand, 25 + _rng.Next(50));
                return;
            }
            var ceiling = _profile.Caps.Ceiling ? world.CeilingAt(X, Y, 6 * _ps + 3) : null;
            if (ceiling is not null)
            {
                Attach(ceiling, world);
                Y = ceiling.At;
                State = PetState.GrabCeiling;
                _stateTicks = 12 + _rng.Next(25);
                _climbTicks = 150 + _rng.Next(300);
                _climbDir = wall.Side;     // head away from the corner
                SetAnim(_a.GrabCeiling);
            }
            else { _climbDir = +1; State = PetState.GrabWall; _stateTicks = 10; SetAnim(_a.GrabWall); }
        }
        else if (_climbDir > 0 && Y >= wall.To)
        {
            Y = wall.To;
            if (world.FloorAt(X, Y, 6 * _ps + 3) is { } floor)
            {
                Attach(floor, world);
                Y = floor.At;
                Facing = wall.Side;
                Enter(PetState.Stand, 25 + _rng.Next(50));
            }
            else StartFall(PetState.Fall, wall.Side * 2 * _ps, 0);
        }
    }

    private void StartClimbCeiling()
    {
        if (_on is not { Kind: SurfaceKind.Ceiling }) { StartFall(PetState.Fall, 0, 0); return; }
        Facing = _climbDir > 0 ? 1 : -1;
        State = PetState.ClimbCeiling;
        SetAnim(_a.ClimbCeiling);
    }

    private void StepClimbCeiling(World world)
    {
        if (_on is not { Kind: SurfaceKind.Ceiling } ceiling) { StartFall(PetState.Fall, 0, 0); return; }
        if (--_climbTicks <= 0) { StartFall(PetState.Fall, 0, 0); return; }
        X += -Pose.Vx * Facing * _ps;
        if (X >= ceiling.From && X <= ceiling.To) return;
        X = Math.Clamp(X, ceiling.From, ceiling.To);
        // The corner: down the screen edge if there is one, else turn back.
        var wall = world.Surfaces.FirstOrDefault(s => s.Kind == SurfaceKind.Wall && s.Owner == 0 && Math.Abs(s.At - X) <= 2 && Math.Abs(s.From - Y) <= 6 * _ps + 3);
        if (wall is not null && _profile.Caps.Climb)
        {
            Attach(wall, world);
            X = wall.At; Y = wall.From + 2;
            Facing = -wall.Side;
            _climbDir = +1;
            State = PetState.GrabWall;
            _stateTicks = 10;
            _climbTicks = 150 + _rng.Next(300);
            SetAnim(_a.GrabWall);
        }
        else Facing = -Facing;
    }

    // ---- reacting and being held -----------------------------------------------------------------

    private void StepReact(World world)
    {
        _reactTicks++;
        if (_reactTicks >= _reactPoses.Sum(p => p.Ticks)) Enter(PetState.Stand, 40 + _rng.Next(60));
    }

    private Pose[] Held(double smoothVx)
    {
        var frame = _a.Pinched(-smoothVx * 4 / _ps, 0);   // the body trails behind the cursor's motion
        var st = _a.Stand[0];
        return [new Pose(frame, 5, st.AnchorX, st.AnchorY, 0, 0, st.Mirror)];
    }

    // ---- animation -------------------------------------------------------------------------------

    private void SetAnim(Pose[] anim)
    {
        if (ReferenceEquals(_anim, anim) && anim.Length > 1) return;
        _anim = anim;
        _animIndex = 0;
        _animTicks = anim[0].Ticks;
    }

    private void AdvanceAnim()
    {
        if (--_animTicks > 0) return;
        _animIndex = (_animIndex + 1) % _anim.Length;
        if (State == PetState.React && _animIndex == 0) _animIndex = _anim.Length - 1;   // play once
        _animTicks = _anim[_animIndex].Ticks;
    }
}
