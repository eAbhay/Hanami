using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

public sealed class PetBrainTests
{
    private static readonly PxRect Screen = new(0, 0, 1920, 1040);
    private static readonly IReadOnlySet<int> AllFrames = Enumerable.Range(1, ShimejiFrames.NumFrames).ToHashSet();

    private static PetBrain Pet(int seed = 1, double ps = 1, string activity = "normal", PetCapabilities? caps = null, IReadOnlySet<int>? frames = null) =>
        new(new PetProfile(caps ?? new PetCapabilities(true, true, true, true), frames ?? AllFrames, ps, activity), new Random(seed));

    private static void Run(PetBrain pet, World world, int ticks)
    {
        for (var i = 0; i < ticks; i++) pet.Step(world);
    }

    private static World Plain() => World.Build([Screen]);

    [Fact]
    public void SpawnedPet_FallsAndLandsOnTheFloor()
    {
        var world = Plain();
        var pet = Pet();
        pet.Spawn(900, -160);
        Assert.Equal(PetState.Fall, pet.State);
        Run(pet, world, 200);
        Assert.True(pet.State is PetState.Land or PetState.Stand or PetState.Walk or PetState.Run or PetState.Sit or PetState.Lie);
        Assert.Equal(Screen.Bottom, pet.Y);
        Assert.NotNull(pet.Surface);
    }

    [Fact]
    public void Gravity_StopsAtATerminalVelocity()
    {
        var world = World.Build([new PxRect(0, 0, 1920, 100000)]);
        var pet = Pet();
        pet.Spawn(500, 0);
        Run(pet, world, 300);
        Assert.InRange(pet.Vy, 15, 20.5);
    }

    [Fact]
    public void ThrownAtAWall_BouncesBack_AndLands()
    {
        var world = Plain();
        var pet = Pet();
        pet.BeginDrag(100, 500);
        pet.EndDrag(-30, 0);                    // fast to the left, into the left edge
        Assert.Equal(PetState.Thrown, pet.State);
        var maxLeft = double.MaxValue;
        var turned = false;
        for (var i = 0; i < 200; i++)
        {
            pet.Step(world);
            maxLeft = Math.Min(maxLeft, pet.X);
            if (pet.Vx > 0) turned = true;
        }
        Assert.True(turned, "the pet bounced off the wall");
        Assert.True(maxLeft >= Screen.Left);
        Assert.Equal(Screen.Bottom, pet.Y);
    }

    [Fact]
    public void DraggedPet_FollowsTheCursorExactly()
    {
        var world = Plain();
        var pet = Pet();
        pet.Spawn(900, 500);
        Run(pet, world, 5);
        pet.BeginDrag(300, 400);
        foreach (var (x, y) in new[] { (310.0, 405.0), (500.0, 100.0), (-20.0, 2000.0), (640.5, 480.25) })
        {
            pet.DragTo(x, y);
            pet.Step(world);
            Assert.Equal((x, y), (pet.X, pet.Y));
            Assert.Equal(PetState.Dragged, pet.State);
        }
    }

    [Fact]
    public void Dragging_SwingsTheFrameWithTheCursor()
    {
        var world = Plain();
        var pet = Pet();
        pet.BeginDrag(300, 400);
        for (var i = 0; i < 6; i++) { pet.DragTo(300 + 40 * (i + 1), 400); pet.Step(world); }   // moving right, fast
        Assert.Contains(pet.Frame, new[] { 7, 9 });
        for (var i = 0; i < 6; i++) { pet.DragTo(540 - 40 * (i + 1), 400); pet.Step(world); }   // moving left, fast
        Assert.Contains(pet.Frame, new[] { 8, 10 });
    }

    [Fact]
    public void ReleasedPet_IsThrownWithTheReleaseVelocity_AndLands()
    {
        var world = Plain();
        var pet = Pet();
        pet.BeginDrag(800, 300);
        pet.EndDrag(10, -5);
        Assert.Equal((10, -5), (pet.Vx, pet.Vy));
        Run(pet, world, 300);
        Assert.Equal(Screen.Bottom, pet.Y);
        Assert.True(pet.X > 800);
    }

    [Fact]
    public void Click_ReactsOnAFloor_ThenCarriesOn()
    {
        var world = Plain();
        var pet = Pet();
        pet.Spawn(900, 900);
        Run(pet, world, 80);
        pet.Click();
        Assert.Equal(PetState.React, pet.State);
        Run(pet, world, 60);
        Assert.NotEqual(PetState.React, pet.State);
    }

    [Fact]
    public void Click_WhileFalling_IsIgnored()
    {
        var pet = Pet();
        pet.Spawn(900, -160);
        pet.Click();
        Assert.Equal(PetState.Fall, pet.State);
    }

    [Fact]
    public void PackWithoutSitOrSleepFrames_NeverSitsOrLies()
    {
        var world = Plain();
        var caps = new PetCapabilities(false, false, false, false);
        var frames = ShimejiFrames.Required.Concat([1]).ToHashSet();
        var pet = Pet(3, caps: caps, frames: frames);
        pet.Spawn(900, 900);
        var seen = new HashSet<PetState>();
        for (var i = 0; i < 25 * 60 * 20; i++) { pet.Step(world); seen.Add(pet.State); Assert.Contains(pet.Frame, frames); }
        Assert.DoesNotContain(PetState.Sit, seen);
        Assert.DoesNotContain(PetState.Lie, seen);
        Assert.DoesNotContain(PetState.Sleep, seen);
        Assert.DoesNotContain(PetState.GrabWall, seen);
        Assert.DoesNotContain(PetState.GrabCeiling, seen);
    }

    [Fact]
    public void Activity_ChangesHowMuchItRuns()
    {
        static int RunningTicks(string activity)
        {
            var world = Plain();
            var pet = Pet(7, activity: activity, caps: new PetCapabilities(false, false, true, true));
            pet.Spawn(900, 900);
            var n = 0;
            for (var i = 0; i < 25 * 60 * 30; i++) { pet.Step(world); if (pet.State == PetState.Run) n++; }
            return n;
        }
        Assert.True(RunningTicks("lively") > RunningTicks("calm"));
    }

    [Fact]
    public void CalmPet_EventuallySleeps()
    {
        var world = Plain();
        var pet = Pet(2, activity: "calm", caps: new PetCapabilities(false, false, true, true));
        pet.Spawn(900, 900);
        var slept = false;
        for (var i = 0; i < 25 * 60 * 30 && !slept; i++) { pet.Step(world); slept = pet.State == PetState.Sleep; }
        Assert.True(slept);
    }

    [Fact]
    public void Commands_SitAndSleepOnAFloor()
    {
        var world = Plain();
        var pet = Pet();
        pet.Spawn(900, 900);
        Run(pet, world, 60);
        pet.Command(PetCommand.Sleep);
        Assert.Equal(PetState.Sleep, pet.State);
        pet.Command(PetCommand.Sit);
        Assert.Equal(PetState.Sit, pet.State);
    }

    [Fact]
    public void Pet_ClimbsTheScreenEdge_AndTheCeiling()
    {
        // A pet that can climb and is placed at the left edge reaches the ceiling at some point in a long run.
        var world = Plain();
        var pet = Pet(5, activity: "lively");
        pet.Spawn(5, 900);
        var seen = new HashSet<PetState>();
        for (var i = 0; i < 25 * 60 * 30; i++) { pet.Step(world); seen.Add(pet.State); }
        Assert.True(seen.Contains(PetState.ClimbWall), string.Join(",", seen));
        Assert.True(seen.Contains(PetState.GrabCeiling), string.Join(",", seen));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void LongRun_NeverLeavesTheScreen_OrProducesNaN(double ps)
    {
        var world = Plain();
        for (var seed = 0; seed < 12; seed++)
        {
            var pet = Pet(seed, ps, new[] { "calm", "normal", "lively" }[seed % 3]);
            var (sx, sy) = world.SpawnPoint(new Random(seed));
            pet.Spawn(sx, sy);
            for (var i = 0; i < 25 * 60 * 10; i++)   // ten simulated minutes
            {
                pet.Step(world);
                Assert.False(double.IsNaN(pet.X) || double.IsNaN(pet.Y) || double.IsInfinity(pet.X) || double.IsInfinity(pet.Y));
                Assert.True(pet.X >= Screen.Left - 1 && pet.X <= Screen.Right + 1, $"seed {seed} tick {i} state {pet.State} x {pet.X} facing {pet.Facing} surf {pet.Surface}");
                Assert.InRange(pet.Y, Screen.Top - 700, Screen.Bottom + 1);
            }
        }
    }

    [Fact]
    public void SameSeed_GivesTheSameWalk()
    {
        (double, double) Trace(int seed)
        {
            var world = Plain();
            var pet = Pet(seed);
            pet.Spawn(700, 700);
            Run(pet, world, 5000);
            return (pet.X, pet.Y);
        }
        Assert.Equal(Trace(4), Trace(4));
    }

    // ---- monitors --------------------------------------------------------------------------------

    [Fact]
    public void Pet_WalksAcrossTwoMonitorsThatShareAFloor()
    {
        var world = World.Build([Screen, new PxRect(1920, 0, 1920 + 3840, 1040)]);   // a bigger monitor next door
        var pet = Pet(4, activity: "lively", caps: new PetCapabilities(false, false, true, true));
        pet.Spawn(1800, 900);
        var crossed = false;
        for (var i = 0; i < 25 * 60 * 10 && !crossed; i++) { pet.Step(world); crossed = pet.X > 2100; }
        Assert.True(crossed);
        Assert.Equal(1040, pet.Y);
    }

    [Fact]
    public void Pet_OnAMonitorThatIsUnplugged_FallsAndRespawnsOnThePrimary()
    {
        var both = World.Build([Screen, new PxRect(1920, 0, 3840, 1040)]);
        var pet = Pet(1);
        pet.Spawn(3000, 100);
        Run(pet, both, 200);
        Assert.True(pet.X > 1920);
        var one = Plain();
        Run(pet, one, 400);
        Assert.InRange(pet.X, 0, 1920);
        Assert.Equal(1040, pet.Y);
    }

    // ---- windows as terrain ----------------------------------------------------------------------

    private static World WithWindow(PxRect rect, long id = 1) => World.Build([Screen], [new WorldWindow(id, rect)]);

    [Fact]
    public void Pet_LandsOnAWindowsTopEdge()
    {
        var world = WithWindow(new PxRect(400, 500, 1000, 900));
        var pet = Pet();
        pet.Spawn(700, 100);
        Run(pet, world, 150);
        Assert.Equal(500, pet.Y);
        Assert.Equal(1, pet.Surface!.Owner);
    }

    [Fact]
    public void Pet_IsCarriedWhenItsWindowMoves()
    {
        var pet = Pet();
        pet.Spawn(700, 100);
        var world = WithWindow(new PxRect(400, 500, 1000, 900));
        Run(pet, world, 150);
        var x0 = pet.X;
        var moved = WithWindow(new PxRect(480, 530, 1080, 930));
        pet.Step(moved);
        Assert.Equal(530, pet.Y);
        Assert.True(Math.Abs(pet.X - (x0 + 80)) < 6);   // 80 px along, plus at most one step of its own walking
    }

    [Fact]
    public void Pet_FallsWhenItsWindowDisappears()
    {
        var pet = Pet();
        pet.Spawn(700, 100);
        Run(pet, WithWindow(new PxRect(400, 500, 1000, 900)), 150);
        Assert.Equal(500, pet.Y);
        var bare = Plain();
        pet.Step(bare);
        Assert.Equal(PetState.Fall, pet.State);
        Run(pet, bare, 150);
        Assert.Equal(Screen.Bottom, pet.Y);
    }

    [Fact]
    public void Pet_FallsWhenAWindowCoversItsFloor()
    {
        var pet = Pet();
        pet.Spawn(700, 100);
        Run(pet, World.Build([Screen], [new WorldWindow(1, new PxRect(400, 500, 1000, 900))]), 150);
        // another window comes to the front over the whole top edge
        var covered = World.Build([Screen], [new WorldWindow(2, new PxRect(300, 400, 1100, 950)), new WorldWindow(1, new PxRect(400, 500, 1000, 900))]);
        pet.Step(covered);
        Assert.Equal(PetState.Fall, pet.State);
    }

    [Fact]
    public void Pet_ClimbsAWindowSide_AndStepsOntoItsTopEdge()
    {
        // A tall window from the floor up; a climber next to its left side ends up standing on top of it.
        var window = new PxRect(600, 300, 1200, 1040);
        var world = World.Build([Screen], [new WorldWindow(1, window)]);
        var climbed = false;
        for (var seed = 0; seed < 40 && !climbed; seed++)
        {
            var pet = Pet(seed, activity: "lively", caps: new PetCapabilities(true, true, true, true));
            pet.Spawn(590, 900);
            for (var i = 0; i < 25 * 60 * 3 && !climbed; i++)
            {
                pet.Step(world);
                climbed = pet.Surface is { Owner: 1, Kind: SurfaceKind.Floor } && Math.Abs(pet.Y - 300) < 1;
            }
        }
        Assert.True(climbed);
    }

    [Fact]
    public void PetOnAWindow_NeverEndsUpFarOffScreen()
    {
        var world = WithWindow(new PxRect(400, 500, 1000, 900));
        for (var seed = 0; seed < 8; seed++)
        {
            var pet = Pet(seed, activity: "lively");
            pet.Spawn(700, 100);
            for (var i = 0; i < 25 * 60 * 5; i++)
            {
                pet.Step(world);
                Assert.InRange(pet.X, -1, Screen.Right + 1);
                Assert.InRange(pet.Y, -700, Screen.Bottom + 1);
            }
        }
    }
}
