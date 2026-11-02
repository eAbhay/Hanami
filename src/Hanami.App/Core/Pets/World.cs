namespace Hanami.Core.Pets;

/// <summary>A rectangle in physical screen pixels (y grows downwards). Right and Bottom are exclusive edges of the area, used as coordinates of the border.</summary>
public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public PxRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);
}

public enum SurfaceKind { Floor, Wall, Ceiling }

/// <summary>
/// Something a pet can stand on or hold. A floor or ceiling is the horizontal line y = <see cref="At"/> over x in
/// [<see cref="From"/>, <see cref="To"/>]; a wall is the vertical line x = At over y in [From, To]. For a wall,
/// <see cref="Side"/> is the side the pet is on: +1 = to the right of the line (a left screen edge), −1 = to the left
/// (a right screen edge, or the left side of a window). <see cref="Owner"/> is the window it belongs to (0 = the work area).
/// </summary>
public sealed record Surface(SurfaceKind Kind, int At, int From, int To, int Side, long Owner);

/// <summary>An application window as pets see it: top-level, visible, in z-order (topmost first in <see cref="World"/>).</summary>
public sealed record WorldWindow(long Id, PxRect Rect, bool Maximized = false);

/// <summary>
/// The pets' world: surfaces built from the monitors' work areas and (optionally) application windows. Pure and
/// immutable; rebuilt whenever the desktop changes. Window edges only count where no window above covers them.
/// </summary>
public sealed class World
{
    private const int MinSegment = 8;
    private const int Tolerance = 2;

    public IReadOnlyList<PxRect> WorkAreas { get; }
    public IReadOnlyList<WorldWindow> Windows { get; }
    public IReadOnlyList<Surface> Surfaces { get; }
    /// <summary>The union (bounding box) of all work areas.</summary>
    public PxRect Bounds { get; }

    private readonly Surface[] _floors, _walls, _ceilings;
    private readonly Dictionary<long, PxRect> _rects;

    private World(IReadOnlyList<PxRect> areas, IReadOnlyList<WorldWindow> windows, List<Surface> surfaces)
    {
        WorkAreas = areas;
        Windows = windows;
        Surfaces = surfaces;
        Bounds = new PxRect(areas.Min(a => a.Left), areas.Min(a => a.Top), areas.Max(a => a.Right), areas.Max(a => a.Bottom));
        _floors = surfaces.Where(s => s.Kind == SurfaceKind.Floor).ToArray();
        _walls = surfaces.Where(s => s.Kind == SurfaceKind.Wall).ToArray();
        _ceilings = surfaces.Where(s => s.Kind == SurfaceKind.Ceiling).ToArray();
        _rects = windows.ToDictionary(w => w.Id, w => w.Rect);
    }

    /// <summary>Builds the world. <paramref name="windows"/> is in z-order, topmost first; pass none for work-area-only terrain.</summary>
    public static World Build(IEnumerable<PxRect> workAreas, IEnumerable<WorldWindow>? windows = null)
    {
        var areas = workAreas.ToList();
        if (areas.Count == 0) areas.Add(new PxRect(0, 0, 1920, 1040));
        var wins = (windows ?? []).ToList();
        var list = new List<Surface>();

        // Work areas: floor (top of the taskbar), ceiling, and side walls except where another monitor continues them.
        foreach (var a in areas)
        {
            list.Add(new Surface(SurfaceKind.Floor, a.Bottom, a.Left, a.Right, 0, 0));
            list.Add(new Surface(SurfaceKind.Ceiling, a.Top, a.Left, a.Right, 0, 0));
            var left = new List<(int, int)> { (a.Top, a.Bottom) };
            var right = new List<(int, int)> { (a.Top, a.Bottom) };
            foreach (var b in areas.Where(b => b != a))
            {
                if (Math.Abs(b.Right - a.Left) <= 1) left = Subtract(left, (b.Top, b.Bottom));
                if (Math.Abs(b.Left - a.Right) <= 1) right = Subtract(right, (b.Top, b.Bottom));
            }
            list.AddRange(left.Select(i => new Surface(SurfaceKind.Wall, a.Left, i.Item1, i.Item2, +1, 0)));
            list.AddRange(right.Select(i => new Surface(SurfaceKind.Wall, a.Right, i.Item1, i.Item2, -1, 0)));
        }
        var merged = MergeFloors(list);

        var bounds = new PxRect(areas.Min(a => a.Left), areas.Min(a => a.Top), areas.Max(a => a.Right), areas.Max(a => a.Bottom));
        for (var k = 0; k < wins.Count; k++)
        {
            var w = wins[k];
            if (w.Maximized || w.Rect.Width < 8 || w.Rect.Height < 8) continue;
            var r = w.Rect;
            var above = wins.Take(k).ToList();   // windows in front of this one

            // Top edge: a floor, minus the parts covered by windows in front whose vertical span includes it.
            if (r.Top > bounds.Top + 8 && r.Top < bounds.Bottom)
            {
                var floor = new List<(int, int)> { (Math.Max(r.Left, bounds.Left), Math.Min(r.Right, bounds.Right)) };
                foreach (var o in above.Where(o => o.Rect.Top <= r.Top && r.Top < o.Rect.Bottom)) floor = Subtract(floor, (o.Rect.Left, o.Rect.Right));
                merged.AddRange(floor.Where(i => i.Item2 - i.Item1 >= MinSegment).Select(i => new Surface(SurfaceKind.Floor, r.Top, i.Item1, i.Item2, 0, w.Id)));
            }

            // Sides: walls, minus the parts covered by windows in front whose horizontal span includes them.
            foreach (var (x, side) in new[] { (r.Left, -1), (r.Right, +1) })
            {
                if (x <= bounds.Left || x >= bounds.Right) continue;   // an edge on the screen's own border adds nothing
                var wall = new List<(int, int)> { (Math.Max(r.Top, bounds.Top), Math.Min(r.Bottom, bounds.Bottom)) };
                foreach (var o in above.Where(o => o.Rect.Left <= x && x < o.Rect.Right)) wall = Subtract(wall, (o.Rect.Top, o.Rect.Bottom));
                merged.AddRange(wall.Where(i => i.Item2 - i.Item1 >= MinSegment).Select(i => new Surface(SurfaceKind.Wall, x, i.Item1, i.Item2, side, w.Id)));
            }
        }
        return new World(areas, wins, merged);
    }

    /// <summary>Joins work-area floors that touch at the same height (two monitors side by side).</summary>
    private static List<Surface> MergeFloors(List<Surface> all)
    {
        var result = all.Where(s => s.Kind != SurfaceKind.Floor && s.Kind != SurfaceKind.Ceiling).ToList();
        foreach (var kind in new[] { SurfaceKind.Floor, SurfaceKind.Ceiling })
            foreach (var g in all.Where(s => s.Kind == kind).GroupBy(s => s.At))
            {
                Surface? cur = null;
                foreach (var s in g.OrderBy(s => s.From))
                {
                    if (cur is not null && s.From <= cur.To + 1) cur = cur with { To = Math.Max(cur.To, s.To) };
                    else { if (cur is not null) result.Add(cur); cur = s; }
                }
                if (cur is not null) result.Add(cur);
            }
        return result;
    }

    /// <summary>Removes <paramref name="cut"/> from every interval (all inclusive-ish ranges as [from, to]).</summary>
    public static List<(int, int)> Subtract(List<(int, int)> intervals, (int, int) cut)
    {
        var result = new List<(int, int)>();
        foreach (var (a, b) in intervals)
        {
            if (cut.Item2 <= a || cut.Item1 >= b) { result.Add((a, b)); continue; }
            if (cut.Item1 > a) result.Add((a, cut.Item1));
            if (cut.Item2 < b) result.Add((cut.Item2, b));
        }
        return result;
    }

    // ---- queries ---------------------------------------------------------------------------------

    public PxRect? RectOf(long owner) => _rects.TryGetValue(owner, out var r) ? r : null;

    /// <summary>The surface of this kind and owner that covers <paramref name="along"/> and lies within <paramref name="tol"/> of <paramref name="at"/>; the nearest one.</summary>
    public Surface? Find(SurfaceKind kind, long owner, double along, double at, double tol)
    {
        var set = kind switch { SurfaceKind.Floor => _floors, SurfaceKind.Wall => _walls, _ => _ceilings };
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in set)
        {
            if (s.Owner != owner || along < s.From - Tolerance || along > s.To + Tolerance) continue;
            var d = Math.Abs(s.At - at);
            if (d <= tol && d < bestD) { best = s; bestD = d; }
        }
        return best;
    }

    /// <summary>The floor at x within a few pixels of y (any owner), e.g. to continue walking onto the next segment.</summary>
    public Surface? FloorAt(double x, double y, double tol = 3)
    {
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _floors)
        {
            if (x < s.From - Tolerance || x > s.To + Tolerance) continue;
            var d = Math.Abs(s.At - y);
            if (d <= tol && d < bestD) { best = s; bestD = d; }
        }
        return best;
    }

    public Surface? CeilingAt(double x, double y, double tol = 3)
    {
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _ceilings)
        {
            if (x < s.From - Tolerance || x > s.To + Tolerance) continue;
            var d = Math.Abs(s.At - y);
            if (d <= tol && d < bestD) { best = s; bestD = d; }
        }
        return best;
    }

    /// <summary>
    /// The first floor a point crosses moving from (x0, y0) down to (x1, y1) (y1 &gt;= y0): the one with the smallest
    /// height between the two, whose range holds the x where the path meets it. Returns the surface and that x.
    /// </summary>
    public (Surface Floor, double X)? FirstFloorCrossed(double x0, double y0, double x1, double y1)
    {
        if (y1 < y0) return null;
        (Surface, double)? best = null;
        var bestAt = double.MaxValue;
        foreach (var s in _floors)
        {
            if (s.At < y0 - 0.001 || s.At > y1 + 0.001 || s.At >= bestAt) continue;
            var t = y1 - y0 < 1e-9 ? 0 : (s.At - y0) / (y1 - y0);
            var x = x0 + (x1 - x0) * t;
            if (x < s.From - Tolerance || x > s.To + Tolerance) continue;
            best = (s, x);
            bestAt = s.At;
        }
        return best;
    }

    /// <summary>
    /// The first wall met moving horizontally from x0 to x1 at height y: walls the mover approaches from their open
    /// side (a pet moving left meets walls with Side +1, moving right Side −1), nearest first.
    /// </summary>
    public Surface? FirstWallCrossed(double x0, double x1, double y)
    {
        if (Math.Abs(x1 - x0) < 1e-9) return null;
        var dir = Math.Sign(x1 - x0);
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _walls)
        {
            if (s.Side != -dir || y < s.From - Tolerance || y > s.To + Tolerance) continue;
            var d = (s.At - x0) * dir;   // distance ahead
            if (d < -0.001 || d > (x1 - x0) * dir + 0.001 || d >= bestD) continue;
            best = s;
            bestD = d;
        }
        return best;
    }

    /// <summary>
    /// The first wall met moving horizontally from x0 to x1 by a body spanning <paramref name="headY"/> (top) to
    /// <paramref name="feetY"/>: a wall counts when its span overlaps that vertical range. Same sides and order as the point version.
    /// </summary>
    public Surface? FirstWallCrossed(double x0, double x1, double headY, double feetY)
    {
        if (Math.Abs(x1 - x0) < 1e-9) return null;
        var dir = Math.Sign(x1 - x0);
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _walls)
        {
            if (s.Side != -dir || feetY < s.From - Tolerance || headY > s.To + Tolerance) continue;
            var d = (s.At - x0) * dir;
            if (d < -0.001 || d > (x1 - x0) * dir + 0.001 || d >= bestD) continue;
            best = s;
            bestD = d;
        }
        return best;
    }

    /// <summary>The nearest wall (either side) within <paramref name="reach"/> of x whose span holds y.</summary>
    public Surface? NearestWall(double x, double y, double reach)
    {
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _walls)
        {
            if (y < s.From - Tolerance || y > s.To + Tolerance) continue;
            var d = Math.Abs(s.At - x);
            if (d <= reach && d < bestD) { best = s; bestD = d; }
        }
        return best;
    }

    /// <summary>
    /// The nearest floor whose range holds x with its height in [y − <paramref name="below"/>, y + <paramref name="above"/>],
    /// i.e. up to <paramref name="below"/> px above the point and <paramref name="above"/> px under it (a pet let go a few
    /// pixels inside a title bar still lands on it). Floors are already cut where a window in front covers them.
    /// </summary>
    public Surface? DropFloor(double x, double y, double above, double below)
    {
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _floors)
        {
            if (x < s.From - Tolerance || x > s.To + Tolerance || s.At < y - below || s.At > y + above) continue;
            var d = Math.Abs(s.At - y);
            if (d < bestD) { best = s; bestD = d; }
        }
        return best;
    }

    /// <summary>The first ceiling a point crosses moving up from (x0, y0) to (x1, y1) (y1 &lt;= y0), and the x where it does.</summary>
    public (Surface Ceiling, double X)? FirstCeilingCrossed(double x0, double y0, double x1, double y1)
    {
        if (y1 > y0) return null;
        (Surface, double)? best = null;
        var bestAt = double.MinValue;
        foreach (var s in _ceilings)
        {
            if (s.At > y0 + 0.001 || s.At < y1 - 0.001 || s.At <= bestAt) continue;
            var t = y0 - y1 < 1e-9 ? 0 : (y0 - s.At) / (y0 - y1);
            var x = x0 + (x1 - x0) * t;
            if (x < s.From - Tolerance || x > s.To + Tolerance) continue;
            best = (s, x);
            bestAt = s.At;
        }
        return best;
    }

    /// <summary>
    /// A wall of another window a pet standing at (x, feetY) could jump to: its open side faces the pet, it lies within
    /// <paramref name="maxDistance"/> ahead, and its bottom is no more than <paramref name="reach"/> above the feet
    /// (or it reaches down to them). The nearest first.
    /// </summary>
    public Surface? ReachableWall(double x, double feetY, double reach, double maxDistance)
    {
        Surface? best = null;
        var bestD = double.MaxValue;
        foreach (var s in _walls)
        {
            if (s.Owner == 0) continue;
            var ahead = (x - s.At) * s.Side;      // > 0: the pet is on the wall's open side
            if (ahead < 0 || ahead > maxDistance) continue;
            if (s.To < feetY - reach || s.From > feetY + Tolerance) continue;
            if (ahead < bestD) { best = s; bestD = ahead; }
        }
        return best;
    }

    /// <summary>
    /// The floor of another window to hop to from (x, y) in direction <paramref name="facing"/> (±1): the nearest one ahead
    /// whose gap is at most <paramref name="gapMax"/> and whose height differs by at most <paramref name="riseMax"/>, with
    /// the x to land on. A window's own floor (<paramref name="exceptOwner"/>) never counts.
    /// </summary>
    public (Surface Floor, double X)? HopTarget(double x, double y, int facing, double gapMax, double riseMax, long exceptOwner = -1)
    {
        (Surface, double)? best = null;
        var bestGap = double.MaxValue;
        foreach (var s in _floors)
        {
            if (s.Owner == 0 || s.Owner == exceptOwner || Math.Abs(s.At - y) > riseMax) continue;
            var gap = facing > 0 ? s.From - x : x - s.To;
            if (gap < 0 || gap > gapMax || gap >= bestGap) continue;
            var inset = Math.Min(16, (s.To - s.From) / 2);
            best = (s, facing > 0 ? s.From + inset : s.To - inset);
            bestGap = gap;
        }
        return best;
    }

    /// <summary>Whether a point is over any monitor's work area (with a margin), i.e. somewhere a pet can be.</summary>
    public bool InsideWorkArea(double x, double y, int margin = 0) =>
        WorkAreas.Any(a => x >= a.Left - margin && x <= a.Right + margin && y >= a.Top - margin && y <= a.Bottom + margin);

    /// <summary>A random spot above the primary (first) work area to start falling from.</summary>
    public (double X, double Y) SpawnPoint(Random rng)
    {
        var a = WorkAreas[0];
        var margin = Math.Min(200, a.Width / 4);
        return (a.Left + margin + rng.NextDouble() * Math.Max(1, a.Width - 2 * margin), a.Top - 160);
    }
}
