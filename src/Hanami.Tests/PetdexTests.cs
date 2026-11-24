using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hanami.Core;
using Hanami.Core.Pets;
using Xunit;

namespace Hanami.Tests;

/// <summary>Petdex sprite sheets, read from small synthetic sheets made here (no real Petdex art in the repo).</summary>
public sealed class PetdexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hanami-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Library _library;
    private readonly PetLibrary _pets;

    public PetdexTests()
    {
        Directory.CreateDirectory(_dir);
        _library = new Library(Path.Combine(_dir, "Library"));
        _pets = new PetLibrary(_library);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static readonly (int x, int y) cellFeet = (80, 200);

    private static readonly (int row, int col)[] Needed =
        [(0, 0), (0, 1), (1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2), (4, 0), (4, 1), (4, 2), (4, 3), (4, 4)];

    /// <summary>BGRA pixels of a sheet: each listed cell holds an opaque 40×100 box whose bottom is at y = 200.</summary>
    private static byte[] Pixels(int rows, IEnumerable<(int row, int col)> cells, int width = PetdexLayout.SheetWidth, int boxHeight = 100)
    {
        var height = rows * PetdexLayout.CellHeight;
        var px = new byte[width * height * 4];
        foreach (var (row, col) in cells)
            for (var y = 200 - boxHeight; y < 200; y++)
                for (var x = 60; x < 100; x++)
                {
                    var o = (((row * PetdexLayout.CellHeight) + y) * width + col * PetdexLayout.CellWidth + x) * 4;
                    px[o] = 80; px[o + 1] = 120; px[o + 2] = 220; px[o + 3] = 255;
                }
        return px;
    }

    private static byte[] SheetPng(int rows, IEnumerable<(int row, int col)> cells, int width = PetdexLayout.SheetWidth)
    {
        var height = rows * PetdexLayout.CellHeight;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, Pixels(rows, cells, width), width * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string PetJson(string name = "Test Pet", string sheet = "spritesheet.png") =>
        $$"""{"id":"test-pet","displayName":"{{name}}","spritesheetPath":"{{sheet}}"}""";

    private string MakeFolder(string name, byte[]? sheet = null, string? json = null)
    {
        var folder = Path.Combine(_dir, "src", name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pet.json"), json ?? PetJson());
        File.WriteAllBytes(Path.Combine(folder, "spritesheet.png"), sheet ?? SheetPng(9, Needed));
        return folder;
    }

    // ---- layout -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(1536, 1872, 9)]
    [InlineData(1536, 2288, 11)]
    [InlineData(1536, 2080, null)]
    [InlineData(1500, 1872, null)]
    public void RowsFor_AcceptsOnlyTheTwoSheetSizes(int w, int h, int? rows) => Assert.Equal(rows, PetdexLayout.RowsFor(w, h));

    [Fact]
    public void FrameNumber_IsRowTimesEightPlusColumnPlusOne()
    {
        Assert.Equal(1, PetdexLayout.FrameNumber(0, 0));
        Assert.Equal(17, PetdexLayout.FrameNumber(2, 0));
        Assert.Equal(88, PetdexLayout.FrameNumber(10, 7));
    }

    [Fact]
    public void Analyze_FindsFilledCellsAndTheIdleFeet()
    {
        var sheet = PetdexLayout.Analyze(Pixels(9, Needed), 1536, 1872, out var error)!;
        Assert.Null(error);
        Assert.Equal(9, sheet.Rows);
        Assert.True(sheet.Filled[0, 1]);
        Assert.False(sheet.Filled[0, 2]);    // empty cells are found by their pixels
        Assert.False(sheet.Filled[3, 0]);
        Assert.Equal((80, 200), (sheet.FeetX, sheet.FeetY));
    }

    [Fact]
    public void Analyze_IgnoresFaintHalosAndSpecks()
    {
        var px = Pixels(9, Needed);
        // A nearly transparent cell and a cell with 3 stray pixels are not frames.
        for (var i = 0; i < 40; i++) { var o = ((3 * 208 + 100) * 1536 + i) * 4 + 3; px[o] = PetdexLayout.AlphaThreshold; }
        for (var i = 0; i < 3; i++) { var o = ((3 * 208 + 100) * 1536 + 192 + i) * 4 + 3; px[o] = 255; }
        var sheet = PetdexLayout.Analyze(px, 1536, 1872, out _)!;
        Assert.False(sheet.Filled[3, 0]);
        Assert.False(sheet.Filled[3, 1]);
    }

    [Fact]
    public void Analyze_RejectsAWrongSize()
    {
        Assert.Null(PetdexLayout.Analyze(new byte[10], 1000, 1000, out var error));
        Assert.Contains("1536", error);
    }

    [Fact]
    public void Build_MapsRowsToAnimations_WithRightFacingTwins_AndNoCeilingSitOrSleep()
    {
        var sheet = PetdexLayout.Analyze(Pixels(9, Needed), 1536, 1872, out _)!;
        var built = PetdexLayout.Build(sheet, out var error)!;
        Assert.Null(error);
        var anims = PetAnimations.FromMapping(built.Mapping, 192, 208);

        Assert.Equal([1, 2], anims.Stand.Select(p => p.Frame));
        Assert.Equal([17, 18, 19], anims.Walk.Select(p => p.Frame));         // run left = row 2
        Assert.Equal([9, 10, 11], anims.Walk.Select(p => p.Twin));            // each with its run-right frame (row 1)
        Assert.All(anims.Walk, p => Assert.Equal((80, 200), (p.AnchorX, p.AnchorY)));
        Assert.True(anims.Walk.All(p => p.Vx < 0));
        Assert.Equal([17], anims.GrabWall.Select(p => p.Frame));
        Assert.Equal([17, 18, 19], anims.ClimbWallUp.Select(p => p.Frame));
        Assert.True(anims.ClimbWallUp.All(p => p.Vy < 0) && anims.ClimbWallDown.All(p => p.Vy > 0));
        Assert.Equal([33, 34, 35, 36, 37], anims.Fall.Select(p => p.Frame));  // jump = row 4
        Assert.Equal([33, 34, 35, 36, 37], anims.Swing);
        Assert.Equal([1, 2], anims.Resist.Select(p => p.Frame));              // no running or wave row: idle stands in
        Assert.Equal(new PetCapabilities(true, false, false, false), built.Abilities);
        Assert.Equal(Needed.Select(c => PetdexLayout.FrameNumber(c.row, c.col)).Order(), built.Frames);

        // Frames a Shimeji pack would use for sitting, sleeping and ceilings are never looked up.
        Assert.Equal([1, 2], anims.Sit.Select(p => p.Frame));
        Assert.Equal([1, 2], anims.GrabCeiling.Select(p => p.Frame));
    }

    [Fact]
    public void Build_SitsOnTheWaitingRow_SleepsOnTheFailedRow_AndWavesOnTheWaveRow()
    {
        var cells = Needed.Concat([(3, 0), (3, 1), (5, 0), (5, 1), (6, 0)]);
        var built = PetdexLayout.Build(PetdexLayout.Analyze(Pixels(9, cells), 1536, 1872, out _)!, out _)!;
        var anims = PetAnimations.FromMapping(built.Mapping, 192, 208);
        Assert.Equal([25, 26], anims.Wave.Select(p => p.Frame));
        Assert.Equal([49], anims.Sit.Select(p => p.Frame));
        Assert.Equal([49], anims.SitDangle.Select(p => p.Frame));
        Assert.Equal([41, 42], anims.Sleep.Select(p => p.Frame));
        Assert.Equal([41, 42], anims.Sprawl.Select(p => p.Frame));           // the dizzy landing lies down on it too
        Assert.Equal([25, 26], anims.Resist.Select(p => p.Frame));           // no running row: the wave row stands in
        Assert.Equal([36, 37], anims.Bounce.Select(p => p.Frame));           // a hard landing: the jump row's last frames
        Assert.Equal(new PetCapabilities(Climb: true, Ceiling: false, Sit: true, Sleep: true), built.Abilities);
        Assert.Empty(anims.Fidgets);
    }

    [Fact]
    public void Build_TurnsTheRunningReviewAndTurningRowsIntoFidgets()
    {
        var cells = Needed.Concat([(7, 0), (7, 1), (8, 0), (9, 0), (10, 0), (10, 1)]);
        var built = PetdexLayout.Build(PetdexLayout.Analyze(Pixels(11, cells), 1536, 2288, out _)!, out _)!;
        var anims = PetAnimations.FromMapping(built.Mapping, 192, 208);
        Assert.Equal([[57, 58], [65], [73], [81, 82]], anims.Fidgets.Select(f => f.Select(p => p.Frame).ToArray()));
        Assert.Equal([57, 58], anims.Resist.Select(p => p.Frame));
    }

    [Fact]
    public void APackStoredBeforeTheMappingChanged_IsRebuiltWhenRead_AndSaved()
    {
        var cells = Needed.Concat([(5, 0), (5, 1), (6, 0), (7, 0)]);
        var pack = Assert.Single(PetPackReader.ImportFolder(_pets, MakeFolder("old", SheetPng(9, cells))).Imported);
        Assert.Equal(PetdexLayout.MappingVersion, pack.MappingVersion);
        Assert.Equal(new PetCapabilities(true, false, true, true), _pets.Read(pack.Id)!.Abilities);

        // Make it look like a pack from the first version: no sit, no sleep, no version number.
        var path = Path.Combine(_pets.DirOf(pack.Id), "manifest.json");
        var old = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        old["mappingVersion"] = 0;
        old["abilities"] = System.Text.Json.Nodes.JsonNode.Parse("""{"climb":true,"ceiling":false,"sit":false,"sleep":false}""");
        File.WriteAllText(path, old.ToJsonString());

        var upgraded = _pets.Read(pack.Id)!;
        Assert.Equal(PetdexLayout.MappingVersion, upgraded.MappingVersion);
        Assert.Equal(new PetCapabilities(true, false, true, true), upgraded.Abilities);
        Assert.Equal([41, 42], upgraded.Animations().Sleep.Select(p => p.Frame));
        Assert.Single(upgraded.Animations().Fidgets);
        Assert.Equal((cellFeet.x, cellFeet.y), (upgraded.Animations().Stand[0].AnchorX, upgraded.Animations().Stand[0].AnchorY));   // the feet it had

        // It was saved: a second read does not rebuild again.
        Assert.Equal(PetdexLayout.MappingVersion, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!["mappingVersion"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(0, "idle")]
    [InlineData(1, "run right")]
    [InlineData(2, "run left")]
    [InlineData(4, "jump")]
    public void Build_RejectsASheetMissingARequiredRow(int row, string name)
    {
        var sheet = PetdexLayout.Analyze(Pixels(9, Needed.Where(c => c.row != row)), 1536, 1872, out _)!;
        Assert.Null(PetdexLayout.Build(sheet, out var error));
        Assert.Contains($"“{name}”", error);
    }

    // ---- import -----------------------------------------------------------------------------------

    [Fact]
    public void ImportFolder_StoresAPetdexPackWithTheNameFromPetJson()
    {
        var result = PetPackReader.ImportFolder(_pets, MakeFolder("nezuko", json: PetJson("Nezuko")));
        Assert.Empty(result.Rejected);
        var pack = Assert.Single(result.Imported);
        Assert.Equal("Nezuko", pack.Name);
        Assert.Equal(PetPackManifest.KindPetdex, pack.Kind);
        Assert.Equal((192, 208), (pack.FrameWidth, pack.FrameHeight));
        Assert.Equal(Needed.Length, pack.Frames.Count);
        foreach (var n in pack.Frames) Assert.True(File.Exists(_pets.FramePath(pack.Id, n)));
        Assert.False(File.Exists(_pets.FramePath(pack.Id, 3)));   // empty cells are not stored

        var info = _pets.Info(_pets.Read(pack.Id)!);
        Assert.Equal(new PetCapabilities(true, false, false, false), info.Capabilities);   // from the layout, not from which frames exist
        Assert.Equal("petdex", info.Kind);
        Assert.Null(info.Credit);
    }

    [Fact]
    public void Capabilities_DoNotCountFramesThatOnlyHappenToExist()
    {
        // Frames 23-25 (the Shimeji ceiling frames) sit in the run-left row here, and 11 and 21 are run-right and run-left cells.
        var cells = Needed.Concat([(2, 6), (2, 7), (3, 0)]);   // run left gets frames 23 and 24
        var pack = Assert.Single(PetPackReader.ImportFolder(_pets, MakeFolder("x", SheetPng(9, cells))).Imported);
        Assert.Contains(23, pack.Frames);
        Assert.False(_pets.Info(pack).Capabilities.Ceiling);
        Assert.False(_pets.Info(pack).Capabilities.Sit);
    }

    [Fact]
    public void ImportFolder_GivesTheSourceAsACredit()
    {
        var result = PetdexReader.ImportFolder(_pets, MakeFolder("frieren"), new PetSource { Site = "petdex", Slug = "frieren-6", SubmittedBy = "Ana" })!;
        var info = _pets.Info(Assert.Single(result.Imported));
        Assert.Equal("by Ana · Petdex", info.Credit);
    }

    [Fact]
    public void ImportFolder_ImportsEachPetOfAFolderOfPets()
    {
        var root = Path.Combine(_dir, "pets-root");
        foreach (var n in new[] { "a", "b" })
        {
            var sub = Path.Combine(root, n);
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "pet.json"), PetJson(n.ToUpperInvariant()));
            File.WriteAllBytes(Path.Combine(sub, "spritesheet.png"), SheetPng(9, Needed));
        }
        var result = PetPackReader.ImportFolder(_pets, root);
        Assert.Equal(["A", "B"], result.Imported.Select(p => p.Name).Order());
    }

    [Fact]
    public void ImportFolder_FallsBackToTheIdWhenThereIsNoDisplayName()
    {
        var folder = MakeFolder("noname", json: """{"id":"some-id","spritesheetPath":"spritesheet.png"}""");
        Assert.Equal("some-id", Assert.Single(PetPackReader.ImportFolder(_pets, folder).Imported).Name);
    }

    [Fact]
    public void ImportV2Sheet_KeepsFramesUpTo88()
    {
        var cells = Needed.Concat([(9, 0), (10, 7)]);
        var pack = Assert.Single(PetPackReader.ImportFolder(_pets, MakeFolder("v2", SheetPng(11, cells))).Imported);
        Assert.Contains(88, pack.Frames);
        Assert.True(File.Exists(_pets.FramePath(pack.Id, 88)));
    }

    [Theory]
    [InlineData("wrongwidth")]
    [InlineData("wrongheight")]
    public void ImportFolder_RejectsASheetOfTheWrongSize(string kind)
    {
        var sheet = kind == "wrongwidth" ? SheetPng(9, [(0, 0)], width: 1200) : SheetPng(10, [(0, 0)]);
        var result = PetPackReader.ImportFolder(_pets, MakeFolder(kind, sheet));
        Assert.Empty(result.Imported);
        Assert.Contains("1536", Assert.Single(result.Rejected));
    }

    [Fact]
    public void ImportFolder_RejectsAMissingRequiredRow_WithAReadableReason()
    {
        var result = PetPackReader.ImportFolder(_pets, MakeFolder("norun", SheetPng(9, Needed.Where(c => c.row != 2))));
        Assert.Empty(result.Imported);
        Assert.Contains("“run left”", Assert.Single(result.Rejected));
    }

    [Fact]
    public void ImportFolder_RejectsAMissingOrUnreadableSheet()
    {
        var missing = MakeFolder("nosheet");
        File.Delete(Path.Combine(missing, "spritesheet.png"));
        Assert.Contains("missing", Assert.Single(PetPackReader.ImportFolder(_pets, missing).Rejected));

        var junk = MakeFolder("junk", Encoding.ASCII.GetBytes("this is not an image"));
        Assert.Empty(PetPackReader.ImportFolder(_pets, junk).Imported);
    }

    [Fact]
    public void ImportFolder_RejectsAnOversizeSheet()
    {
        var folder = MakeFolder("huge", new byte[(int)PetdexReader.MaxSheetBytes + 1]);
        Assert.Contains("larger than", Assert.Single(PetPackReader.ImportFolder(_pets, folder).Rejected));
    }

    [Theory]
    [InlineData("../../escape.png")]
    [InlineData("..\\..\\escape.png")]
    [InlineData("sub/spritesheet.png")]
    public void TheSheetIsFoundByFileNameOnly(string named)
    {
        var folder = MakeFolder("path", json: PetJson(sheet: named.Replace("\\", "\\\\")));
        Assert.Equal(named.Replace("\\", "/").Split('/')[^1], PetdexReader.SheetFileName(PetJson(sheet: named.Replace("\\", "\\\\"))));
        // "escape.png" is not next to pet.json, so nothing outside the folder is ever read.
        var result = PetPackReader.ImportFolder(_pets, folder);
        if (named.EndsWith("escape.png")) Assert.Empty(result.Imported);
        else Assert.Single(result.Imported);
    }

    [Fact]
    public void SheetFileName_RefusesNonImageNames()
    {
        Assert.Null(PetdexReader.SheetFileName(PetJson(sheet: "evil.exe")));
        Assert.Equal("spritesheet.webp", PetdexReader.SheetFileName("""{"id":"x"}"""));
        Assert.Null(PetdexReader.SheetFileName("not json"));
    }

    // ---- zips -------------------------------------------------------------------------------------

    private string MakeZip(string name, params (string path, byte[] data)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (p, data) in entries)
        {
            using var s = zip.CreateEntry(p).Open();
            s.Write(data);
        }
        return path;
    }

    [Fact]
    public void ImportZip_ReadsPetJsonAndTheSheet_AndRouteIsAutomatic()
    {
        var zip = MakeZip("pet.zip", ("pet.json", Encoding.UTF8.GetBytes(PetJson("Zipped"))), ("spritesheet.png", SheetPng(9, Needed)));
        var result = PetPackReader.ImportZip(_pets, zip);
        Assert.Empty(result.Rejected);
        Assert.Equal("Zipped", Assert.Single(result.Imported).Name);
    }

    [Fact]
    public void ImportZip_FindsEachPetInItsOwnFolder()
    {
        var sheet = SheetPng(9, Needed);
        var zip = MakeZip("two.zip",
            ("a/pet.json", Encoding.UTF8.GetBytes(PetJson("A"))), ("a/spritesheet.png", sheet),
            ("b/pet.json", Encoding.UTF8.GetBytes(PetJson("B"))), ("b/spritesheet.png", sheet));
        Assert.Equal(["A", "B"], PetPackReader.ImportZip(_pets, zip).Imported.Select(p => p.Name).Order());
    }

    [Fact]
    public void ImportZip_NeverTrustsEntryPaths()
    {
        var zip = MakeZip("evil.zip",
            ("../evil/pet.json", Encoding.UTF8.GetBytes(PetJson("Evil"))), ("../evil/spritesheet.png", SheetPng(9, Needed)));
        var result = PetPackReader.ImportZip(_pets, zip);
        Assert.Empty(result.Imported);                  // not a Petdex pet, and no Shimeji images either
        Assert.False(Directory.Exists(Path.Combine(_dir, "evil")));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(_dir, "Library"), "*", SearchOption.AllDirectories).Where(d => d.Contains("evil")));
    }

    [Fact]
    public void ImportZip_RejectsAZipThatIsTooBig()
    {
        var zip = Path.Combine(_dir, "big.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var s = z.CreateEntry("pet.json").Open()) s.Write(Encoding.UTF8.GetBytes(PetJson()));
            using var big = z.CreateEntry("spritesheet.png", CompressionLevel.NoCompression).Open();
            big.Write(new byte[(int)PetdexReader.MaxZipBytes + 1024]);
        }
        var result = PetPackReader.ImportZip(_pets, zip);
        Assert.Empty(result.Imported);
        Assert.Contains("larger than", Assert.Single(result.Rejected));
    }

    [Fact]
    public void AZipWithoutPetJson_StillGoesThroughShimejiImport()
    {
        var zip = MakeZip("plain.zip", ("readme.txt", "hi"u8.ToArray()));
        Assert.Contains("Shimeji", Assert.Single(PetPackReader.ImportZip(_pets, zip).Rejected));
    }

    // ---- limits and exports -----------------------------------------------------------------------

    [Fact]
    public void ShimejiImport_StillStopsAt46_ButAPetdexPackMayGoTo99()
    {
        Assert.Null(PetPackReader.FrameNumber("shime47.png"));
        Assert.Equal(47, PetPackReader.FrameNumber("shime47.png", ShimejiFrames.MaxFrameNumber));
        Assert.Null(PetPackReader.FrameNumber("shime401.png", ShimejiFrames.MaxFrameNumber));
    }

    [Fact]
    public void OldManifests_LoadAsShimeji()
    {
        var m = System.Text.Json.JsonSerializer.Deserialize<PetPackManifest>("""{"id":"pet-12345678","name":"Old","frameWidth":64,"frameHeight":64,"frames":[1,2]}""", Json.Options)!;
        Assert.Equal(PetPackManifest.KindShimeji, m.Kind);
        Assert.Null(m.Source);
        Assert.Null(m.Abilities);
        Assert.Equal(46, m.MaxFrame);
    }

    [Fact]
    public async Task Pack_RoundTrip_KeepsAPetdexPetWithItsSource()
    {
        var source = new Library(Path.Combine(_dir, "A"));
        var sourcePets = new PetLibrary(source);
        var folder = MakeFolder("rt", SheetPng(11, Needed.Concat([(10, 7)])));
        var pack = Assert.Single(PetdexReader.ImportFolder(sourcePets, folder, new PetSource { Site = "petdex", Slug = "rt-1", SubmittedBy = "Ana" })!.Imported);

        var file = Path.Combine(_dir, "pd" + PackService.Extension);
        await PackService.ExportAsync(source, null, [pack.Id], file, "test", null, null, TestContext.Current.CancellationToken);
        var target = new Library(Path.Combine(_dir, "B"));
        var (result, _) = await PackService.ImportAsync(target, file, null, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Pets);
        var targetPets = new PetLibrary(target);
        var back = targetPets.Read(pack.Id)!;
        Assert.Equal(PetPackManifest.KindPetdex, back.Kind);
        Assert.Equal("rt-1", back.Source!.Slug);
        Assert.Equal("Ana", back.Source.SubmittedBy);
        Assert.Equal(new PetCapabilities(true, false, false, false), back.Abilities);
        Assert.True(File.Exists(targetPets.FramePath(pack.Id, 88)));           // frames above 46 survive the trip
        Assert.Equal([17, 18, 19], back.Animations().Walk.Select(p => p.Frame));
        Assert.Equal([9, 10, 11], back.Animations().Walk.Select(p => p.Twin));
    }
}
