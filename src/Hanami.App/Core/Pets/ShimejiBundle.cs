using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hanami.Core.Pets;

/// <summary>
/// A "Shimeji bundle" (the zips from shimeji.org): <c>manifest.json</c> (name, and where the sprites are: a folder, a file
/// pattern like <c>%04d.webp</c>, a count and a size), <c>animation.json</c> (named animations made of sprite numbers, with
/// ticks and a little movement) and the numbered sprites. Hanami reads the animations it plays (stand, walk, wall and
/// ceiling climbing, fall, drag, bounce, idle) as data; nothing in the bundle is run. Only the sprites those animations
/// use are kept, shrunk to at most <see cref="MaxCell"/> px, stored as frame <c>sprite + 1</c>.
/// </summary>
public static class ShimejiBundle
{
    public const string ManifestFile = "manifest.json", AnimationFile = "animation.json";
    public const int MaxSprites = ShimejiFrames.MaxFrameNumber - 1, MaxCell = 256, MaxSourceSize = 2048;
    public const long MaxZipBytes = 40L * 1024 * 1024, MaxSpriteBytes = 4L * 1024 * 1024;
    private const int MaxJsonBytes = 2 * 1024 * 1024, MaxBundles = 20, MaxDepth = 2;

    // ---- the animation table -----------------------------------------------------------------------

    private sealed class Anim
    {
        public string Subtype = "", Direction = "ANY";
        public List<(int sprite, int ticks)> Frames = new();
    }

    private static readonly Regex Pattern = new(@"^[A-Za-z0-9_.\-]{0,40}%0?\d{0,2}d\.(webp|png)$", RegexOptions.CultureInvariant);

    /// <summary>What was found: the animation names it plays by kind, so tests and the reader share one reading of animation.json.</summary>
    private static List<Anim>? ParseAnimations(string json, int spriteCount, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("animations", out var animations) || animations.ValueKind != JsonValueKind.Array)
            {
                error = "its animation.json has no animations";
                return null;
            }
            var list = new List<Anim>();
            foreach (var a in animations.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array) continue;
                var anim = new Anim
                {
                    Subtype = (a.TryGetProperty("subtype", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : "")!.ToUpperInvariant(),
                    Direction = (a.TryGetProperty("direction", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : "ANY")!.ToUpperInvariant(),
                };
                foreach (var f in frames.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object || !f.TryGetProperty("sprite", out var sp) || !sp.TryGetInt32(out var sprite)) continue;
                    if (sprite < 0 || sprite >= spriteCount) continue;
                    var ticks = f.TryGetProperty("durationTicks", out var t) && t.TryGetInt32(out var tv) ? Math.Clamp(tv, 1, 1000) : 6;
                    anim.Frames.Add((sprite, ticks));
                }
                if (anim.Frames.Count > 0) list.Add(anim);
            }
            return list;
        }
        catch (JsonException)
        {
            error = "its animation.json isn't valid";
            return null;
        }
    }

    /// <summary>The sprite numbers (0-based) the animations use, which are the only ones kept.</summary>
    internal static IReadOnlyList<int> SpritesUsed(string animationJson, int spriteCount) =>
        ParseAnimations(animationJson, spriteCount, out _)?.SelectMany(a => a.Frames.Select(f => f.sprite)).Distinct().Order().ToList() ?? new List<int>();

    /// <summary>The stand animation's sprites and the climb-from-the-ceiling ones, which decide where the feet and the ceiling grip are.</summary>
    internal static (IReadOnlyList<int> Stand, IReadOnlyList<int> Hang) AnchorSprites(string animationJson, int spriteCount)
    {
        var anims = ParseAnimations(animationJson, spriteCount, out _) ?? new();
        return (anims.Where(a => a.Subtype == "STAND").SelectMany(a => a.Frames.Select(f => f.sprite)).Distinct().ToList(),
                anims.Where(a => a.Subtype == "HANG").SelectMany(a => a.Frames.Select(f => f.sprite)).Distinct().ToList());
    }

    /// <summary>
    /// Builds the pack's data from animation.json. <paramref name="feetX"/>/<paramref name="feetY"/> are the bottom centre of the
    /// stand sprites' visible pixels and <paramref name="ceilingY"/> the top of the hanging ones, in the stored (shrunk) pixels.
    /// Needs a stand, a walk, a fall and a drag animation; null with an error otherwise.
    /// </summary>
    public static PetdexLayout.Built? Build(string animationJson, int spriteCount, int feetX, int feetY, int ceilingY, out string? error)
    {
        var anims = ParseAnimations(animationJson, spriteCount, out error);
        if (anims is null) return null;

        Anim? Find(string subtype, string direction = "ANY") =>
            anims.FirstOrDefault(a => a.Subtype == subtype && a.Direction == direction)
            ?? (direction == "ANY" ? anims.FirstOrDefault(a => a.Subtype == subtype) : null);

        var stand = Find("STAND"); var fall = Find("FALL"); var drag = Find("DRAG");
        var walkL = Find("WALK", "LEFT"); var walkR = Find("WALK", "RIGHT");
        foreach (var (name, anim) in new[] { ("stand", stand), ("walk", walkL ?? walkR), ("fall", fall), ("drag", drag) })
            if (anim is null) { error = $"its animation.json has no “{name}” animation"; return null; }
        // One of the two walks may be missing: the other is then drawn mirrored for its direction.
        var mirrorWalk = walkL is null;
        walkL ??= walkR;

        var bounce = Find("BOUNCE"); var idle = Find("IDLE"); var fling = Find("FLING");
        var climbL = Find("CLIMB", "LEFT") ?? Find("CLIMB", "RIGHT"); var climbR = Find("CLIMB", "RIGHT");
        var descendL = Find("DESCEND", "LEFT") ?? Find("DESCEND", "RIGHT"); var descendR = Find("DESCEND", "RIGHT");
        var hangL = Find("HANG", "LEFT") ?? Find("HANG", "RIGHT"); var hangR = Find("HANG", "RIGHT");

        PoseDef Pose(int sprite, int ticks, int anchorY, int vx = 0, int vy = 0, int twin = 0, bool mirror = false) =>
            new() { Frame = sprite + 1, Ticks = ticks, AnchorX = feetX, AnchorY = anchorY, Vx = vx, Vy = vy, Twin = twin, Mirror = mirror };

        // The right-facing twin of each frame: the paired animation's sprite when it draws something else (else mirrored).
        List<PoseDef> Play(Anim a, Anim? right, int anchorY, int vx = 0, int vy = 0, double tickScale = 1, bool mirror = false) =>
            a.Frames.Select((f, i) =>
            {
                var twin = right is not null && right.Frames.Count > 0 ? right.Frames[i % right.Frames.Count].sprite : f.sprite;
                return Pose(f.sprite, Math.Max(1, (int)Math.Round(f.ticks * tickScale)), anchorY, vx, vy, twin != f.sprite ? twin + 1 : 0, mirror);
            }).ToList();
        List<PoseDef> One(Anim a, int ticks, int anchorY, Anim? right = null)
        {
            var first = Play(a, right, anchorY)[0];
            first.Ticks = ticks;
            return [first];
        }

        var map = new Dictionary<string, List<PoseDef>>
        {
            ["stand"] = Play(stand!, null, feetY),
            ["walk"] = Play(walkL!, mirrorWalk ? null : walkR, feetY, vx: -3, mirror: mirrorWalk),
            ["run"] = Play(walkL!, mirrorWalk ? null : walkR, feetY, vx: -5, tickScale: 0.6, mirror: mirrorWalk),
            ["fall"] = Play(fall!, null, feetY),
            ["jump"] = Play(fling ?? fall!, null, feetY),
            ["bounce"] = Play(bounce ?? stand!, null, feetY),
            ["resist"] = Play(drag!, null, feetY),
            ["wave"] = Play(idle ?? stand!, null, feetY),
            ["waiting"] = Play(idle ?? stand!, null, feetY),
        };
        if (climbL is not null)
        {
            map["grabWall"] = One(climbL, 250, feetY, climbR);
            map["climbWallUp"] = Play(climbL, climbR, feetY, vy: -2);
            map["climbWallDown"] = Play(descendL ?? climbL, descendL is not null ? descendR : climbR, feetY, vy: 2);
        }
        if (hangL is not null)
        {
            map["grabCeiling"] = One(hangL, 250, ceilingY, hangR);
            map["climbCeiling"] = Play(hangL, hangR, ceilingY, vx: -2);
        }
        // Sitting, lying and sleeping don't exist in a bundle (see Abilities); they are mapped to frames that do.
        foreach (var key in new[] { "sit", "sitLookUp", "sprawl", "sleep", "sitDangle" }) map[key] = map["stand"];
        foreach (var key in new[] { "grabWall", "climbWallUp", "climbWallDown", "grabCeiling", "climbCeiling" })
            if (!map.ContainsKey(key)) map[key] = map["stand"];

        var dragFrame = drag!.Frames[0].sprite;
        var frames = map.Values.SelectMany(p => p).SelectMany(p => p.Twin > 0 ? new[] { p.Frame, p.Twin } : new[] { p.Frame }).Distinct().Order().ToList();
        return new PetdexLayout.Built(frames,
            new PetMapping { Animations = map, Swing = Enumerable.Repeat(dragFrame + 1, 5).ToList(), ImagePixels = true },
            new PetCapabilities(Climb: climbL is not null, Ceiling: hangL is not null, Sit: false, Sleep: false));
    }

    // ---- finding bundles ---------------------------------------------------------------------------

    /// <summary>Whether a folder is, or (a level or two down) holds, a bundle: a folder with an animation.json and a manifest.json.</summary>
    public static bool HasBundle(string folder) => FindFolders(folder).Any();

    private static IEnumerable<string> FindFolders(string root, int depth = 0)
    {
        if (File.Exists(Path.Combine(root, AnimationFile)) && File.Exists(Path.Combine(root, ManifestFile))) { yield return root; yield break; }
        if (depth >= MaxDepth) yield break;
        List<string> subs;
        try { subs = Directory.EnumerateDirectories(root).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var d in subs)
            foreach (var found in FindFolders(d, depth + 1)) yield return found;
    }

    private sealed record Source(string Label, string Manifest, string Animation, Func<string, byte[]?> ReadSprite);

    public static PetImportResult? ImportFolder(PetLibrary library, string folder)
    {
        var dirs = FindFolders(folder).Take(MaxBundles).ToList();
        if (dirs.Count == 0) return null;
        var result = new PetImportResult(new(), new());
        foreach (var dir in dirs)
        {
            var label = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            try
            {
                var manifest = ReadText(Path.Combine(dir, ManifestFile));
                var animation = ReadText(Path.Combine(dir, AnimationFile));
                Adopt(library, new Source(label, manifest, animation, rel =>
                {
                    // rel is built from the manifest's base path and a file pattern, never a path from outside; still, stay inside the folder.
                    var full = Path.GetFullPath(Path.Combine(dir, rel));
                    if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
                    return new FileInfo(full).Length > MaxSpriteBytes ? [] : File.ReadAllBytes(full);
                }), result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Rejected.Add($"“{label}”: couldn't be read ({ex.Message})");
            }
        }
        return result;
    }

    public static PetImportResult? ImportZip(PetLibrary library, string zipPath)
    {
        var result = new PetImportResult(new(), new());
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries.Take(PetPackReader.MaxZipEntries).ToList();
            string Dir(ZipArchiveEntry e) { var p = e.FullName.Replace('\\', '/'); return p[..(p.LastIndexOf('/') + 1)].TrimEnd('/'); }
            bool Safe(ZipArchiveEntry e) => !e.FullName.Replace('\\', '/').Split('/').Contains("..");
            var animations = entries.Where(e => e.Name.Equals(AnimationFile, StringComparison.OrdinalIgnoreCase) && Safe(e))
                .Where(a => entries.Any(m => m.Name.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase) && Dir(m) == Dir(a))).Take(MaxBundles).ToList();
            if (animations.Count == 0) return null;
            if (new FileInfo(zipPath).Length > MaxZipBytes)
            {
                result.Rejected.Add($"“{Path.GetFileName(zipPath)}” is larger than {MaxZipBytes / 1024 / 1024} MB");
                return result;
            }

            foreach (var anim in animations)
            {
                var dir = Dir(anim);
                var label = dir.Length == 0 ? Path.GetFileNameWithoutExtension(zipPath) : dir.Split('/')[^1];
                var manifestEntry = entries.First(m => m.Name.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase) && Dir(m) == dir);
                if (anim.Length > MaxJsonBytes || manifestEntry.Length > MaxJsonBytes) { result.Rejected.Add($"“{label}”: its json files are too large"); continue; }
                string Text(ZipArchiveEntry e) { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); }
                // Sprites are looked up by name among this bundle's entries; no entry path is ever used to write anything.
                Adopt(library, new Source(label, Text(manifestEntry), Text(anim), rel =>
                {
                    var want = (dir.Length == 0 ? "" : dir + "/") + rel;
                    var e = entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').Equals(want, StringComparison.OrdinalIgnoreCase));
                    if (e is null) return null;
                    if (e.Length > MaxSpriteBytes) return [];
                    using var ms = new MemoryStream();
                    using (var s = e.Open()) s.CopyTo(ms);
                    return ms.ToArray();
                }), result);
            }
        }
        catch (InvalidDataException)
        {
            result.Rejected.Add($"“{Path.GetFileName(zipPath)}” isn't a readable zip file.");
        }
        return result;
    }

    private static string ReadText(string path)
    {
        if (new FileInfo(path).Length > MaxJsonBytes) throw new IOException($"{Path.GetFileName(path)} is too large");
        return File.ReadAllText(path);
    }

    // ---- one bundle --------------------------------------------------------------------------------

    private static void Adopt(PetLibrary library, Source source, PetImportResult result)
    {
        var label = source.Label;
        string name = label, basePath, pattern;
        int count;
        try
        {
            using var doc = JsonDocument.Parse(source.Manifest);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())) name = n.GetString()!;
            else if (root.TryGetProperty("nameSlug", out var slug) && slug.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(slug.GetString())) name = slug.GetString()!;
            if (!root.TryGetProperty("sprites", out var sprites) || sprites.ValueKind != JsonValueKind.Object) { result.Rejected.Add($"“{label}”: its manifest has no sprites"); return; }
            basePath = sprites.TryGetProperty("basePath", out var bp) && bp.ValueKind == JsonValueKind.String ? bp.GetString()! : "sprites/";
            pattern = sprites.TryGetProperty("filePattern", out var fp) && fp.ValueKind == JsonValueKind.String ? fp.GetString()! : "%04d.webp";
            count = sprites.TryGetProperty("spriteCount", out var sc) && sc.TryGetInt32(out var scv) ? scv : 0;
        }
        catch (JsonException) { result.Rejected.Add($"“{label}”: its manifest isn't valid"); return; }

        basePath = basePath.Replace('\\', '/');
        if (basePath.StartsWith('/') || basePath.Contains(':') || basePath.Split('/').Contains("..") || basePath.Length > 60) { result.Rejected.Add($"“{label}”: its sprite folder isn't usable"); return; }
        if (basePath.Length > 0 && !basePath.EndsWith('/')) basePath += "/";
        if (!Pattern.IsMatch(pattern)) { result.Rejected.Add($"“{label}”: its sprite file names aren't supported"); return; }
        if (count < 1 || count > MaxSprites) { result.Rejected.Add($"“{label}”: it has {count} sprites (1 to {MaxSprites} are supported)"); return; }

        var used = SpritesUsed(source.Animation, count);
        if (used.Count == 0) { result.Rejected.Add($"“{label}”: its animation.json plays no sprites"); return; }
        var (standSprites, hangSprites) = AnchorSprites(source.Animation, count);

        // Decode, shrink and re-encode the sprites that are used.
        var pngs = new Dictionary<int, byte[]>();
        var visible = new Dictionary<int, (int minX, int maxX, int minY, int maxY)>();
        (int w, int h)? cell = null;
        foreach (var index in used)
        {
            var file = basePath + FormatPattern(pattern, index);
            var bytes = source.ReadSprite(file);
            if (bytes is null) { result.Rejected.Add($"“{label}”: sprite {file} is missing"); return; }
            if (bytes.Length == 0) { result.Rejected.Add($"“{label}”: sprite {file} is larger than {MaxSpriteBytes / 1024 / 1024} MB"); return; }
            var sprite = DecodeSprite(bytes, out var error);
            if (sprite is null) { result.Rejected.Add($"“{label}”: sprite {file}: {error}"); return; }
            var (png, w, h, box) = sprite.Value;
            if (cell is null) cell = (w, h);
            else if (cell != (w, h)) { result.Rejected.Add($"“{label}”: its sprites aren't all one size"); return; }
            pngs[index + 1] = png;
            visible[index] = box;
        }

        // Where the feet are (the stand sprites' bottom centre) and where the ceiling grip is (the top of the hanging ones).
        var stand = standSprites.Where(visible.ContainsKey).Select(i => visible[i]).ToList();
        if (stand.Count == 0) stand = visible.Values.Take(1).ToList();
        var feetX = (stand.Min(b => b.minX) + stand.Max(b => b.maxX) + 1) / 2;
        var feetY = stand.Max(b => b.maxY) + 1;
        var hang = hangSprites.Where(visible.ContainsKey).Select(i => visible[i]).ToList();
        var ceilingY = hang.Count > 0 ? hang.Min(b => b.minY) : 0;

        var built = Build(source.Animation, count, feetX, feetY, ceilingY, out var buildError);
        if (built is null) { result.Rejected.Add($"“{label}”: {buildError}"); return; }

        var frames = built.Frames.Where(pngs.ContainsKey).ToDictionary(f => f, f => pngs[f]);
        var check = PetPackReader.Validate(frames, built.Mapping);
        if (check.Error is { } problem) { result.Rejected.Add($"“{label}”: {problem}"); return; }
        result.Imported.Add(library.Add(name, frames, cell!.Value.w, cell.Value.h, built.Mapping, PetPackManifest.KindBundle, null, built.Abilities));
    }

    /// <summary>Expands a pattern like <c>%04d.webp</c> for a sprite number.</summary>
    internal static string FormatPattern(string pattern, int index) =>
        Regex.Replace(pattern, @"%(0?)(\d{0,2})d", m =>
            index.ToString(m.Groups[1].Length > 0 && m.Groups[2].Length > 0 ? "D" + m.Groups[2].Value : "D"), RegexOptions.CultureInvariant);

    /// <summary>Decodes a sprite (WebP or PNG), shrinks it to at most <see cref="MaxCell"/> px, and returns it as a PNG with its size and the box around its visible pixels.</summary>
    private static (byte[] Png, int Width, int Height, (int minX, int maxX, int minY, int maxY) Box)? DecodeSprite(byte[] bytes, out string? error)
    {
        error = null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if (frame.PixelWidth > MaxSourceSize || frame.PixelHeight > MaxSourceSize) { error = $"it is {frame.PixelWidth}×{frame.PixelHeight}, larger than {MaxSourceSize}"; return null; }
            var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (longest > MaxCell)
            {
                var k = (double)MaxCell / longest;
                frame = new TransformedBitmap(frame, new ScaleTransform(k, k));
            }
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var (w, h) = (converted.PixelWidth, converted.PixelHeight);
            var px = new byte[w * h * 4];
            converted.CopyPixels(px, w * 4, 0);

            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (px[(y * w + x) * 4 + 3] > PetdexLayout.AlphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        maxY = y;
                    }
            var box = maxX < 0 ? (0, w - 1, 0, h - 1) : (minX, maxX, minY, maxY);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
            using var output = new MemoryStream();
            encoder.Save(output);
            return (output.ToArray(), w, h, box);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            error = "Windows can't read it (WebP needs the “Web Media Extensions” from the Microsoft Store)";
            return null;
        }
    }
}
