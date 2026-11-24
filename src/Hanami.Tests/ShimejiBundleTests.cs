using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hanami.Core;
using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>shimeji.org bundles: manifest.json + animation.json + numbered sprites, read as data from small synthetic bundles.</summary>
public sealed class ShimejiBundleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PetLibrary _pets;

    public ShimejiBundleTests()
    {
        Directory.CreateDirectory(_dir);
        _pets = new PetLibrary(new Library(Path.Combine(_dir, "Library")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A sprite: a box 20 wide and <paramref name="boxHeight"/> tall, its bottom at y = <paramref name="bottom"/>, centred.</summary>
    private static byte[] Sprite(int size, int boxHeight = 40, int bottom = 56)
    {
        var px = new byte[size * size * 4];
        for (var y = bottom - boxHeight; y < bottom; y++)
            for (var x = size / 2 - 10; x < size / 2 + 10; x++)
            {
                var o = (y * size + x) * 4;
                px[o] = 90; px[o + 1] = 150; px[o + 2] = 230; px[o + 3] = 255;
            }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string Animation(bool ceiling = true, bool climb = true, bool walkLeft = true, bool drag = true) =>
        """{"animations":[""" + string.Join(",", new[]
        {
            """{"key":"stand","type":"GROUND","subtype":"STAND","direction":"ANY","frames":[{"sprite":0,"durationTicks":120}]}""",
            walkLeft ? """{"key":"walk_left","type":"GROUND","subtype":"WALK","direction":"LEFT","frames":[{"sprite":1,"durationTicks":9},{"sprite":2,"dx":-2,"durationTicks":8}]}""" : null,
            """{"key":"walk_right","type":"GROUND","subtype":"WALK","direction":"RIGHT","frames":[{"sprite":3,"durationTicks":9},{"sprite":4,"dx":2,"durationTicks":8}]}""",
            """{"key":"fall","type":"AIR","subtype":"FALL","direction":"ANY","frames":[{"sprite":5,"dy":10,"durationTicks":10}]}""",
            drag ? """{"key":"drag","type":"USER","subtype":"DRAG","direction":"ANY","frames":[{"sprite":6,"durationTicks":6}]}""" : null,
            climb ? """{"key":"climb_left","type":"WALL","subtype":"CLIMB","direction":"LEFT","frames":[{"sprite":7,"durationTicks":9},{"sprite":8,"dy":-2,"durationTicks":8}]}""" : null,
            climb ? """{"key":"climb_right","type":"WALL","subtype":"CLIMB","direction":"RIGHT","frames":[{"sprite":7,"durationTicks":9},{"sprite":8,"dy":-2,"durationTicks":8}]}""" : null,
            ceiling ? """{"key":"hang_left","type":"CEILING","subtype":"HANG","direction":"LEFT","frames":[{"sprite":9,"durationTicks":6},{"sprite":10,"dx":-2,"durationTicks":6}]}""" : null,
            ceiling ? """{"key":"hang_right","type":"CEILING","subtype":"HANG","direction":"RIGHT","frames":[{"sprite":9,"durationTicks":6},{"sprite":10,"dx":2,"durationTicks":6}]}""" : null,
            """{"key":"bounce","type":"GROUND","subtype":"BOUNCE","direction":"ANY","frames":[{"sprite":11,"durationTicks":5}]}""",
            """{"key":"idle","type":"GROUND","subtype":"IDLE","direction":"ANY","frames":[{"sprite":12,"durationTicks":6}]}""",
        }.Where(s => s is not null)) + "]}";

    private static string Manifest(string name = "Test Bundle", int count = 13, string pattern = "%04d.png", string basePath = "sprites/") =>
        $$$"""{"name":"{{{name}}}","nameSlug":"test","sprites":{"type":"SEQUENCE","basePath":"{{{basePath}}}","filePattern":"{{{pattern}}}","spriteCount":{{{count}}},"size":[64,64]}}""";

    private string MakeFolder(string name, string? animation = null, string? manifest = null, int size = 64, int count = 13)
    {
        var folder = Path.Combine(_dir, "src", name);
        Directory.CreateDirectory(Path.Combine(folder, "sprites"));
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest ?? Manifest());
        File.WriteAllText(Path.Combine(folder, "animation.json"), animation ?? Animation());
        for (var i = 0; i < count; i++) File.WriteAllBytes(Path.Combine(folder, "sprites", $"{i:D4}.png"), Sprite(size));
        return folder;
    }

    private string MakeZip(string name, string? prefix = null, string? animation = null, string? manifest = null, int size = 64, int count = 13)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string entry, byte[] data) { using var s = zip.CreateEntry((prefix ?? "") + entry).Open(); s.Write(data); }
        Add("manifest.json", Encoding.UTF8.GetBytes(manifest ?? Manifest()));
        Add("animation.json", Encoding.UTF8.GetBytes(animation ?? Animation()));
        for (var i = 0; i < count; i++) Add($"sprites/{i:D4}.png", Sprite(size));
        return path;
    }

    // ---- reading the animations ---------------------------------------------------------------------

    [Theory]
    [InlineData("%04d.webp", 7, "0007.webp")]
    [InlineData("%04d.png", 123, "0123.png")]
    [InlineData("s%03d.png", 5, "s005.png")]
    [InlineData("%d.png", 42, "42.png")]
    public void FormatPattern_ExpandsTheSpriteNumber(string pattern, int index, string expected) =>
        Assert.Equal(expected, ShimejiBundle.FormatPattern(pattern, index));

    [Fact]
    public void Build_MapsTheAnimations_WithTwinsAndRealAbilities()
    {
        var built = ShimejiBundle.Build(Animation(), 13, feetX: 32, feetY: 56, ceilingY: 16, out var error)!;
        Assert.Null(error);
        var a = PetAnimations.FromMapping(built.Mapping, 64, 64);

        Assert.Equal([1], a.Stand.Select(p => p.Frame));                    // sprite 0 is frame 1
        Assert.Equal([120], a.Stand.Select(p => p.Ticks));
        Assert.Equal([2, 3], a.Walk.Select(p => p.Frame));
        Assert.Equal([4, 5], a.Walk.Select(p => p.Twin));                   // walk right draws other sprites: used unmirrored
        Assert.True(a.Walk.All(p => p.Vx < 0));
        Assert.True(a.Run.All(p => p.Vx < a.Walk[0].Vx) && a.Run[0].Ticks < a.Walk[0].Ticks);
        Assert.All(a.Walk, p => Assert.Equal((32, 56), (p.AnchorX, p.AnchorY)));

        Assert.Equal([8, 9], a.ClimbWallUp.Select(p => p.Frame));
        Assert.All(a.ClimbWallUp, p => Assert.Equal(0, p.Twin));            // the right wall uses the same sprites: mirrored
        Assert.True(a.ClimbWallUp.All(p => p.Vy < 0) && a.ClimbWallDown.All(p => p.Vy > 0));
        Assert.Equal([10, 11], a.ClimbCeiling.Select(p => p.Frame));
        Assert.All(a.ClimbCeiling, p => Assert.Equal(16, p.AnchorY));       // hung from the top of the sprite
        Assert.Equal([6], a.Fall.Select(p => p.Frame));
        Assert.Equal([7, 7, 7, 7, 7], a.Swing);                             // dragged: the drag sprite
        Assert.Equal([13], a.Wave.Select(p => p.Frame));
        Assert.Equal(new PetCapabilities(Climb: true, Ceiling: true, Sit: false, Sleep: false), built.Abilities);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13], built.Frames);

        // Frames a Shimeji pack would use for sitting and sleeping are never looked up.
        Assert.Equal([1], a.Sit.Select(p => p.Frame));
    }

    [Fact]
    public void Build_WithoutWallOrCeilingAnimations_SwitchesThoseAbilitiesOff()
    {
        var built = ShimejiBundle.Build(Animation(ceiling: false, climb: false), 13, 32, 56, 0, out _)!;
        Assert.Equal(new PetCapabilities(false, false, false, false), built.Abilities);
        var a = PetAnimations.FromMapping(built.Mapping, 64, 64);
        Assert.Equal([1], a.ClimbWallUp.Select(p => p.Frame));              // mapped to the stand sprite, never to a frame that isn't there
    }

    [Fact]
    public void Build_UsesTheRightWalkMirrored_WhenThereIsNoLeftOne()
    {
        var built = ShimejiBundle.Build(Animation(walkLeft: false), 13, 32, 56, 0, out _)!;
        var a = PetAnimations.FromMapping(built.Mapping, 64, 64);
        Assert.Equal([4, 5], a.Walk.Select(p => p.Frame));
        Assert.All(a.Walk, p => Assert.True(p.Mirror));
    }

    [Theory]
    [InlineData("stand")]
    [InlineData("walk")]
    public void Build_RejectsABundleMissingAStandOrWalkAnimation(string missing)
    {
        var json = missing == "stand"
            ? Animation().Replace("\"key\":\"stand\"", "\"key\":\"nope\"").Replace("\"subtype\":\"STAND\"", "\"subtype\":\"NOPE\"")
            : Animation().Replace("\"subtype\":\"WALK\"", "\"subtype\":\"NOPE\"").Replace("\"key\":\"walk_left\"", "\"key\":\"nope1\"").Replace("\"key\":\"walk_right\"", "\"key\":\"nope2\"");
        Assert.Null(ShimejiBundle.Build(json, 13, 32, 56, 0, out var error));
        Assert.Contains($"“{missing}”", error);
    }

    [Fact]
    public void Build_ToleratesAMissingFallOrDragAnimation_PlayingTheStandOneThere()
    {
        var noDrag = ShimejiBundle.Build(Animation(drag: false), 13, 32, 56, 0, out var error)!;
        Assert.Null(error);
        var a = PetAnimations.FromMapping(noDrag.Mapping, 64, 64);
        Assert.Equal([1, 1, 1, 1, 1], a.Swing);                           // dragged: the stand sprite
        Assert.Equal([1], a.Resist.Select(p => p.Frame));

        var noFall = ShimejiBundle.Build(Animation().Replace("\"subtype\":\"FALL\"", "\"subtype\":\"NOPE\"").Replace("\"key\":\"fall\"", "\"key\":\"nope\""), 13, 32, 56, 0, out _)!;
        Assert.Equal([1], PetAnimations.FromMapping(noFall.Mapping, 64, 64).Fall.Select(p => p.Frame));
    }

    [Fact]
    public void Build_ReadsTheConvertedShimejiEeSchema_WithRealSittingAndLyingDown()
    {
        // Its animations are keyed climb_wall_left, climb_wall_descend_left, sit, sprawl, jumping, fling ... and some have other subtypes.
        static string A(string key, string type, string subtype, string direction, params int[] sprites) =>
            $$$"""{"key":"{{{key}}}","type":"{{{type}}}","subtype":"{{{subtype}}}","direction":"{{{direction}}}","frames":[{{{string.Join(",", sprites.Select(s => $$$"""{"sprite":{{{s}}},"durationTicks":6}"""))}}}]}""";
        var json = """{"schema_id":"pc_import_v1","animations":[""" + string.Join(",",
            A("stand", "GROUND", "STAND", "ANY", 0), A("walk_left", "GROUND", "WALK", "LEFT", 1, 2), A("walk_right", "GROUND", "WALK", "RIGHT", 1, 2),
            A("run_left", "GROUND", "RUN", "LEFT", 3), A("run_right", "GROUND", "RUN", "RIGHT", 4),
            A("sit", "GROUND", "SIT", "ANY", 5), A("sit_and_look_up", "GROUND", "SITANDLOOKUP", "ANY", 6), A("sit_and_dangle_legs", "GROUND", "SITANDDANGLELEGS", "ANY", 7),
            A("sprawl", "GROUND", "SPRAWL", "ANY", 8),
            A("grab_wall_left", "WALL", "GRABWALL", "LEFT", 9), A("climb_wall_left", "WALL", "CLIMBWALL", "LEFT", 9, 10), A("climb_wall_right", "WALL", "CLIMBWALL", "RIGHT", 9, 10),
            A("climb_wall_descend_left", "WALL", "DESCEND", "LEFT", 11), A("climb_wall_descend_right", "WALL", "DESCEND", "RIGHT", 11),
            A("grab_ceiling_left", "CEILING", "GRABCEILING", "LEFT", 12), A("climb_ceiling_left", "CEILING", "CLIMBCEILING", "LEFT", 12, 13), A("climb_ceiling_right", "CEILING", "CLIMBCEILING", "RIGHT", 12, 13),
            A("jumping", "AIR", "JUMPING", "ANY", 14), A("fall", "AIR", "FALLING", "ANY", 15), A("bounce", "GROUND", "BOUNCING", "ANY", 16),
            A("drag", "USER", "RESISTING", "ANY", 17), A("fling", "USER", "FALLING", "ANY", 18)) + "]}";
        var built = ShimejiBundle.Build(json, 20, 32, 56, 4, out var error)!;
        Assert.Null(error);
        var a = PetAnimations.FromMapping(built.Mapping, 64, 64);
        Assert.Equal([6], a.Sit.Select(p => p.Frame));
        Assert.Equal([7], a.SitLookUp.Select(p => p.Frame));
        Assert.Equal([8], a.SitDangle.Select(p => p.Frame));
        Assert.Equal([9], a.Sprawl.Select(p => p.Frame));
        Assert.Equal([9], a.Sleep.Select(p => p.Frame));
        Assert.Equal([4], a.Run.Select(p => p.Frame));                    // its own run animation, with the right one as the twin
        Assert.Equal([5], a.Run.Select(p => p.Twin));
        Assert.Equal([10], a.GrabWall.Select(p => p.Frame));              // its own wall grab
        Assert.Equal([10, 11], a.ClimbWallUp.Select(p => p.Frame));
        Assert.Equal([12], a.ClimbWallDown.Select(p => p.Frame));
        Assert.Equal([13, 14], a.ClimbCeiling.Select(p => p.Frame));
        Assert.Equal([16], a.Fall.Select(p => p.Frame));                  // "fall" (FALLING) by its key, not the fling
        Assert.Equal([15], a.Jump.Select(p => p.Frame));
        Assert.Equal([18, 18, 18, 18, 18], a.Swing);
        Assert.Equal(new PetCapabilities(Climb: true, Ceiling: true, Sit: true, Sleep: true), built.Abilities);
    }

    [Fact]
    public void Build_IgnoresFramesPointingOutsideTheSprites_AndBadJson()
    {
        var json = Animation().Replace("\"sprite\":0", "\"sprite\":99");   // the only stand frame is out of range
        Assert.Null(ShimejiBundle.Build(json, 13, 32, 56, 0, out var error));
        Assert.Contains("“stand”", error);
        Assert.Null(ShimejiBundle.Build("nope", 13, 32, 56, 0, out error));
        Assert.Contains("isn't valid", error);
    }

    // ---- import -----------------------------------------------------------------------------------

    [Fact]
    public void ImportFolder_StoresABundlePack()
    {
        var result = PetPackReader.ImportFolder(_pets, MakeFolder("rei"));
        Assert.Empty(result.Rejected);
        var pack = Assert.Single(result.Imported);
        Assert.Equal("Test Bundle", pack.Name);
        Assert.Equal(PetPackManifest.KindBundle, pack.Kind);
        Assert.Equal((64, 64), (pack.FrameWidth, pack.FrameHeight));
        Assert.Equal(13, pack.Frames.Count);
        Assert.True(File.Exists(_pets.FramePath(pack.Id, 13)));
        Assert.Equal(40, pack.StandHeight);                                 // measured from the stand sprite

        var info = _pets.Info(_pets.Read(pack.Id)!);
        Assert.Equal(new PetCapabilities(true, true, false, false), info.Capabilities);
        Assert.Equal("bundle", info.Kind);
        Assert.Equal("Shimeji bundle", info.Credit);

        // The feet are the stand sprite's bottom centre.
        var stand = pack.Animations().Stand[0];
        Assert.Equal((32, 56), (stand.AnchorX, stand.AnchorY));
    }

    [Fact]
    public void ImportZip_ReadsABundle_AtTheRoot_OrInsideAFolder()
    {
        Assert.Equal("Test Bundle", Assert.Single(PetPackReader.ImportZip(_pets, MakeZip("root.zip")).Imported).Name);
        Assert.Equal("Test Bundle", Assert.Single(PetPackReader.ImportZip(_pets, MakeZip("folder.zip", prefix: "Rei/")).Imported).Name);
    }

    [Fact]
    public void BigSprites_AreShrunkToTheLimit()
    {
        var pack = Assert.Single(PetPackReader.ImportZip(_pets, MakeZip("big.zip", size: 512, manifest: Manifest().Replace("[64,64]", "[512,512]"))).Imported);
        Assert.Equal((ShimejiBundle.MaxCell, ShimejiBundle.MaxCell), (pack.FrameWidth, pack.FrameHeight));
        // The box was 40 of 64 px tall at size 64, here the same drawing on a 512 canvas is 8 times bigger, so 320 of 512, i.e. 160 of 256.
        Assert.InRange(pack.StandHeight, 1, 256);
    }

    [Fact]
    public void ABundleWithAMissingSprite_IsRejectedWithAReadableReason()
    {
        var folder = MakeFolder("gap");
        File.Delete(Path.Combine(folder, "sprites", "0008.png"));
        var result = PetPackReader.ImportFolder(_pets, folder);
        Assert.Empty(result.Imported);
        Assert.Contains("0008.png", Assert.Single(result.Rejected));
        Assert.Contains("missing", result.Rejected[0]);
    }

    [Theory]
    [InlineData("../sprites/")]
    [InlineData("/etc/")]
    [InlineData("C:/Windows/")]
    public void ASpriteFolderThatLeavesTheBundle_IsRejected(string basePath)
    {
        var result = PetPackReader.ImportFolder(_pets, MakeFolder("escape" + Math.Abs(basePath.GetHashCode()), manifest: Manifest(basePath: basePath)));
        Assert.Empty(result.Imported);
        Assert.Contains("sprite folder", Assert.Single(result.Rejected));
    }

    [Theory]
    [InlineData("%04d.exe")]
    [InlineData("../%04d.png")]
    [InlineData("%s.png")]
    public void AFilePatternThatIsntAnImageName_IsRejected(string pattern)
    {
        var result = PetPackReader.ImportFolder(_pets, MakeFolder("pattern" + Math.Abs(pattern.GetHashCode()), manifest: Manifest(pattern: pattern)));
        Assert.Empty(result.Imported);
        Assert.Contains("file names", Assert.Single(result.Rejected));
    }

    [Fact]
    public void ZipEntryPaths_AreNeverTrusted()
    {
        var zip = MakeZip("evil.zip", prefix: "../evil/");
        var result = PetPackReader.ImportZip(_pets, zip);
        Assert.Empty(result.Imported);
        Assert.False(Directory.Exists(Path.Combine(_dir, "evil")));
    }

    [Fact]
    public void TooManySprites_AndSpriteCountsThatAreWrong_AreRejected()
    {
        var folder = MakeFolder("count", manifest: Manifest(count: 5000));
        Assert.Contains("sprites", Assert.Single(PetPackReader.ImportFolder(_pets, folder).Rejected));
    }

    [Fact]
    public void ATwoBundleZip_ImportsBoth()
    {
        var path = Path.Combine(_dir, "two.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var n in new[] { "A", "B" })
            {
                void Add(string entry, byte[] data) { using var s = zip.CreateEntry($"{n}/{entry}").Open(); s.Write(data); }
                Add("manifest.json", Encoding.UTF8.GetBytes(Manifest(name: "Bundle " + n)));
                Add("animation.json", Encoding.UTF8.GetBytes(Animation()));
                for (var i = 0; i < 13; i++) Add($"sprites/{i:D4}.png", Sprite(64));
            }
        Assert.Equal(["Bundle A", "Bundle B"], PetPackReader.ImportZip(_pets, path).Imported.Select(p => p.Name).Order());
    }

    [Fact]
    public void TheDropClassifier_RecognisesBundles()
    {
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(MakeZip("drop.zip")).Kind);
        Assert.Equal(DropKind.PetPack, DropClassifier.Classify(MakeFolder("dropfolder")).Kind);
    }

    [Fact]
    public async Task APackFile_CarriesABundlePet()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var sourcePets = new PetLibrary(source);
        var pack = Assert.Single(PetPackReader.ImportFolder(sourcePets, MakeFolder("rt")).Imported);

        var file = Path.Combine(_dir, "bundle" + PackService.Extension);
        await PackService.ExportAsync(source, null, [pack.Id], file, "test", null, null, TestContext.Current.CancellationToken);
        var target = new Library(Path.Combine(_dir, "B"));
        var (result, _) = await PackService.ImportAsync(target, file, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Pets);
        var back = new PetLibrary(target).Read(pack.Id)!;
        Assert.Equal(PetPackManifest.KindBundle, back.Kind);
        Assert.Equal(new PetCapabilities(true, true, false, false), back.Abilities);
        Assert.Equal([2, 3], back.Animations().Walk.Select(p => p.Frame));
    }
}
