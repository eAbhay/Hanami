namespace Hanami.Core.Pets;

public enum PetState { Stand, Walk, Run, Sit, Lie, Sleep, Fall, Land, Dragged, Thrown, React, GrabWall, ClimbWall, GrabCeiling, ClimbCeiling }

public enum PetCommand { Sit, Sleep }

/// <summary>How a pet behaves: from the pack (what its frames allow) and the settings.</summary>
public sealed record PetProfile(PetCapabilities Caps, IReadOnlySet<int> Frames, double PixelScale, string Activity = "normal");

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

    private Pose[] _anim = ShimejiFrames.Stand;
    private int _animIndex, _animTicks;
    private int _stateTicks;
    private Surface? _on;
    private PxRect? _ownerRect;
    private int _calmTicks;
    private int _climbDir = -1;          // -1 up, +1 down
    private int _holdTicks;              // how long the pet has been held without moving
    private double _lastX, _smoothVx;
    private int _reactTicks;

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
    public bool IsHeld => State == PetState.Dragged;

    public PetBrain(PetProfile profile, Random rng)
    {
        _profile = profile;
        _rng = rng;
        _ps = profile.PixelScale;
        SetAnim(ShimejiFrames.Fall);
    }

    // ---- input from the mouse / app --------------------------------------------------------------

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
        _on = null; _ownerRect = null; _smoothVx = 0; _holdTicks = 0; _calmTicks = 0;
        State = PetState.Dragged;
        SetAnim(ShimejiFrames.Fall);
    }

    public void DragTo(double cursorX, double cursorY)
    {
        if (State != PetState.Dragged) return;
        X = cursorX; Y = cursorY;
    }

    /// <summary>Let go with the cursor's velocity in px per tick: the pet is thrown.</summary>
    public void EndDrag(double vx, double vy)
    {
        if (State != PetState.Dragged) return;
        var max = 40 * _ps;
        StartFall(PetState.Thrown, Math.Clamp(vx, -max, max), Math.Clamp(vy, -max, max));
    }

    /// <summary>A click without dragging: the pet reacts (only while it is on a floor).</summary>
    public void Click()
    {
        if (State is PetState.Stand or PetState.Walk or PetState.Run or PetState.Sit or PetState.Lie or PetState.Sleep)
        {
            State = PetState.React;
            _reactTicks = 0;
            _calmTicks = 0;
            Vx = 0;
            SetAnim(ReactPoses);
        }
    }

    /// <summary>The menu's Sit / Sleep (ignored when the pack can't, or the pet is not on a floor).</summary>
    public void Command(PetCommand command)
    {
        if (_on is not { Kind: SurfaceKind.Floor } || State is PetState.Fall or PetState.Thrown or PetState.Dragged or PetState.Land) return;
        if (command == PetCommand.Sit && _profile.Caps.Sit) Enter(PetState.Sit, 250 + _rng.Next(250));
        else if (command == PetCommand.Sleep && _profile.Caps.Sleep) Enter(PetState.Sleep, 1500 + _rng.Next(1500));
    }

    // ---- the tick --------------------------------------------------------------------------------

    public void Step(World world)
    {
        if (State == PetState.Dragged)
        {
            _smoothVx = _smoothVx * 0.7 + (X - _lastX) * 0.3;
            _lastX = X;
            if (Math.Abs(_smoothVx) < 0.5 * _ps) _holdTicks++; else _holdTicks = 0;
            if (_holdTicks > 20 && _anim != ShimejiFrames.Resist) SetAnim(ShimejiFrames.Resist);
            else if (_holdTicks <= 20) SetAnim(Held(_smoothVx));
            AdvanceAnim();
            return;
        }

        if (State is not (PetState.Fall or PetState.Thrown or PetState.Land) && !FollowSurface(world)) StartFall(PetState.Fall, 0, 0);

        switch (State)
        {
            case PetState.Fall:
            case PetState.Thrown: StepAir(world); break;
            case PetState.Land: if (--_stateTicks <= 0) Enter(PetState.Stand, 50 + _rng.Next(100)); break;
            case PetState.React: StepReact(); break;
            case PetState.Stand or PetState.Sit or PetState.Lie or PetState.Sleep:
                _calmTicks++;
                if (--_stateTicks <= 0) ChooseNext();
                break;
            case PetState.Walk or PetState.Run: StepWalk(world); break;
            case PetState.GrabWall: if (--_stateTicks <= 0) StartClimbWall(); break;
            case PetState.ClimbWall: StepClimbWall(world); break;
            case PetState.GrabCeiling: if (--_stateTicks <= 0) StartClimbCeiling(); break;
            case PetState.ClimbCeiling: StepClimbCeiling(world); break;
        }
        AdvanceAnim();
    }

    // ---- surfaces that move ----------------------------------------------------------------------

    /// <summary>
    /// Keeps the pet on the surface it is on: carried along when its window moves, snapped back onto the line
    /// after a small change, and released (false) when the surface is gone (closed, minimized, covered).
    /// </summary>
    private bool FollowSurface(World world)
    {
        if (_on is null) return false;
        if (_on.Owner != 0)
        {
            if (world.RectOf(_on.Owner) is not { } rect || _ownerRect is not { } prev) return false;
            X += rect.Left - prev.Left;
            Y += rect.Top - prev.Top;
            _ownerRect = rect;
        }
        var along = _on.Kind == SurfaceKind.Wall ? Y : X;
        var at = _on.Kind == SurfaceKind.Wall ? X : Y;
        var found = world.Find(_on.Kind, _on.Owner, along, at, 12 * _ps + 2);
        if (found is null) return false;
        _on = found;
        if (found.Kind == SurfaceKind.Wall) X = found.At; else Y = found.At;
        return true;
    }

    private void Attach(Surface s, World world)
    {
        _on = s;
        _ownerRect = s.Owner != 0 ? world.RectOf(s.Owner) : null;
    }

    // ---- air -------------------------------------------------------------------------------------

    private void StartFall(PetState state, double vx, double vy)
    {
        State = state;
        Vx = vx; Vy = vy;
        _on = null; _ownerRect = null;
        SetAnim(ShimejiFrames.Fall);
    }

    private void StepAir(World world)
    {
        Vx -= Vx * ShimejiFrames.ResistanceX;
        Vy += ShimejiFrames.Gravity * _ps - Vy * ShimejiFrames.ResistanceY;
        var nx = X + Vx;
        var ny = Y + Vy;

        if (world.FirstWallCrossed(X, nx, Y - 6 * _ps) is { } wall) { nx = wall.At; Vx = -Vx * 0.5; }
        var b = world.Bounds;
        if (nx < b.Left) { nx = b.Left; Vx = Math.Abs(Vx) * 0.5; }
        else if (nx > b.Right) { nx = b.Right; Vx = -Math.Abs(Vx) * 0.5; }
        if (ny < b.Top - 600) { ny = b.Top - 600; Vy = Math.Max(Vy, 0); }

        if (Vy >= 0 && world.FirstFloorCrossed(X, Y, nx, ny) is { } hit)
        {
            X = hit.X; Y = hit.Floor.At;
            Attach(hit.Floor, world);
            var hard = Vy > 9 * _ps;
            Vx = Vy = 0;
            if (hard && _profile.Frames.Contains(18)) { State = PetState.Land; _stateTicks = 8; SetAnim(ShimejiFrames.Bounce); }
            else Enter(PetState.Stand, 25 + _rng.Next(75));
            return;
        }
        X = nx; Y = ny;

        // Fell past everything (a monitor went away): start again from above the primary monitor.
        if (Y > b.Bottom + 400) { var (sx, sy) = world.SpawnPoint(_rng); Spawn(sx, sy); }
    }

    // ---- floor -----------------------------------------------------------------------------------

    private void Enter(PetState state, int ticks)
    {
        State = state;
        _stateTicks = ticks;
        Vx = Vy = 0;
        SetAnim(state switch
        {
            PetState.Walk => ShimejiFrames.Walk,
            PetState.Run => ShimejiFrames.Run,
            PetState.Sit => _rng.Next(4) == 0 && _profile.Caps.Sit ? ShimejiFrames.SitLookUp : ShimejiFrames.Sit,
            PetState.Lie or PetState.Sleep => ShimejiFrames.Sprawl,
            _ => ShimejiFrames.Stand,
        });
    }

    private void ChooseNext()
    {
        var calm = _profile.Activity == "calm";
        var lively = _profile.Activity == "lively";
        // Sleep only after a long untouched stretch.
        var sleepAfter = ShimejiFrames.TickRate * (calm ? 45 : lively ? 600 : 150);
        var options = new List<(PetState state, double weight, int ticks)>
        {
            (PetState.Stand, calm ? 4 : lively ? 1 : 3, 50 + _rng.Next(100)),
            (PetState.Walk, calm ? 2 : 5, 75 + _rng.Next(175)),
            (PetState.Run, calm ? 0.2 : lively ? 4 : 1, 50 + _rng.Next(100)),
        };
        if (_profile.Caps.Sit) options.Add((PetState.Sit, calm ? 5 : lively ? 1 : 3, 125 + _rng.Next(375)));
        if (_profile.Caps.Sleep)
        {
            options.Add((PetState.Lie, calm ? 3 : lively ? 0.5 : 1, 100 + _rng.Next(200)));
            if (_calmTicks >= sleepAfter) options.Add((PetState.Sleep, 3, 750 + _rng.Next(1750)));
        }
        var roll = _rng.NextDouble() * options.Sum(o => o.weight);
        var pick = options[^1];
        foreach (var o in options) { if ((roll -= o.weight) < 0) { pick = o; break; } }

        if (pick.state is PetState.Walk or PetState.Run) Facing = _rng.Next(2) == 0 ? -1 : 1;
        Enter(pick.state, pick.ticks);
    }

    private void StepWalk(World world)
    {
        if (--_stateTicks <= 0) { Enter(PetState.Stand, 25 + _rng.Next(50)); return; }
        if (_on is null) return;
        var step = -Pose.Vx * Facing * _ps;   // Pose.Vx is for a pet facing left
        var nx = X + step;

        if (world.FirstWallCrossed(X, nx, Y - 6 * _ps) is { } wall)
        {
            X = wall.At;
            if (_profile.Caps.Climb && _rng.NextDouble() < 0.6) GrabWall(wall, world);
            else Facing = -Facing;
            return;
        }
        X = nx;
        if (X >= _on.From - 0.001 && X <= _on.To + 0.001) return;

        // The end of this floor: carry on if another floor continues it, else turn back or step off.
        if (world.FloorAt(X, Y) is { } next) { Attach(next, world); return; }
        X = Math.Clamp(X, _on.From, _on.To);
        if (_on.Owner != 0 && _rng.NextDouble() < 0.5) Facing = -Facing;     // the edge of a window: sometimes jump down
        else if (_on.Owner == 0 && (X <= world.Bounds.Left + 1 || X >= world.Bounds.Right - 1)) Facing = -Facing;
        else StartFall(PetState.Fall, 0, 0);
    }

    // ---- walls and ceilings ----------------------------------------------------------------------

    private void GrabWall(Surface wall, World world)
    {
        Attach(wall, world);
        X = wall.At;
        Facing = -wall.Side;                         // look at the wall
        State = PetState.GrabWall;
        _stateTicks = 12 + _rng.Next(25);
        _climbDir = _rng.NextDouble() < 0.7 ? -1 : +1;
        _climbTicks = 150 + _rng.Next(400);
        SetAnim(ShimejiFrames.GrabWall);
    }

    private int _climbTicks;

    private void StartClimbWall()
    {
        if (_on is not { Kind: SurfaceKind.Wall } wall) { StartFall(PetState.Fall, 0, 0); return; }
        // Go up only if there is wall left above, down only if there is wall below.
        if (_climbDir < 0 && Y <= wall.From + 4) _climbDir = +1;
        if (_climbDir > 0 && Y >= wall.To - 4) _climbDir = -1;
        State = PetState.ClimbWall;
        // Usually climbs all the way to the end (about half a pixel per tick); sometimes lets go part-way.
        var distance = _climbDir < 0 ? Y - wall.From : wall.To - Y;
        _climbTicks = _rng.NextDouble() < 0.6 ? (int)(distance / (0.5 * _ps)) + 200 : 150 + _rng.Next(400);
        SetAnim(_climbDir < 0 ? ShimejiFrames.ClimbWallUp : ShimejiFrames.ClimbWallDown);
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
                SetAnim(ShimejiFrames.GrabCeiling);
            }
            else { _climbDir = +1; State = PetState.GrabWall; _stateTicks = 10; SetAnim(ShimejiFrames.GrabWall); }
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
        SetAnim(ShimejiFrames.ClimbCeiling);
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
            SetAnim(ShimejiFrames.GrabWall);
        }
        else Facing = -Facing;
    }

    // ---- reacting and being held -----------------------------------------------------------------

    private static readonly Pose[] ReactPoses =
    [
        new(9, 5, 64, 128, 0, 0), new(7, 5, 64, 128, 0, 0), new(1, 5, 64, 128, 0, 0), new(8, 5, 64, 128, 0, 0), new(10, 5, 64, 128, 0, 0),
        new(1, 8, 64, 128, 0, 0),
    ];

    private void StepReact()
    {
        _reactTicks++;
        if (_reactTicks >= ReactPoses.Sum(p => p.Ticks)) Enter(PetState.Stand, 40 + _rng.Next(60));
    }

    private Pose[] Held(double smoothVx)
    {
        var frame = ShimejiFrames.Pinched(-smoothVx * 4 / _ps, 0);   // the body trails behind the cursor's motion
        return [new Pose(frame, 5, 64, 128, 0, 0)];
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
