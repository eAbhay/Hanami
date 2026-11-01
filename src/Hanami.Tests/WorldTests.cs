using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

public sealed class WorldTests
{
    private static readonly PxRect Screen = new(0, 0, 1920, 1040);

    private static IEnumerable<Surface> Of(World w, SurfaceKind kind, long owner) => w.Surfaces.Where(s => s.Kind == kind && s.Owner == owner);

    [Fact]
    public void WorkArea_HasFloorCeilingAndBothWalls()
    {
        var w = World.Build([Screen]);
        Assert.Equal(new Surface(SurfaceKind.Floor, 1040, 0, 1920, 0, 0), Assert.Single(Of(w, SurfaceKind.Floor, 0)));
        Assert.Equal(new Surface(SurfaceKind.Ceiling, 0, 0, 1920, 0, 0), Assert.Single(Of(w, SurfaceKind.Ceiling, 0)));
        var walls = Of(w, SurfaceKind.Wall, 0).OrderBy(s => s.At).ToList();
        Assert.Equal(new Surface(SurfaceKind.Wall, 0, 0, 1040, +1, 0), walls[0]);
        Assert.Equal(new Surface(SurfaceKind.Wall, 1920, 0, 1040, -1, 0), walls[1]);
    }

    [Fact]
    public void FallingPath_MeetsTheFirstFloor()
    {
        var w = World.Build([Screen], [new WorldWindow(1, new PxRect(400, 500, 1000, 900))]);
        var hit = w.FirstFloorCrossed(700, 100, 700, 1100);
        Assert.Equal(500, hit!.Value.Floor.At);
        Assert.Equal(1040, w.FirstFloorCrossed(1500, 100, 1500, 1100)!.Value.Floor.At);   // beside the window
        Assert.Null(w.FirstFloorCrossed(700, 600, 700, 700));                              // already below its top edge, nothing before the floor
    }

    [Fact]
    public void WindowEdges_ArePartlyHiddenByWindowsInFront()
    {
        // Back window 400–1000 × 500–900; front window covers its top-right part.
        var back = new WorldWindow(2, new PxRect(400, 500, 1000, 900));
        var front = new WorldWindow(1, new PxRect(800, 400, 1300, 700));
        var w = World.Build([Screen], [front, back]);

        var floor = Assert.Single(Of(w, SurfaceKind.Floor, 2));
        Assert.Equal((500, 400, 800), (floor.At, floor.From, floor.To));   // the part under the front window is gone

        // The back window's right wall (x=1000) is hidden by the front window from y 400 to 700.
        var wall = Assert.Single(Of(w, SurfaceKind.Wall, 2), s => s.At == 1000);
        Assert.Equal((700, 900), (wall.From, wall.To));
        // The front window is fully visible.
        Assert.Single(Of(w, SurfaceKind.Floor, 1));
        Assert.Equal(2, Of(w, SurfaceKind.Wall, 1).Count());
    }

    [Fact]
    public void FullyCoveredEdge_GivesNoSurface()
    {
        var back = new WorldWindow(2, new PxRect(500, 500, 900, 800));
        var front = new WorldWindow(1, new PxRect(300, 400, 1100, 900));   // hides back's top edge and both sides
        var w = World.Build([Screen], [front, back]);
        Assert.Empty(Of(w, SurfaceKind.Floor, 2));
        Assert.Empty(Of(w, SurfaceKind.Wall, 2));
    }

    [Fact]
    public void MaximizedWindows_GiveNoEdges_ButStillCoverWhatIsBehind()
    {
        var max = new WorldWindow(1, Screen, Maximized: true);
        var behind = new WorldWindow(2, new PxRect(400, 500, 1000, 900));
        var w = World.Build([Screen], [max, behind]);
        Assert.Empty(w.Surfaces.Where(s => s.Owner == 1));
        Assert.Empty(w.Surfaces.Where(s => s.Owner == 2));   // hidden by the maximized one
    }

    [Fact]
    public void Subtract_SplitsIntervals()
    {
        var r = World.Subtract([(0, 100)], (40, 60));
        Assert.Equal([(0, 40), (60, 100)], r);
        Assert.Equal([(0, 100)], World.Subtract([(0, 100)], (200, 300)));
        Assert.Empty(World.Subtract([(10, 20)], (0, 100)));
    }

    [Fact]
    public void TwoMonitorsSideBySide_ShareOneFloor_AndHaveNoWallBetween()
    {
        var a = new PxRect(0, 0, 1920, 1040);
        var b = new PxRect(1920, 0, 3840, 1040);
        var w = World.Build([a, b]);
        var floor = Assert.Single(Of(w, SurfaceKind.Floor, 0));
        Assert.Equal((0, 3840), (floor.From, floor.To));
        Assert.DoesNotContain(w.Surfaces, s => s.Kind == SurfaceKind.Wall && s.At == 1920);
        Assert.Equal(2, Of(w, SurfaceKind.Wall, 0).Count());
        Assert.Equal(new PxRect(0, 0, 3840, 1040), w.Bounds);
    }

    [Fact]
    public void MonitorsOfDifferentHeight_LeaveAWallWhereTheOtherIsShorter()
    {
        var a = new PxRect(0, 0, 1920, 1040);
        var b = new PxRect(1920, 400, 3840, 1040);   // starts lower
        var w = World.Build([a, b]);
        var wall = Assert.Single(Of(w, SurfaceKind.Wall, 0), s => s.At == 1920);
        Assert.Equal((0, 400), (wall.From, wall.To));
    }

    [Fact]
    public void MonitorsAtDifferentFloorHeights_KeepSeparateFloors()
    {
        var a = new PxRect(0, 0, 1920, 1040);
        var b = new PxRect(1920, 0, 3840, 1000);
        var w = World.Build([a, b]);
        Assert.Equal(2, Of(w, SurfaceKind.Floor, 0).Count());
        // dropping off the taller floor's right end lands on the lower... here the other is higher, so it is simply a wall
        Assert.NotNull(w.FloorAt(2500, 1000));
        Assert.Null(w.FloorAt(2500, 1040));
    }

    [Fact]
    public void WallCrossing_OnlyFromTheOpenSide()
    {
        var w = World.Build([Screen]);
        Assert.Equal(0, w.FirstWallCrossed(50, -10, 500)!.At);     // moving left meets the left edge
        Assert.Null(w.FirstWallCrossed(50, 100, 500));              // moving right: the left edge is behind
        Assert.Equal(1920, w.FirstWallCrossed(1900, 1930, 500)!.At);
        Assert.Null(w.FirstWallCrossed(50, -10, 2000));             // below the wall's range
    }

    [Fact]
    public void Find_RespectsOwnerAndTolerance()
    {
        var w = World.Build([Screen], [new WorldWindow(7, new PxRect(400, 500, 1000, 900))]);
        Assert.NotNull(w.Find(SurfaceKind.Floor, 7, 700, 505, 10));
        Assert.Null(w.Find(SurfaceKind.Floor, 7, 700, 540, 10));
        Assert.Null(w.Find(SurfaceKind.Floor, 8, 700, 500, 10));
        Assert.Equal(new PxRect(400, 500, 1000, 900), w.RectOf(7));
        Assert.Null(w.RectOf(8));
    }

    [Fact]
    public void SpawnPoint_IsAboveThePrimaryMonitor()
    {
        var w = World.Build([Screen, new PxRect(1920, 0, 3840, 1040)]);
        for (var i = 0; i < 50; i++)
        {
            var (x, y) = w.SpawnPoint(new Random(i));
            Assert.InRange(x, 0, 1920);
            Assert.True(y < 0);
        }
    }
}
