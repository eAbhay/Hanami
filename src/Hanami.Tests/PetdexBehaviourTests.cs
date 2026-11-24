using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>How a Petdex pet behaves: right-facing twin frames, and none of the ceiling, sitting or sleeping a Shimeji pet does.</summary>
public sealed class PetdexBehaviourTests
{
    private static readonly PxRect Screen = new(0, 0, 1920, 1040);

    // Idle 1-2, run right 9-11, run left 17-19, wave 25-26, jump 33-37, failed 41-42, waiting 49-50.
    private static readonly (int row, int col)[] Cells =
        [(0, 0), (0, 1), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2), (3, 0), (3, 1), (4, 0), (4, 1), (4, 2), (4, 3), (4, 4), (5, 0), (5, 1), (6, 0), (6, 1)];

    /// <summary>The same sheet without its failed and waiting rows: such a pet can neither sleep nor sit.</summary>
    private static readonly (int row, int col)[] Basic = Cells.Where(c => c.row is not (5 or 6)).ToArray();

    private static PetdexLayout.Built Built(bool basic = false)
    {
        var filled = new bool[9, 8];
        foreach (var (r, c) in basic ? Basic : Cells) filled[r, c] = true;
        return PetdexLayout.Build(new PetdexLayout.Sheet(filled, 96, 200), out _)!;
    }

    private static PetBrain Pet(int seed = 1, string activity = "normal", IReadOnlySet<int>? frames = null, bool basic = false)
    {
        var built = Built(basic);
        return new PetBrain(new PetProfile(built.Abilities, frames ?? built.Frames.ToHashSet(), 1, activity,
            PetAnimations.FromMapping(built.Mapping, 192, 208), BodyHeight: 177, Petdex: true), new Random(seed));
    }

    private static void Run(PetBrain pet, World world, int ticks)
    {
        for (var i = 0; i < ticks; i++) pet.Step(world);
    }

    private static PetBrain OnFloor(World world, int seed = 1, IReadOnlySet<int>? frames = null)
    {
        var pet = Pet(seed, frames: frames);
        pet.BeginDrag(900, 1040);
        pet.EndDrag(0, 0, world);
        return pet;
    }

    private static readonly HashSet<PetState> CeilingOnly = [PetState.GrabCeiling, PetState.ClimbCeiling];
    private static readonly HashSet<PetState> NoRest = [PetState.Sit, PetState.Lie, PetState.Sleep, PetState.Perch, PetState.GrabCeiling, PetState.ClimbCeiling];

    // ---- twin frames ------------------------------------------------------------------------------

    [Fact]
    public void FacingRight_DrawsTheRunRightTwin_Unmirrored_AndFacingLeftDrawsRunLeft()
    {
        var world = World.Build([Screen]);
        var pet = OnFloor(world);
        var sawRight = false;
        var sawLeft = false;
        for (var i = 0; i < 6000 && !(sawRight && sawLeft); i++)
        {
            pet.Step(world);
            if (pet.State is not (PetState.Walk or PetState.Run or PetState.Chase)) continue;
            if (pet.Facing > 0) { sawRight = true; Assert.Contains(pet.Frame, new[] { 9, 10, 11 }); Assert.False(pet.Mirrored); }
            else { sawLeft = true; Assert.Contains(pet.Frame, new[] { 17, 18, 19 }); Assert.False(pet.Mirrored); }
        }
        Assert.True(sawRight && sawLeft, "the pet should walk both ways");
    }

    [Fact]
    public void AShimejiPose_WithoutATwin_IsStillMirroredWhenFacingRight()
    {
        var frames = Enumerable.Range(1, ShimejiFrames.NumFrames).ToHashSet();
        var world = World.Build([Screen]);
        var pet = new PetBrain(new PetProfile(new PetCapabilities(true, true, true, true), frames, 1), new Random(3));
        pet.BeginDrag(900, 1040);
        pet.EndDrag(0, 0, world);
        for (var i = 0; i < 6000; i++)
        {
            pet.Step(world);
            if (pet.State == PetState.Walk && pet.Facing > 0) { Assert.True(pet.Mirrored); Assert.InRange(pet.Frame, 1, 3); return; }
        }
        Assert.Fail("the pet never walked right");
    }

    [Fact]
    public void ATwinMissingFromThePack_FallsBackToMirroring()
    {
        var frames = Built().Frames.Where(f => f is < 9 or > 11).ToHashSet();   // no run-right frames
        var world = World.Build([Screen]);
        var pet = OnFloor(world, 2, frames);
        for (var i = 0; i < 6000; i++)
        {
            pet.Step(world);
            if (pet.State == PetState.Walk && pet.Facing > 0) { Assert.True(pet.Mirrored); Assert.InRange(pet.Frame, 17, 19); return; }
        }
        Assert.Fail("the pet never walked right");
    }

    // ---- abilities --------------------------------------------------------------------------------

    [Theory]
    [InlineData("calm")]
    [InlineData("normal")]
    [InlineData("lively")]
    public void APetdexPet_WithoutFailedAndWaitingRows_NeverSitsSleepsLiesOrHangsFromACeiling(string activity)
    {
        var world = World.Build([Screen], [new WorldWindow(1, new PxRect(400, 500, 1000, 900))]);
        for (var seed = 1; seed <= 6; seed++)
        {
            var pet = Pet(seed, activity, basic: true);
            Assert.Equal(new PetCapabilities(true, false, false, false), pet.Caps);
            pet.BeginDrag(700, 1040);
            pet.EndDrag(0, 0, world);
            for (var i = 0; i < 6000; i++)
            {
                pet.Step(world);
                Assert.DoesNotContain(pet.State, NoRest);
            }
        }
    }

    [Theory]
    [InlineData("calm")]
    [InlineData("lively")]
    public void APetdexPet_WithThoseRows_SitsAndSleeps_ButNeverHangsFromACeiling(string activity)
    {
        var world = World.Build([Screen], [new WorldWindow(1, new PxRect(400, 500, 1000, 900))]);
        var seen = new HashSet<PetState>();
        for (var seed = 1; seed <= 8; seed++)
        {
            var pet = Pet(seed, activity);
            Assert.Equal(new PetCapabilities(true, false, true, true), pet.Caps);
            pet.BeginDrag(700, 1040);
            pet.EndDrag(0, 0, world);
            for (var i = 0; i < 12000; i++)
            {
                pet.Step(world);
                seen.Add(pet.State);
                Assert.DoesNotContain(pet.State, CeilingOnly);
            }
        }
        Assert.Contains(PetState.Sit, seen);
        if (activity == "calm") Assert.Contains(PetState.Sleep, seen);
    }

    [Fact]
    public void SittingPlaysTheWaitingRow_AndSleepingTheFailedRow()
    {
        var world = World.Build([Screen]);
        var pet = OnFloor(world);
        Run(pet, world, 10);
        pet.Command(PetCommand.Sit);
        Assert.Equal(PetState.Sit, pet.State);
        pet.Step(world);
        Assert.Contains(pet.Frame, new[] { 49, 50 });

        pet.Command(PetCommand.Sleep);
        Assert.Equal(PetState.Sleep, pet.State);
        pet.Step(world);
        Assert.Contains(pet.Frame, new[] { 41, 42 });
    }

    [Fact]
    public void ThePetsExtraRows_AreFidgetsWhileStanding_ButOnlyWhenTheyHaveFrames()
    {
        var cells = Cells.Concat([(7, 0), (7, 1), (8, 0)]);
        var filled = new bool[9, 8];
        foreach (var (r, c) in cells) filled[r, c] = true;
        var built = PetdexLayout.Build(new PetdexLayout.Sheet(filled, 96, 200), out _)!;
        var anims = PetAnimations.FromMapping(built.Mapping, 192, 208);
        Assert.Equal(2, anims.Fidgets.Count);
        Assert.Equal([57, 58], anims.Fidgets[0].Select(p => p.Frame));      // running
        Assert.Equal([65], anims.Fidgets[1].Select(p => p.Frame));          // review
        Assert.Equal([57, 58], anims.Resist.Select(p => p.Frame));           // struggling while held: the running row

        Assert.Empty(PetAnimations.FromMapping(Built(basic: true).Mapping, 192, 208).Fidgets);

        // On the floor such a pet now and then stands doing one of them.
        var world = World.Build([Screen]);
        var pet = new PetBrain(new PetProfile(built.Abilities, built.Frames.ToHashSet(), 1, "normal", anims, BodyHeight: 177, Petdex: true), new Random(3));
        pet.BeginDrag(900, 1040);
        pet.EndDrag(0, 0, world);
        var sawFidget = false;
        for (var i = 0; i < 12000 && !sawFidget; i++)
        {
            pet.Step(world);
            sawFidget = pet.State == PetState.Stand && pet.Frame is 57 or 58 or 65;
        }
        Assert.True(sawFidget);
    }

    [Fact]
    public void ThrownAtTheCeiling_DoesNotLatchItAndFallsBack()
    {
        var world = World.Build([Screen]);
        var pet = Pet();
        pet.BeginDrag(900, 300);
        pet.EndDrag(0, -30, world);
        for (var i = 0; i < 400; i++)
        {
            pet.Step(world);
            Assert.DoesNotContain(pet.State, CeilingOnly);
        }
        Assert.Equal(Screen.Bottom, pet.Y, 1);
    }

    [Fact]
    public void DroppedAtTheCeiling_FallsInsteadOfHanging()
    {
        var world = World.Build([Screen]);
        var pet = Pet();
        pet.BeginDrag(900, 20);
        pet.EndDrag(0, 0, world);
        Assert.NotEqual(PetState.GrabCeiling, pet.State);
        Run(pet, world, 300);
        Assert.DoesNotContain(pet.State, CeilingOnly);
    }

    [Fact]
    public void DroppedBesideAWindowSide_ItGrabsTheWall_AndClimbsOnRunFrames()
    {
        var world = World.Build([Screen], [new WorldWindow(1, new PxRect(400, 500, 1000, 900))]);
        var pet = Pet();
        pet.BeginDrag(1010, 700);
        pet.EndDrag(0, 0, world);
        Assert.Equal(PetState.GrabWall, pet.State);
        Assert.Contains(pet.Frame, new[] { 9, 17 });
        var climbed = false;
        for (var i = 0; i < 1500 && !climbed; i++)
        {
            pet.Step(world);
            if (pet.State == PetState.ClimbWall) { climbed = true; Assert.Contains(pet.Frame, new[] { 9, 10, 11, 17, 18, 19 }); Assert.False(pet.Mirrored); }
        }
        Assert.True(climbed);
    }

    // ---- reactions --------------------------------------------------------------------------------

    [Fact]
    public void ALongCarry_EndsWithLyingDownOnTheFailedRow_ThenGettingUp()
    {
        var world = World.Build([Screen]);
        var pet = Pet();
        pet.BeginDrag(900, 500);
        Run(pet, world, 200);          // carried for 8 s
        pet.EndDrag(0, 0, world);
        var sawLying = false;
        for (var i = 0; i < 600; i++)
        {
            pet.Step(world);
            if (pet.State == PetState.Lie && pet.Frame is 41 or 42) sawLying = true;
            Assert.DoesNotContain(pet.State, CeilingOnly);
        }
        Assert.True(sawLying);
        Assert.DoesNotContain(pet.State, new[] { PetState.Lie });
    }

    [Fact]
    public void ALongCarry_WithoutAFailedRow_PlaysItsStrugglingAnimationWhileStanding_ThenCarriesOn()
    {
        var world = World.Build([Screen]);
        var pet = Pet(basic: true);
        pet.BeginDrag(900, 500);
        Run(pet, world, 200);
        pet.EndDrag(0, 0, world);
        var sawStruggle = false;
        for (var i = 0; i < 400; i++)
        {
            pet.Step(world);
            if (pet.State == PetState.Stand && pet.Frame is 25 or 26) sawStruggle = true;   // no running row: the wave row stands in
            Assert.DoesNotContain(pet.State, NoRest);
        }
        Assert.True(sawStruggle);
    }

    [Fact]
    public void ShakingTheWindowAgainRightAfterALanding_ThrowsThePetAgain_WithoutWaitingForTheFailedRow()
    {
        var rect = new PxRect(400, 500, 1000, 900);
        World At(int dx) => World.Build([Screen], [new WorldWindow(1, rect.Offset(dx, 0))]);
        foreach (var basic in new[] { false, true }) ShakeTwice(At, basic);
    }

    private static void ShakeTwice(Func<int, World> At, bool basic)
    {
        var pet = Pet(1, basic: basic);
        pet.Spawn(700, 100);
        Run(pet, At(0), 150);
        Assert.Equal(1, pet.Surface!.Owner);

        // First shake: back and forth at 30 px per tick, until it is thrown.
        var thrown = false;
        for (var i = 1; i <= 8 && !thrown; i++)
        {
            pet.Step(At(i % 2 == 0 ? 0 : 30));
            thrown = pet.State == PetState.Thrown;
        }
        Assert.True(thrown);

        // It lands again (on the window or below it) and plays the failed row.
        var world = At(0);
        for (var i = 0; i < 400 && pet.State is PetState.Thrown or PetState.Fall or PetState.Land; i++) pet.Step(world);
        Assert.Equal(basic ? PetState.Stand : PetState.Lie, pet.State);   // lying down on the failed row, or struggling while standing
        if (!basic) Assert.Contains(pet.Frame, new[] { 41, 42 });
        if (pet.Surface is not { Owner: 1 }) return;   // it fell to the floor: nothing left to shake

        // Second shake straight away: thrown again within a few ticks, not after the failed row has finished.
        thrown = false;
        for (var i = 1; i <= 8 && !thrown; i++)
        {
            pet.Step(At(i % 2 == 0 ? 0 : 30));
            thrown = pet.State == PetState.Thrown;
        }
        Assert.True(thrown);
    }

    [Fact]
    public void AClick_PlaysTheWaveRow_AndAStrokeKeepsWaving()
    {
        var world = World.Build([Screen]);
        var pet = OnFloor(world);
        Run(pet, world, 60);
        pet.Click();
        Assert.Equal(PetState.React, pet.State);
        pet.Step(world);
        Assert.Contains(pet.Frame, new[] { 25, 26 });

        Run(pet, world, 200);
        pet.BeginStroke();
        Assert.Equal(PetState.Stroked, pet.State);
        pet.Step(world);
        Assert.Contains(pet.Frame, new[] { 25, 26 });
    }

    [Fact]
    public void TheMenuCommands_SitAndSleep_AreIgnored_WithoutThoseRows()
    {
        var world = World.Build([Screen]);
        var pet = Pet(basic: true);
        pet.BeginDrag(900, 1040);
        pet.EndDrag(0, 0, world);
        Run(pet, world, 10);
        pet.Command(PetCommand.Sit);
        pet.Command(PetCommand.Sleep);
        Assert.DoesNotContain(pet.State, NoRest);
    }
}
