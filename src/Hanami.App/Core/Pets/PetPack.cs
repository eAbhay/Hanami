using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Hanami.Core.Pets;

/// <summary>Library/pets/&lt;id&gt;/manifest.json (see docs/CONTRACTS.md).</summary>
public sealed class PetPackManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    /// <summary>Which shime numbers exist.</summary>
    public List<int> Frames { get; set; } = new();
}

public sealed record PetCapabilities(bool Climb, bool Ceiling, bool Sit, bool Sleep)
{
    public static PetCapabilities Of(IReadOnlyCollection<int> frames)
    {
        bool Has(int[] need) => need.All(frames.Contains);
        return new(Has(ShimejiFrames.ClimbFrames), Has(ShimejiFrames.CeilingFrames), Has(ShimejiFrames.SitFrames), Has(ShimejiFrames.SleepFrames));
    }
}

/// <summary>What the settings UI lists (PetPackInfo in docs/CONTRACTS.md).</summary>
public sealed record PetPackInfo(string Id, string Name, string Preview, int Frames, long SizeBytes,
    int FrameWidth, int FrameHeight, PetCapabilities Capabilities);

/// <summary>Pets that were found in a source, and the ones that were not usable (with a readable reason each).</summary>
public sealed record PetImportResult(List<PetPackManifest> Imported, List<string> Rejected);

/// <summary>
/// Reads Shimeji image sets (shime1.png … shime46.png) from folders and zips, validates them and stores
/// them in the pet library. Paths inside a zip are never used to write files: frames are read into memory by
/// file name and written to paths this class builds itself.
/// </summary>
public static class PetPackReader
{
    public const int MaxPngBytes = 2 * 1024 * 1024;
    public const long MaxPackBytes = 40L * 1024 * 1024;
    /// <summary>Entries looked at per zip (a zip may hold several characters) and per folder.</summary>
    public const int MaxZipEntries = 2000, MaxFilesPerFolder = 200;
    public const int MaxFrameSize = 1024;

    private static readonly Regex FrameName = new(@"^shime(\d{1,2})\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>N for a file called shime&lt;N&gt;.png with N in 1–46, else null.</summary>
    public static int? FrameNumber(string fileName)
    {
        var m = FrameName.Match(fileName);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is >= 1 and <= ShimejiFrames.NumFrames ? n : null;
    }

    private sealed record Candidate(string Name, Dictionary<int, byte[]> Frames);

    // ---- folders ---------------------------------------------------------------------------------

    public static PetImportResult ImportFolder(PetLibrary library, string folder)
    {
        var result = new PetImportResult(new(), new());
        var found = new List<Candidate>();
        foreach (var dir in ImageFolders(folder))
        {
            var frames = new Dictionary<int, byte[]>();
            foreach (var file in Directory.EnumerateFiles(dir).Take(MaxFilesPerFolder))
            {
                if (FrameNumber(Path.GetFileName(file)) is not { } n) continue;
                if (new FileInfo(file).Length > MaxPngBytes) { frames[n] = []; continue; }   // empty = oversize, rejected by Validate
                frames[n] = File.ReadAllBytes(file);
            }
            if (frames.Count > 0) found.Add(new Candidate(NameOfFolder(dir, folder), frames));
        }
        if (found.Count == 0) result.Rejected.Add($"No Shimeji images (shime1.png … shime{ShimejiFrames.NumFrames}.png) found in “{Path.GetFileName(folder.TrimEnd('\\', '/'))}”.");
        Adopt(library, found, result);
        return result;
    }

    /// <summary>The folder itself, its img/, one level deeper, and img/&lt;Name&gt; (shimeji-ee's own layout).</summary>
    private static IEnumerable<string> ImageFolders(string root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Candidates()
        {
            yield return root;
            var img = Path.Combine(root, "img");
            if (Directory.Exists(img))
            {
                yield return img;
                foreach (var d in Directory.EnumerateDirectories(img)) yield return d;
            }
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                yield return d;
                var inner = Path.Combine(d, "img");
                if (Directory.Exists(inner)) yield return inner;
            }
        }
        foreach (var dir in Candidates())
            if (seen.Add(Path.GetFullPath(dir))) yield return dir;
    }

    private static string NameOfFolder(string dir, string root)
    {
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (name.Equals("img", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('\\', '/')) ?? "");
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(root.TrimEnd('\\', '/')) : name;
    }

    // ---- zips ------------------------------------------------------------------------------------

    public static PetImportResult ImportZip(PetLibrary library, string zipPath)
    {
        var result = new PetImportResult(new(), new());
        var fallbackName = Path.GetFileNameWithoutExtension(zipPath);
        var groups = new Dictionary<string, Dictionary<int, byte[]>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries.Take(MaxZipEntries))
            {
                if (entry.Name.Length == 0 || FrameNumber(entry.Name) is not { } n) continue;
                var path = entry.FullName.Replace('\\', '/');
                if (path.Split('/').Contains("..") || path.StartsWith('/') || path.Contains(':')) continue;   // never trust entry paths
                var dir = path[..(path.LastIndexOf('/') + 1)].TrimEnd('/');
                if (!groups.TryGetValue(dir, out var frames)) groups[dir] = frames = new();
                if (frames.Count >= MaxFilesPerFolder) continue;
                if (entry.Length > MaxPngBytes) { frames[n] = []; continue; }
                using var input = entry.Open();
                using var ms = new MemoryStream();
                input.CopyTo(ms);
                frames[n] = ms.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            result.Rejected.Add($"“{Path.GetFileName(zipPath)}” isn't a readable zip file.");
            return result;
        }

        var found = groups.Select(kv => new Candidate(NameOfZipFolder(kv.Key, fallbackName), kv.Value)).ToList();
        if (found.Count == 0) result.Rejected.Add($"No Shimeji images (shime1.png … shime{ShimejiFrames.NumFrames}.png) found in “{Path.GetFileName(zipPath)}”.");
        Adopt(library, found, result);
        return result;
    }

    private static string NameOfZipFolder(string dir, string fallback)
    {
        var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = parts.Length - 1;
        if (i >= 0 && parts[i].Equals("img", StringComparison.OrdinalIgnoreCase)) i--;
        return i >= 0 ? parts[i] : fallback;
    }

    // ---- validate + store ------------------------------------------------------------------------

    private static void Adopt(PetLibrary library, List<Candidate> found, PetImportResult result)
    {
        foreach (var c in found)
        {
            var check = Validate(c.Frames);
            if (check.Error is { } error) { result.Rejected.Add($"“{c.Name}”: {error}"); continue; }
            result.Imported.Add(library.Add(c.Name, c.Frames, check.Width, check.Height));
        }
    }

    public sealed record Validation(string? Error, int Width, int Height);

    /// <summary>Checks the required frames exist, every PNG decodes, and all frames share one size.</summary>
    public static Validation Validate(IReadOnlyDictionary<int, byte[]> frames)
    {
        var missing = ShimejiFrames.Required.Where(n => !frames.ContainsKey(n)).ToList();
        if (missing.Count > 0)
            return new($"missing required frame{(missing.Count == 1 ? "" : "s")} {string.Join(", ", missing.Select(n => $"shime{n}"))}", 0, 0);
        if (frames.Values.Sum(b => (long)b.Length) > MaxPackBytes)
            return new($"the images add up to more than {MaxPackBytes / 1024 / 1024} MB", 0, 0);

        int width = 0, height = 0;
        foreach (var (n, bytes) in frames.OrderBy(kv => kv.Key))
        {
            if (bytes.Length == 0) return new($"shime{n}.png is larger than {MaxPngBytes / 1024 / 1024} MB", 0, 0);
            if (!TryDecodeSize(bytes, out var w, out var h)) return new($"shime{n}.png isn't a valid PNG image", 0, 0);
            if (w > MaxFrameSize || h > MaxFrameSize) return new($"shime{n}.png is {w}×{h}, larger than {MaxFrameSize}×{MaxFrameSize}", 0, 0);
            if (width == 0) (width, height) = (w, h);
            else if ((w, h) != (width, height)) return new($"the frames aren't all the same size (shime{n}.png is {w}×{h}, others {width}×{height})", 0, 0);
        }
        return new(null, width, height);
    }

    /// <summary>Whether <paramref name="bytes"/> is a PNG the system can decode, and its size.</summary>
    public static bool TryDecodeSize(byte[] bytes, out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return false;
            using var ms = new MemoryStream(bytes);
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            (width, height) = (frame.PixelWidth, frame.PixelHeight);
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or IOException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>The pet packs in Library/pets/: one folder per pack with manifest.json and img/shime&lt;N&gt;.png.</summary>
public sealed class PetLibrary
{
    public const string IdPrefix = "pet-";

    private readonly Library _library;

    public PetLibrary(Library library) => _library = library;

    public string Dir => Path.Combine(_library.Root, "pets");

    /// <summary>Ids also arrive from packs and settings: plain names only, no separators or "..".</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 40 && id.StartsWith(IdPrefix, StringComparison.Ordinal)
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public string DirOf(string id) =>
        IsValidId(id) ? Path.Combine(Dir, id) : throw new ArgumentException($"Invalid pet pack id: {id}");

    public string FramePath(string id, int frame) => Path.Combine(DirOf(id), "img", $"shime{frame}.png");

    public bool Exists(string id) => IsValidId(id) && File.Exists(Path.Combine(DirOf(id), "manifest.json"));

    public PetPackManifest? Read(string id)
    {
        try
        {
            var path = Path.Combine(DirOf(id), "manifest.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<PetPackManifest>(File.ReadAllText(path), Json.Options) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException) { return null; }
    }

    public IEnumerable<PetPackManifest> Manifests()
    {
        if (!Directory.Exists(Dir)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(Dir))
            if (Read(Path.GetFileName(dir)) is { } m && m.Id == Path.GetFileName(dir)) yield return m;
    }

    public List<PetPackInfo> List() => Manifests().OrderBy(m => m.CreatedAt).Select(Info).ToList();

    public PetPackInfo Info(PetPackManifest m)
    {
        var size = new DirectoryInfo(DirOf(m.Id)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        return new PetPackInfo(m.Id, m.Name, $"{Library.Origin}/pets/{m.Id}/img/shime1.png", m.Frames.Count, size,
            m.FrameWidth, m.FrameHeight, PetCapabilities.Of(m.Frames));
    }

    /// <summary>Stores a validated image set as a new pack.</summary>
    public PetPackManifest Add(string name, IReadOnlyDictionary<int, byte[]> frames, int width, int height)
    {
        var id = IdPrefix + Guid.NewGuid().ToString("N")[..8];
        var dir = DirOf(id);
        var staging = dir + ".importing";
        Directory.CreateDirectory(Path.Combine(staging, "img"));
        try
        {
            foreach (var (n, bytes) in frames) File.WriteAllBytes(Path.Combine(staging, "img", $"shime{n}.png"), bytes);
            var manifest = new PetPackManifest
            {
                Id = id, Name = CleanName(name), CreatedAt = DateTime.UtcNow, FrameWidth = width, FrameHeight = height,
                Frames = frames.Keys.Order().ToList(),
            };
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, Json.FileOptions));
            Directory.Move(staging, dir);
            return manifest;
        }
        catch
        {
            try { Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }

    public void Rename(string id, string name)
    {
        var m = Read(id) ?? throw new InvalidOperationException("Pet pack not found.");
        m.Name = CleanName(name, m.Name);
        File.WriteAllText(Path.Combine(DirOf(id), "manifest.json"), JsonSerializer.Serialize(m, Json.FileOptions));
    }

    public void Delete(string id)
    {
        if (!Exists(id)) throw new InvalidOperationException("Pet pack not found.");
        Directory.Delete(DirOf(id), recursive: true);
    }

    /// <summary>
    /// Re-checks a pack that arrived from a .hanamipack (extracted to <paramref name="staging"/>): only
    /// manifest.json and img/shime&lt;N&gt;.png are accepted, and the images must pass the same validation as an import.
    /// </summary>
    public static string? VerifyExtracted(string staging)
    {
        try
        {
            var manifestPath = Path.Combine(staging, "manifest.json");
            if (!File.Exists(manifestPath)) return "has no manifest";
            var manifest = JsonSerializer.Deserialize<PetPackManifest>(File.ReadAllText(manifestPath), Json.Options);
            if (manifest is null || !IsValidId(manifest.Id)) return "has an invalid manifest";
            var frames = new Dictionary<int, byte[]>();
            var imgDir = Path.Combine(staging, "img");
            if (!Directory.Exists(imgDir)) return "has no images";
            foreach (var file in Directory.EnumerateFiles(imgDir).Take(PetPackReader.MaxFilesPerFolder))
            {
                if (PetPackReader.FrameNumber(Path.GetFileName(file)) is not { } n) continue;
                frames[n] = new FileInfo(file).Length > PetPackReader.MaxPngBytes ? [] : File.ReadAllBytes(file);
            }
            return PetPackReader.Validate(frames).Error;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return "is unreadable"; }
    }

    private static string CleanName(string name, string fallback = "Pet")
    {
        var n = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length > 60) n = n[..60].TrimEnd();
        return n.Length == 0 ? fallback : n;
    }
}
