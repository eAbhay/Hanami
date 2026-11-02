using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

public sealed class PetAnimationTests
{
    private static PetMapping Own(params (string name, int frame)[] anims) => new()
    {
        ImagePixels = true,
        Animations = anims.ToDictionary(a => a.name, a => new List<PoseDef> { new() { Frame = a.frame, Ticks = 5 } }),
    };

    [Fact]
    public void DefaultTable_HasTheJumpAndDangleFrames()
    {
        Assert.Equal(ShimejiFrames.Jump, PetAnimations.Default.Jump);
        Assert.Equal(ShimejiFrames.SitDangleLegs, PetAnimations.Default.SitDangle);
        Assert.Contains("jump", PetAnimations.Names);
        Assert.Contains("sitDangle", PetAnimations.Names);
    }

    [Fact]
    public void APackWithItsOwnMap_ButNoJumpOrDangle_PlaysItsOwnFallAndSit()
    {
        // its frames are numbered differently from the default table, so the default jump/dangle frames would show the wrong images
        var a = PetAnimations.FromMapping(Own(("fall", 7), ("sit", 9), ("stand", 1)), 128, 128);
        Assert.Equal(a.Fall, a.Jump);
        Assert.Equal(a.Sit, a.SitDangle);
        Assert.Equal(7, a.Jump[0].Frame);
        Assert.Equal(9, a.SitDangle[0].Frame);
    }

    [Fact]
    public void APackThatMapsJumpAndDangle_KeepsThem()
    {
        var a = PetAnimations.FromMapping(Own(("fall", 7), ("jump", 22), ("sit", 9), ("sitDangle", 31)), 128, 128);
        Assert.Equal(22, a.Jump[0].Frame);
        Assert.Equal(31, a.SitDangle[0].Frame);
    }

    [Fact]
    public void AMappingThatOnlyMapsTheSwingFrames_KeepsTheDefaultJump()
    {
        var a = PetAnimations.FromMapping(new PetMapping { Swing = [9, 7, 1, 8, 10] }, 128, 128);
        Assert.Equal(ShimejiFrames.Jump, a.Jump);
        Assert.Equal(ShimejiFrames.SitDangleLegs, a.SitDangle);
    }

    [Fact]
    public void ActionsXml_JumpingAndDanglingAreRead()
    {
        const string xml = """
            <Mascot><ActionList>
              <Action Name="Falling" Type="Embedded"><Animation><Pose Image="/shime4.png" ImageAnchor="64,128" Velocity="0,0" Duration="5"/></Animation></Action>
              <Action Name="Jumping" Type="Embedded"><Animation><Pose Image="/shime22.png" ImageAnchor="64,128" Velocity="0,0" Duration="5"/></Animation></Action>
              <Action Name="SitAndDangleLegs" Type="Animate"><Animation><Pose Image="/shime31.png" ImageAnchor="64,112" Velocity="0,0" Duration="5"/></Animation></Action>
            </ActionList></Mascot>
            """;
        var map = PetAnimations.MappingFromActionsXml(xml);
        Assert.NotNull(map);
        var a = PetAnimations.FromMapping(map, 128, 128);
        Assert.Equal(22, a.Jump[0].Frame);
        Assert.Equal(31, a.SitDangle[0].Frame);
    }
}
