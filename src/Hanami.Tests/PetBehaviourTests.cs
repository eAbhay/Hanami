using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>Round 6 pet behaviour: dropping onto windows and latching, window edges and being blocked, window play and reactions, companions, stroking, Stay and Follow.</summary>
public sealed class PetBehaviourTests
{
    private static readonly PxRect Screen = new(0, 0, 1920, 1040);
    private static readonly IReadOnlySet<int> AllFrames = Enumerable.Range(1, ShimejiFrames.NumFrames).ToHashSet();
    private static readonly PetCapabilities Everything = new(true, true, true, true);
    private static readonly PetCapabilities NoClimb = new(false, false, true, true);

    private static PetBrain Pet(int seed = 1, string activity = "normal", PetCapabilities? caps = null) =>
        new(new PetProfile(caps ?? Everything, AllFrames, 1, activity), new Random(seed));

    private static void Run(PetBrain pet, World world, int ticks)
    {
        for (var i = 0; i < ticks; i++) pet.Step(world);
    }

    private static World Plain() => World.Build([Screen]);
    private static World Windows(params WorldWindow[] windows) => World.Build([Screen], windows);
    private static World Window(PxRect rect, long id = 1) => Windows(new WorldWindow(id, rect));

    private static PetBrain Dropped(World world, double x, double y, double vx = 0, double vy = 0, PetCapabilities? caps = null, int seed = 1)
    {
        var pet = Pet(seed, caps: caps);
        pet.BeginDrag(x, y);
        pet.EndDrag(vx, vy, world);
        return pet;
    }

    // ---- world queries ---------------------------------------------------------------------------

    [Fact]
    public void World_NearestWall_DropFloor_AndCeilingCrossing()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        Assert.Equal(1000, world.NearestWall(1010, 700, 40)!.At);
        Assert.Null(world.NearestWall(1100, 700, 40));        // too far
        Assert.Null(world.NearestWall(1010, 950, 40));        // below the wall's span
        Assert.Equal(0, world.NearestWall(15, 300, 40)!.At);

        Assert.Equal(500, world.DropFloor(700, 515, 24, 56)!.At);   // inside the title bar
        Assert.Equal(500, world.DropFloor(700, 485, 24, 56)!.At);   // just above it
        Assert.Equal(Screen.Bottom, world.DropFloor(100, 1030, 24, 56)!.At);
        Assert.Null(world.DropFloor(700, 700, 24, 56));              // deep in the window

        var hit = world.FirstCeilingCrossed(900, 100, 900, -50);
        Assert.NotNull(hit);
        Assert.Equal(0, hit!.Value.Ceiling.At);
        Assert.Null(world.FirstCeilingCrossed(900, 400, 900, 200));
        Assert.Null(world.FirstCeilingCrossed(900, 100, 900, 200));  // going down
    }

    [Fact]
    public void World_FirstWallCrossed_UsesTheWholeBody()
    {
        var world = Window(new PxRect(600, 800, 1200, 960));        // a floating window, its bottom 80 px above the floor
        Assert.Null(world.FirstWallCrossed(500, 650, 1040 - 6, 1040 - 6));    // the feet pass under it
        Assert.NotNull(world.FirstWallCrossed(500, 650, 1040 - 128, 1040 - 6));  // the body doesn't
    }

    [Fact]
    public void World_ReachableWall_StopsAtReach_AndHopTargetsAreNearbyWindowsOnly()
    {
        var near = Window(new PxRect(600, 800, 1200, 960));
        Assert.NotNull(near.ReachableWall(450, 1040, 256, 600));
        Assert.Null(near.ReachableWall(450, 1040, 256, 100));        // too far away
        Assert.Null(Window(new PxRect(600, 100, 1200, 500)).ReachableWall(450, 1040, 256, 600));   // bottom 540 px above the feet
        Assert.Null(near.ReachableWall(1300, 1040, 256, 600) is { Side: -1 } ? near : null);       // its left side doesn't face a pet on the right

        var two = Windows(new WorldWindow(1, new PxRect(400, 500, 900, 900)), new WorldWindow(2, new PxRect(1000, 520, 1500, 900)));
        var hop = two.HopTarget(890, 500, +1, 384, 192, exceptOwner: 1);
        Assert.NotNull(hop);
        Assert.Equal(2, hop!.Value.Floor.Owner);
        Assert.Null(two.HopTarget(890, 500, +1, 50, 192, exceptOwner: 1));      // gap too wide
        Assert.Null(two.HopTarget(890, 500, -1, 384, 192, exceptOwner: 1));     // nothing that way
    }

    // ---- phase 4: dropping and latching ----------------------------------------------------------

    [Fact]
    public void DroppedInsideATitleBar_OrJustAboveIt_LandsOnTheWindow()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        foreach (var y in new[] { 515.0, 485.0 })
        {
            var pet = Dropped(world, 700, y);
            Assert.Equal(PetState.Stand, pet.State);
            Assert.Equal(500, pet.Y);
            Assert.Equal(1, pet.Surface!.Owner);
        }
    }

    [Fact]
    public void DroppedDeepInsideAWindow_Falls_ToTheFloor()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        var pet = Dropped(world, 700, 700);
        Assert.Equal(PetState.Thrown, pet.State);
        Run(pet, world, 200);
        Assert.Equal(Screen.Bottom, pet.Y);
    }

    [Fact]
    public void DroppedBesideAWindowSide_OnAScreenEdge_OrAtTheCeiling_LatchesAndStaysThroughTheHold()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        var side = Dropped(world, 1010, 700);
        Assert.Equal(PetState.GrabWall, side.State);
        Assert.Equal(1000, side.X);
        Assert.Equal(1, side.Surface!.Owner);

        var edge = Dropped(world, 15, 600);
        Assert.Equal(PetState.GrabWall, edge.State);
        Assert.Equal(0, edge.X);

        var ceiling = Dropped(world, 900, 20);
        Assert.Equal(PetState.GrabCeiling, ceiling.State);
        Assert.Equal(0, ceiling.Y);

        // It hangs on for 5 – 20 s before it starts climbing.
        foreach (var pet in new[] { side, edge, ceiling })
        {
            var start = pet.State;
            Run(pet, world, 120);
            Assert.Equal(start, pet.State);
        }
    }

    [Fact]
    public void LatchedPet_StartsClimbingAfterTheHold()
    {
        var world = Plain();
        var pet = Dropped(world, 15, 600);
        Run(pet, world, 25 * 21);
        Assert.NotEqual(PetState.GrabWall, pet.State);
    }

    [Fact]
    public void PackWithoutClimbFrames_IsNotLatched()
    {
        var world = Plain();
        Assert.Equal(PetState.Thrown, Dropped(world, 15, 600, caps: NoClimb).State);
        Assert.Equal(PetState.Thrown, Dropped(world, 900, 20, caps: NoClimb).State);
    }

    [Fact]
    public void ACoveredTitleBar_DoesNotCatchADrop()
    {
        var world = Windows(new WorldWindow(2, new PxRect(300, 400, 1100, 950)), new WorldWindow(1, new PxRect(400, 500, 1000, 900)));
        Assert.Equal(PetState.Thrown, Dropped(world, 700, 510).State);
    }

    [Fact]
    public void ACorner_PicksTheCloserSurface()
    {
        var world = Plain();
        Assert.Equal(PetState.Stand, Dropped(world, 12, 1030).State);       // floor 10 away, wall 12
        Assert.Equal(PetState.GrabWall, Dropped(world, 5, 1030).State);     // wall 5 away, floor 10
    }

    [Fact]
    public void FastThrowIntoAWall_OrCeiling_Latches_ButOpenAirDoesNot()
    {
        var world = Plain();
        var wall = Dropped(world, 100, 500, vx: -30);
        var seen = new HashSet<PetState>();
        for (var i = 0; i < 20; i++) { wall.Step(world); seen.Add(wall.State); }
        Assert.Contains(PetState.GrabWall, seen);
        Assert.Equal(0, wall.X);

        var up = Dropped(world, 900, 60, vy: -30);
        seen.Clear();
        for (var i = 0; i < 20; i++) { up.Step(world); seen.Add(up.State); }
        Assert.Contains(PetState.GrabCeiling, seen);

        var open = Dropped(world, 900, 500, vx: 8, vy: -3);
        seen.Clear();
        for (var i = 0; i < 120; i++) { open.Step(world); seen.Add(open.State); }
        Assert.DoesNotContain(PetState.GrabWall, seen);
        Assert.DoesNotContain(PetState.GrabCeiling, seen);
    }

    // ---- phase 5: window edges and being blocked -------------------------------------------------

    private static readonly PxRect Floating = new(600, 800, 1200, 960);     // bottom 80 px above the floor

    [Fact]
    public void PetWalksPastAWindowFarOverhead()
    {
        var world = Window(new PxRect(600, 100, 1200, 500));
        var pet = Pet(2, "lively", NoClimb);
        pet.Spawn(900, 900);
        double min = double.MaxValue, max = double.MinValue;
        for (var i = 0; i < 25 * 60 * 5; i++) { pet.Step(world); min = Math.Min(min, pet.X); max = Math.Max(max, pet.X); }
        Assert.True(min < 580 && max > 1220, $"walked {min}..{max}");
    }

    [Fact]
    public void AWindowAtHeadHeight_BlocksAPetThatCannotClimb()
    {
        var world = Window(Floating);
        var pet = Pet(2, "lively", NoClimb);
        pet.Spawn(400, 900);
        var max = double.MinValue;
        for (var i = 0; i < 25 * 60 * 5; i++) { pet.Step(world); if (pet.State is not (PetState.Fall or PetState.Thrown)) max = Math.Max(max, pet.X); }
        Assert.True(max <= 600.5, $"got as far as {max}");
        Assert.True(max > 500, "it did walk up to the window");
    }

    [Fact]
    public void ABlockedClimber_JumpsToTheWindowSide_AndGrabsIt()
    {
        var world = Window(Floating);
        var jumped = false;
        var grabbed = false;
        for (var seed = 0; seed < 30 && !grabbed; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(450, 900);
            for (var i = 0; i < 25 * 60 * 2 && !grabbed; i++)
            {
                pet.Step(world);
                if (pet.State == PetState.Jump) jumped = true;
                if (jumped && pet.State is PetState.GrabWall or PetState.ClimbWall && pet.Surface is { Owner: 1 }) grabbed = true;
            }
        }
        Assert.True(jumped, "it jumped");
        Assert.True(grabbed, "it took hold of the window's side");
    }

    [Fact]
    public void APetNeverJumpsOrSeeksBeyondItsReach()
    {
        var world = Window(new PxRect(600, 100, 1200, 500));
        var pet = Pet(3, "lively");
        pet.Spawn(450, 900);
        for (var i = 0; i < 25 * 60 * 3; i++)
        {
            pet.Step(world);
            if (pet.Surface is { Owner: 0, Kind: SurfaceKind.Floor }) Assert.NotEqual(PetState.Jump, pet.State);
            Assert.NotEqual(PetState.Seek, pet.State);
        }
    }

    [Fact]
    public void TargetClosingMidJump_MakesThePetFall()
    {
        var world = Window(Floating);
        for (var seed = 0; seed < 40; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(450, 900);
            for (var i = 0; i < 25 * 60 * 2 && pet.State != PetState.Jump; i++) pet.Step(world);
            if (pet.State != PetState.Jump) continue;
            pet.Step(world);
            Assert.Equal(PetState.Jump, pet.State);
            pet.Step(Plain());                        // the window is gone
            Assert.Equal(PetState.Fall, pet.State);
            Run(pet, Plain(), 200);
            Assert.Equal(Screen.Bottom, pet.Y);
            return;
        }
        Assert.Fail("no pet ever jumped");
    }

    [Fact]
    public void AJump_FollowsAnArcOverThePathBetweenItsEnds()
    {
        var world = Window(Floating);
        for (var seed = 0; seed < 40; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(450, 900);
            for (var i = 0; i < 25 * 60 * 2 && pet.State != PetState.Jump; i++) pet.Step(world);
            if (pet.State != PetState.Jump) continue;
            double startY = pet.Y, lowest = pet.Y;
            var n = 0;
            while (pet.State == PetState.Jump && n++ < 200) { pet.Step(world); lowest = Math.Min(lowest, pet.Y); }
            Assert.True(lowest < startY - 20, "it rose above its start");
            Assert.True(n > 14, "it took at least the minimum number of ticks");
            return;
        }
        Assert.Fail("no pet ever jumped");
    }

    [Fact]
    public void AnEdgeOfAWindow_SometimesLetsThePetClimbDown_ThenFallToTheFloor()
    {
        var world = Window(new PxRect(400, 500, 1000, 700));          // floating: the side ends 340 px above the floor
        var climbedDown = false;
        for (var seed = 0; seed < 40 && !climbedDown; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(700, 100);
            for (var i = 0; i < 25 * 60 * 3 && !climbedDown; i++)
            {
                pet.Step(world);
                if (pet.State == PetState.ClimbWall && pet.Surface is { Owner: 1 } && pet.Y > 520) climbedDown = true;
            }
            if (!climbedDown) continue;
            Run(pet, world, 25 * 60 * 2);                              // it ends up somewhere sensible
            Assert.InRange(pet.Y, 0, Screen.Bottom);
        }
        Assert.True(climbedDown);
    }

    [Fact]
    public void ASeekingPet_ReachesTheTopOfAWindow()
    {
        var world = Window(new PxRect(900, 500, 1500, 1040));
        var sought = false;
        var reached = false;
        for (var seed = 0; seed < 60 && !reached; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(300, 900);
            for (var i = 0; i < 25 * 60 * 3 && !reached; i++)
            {
                pet.Step(world);
                if (pet.State == PetState.Seek) sought = true;
                reached = sought && pet.Surface is { Owner: 1, Kind: SurfaceKind.Floor } && Math.Abs(pet.Y - 500) < 1;
            }
        }
        Assert.True(sought, "it went looking for the window");
        Assert.True(reached, "and got on top");
    }

    [Fact]
    public void WithWindows_NoPetEverLeavesTheScreen()
    {
        var world = Windows(new WorldWindow(1, Floating), new WorldWindow(2, new PxRect(1300, 300, 1700, 700)));
        for (var seed = 0; seed < 10; seed++)
        {
            var pet = Pet(seed, new[] { "calm", "normal", "lively" }[seed % 3]);
            var (sx, sy) = world.SpawnPoint(new Random(seed));
            pet.Spawn(sx, sy);
            for (var i = 0; i < 25 * 60 * 6; i++)
            {
                pet.Step(world);
                Assert.False(double.IsNaN(pet.X) || double.IsNaN(pet.Y));
                Assert.InRange(pet.X, Screen.Left - 1, Screen.Right + 1);
                Assert.InRange(pet.Y, Screen.Top - 700, Screen.Bottom + 1);
            }
        }
    }

    // ---- phase 6: window play and reactions ------------------------------------------------------

    [Fact]
    public void AtAWindowEdge_APetSometimesSitsWithItsLegsDangling()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        var perched = false;
        for (var seed = 0; seed < 40 && !perched; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(700, 100);
            for (var i = 0; i < 25 * 60 * 4 && !perched; i++)
            {
                pet.Step(world);
                if (pet.State == PetState.Perch)
                {
                    perched = true;
                    Assert.Equal(1, pet.Surface!.Owner);
                    Assert.Equal(500, pet.Y);
                    Assert.True(pet.X is > 400 and < 1000);
                }
            }
        }
        Assert.True(perched);
    }

    [Fact]
    public void PackWithoutSit_NeverPerches()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        var caps = new PetCapabilities(true, true, false, false);
        for (var seed = 0; seed < 6; seed++)
        {
            var pet = Pet(seed, "lively", caps);
            pet.Spawn(700, 100);
            for (var i = 0; i < 25 * 60 * 3; i++) { pet.Step(world); Assert.NotEqual(PetState.Perch, pet.State); }
        }
    }

    [Fact]
    public void APet_CanHopBetweenTwoCloseWindows()
    {
        var world = Windows(new WorldWindow(1, new PxRect(400, 500, 900, 900)), new WorldWindow(2, new PxRect(1000, 520, 1500, 900)));
        var hopped = false;
        for (var seed = 0; seed < 60 && !hopped; seed++)
        {
            var pet = Pet(seed, "lively");
            pet.Spawn(700, 100);
            var jumped = false;
            for (var i = 0; i < 25 * 60 * 4 && !hopped; i++)
            {
                pet.Step(world);
                if (pet.State == PetState.Jump) jumped = true;
                hopped = jumped && pet.Surface is { Owner: 2, Kind: SurfaceKind.Floor };
            }
        }
        Assert.True(hopped);
    }

    [Fact]
    public void ShakingTheWindow_ThrowsThePetOff_ButDraggingItKeepsCarrying()
    {
        PetBrain OnAWindow(out PxRect rect)
        {
            rect = new PxRect(400, 500, 1000, 900);
            var pet = Pet(1);
            pet.Spawn(700, 100);
            Run(pet, Window(rect), 150);
            Assert.Equal(1, pet.Surface!.Owner);
            return pet;
        }

        // a slow drag, 5 px per tick one way
        var drag = OnAWindow(out var r);
        for (var i = 1; i <= 40; i++) drag.Step(Window(r.Offset(5 * i, 0)));
        Assert.Equal(1, drag.Surface!.Owner);

        // back and forth at 30 px per tick
        var shake = OnAWindow(out r);
        var thrown = false;
        for (var i = 1; i <= 8 && !thrown; i++)
        {
            shake.Step(Window(r.Offset(i % 2 == 0 ? 0 : 30, 0)));
            thrown = shake.State == PetState.Thrown;
        }
        Assert.True(thrown);

        // one violent jerk
        var jerk = OnAWindow(out r);
        jerk.Step(Window(r.Offset(150, 0)));
        Assert.Equal(PetState.Thrown, jerk.State);
    }

    [Fact]
    public void WhenItsWindowCloses_ThePetHopsAndShowsAnExclamationMark_ButNotWhenMerelyCovered()
    {
        var rect = new PxRect(400, 500, 1000, 900);
        PetBrain OnAWindow()
        {
            var pet = Pet(1);
            pet.Spawn(700, 100);
            Run(pet, Window(rect), 150);
            return pet;
        }

        var closed = OnAWindow();
        closed.Step(Plain());
        Assert.Equal(PetState.Fall, closed.State);
        Assert.Equal(PetMark.Startled, closed.Mark);
        Assert.InRange(closed.MarkProgress, 0, 1);
        Assert.True(closed.Vy < 0, "it hopped");
        Run(closed, Plain(), 30);
        Assert.Equal(PetMark.None, closed.Mark);

        var covered = OnAWindow();
        covered.Step(Windows(new WorldWindow(2, new PxRect(300, 400, 1100, 950)), new WorldWindow(1, rect)));
        Assert.Equal(PetState.Fall, covered.State);
        Assert.Equal(PetMark.None, covered.Mark);
    }

    // ---- phase 7: companions, stroking, Stay and Follow ------------------------------------------

    private static PetBrain Standing(World world, double x, int seed)
    {
        var p = Pet(seed);
        p.Spawn(x, 900);
        for (var i = 0; i < 600 && !p.IsFree; i++) p.Step(world);
        return p;
    }

    [Fact]
    public void TwoClosePetsOnTheSameFloor_MeetAndShowHearts()
    {
        var world = Plain();
        var a = Standing(world, 900, 1);
        var b = Standing(world, 950, 2);
        Assert.True(a.IsFree && b.IsFree && Math.Abs(a.X - b.X) <= 90);
        new PetSocial().Update([("a", a), ("b", b)], 0, "normal", new Random(1));
        Assert.Equal(PetState.Meet, a.State);
        Assert.Equal(PetState.Meet, b.State);
        Assert.Equal(PetMark.Hearts, a.Mark);
        Assert.Equal(a.X < b.X ? 1 : -1, a.Facing);
        Assert.Equal(b.X < a.X ? 1 : -1, b.Facing);
    }

    [Fact]
    public void PetsFarApart_OnDifferentSurfaces_OrBusy_DoNotMeet()
    {
        var world = Window(new PxRect(400, 500, 1000, 900));
        var a = Pet(1); var b = Pet(2);
        a.Spawn(700, 100);     // lands on the window
        b.Spawn(720, 900);     // under it on the floor... falls past? the window is only 600 wide, so it lands on the taskbar floor
        Run(a, world, 150); Run(b, world, 150);
        var pets = new List<(string Id, PetBrain Brain)> { ("a", a), ("b", b) };
        var social = new PetSocial();
        social.Update(pets, 0, "normal", new Random(1));
        Assert.NotEqual(PetState.Meet, a.State);
        Assert.NotEqual(PetState.Meet, b.State);

        var c = Pet(3); var d = Pet(4);
        c.Spawn(200, 900); d.Spawn(1500, 900);
        Run(c, Plain(), 150); Run(d, Plain(), 150);
        social.Update([("c", c), ("d", d)], 0, "normal", new Random(1));
        Assert.NotEqual(PetState.Meet, c.State);
    }

    [Fact]
    public void PairCooldown_BlocksAnotherMeetingForAMinute()
    {
        var social = new PetSocial();
        var first = new List<(string, PetBrain)> { ("a", Standing(Plain(), 900, 1)), ("b", Standing(Plain(), 950, 2)) };
        social.Update(first, 0, "normal", new Random(1));
        Assert.Equal(PetState.Meet, first[0].Item2.State);

        // the same two, standing close again, 4 s and 59 s later: still cooling down; after 60 s they meet again
        var later = new List<(string, PetBrain)> { ("a", Standing(Plain(), 900, 3)), ("b", Standing(Plain(), 950, 4)) };
        Assert.True(later.All(p => p.Item2.IsFree));
        social.Update(later, 100, "normal", new Random(1));
        Assert.NotEqual(PetState.Meet, later[0].Item2.State);
        social.Update(later, PetSocial.CooldownTicks("normal") - 1, "normal", new Random(1));
        Assert.NotEqual(PetState.Meet, later[0].Item2.State);
        social.Update(later, PetSocial.CooldownTicks("normal") + 1, "normal", new Random(1));
        Assert.Equal(PetState.Meet, later[0].Item2.State);

        Assert.True(PetSocial.CooldownTicks("calm") > PetSocial.CooldownTicks("normal"));
        Assert.True(PetSocial.CooldownTicks("lively") < PetSocial.CooldownTicks("normal"));
    }

    [Fact]
    public void PetsOnStayOrFollow_DoNotMeet()
    {
        var world = Plain();
        var a = Pet(1); var b = Pet(2);
        a.Spawn(900, 900); b.Spawn(940, 900);
        Run(a, world, 150); Run(b, world, 150);
        a.Stay = true;
        Assert.False(a.IsFree);
        new PetSocial().Update([("a", a), ("b", b)], 0, "normal", new Random(1));
        Assert.NotEqual(PetState.Meet, b.State);
        a.Stay = false;
        a.SetFollowCursor(true);
        Assert.False(a.IsFree);
    }

    [Fact]
    public void AMeetingEndsInOneOfThreeWays_AndAFollowerEndsAfterItsTime()
    {
        var world = Plain();
        var kinds = new HashSet<MeetKind>();
        for (var seed = 0; seed < 30 && kinds.Count < 3; seed++)
        {
            var a = Pet(seed); var b = Pet(seed + 100);
            a.Spawn(900, 900); b.Spawn(950, 900);
            for (var i = 0; i < 400 && !(a.IsFree && b.IsFree); i++) { a.Step(world); b.Step(world); }
            if (!(a.IsFree && b.IsFree) || Math.Abs(a.X - b.X) > 90) continue;
            var pets = new List<(string Id, PetBrain Brain)> { ("a", a), ("b", b) };
            var social = new PetSocial();
            social.Update(pets, 0, "normal", new Random(seed));
            for (var i = 0; i < 60; i++) { a.PetTargetX = social.TargetX("a", pets); b.PetTargetX = social.TargetX("b", pets); a.Step(world); b.Step(world); }
            if (a.State == PetState.Sit && b.State == PetState.Sit) kinds.Add(MeetKind.Sit);
            else if (a.State == PetState.FollowPet || b.State == PetState.FollowPet) kinds.Add(MeetKind.Follow);
            else if (a.State == PetState.Walk && b.State == PetState.Walk) kinds.Add(MeetKind.Part);
        }
        Assert.Equal(3, kinds.Count);
    }

    [Fact]
    public void AFollowingPet_KeepsAGap_AndStopsWhenTimeIsUpOrTheLeaderIsGone()
    {
        var world = Plain();
        var f = Standing(world, 500, 1);
        f.Meet(1000, new MeetPlan(MeetKind.Follow, 200));
        Run(f, world, 50);
        Assert.Equal(PetState.FollowPet, f.State);
        for (var i = 0; i < 300; i++) { f.PetTargetX = 1000; f.Step(world); }
        Assert.InRange(1000 - f.X, 50, 100);          // about 70 px behind, not on top of it

        var timed = Standing(world, 500, 1);
        timed.Meet(1000, new MeetPlan(MeetKind.Follow, 200));
        for (var i = 0; i < 400; i++) { timed.PetTargetX = 1000; timed.Step(world); }
        Assert.NotEqual(PetState.FollowPet, timed.State);

        var left = Standing(world, 500, 1);
        left.Meet(1000, new MeetPlan(MeetKind.Follow, 5000));
        Run(left, world, 60);
        left.PetTargetX = null;
        left.Step(world);
        Assert.NotEqual(PetState.FollowPet, left.State);
    }

    [Fact]
    public void Stroking_HoldsStillWithHearts_AndEnds()
    {
        var world = Plain();
        var pet = Pet(1);
        pet.Spawn(900, 900);
        Run(pet, world, 150);
        pet.BeginStroke();
        Assert.Equal(PetState.Stroked, pet.State);
        var x = pet.X;
        var progress = new List<double>();
        for (var i = 0; i < 100; i++) { pet.Step(world); progress.Add(pet.MarkProgress); }
        Assert.Equal(x, pet.X);
        Assert.Equal(PetMark.Hearts, pet.Mark);
        Assert.All(progress, p => Assert.InRange(p, 0, 1));
        pet.EndStroke();
        Assert.Equal(PetState.Stand, pet.State);
        Assert.Equal(PetMark.None, pet.Mark);

        pet.BeginStroke();
        Run(pet, world, 25 * 31);                     // it ends by itself after 30 s
        Assert.NotEqual(PetState.Stroked, pet.State);
    }

    [Fact]
    public void Stroking_IsIgnoredInTheAir()
    {
        var pet = Pet(1);
        pet.Spawn(900, 100);
        pet.BeginStroke();
        Assert.Equal(PetState.Fall, pet.State);
    }

    [Fact]
    public void StayHere_NeverWalks_AndHoldsAWallForever()
    {
        var world = Plain();
        var pet = Pet(1, "lively");
        pet.Spawn(900, 900);
        Run(pet, world, 100);
        pet.Stay = true;
        var x = pet.X;
        var seen = new HashSet<PetState>();
        for (var i = 0; i < 25 * 60 * 10; i++) { pet.Step(world); seen.Add(pet.State); }
        Assert.Equal(x, pet.X);
        Assert.Subset(new HashSet<PetState> { PetState.Stand, PetState.Sit, PetState.Lie, PetState.Sleep }, seen);

        var wall = Dropped(world, 15, 600);
        wall.Stay = true;
        Run(wall, world, 25 * 60 * 3);
        Assert.Equal(PetState.GrabWall, wall.State);
        Assert.Equal(600, wall.Y);

        var climbing = Pet(2);
        climbing.Spawn(5, 900);
        for (var i = 0; i < 25 * 60 * 20 && climbing.State != PetState.ClimbWall; i++) climbing.Step(world);
        Assert.Equal(PetState.ClimbWall, climbing.State);
        climbing.Stay = true;
        climbing.Step(world);
        var y = climbing.Y;
        Run(climbing, world, 25 * 60);
        Assert.Equal(PetState.GrabWall, climbing.State);
        Assert.Equal(y, climbing.Y);
    }

    [Fact]
    public void AStayPet_Thrown_FallsAndThenStaysWhereItEnds()
    {
        var world = Plain();
        var pet = Pet(1, "lively");
        pet.Stay = true;
        pet.BeginDrag(900, 300);
        pet.EndDrag(10, -2, world);
        Run(pet, world, 150);
        Assert.Equal(Screen.Bottom, pet.Y);
        var x = pet.X;
        Run(pet, world, 25 * 60 * 3);
        Assert.Equal(x, pet.X);
    }

    [Fact]
    public void FollowMe_TracksTheCursorAlongTheFloor_AndEndsAfterAMinuteOrAClick()
    {
        var world = Plain();
        var pet = Pet(1, "calm");
        pet.Spawn(300, 900);
        Run(pet, world, 100);
        pet.SetFollowCursor(true);
        Assert.True(pet.FollowCursor);
        for (var i = 0; i < 25 * 20; i++) { pet.SetCursor(1500, 1000); pet.Step(world); }
        Assert.InRange(pet.X, 1380, 1560);

        for (var i = 0; i < 25 * 45; i++) { pet.SetCursor(200, 1000); pet.Step(world); }
        Assert.False(pet.FollowCursor);                  // 60 s are over

        pet.SetFollowCursor(true);
        pet.Click();
        Assert.False(pet.FollowCursor);

        pet.SetFollowCursor(true);
        pet.Stay = true;                                  // the two cancel each other
        Assert.False(pet.FollowCursor);
        pet.SetFollowCursor(true);
        Assert.False(pet.Stay);
    }

    [Fact]
    public void FollowMe_LetsGoOfAWall_AndNeverClimbs()
    {
        var world = Plain();
        var pet = Dropped(world, 15, 600);
        Assert.Equal(PetState.GrabWall, pet.State);
        pet.SetFollowCursor(true);
        pet.SetCursor(900, 1000);
        var seen = new HashSet<PetState>();
        for (var i = 0; i < 25 * 30; i++) { pet.SetCursor(900, 1000); pet.Step(world); seen.Add(pet.State); }
        Assert.DoesNotContain(PetState.ClimbWall, seen);
        Assert.InRange(pet.X, 800, 1000);
    }
}
