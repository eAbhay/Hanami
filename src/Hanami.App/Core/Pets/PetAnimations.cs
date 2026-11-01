using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Hanami.Core.Pets;

/// <summary>One pose as stored in a pack's manifest (the JSON twin of <see cref="Pose"/>).</summary>
public sealed class PoseDef
{
    public int Frame { get; set; }
    public int Ticks { get; set; } = 10;
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    public int Vx { get; set; }
    public int Vy { get; set; }
    /// <summary>The frame faces the other way round from the usual left (e.g. a wall grab drawn facing right).</summary>
    public bool Mirror { get; set; }
}

/// <summary>
/// A pack's own frame map: which images play for each animation. Optional in a manifest and in a
/// <c>hanami-pet.json</c> next to the images; built from a pack's <c>conf/actions.xml</c> when it has one. Animations it
/// leaves out use the default Shimeji-ee table. Animation names: stand, walk, run, sit, sitLookUp, sprawl, sleep, fall,
/// bounce, grabWall, climbWallUp, climbWallDown, grabCeiling, climbCeiling, resist; <c>swing</c> = the five frames shown
/// while dragged (far left … far right).
/// </summary>
public sealed class PetMapping
{
    public Dictionary<string, List<PoseDef>>? Animations { get; set; }
    public List<int>? Swing { get; set; }
    /// <summary>Anchors in <see cref="Animations"/> are in image pixels (true), instead of authored for 128 px frames (false).</summary>
    public bool ImagePixels { get; set; }
}

/// <summary>
/// The animations a pet plays: Shimeji-ee's default frame table (<see cref="ShimejiFrames"/>), with any animation a pack
/// maps for itself replacing the default. A pet reads everything it plays from here, never from the static table.
/// </summary>
public sealed class PetAnimations
{
    public static readonly string[] Names =
        ["stand", "walk", "run", "sit", "sitLookUp", "sprawl", "sleep", "fall", "bounce", "grabWall", "climbWallUp", "climbWallDown",
         "grabCeiling", "climbCeiling", "resist"];

    public static readonly PetAnimations Default = new();

    private readonly Dictionary<string, Pose[]> _a = new()
    {
        ["stand"] = ShimejiFrames.Stand, ["walk"] = ShimejiFrames.Walk, ["run"] = ShimejiFrames.Run, ["sit"] = ShimejiFrames.Sit,
        ["sitLookUp"] = ShimejiFrames.SitLookUp, ["sprawl"] = ShimejiFrames.Sprawl, ["sleep"] = ShimejiFrames.Sprawl,
        ["fall"] = ShimejiFrames.Fall, ["bounce"] = ShimejiFrames.Bounce, ["grabWall"] = ShimejiFrames.GrabWall,
        ["climbWallUp"] = ShimejiFrames.ClimbWallUp, ["climbWallDown"] = ShimejiFrames.ClimbWallDown,
        ["grabCeiling"] = ShimejiFrames.GrabCeiling, ["climbCeiling"] = ShimejiFrames.ClimbCeiling, ["resist"] = ShimejiFrames.Resist,
    };

    /// <summary>The five dragged frames: far left, left, centre, right, far right (relative to the cursor).</summary>
    public int[] Swing { get; private set; } = [9, 7, 1, 8, 10];

    /// <summary>True when anchors are in image pixels; false when they are authored for 128 px frames and scaled to the pack's size.</summary>
    public bool ImagePixels { get; private set; }

    public Pose[] Stand => _a["stand"];
    public Pose[] Walk => _a["walk"];
    public Pose[] Run => _a["run"];
    public Pose[] Sit => _a["sit"];
    public Pose[] SitLookUp => _a["sitLookUp"];
    public Pose[] Sprawl => _a["sprawl"];
    public Pose[] Sleep => _a["sleep"];
    public Pose[] Fall => _a["fall"];
    public Pose[] Bounce => _a["bounce"];
    public Pose[] GrabWall => _a["grabWall"];
    public Pose[] ClimbWallUp => _a["climbWallUp"];
    public Pose[] ClimbWallDown => _a["climbWallDown"];
    public Pose[] GrabCeiling => _a["grabCeiling"];
    public Pose[] ClimbCeiling => _a["climbCeiling"];
    public Pose[] Resist => _a["resist"];

    /// <summary>The swing frame while dragged (same thresholds as Shimeji's Pinched action).</summary>
    public int Pinched(double footX, double cursorX) =>
        Swing[footX < cursorX - 50 ? 0 : footX < cursorX - 30 ? 1 : footX < cursorX + 30 ? 2 : footX < cursorX + 50 ? 3 : 4];

    private static IEnumerable<int> FramesOf(params Pose[][] poses) => poses.SelectMany(p => p).Select(p => p.Frame);

    /// <summary>Frames a pack must have: standing, walking, falling and everything shown while dragged.</summary>
    public IReadOnlyCollection<int> RequiredFrames =>
        FramesOf(Stand, Walk, Fall, Resist).Concat(Swing).Distinct().Order().ToList();

    /// <summary>What a pack with these frames can do: each ability needs every frame its animations use.</summary>
    public PetCapabilities CapabilitiesFor(IReadOnlyCollection<int> frames)
    {
        bool Has(params Pose[][] poses) => FramesOf(poses).All(frames.Contains);
        return new(Has(GrabWall, ClimbWallUp, ClimbWallDown), Has(GrabCeiling, ClimbCeiling), Has(Sit), Has(Sprawl, Sleep));
    }

    // ---- building from a pack's own data ---------------------------------------------------------

    /// <summary>The default table when <paramref name="mapping"/> is null or empty; else the pack's maps over the default (anchors rescaled to image pixels when needed).</summary>
    public static PetAnimations FromMapping(PetMapping? mapping, int frameWidth, int frameHeight)
    {
        if (mapping is null || (mapping.Animations is not { Count: > 0 } && mapping.Swing is not { Count: 5 })) return Default;
        var result = new PetAnimations { ImagePixels = mapping.ImagePixels };
        double kx = frameWidth / 128.0, ky = frameHeight / 128.0;
        if (mapping.ImagePixels)
        {
            // Everything the pack leaves out falls back to the default table, expressed in the same (image pixel) units.
            foreach (var key in Names)
                result._a[key] = Default._a[key].Select(p => p with { AnchorX = (int)Math.Round(p.AnchorX * kx), AnchorY = (int)Math.Round(p.AnchorY * ky) }).ToArray();
        }
        else foreach (var key in Names) result._a[key] = Default._a[key];

        var defined = new HashSet<string>();
        foreach (var (name, poses) in mapping.Animations ?? new())
        {
            if (Array.IndexOf(Names, name) < 0 || poses is not { Count: > 0 }) continue;
            result._a[name] = poses.Where(p => p.Frame is >= 1 and <= ShimejiFrames.NumFrames)
                .Select(p => new Pose(p.Frame, Math.Clamp(p.Ticks, 1, 1000), p.AnchorX, p.AnchorY, Math.Clamp(p.Vx, -40, 40), Math.Clamp(p.Vy, -40, 40), p.Mirror)).ToArray();
            if (result._a[name].Length == 0) result._a[name] = Default._a[name]; else defined.Add(name);
        }
        // A pack that maps stand/sprawl but not sleep sleeps on its sprawl pose.
        if (!defined.Contains("sleep") && defined.Contains("sprawl")) result._a["sleep"] = result._a["sprawl"];
        if (mapping.Swing is { Count: 5 } sw && sw.All(n => n is >= 1 and <= ShimejiFrames.NumFrames)) result.Swing = sw.ToArray();
        return result;
    }

    // ---- Shimeji-ee actions.xml ------------------------------------------------------------------

    private static readonly Regex ShimeFile = new(@"shime(\d{1,2})\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the animations Hanami uses out of a pack's own <c>actions.xml</c> (<c>Stand, Walk, Run, Sit, SitAndLookUp,
    /// Sprawl, Falling, Bouncing, GrabWall, ClimbWall, GrabCeiling, ClimbCeiling, Pinched, Resisting</c>). Anything it
    /// doesn't define, or defines with images not named shime&lt;N&gt;.png, stays default. Null when the XML is unreadable
    /// or defines none of them.
    /// </summary>
    public static PetMapping? MappingFromActionsXml(string xml)
    {
        XmlDocument doc;
        try
        {
            doc = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc.Load(reader);
        }
        catch (XmlException) { return null; }

        List<List<PoseDef>> Animations(string action)
        {
            var result = new List<List<PoseDef>>();
            foreach (XmlElement a in doc.GetElementsByTagName("Action"))
            {
                if (a.GetAttribute("Name") != action) continue;
                foreach (XmlNode child in a.ChildNodes)
                    if (child is XmlElement { LocalName: "Animation" } anim)
                    {
                        var poses = new List<PoseDef>();
                        foreach (XmlNode p in anim.ChildNodes)
                            if (p is XmlElement { LocalName: "Pose" } pose && ParsePose(pose) is { } def) poses.Add(def);
                        if (poses.Count > 0) result.Add(poses);
                    }
                break;
            }
            return result;
        }

        var map = new Dictionary<string, List<PoseDef>>();
        void Take(string key, string action, int index = 0)
        {
            var anims = Animations(action);
            if (anims.Count > index) map[key] = anims[index];
        }
        Take("stand", "Stand"); Take("walk", "Walk"); Take("run", "Run"); Take("sit", "Sit"); Take("sitLookUp", "SitAndLookUp");
        Take("sprawl", "Sprawl"); Take("fall", "Falling"); Take("bounce", "Bouncing"); Take("grabWall", "GrabWall");
        Take("climbWallUp", "ClimbWall", 0); Take("climbWallDown", "ClimbWall", 1);
        Take("grabCeiling", "GrabCeiling"); Take("climbCeiling", "ClimbCeiling"); Take("resist", "Resisting");

        List<int>? swing = null;
        var pinched = Animations("Pinched");
        if (pinched.Count == 5) swing = pinched.Select(a => a[0].Frame).ToList();

        if (map.Count == 0 && swing is null) return null;
        return new PetMapping { Animations = map, Swing = swing, ImagePixels = true };
    }

    private static PoseDef? ParsePose(XmlElement pose)
    {
        var m = ShimeFile.Match(pose.GetAttribute("Image"));
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var frame) || frame is < 1 or > ShimejiFrames.NumFrames) return null;
        var (ax, ay) = Pair(pose.GetAttribute("ImageAnchor"));
        var (vx, vy) = Pair(pose.GetAttribute("Velocity"));
        var ticks = int.TryParse(pose.GetAttribute("Duration"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 10;
        return new PoseDef { Frame = frame, Ticks = Math.Max(1, ticks), AnchorX = ax, AnchorY = ay, Vx = vx, Vy = vy };
    }

    private static (int, int) Pair(string s)
    {
        var parts = s.Split(',');
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? (x, y) : (0, 0);
    }
}
