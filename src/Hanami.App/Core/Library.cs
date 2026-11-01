using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hanami.Core;

public sealed class WallpaperManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "video";   // video | image
    public string File { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? DurationSec { get; set; }
    public bool HasThumbnail { get; set; }
}

/// <summary>What the settings UI lists (WallpaperInfo in docs/CONTRACTS.md).</summary>
public sealed record WallpaperInfo(
    string Id, string Name, string Type, bool Builtin, string? Src,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Thumbnail,
    long SizeBytes, int? Width, int? Height, double? DurationSec, DateTime? CreatedAt);

/// <summary>What a wallpaper page renders (WallpaperRef in docs/CONTRACTS.md).</summary>
public sealed record WallpaperRef(string Id, string Name, string Type, string? Src, string? Scene, string Fit = "cover");

/// <summary>The wallpapers folder: one sub-folder per wallpaper holding manifest.json, media.* and thumb.jpg.</summary>
public sealed class Library
{
    public const string Origin = "https://library.hanami";

    public static readonly string[] VideoExtensions = [".mp4", ".webm", ".m4v", ".mov"];
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif"];

    private static readonly (string Scene, string Name)[] Builtins =
        [("twilight", "Twilight"), ("dawn", "Dawn"), ("night", "Starry night")];

    public string Root { get; }
    public string WallpapersDir => Path.Combine(Root, "wallpapers");

    public Library(string root)
    {
        Root = root;
        Directory.CreateDirectory(WallpapersDir);
    }

    public static bool IsBuiltin(string id) => id.StartsWith("builtin:", StringComparison.Ordinal);

    public IEnumerable<WallpaperManifest> Manifests()
    {
        foreach (var dir in Directory.EnumerateDirectories(WallpapersDir))
        {
            var m = ReadManifest(dir);
            if (m is not null) yield return m;
        }
    }

    public List<WallpaperInfo> List()
    {
        var list = Builtins
            .Select(b => new WallpaperInfo($"builtin:{b.Scene}", b.Name, "builtin", true, null, null, 0, null, null, null, null))
            .ToList();
        list.AddRange(Manifests().OrderByDescending(m => m.CreatedAt).Select(ToInfo));
        return list;
    }

    public WallpaperRef? Resolve(string? id)
    {
        if (id is null) return null;
        if (IsBuiltin(id))
        {
            var scene = id["builtin:".Length..];
            var b = Builtins.FirstOrDefault(x => x.Scene == scene);
            return b.Scene is null ? null : new WallpaperRef(id, b.Name, "builtin", null, scene);
        }
        var m = ReadManifest(DirOf(id));
        return m is null ? null : new WallpaperRef(m.Id, m.Name, m.Type, MediaUrl(m), null);
    }

    public bool Exists(string id) => IsBuiltin(id) ? Resolve(id) is not null : File.Exists(Path.Combine(DirOf(id), "manifest.json"));

    /// <summary>Copies a video/image into the library so the wallpaper survives the original being deleted.</summary>
    public async Task<WallpaperInfo> ImportAsync(string sourcePath, IProgress<(long done, long total)>? progress, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var type = VideoExtensions.Contains(ext) ? "video"
                 : ImageExtensions.Contains(ext) ? "image"
                 : throw new InvalidOperationException($"Unsupported file type: {ext}");

        var id = NewId();
        var dir = DirOf(id);
        Directory.CreateDirectory(dir);
        var file = "media" + ext;
        try
        {
            await CopyWithProgressAsync(sourcePath, Path.Combine(dir, file), progress, ct);
            var manifest = new WallpaperManifest
            {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(sourcePath),
                Type = type,
                File = file,
                CreatedAt = DateTime.UtcNow,
            };
            WriteManifest(manifest);
            return ToInfo(manifest);
        }
        catch
        {
            TryDeleteDir(dir);
            throw;
        }
    }

    public void Rename(string id, string name)
    {
        var m = RequireManifest(id);
        m.Name = string.IsNullOrWhiteSpace(name) ? m.Name : name.Trim();
        WriteManifest(m);
    }

    public void Delete(string id)
    {
        if (IsBuiltin(id)) throw new InvalidOperationException("Built-in wallpapers can't be deleted.");
        RequireManifest(id);
        TryDeleteDir(DirOf(id));
    }

    public void SaveThumbnail(string id, string dataUrl, int? width, int? height, double? durationSec)
    {
        var m = RequireManifest(id);
        var comma = dataUrl.IndexOf(',');
        if (!dataUrl.StartsWith("data:image/", StringComparison.Ordinal) || comma < 0)
            throw new ArgumentException("Expected an image data URL.");
        File.WriteAllBytes(Path.Combine(DirOf(id), "thumb.jpg"), Convert.FromBase64String(dataUrl[(comma + 1)..]));
        m.HasThumbnail = true;
        m.Width = width ?? m.Width;
        m.Height = height ?? m.Height;
        m.DurationSec = durationSec ?? m.DurationSec;
        WriteManifest(m);
    }

    public long SizeOf(string id) =>
        IsBuiltin(id) || !Directory.Exists(DirOf(id)) ? 0
        : new DirectoryInfo(DirOf(id)).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    public string DirOf(string id)
    {
        // ids are generated by us, but packs come from outside: never let one escape the folder.
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
            throw new ArgumentException($"Invalid wallpaper id: {id}");
        return Path.Combine(WallpapersDir, id);
    }

    public static WallpaperManifest? ReadManifest(string dir)
    {
        var path = Path.Combine(dir, "manifest.json");
        if (!File.Exists(path)) return null;
        try
        {
            var m = JsonSerializer.Deserialize<WallpaperManifest>(File.ReadAllText(path), Json.Options);
            if (m is null || string.IsNullOrEmpty(m.Id) || !File.Exists(Path.Combine(dir, m.File))) return null;
            m.HasThumbnail = File.Exists(Path.Combine(dir, "thumb.jpg"));
            return m;
        }
        catch (Exception ex)
        {
            Log.Warn($"Skipping unreadable manifest {path}: {ex.Message}");
            return null;
        }
    }

    private WallpaperManifest RequireManifest(string id) =>
        ReadManifest(DirOf(id)) ?? throw new InvalidOperationException($"Wallpaper not found: {id}");

    private void WriteManifest(WallpaperManifest m) =>
        Json.WriteFileAtomic(Path.Combine(DirOf(m.Id), "manifest.json"), JsonSerializer.Serialize(m, Json.FileOptions));

    private WallpaperInfo ToInfo(WallpaperManifest m) => new(
        m.Id, m.Name, m.Type, false, MediaUrl(m),
        m.HasThumbnail ? $"{Origin}/wallpapers/{Uri.EscapeDataString(m.Id)}/thumb.jpg?v={File.GetLastWriteTimeUtc(Path.Combine(DirOf(m.Id), "thumb.jpg")).Ticks}" : null,
        SizeOf(m.Id), m.Width, m.Height, m.DurationSec, m.CreatedAt);

    private static string MediaUrl(WallpaperManifest m) =>
        $"{Origin}/wallpapers/{Uri.EscapeDataString(m.Id)}/{Uri.EscapeDataString(m.File)}";

    public static string NewId()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        return string.Create(10, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        });
    }

    private static async Task CopyWithProgressAsync(string src, string dst, IProgress<(long, long)>? progress, CancellationToken ct)
    {
        const int bufferSize = 1 << 20;
        await using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var output = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        var total = input.Length;
        var buffer = new byte[bufferSize];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report((done, total));
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Warn($"Could not delete {dir}: {ex.Message}"); }
    }
}
