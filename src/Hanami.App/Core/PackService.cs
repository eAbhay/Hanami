using System.IO.Compression;
using System.Text.Json;

namespace Hanami.Core;

public sealed record ExportItem(string Id, string Kind, string Name, long SizeBytes, string? Thumbnail);

public sealed record PackResult(int Wallpapers, int Skipped, bool Settings);

/// <summary>
/// A .hanamipack is a zip holding pack.json, optionally settings.json, and
/// wallpapers/&lt;id&gt;/… copied verbatim from the library. Media is stored uncompressed
/// (it is already compressed), so packing a few GB is mostly a straight copy.
/// </summary>
public static class PackService
{
    public const string Extension = ".hanamipack";
    private const int FormatVersion = 1;

    private sealed record PackInfo(int Format, string AppVersion, DateTime CreatedAt, List<string> Wallpapers, bool Settings);

    public static List<ExportItem> Items(Library library)
    {
        var items = new List<ExportItem> { new("settings", "settings", "Settings (assignments, effects, power)", 0, null) };
        items.AddRange(library.List().Where(w => !w.Builtin)
            .Select(w => new ExportItem(w.Id, "wallpaper", w.Name, w.SizeBytes, w.Thumbnail)));
        return items;
    }

    public static async Task ExportAsync(Library library, AppSettings? settings, IReadOnlyCollection<string> ids,
        string destination, string appVersion, IProgress<(long done, long total, string label)>? progress, CancellationToken ct = default)
    {
        var dirs = ids.Where(id => !Library.IsBuiltin(id) && id != "settings")
            .Select(id => (id, dir: library.DirOf(id)))
            .Where(x => Directory.Exists(x.dir))
            .ToList();
        var files = dirs.SelectMany(x => new DirectoryInfo(x.dir).EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(f => (x.id, file: f))).ToList();
        var total = Math.Max(1, files.Sum(f => f.file.Length));
        long done = 0;

        var tmp = destination + ".partial";
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var info = new PackInfo(FormatVersion, appVersion, DateTime.UtcNow, dirs.Select(d => d.id).ToList(), settings is not null);
                await WriteTextAsync(zip, "pack.json", JsonSerializer.Serialize(info, Json.FileOptions));
                if (settings is not null)
                    await WriteTextAsync(zip, "settings.json", JsonSerializer.Serialize(settings, Json.FileOptions));
                if (settings?.Screensaver.Clock.Font is { } font && font.StartsWith(FontLibrary.LibraryPrefix, StringComparison.Ordinal))
                {
                    var fonts = new FontLibrary(library);
                    var fontId = font[FontLibrary.LibraryPrefix.Length..];
                    if (fonts.Exists(fontId)) zip.CreateEntryFromFile(fonts.PathOf(fontId), $"fonts/{fontId}", CompressionLevel.NoCompression);
                }

                var buffer = new byte[1 << 20];
                foreach (var (id, file) in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(library.DirOf(id), file.FullName).Replace('\\', '/');
                    var level = file.Extension is ".json" ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                    var entry = zip.CreateEntry($"wallpapers/{id}/{rel}", level);
                    await using var input = file.OpenRead();
                    await using var output = entry.Open();
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        progress?.Report((done, total, file.Name));
                    }
                }
            }
            File.Move(tmp, destination, overwrite: true);
            progress?.Report((total, total, "Done"));
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>Unpacks wallpapers into the library (existing ids are kept, not overwritten).
    /// Returns the pack's settings, if it had any, for the caller to apply.</summary>
    public static async Task<(PackResult result, AppSettings? settings)> ImportAsync(Library library, string packPath,
        IProgress<(long done, long total, string label)>? progress, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(packPath);
        if (zip.GetEntry("pack.json") is null) throw new InvalidDataException("Not a Hanami pack (pack.json missing).");

        AppSettings? settings = null;
        if (zip.GetEntry("settings.json") is { } settingsEntry)
        {
            using var reader = new StreamReader(settingsEntry.Open());
            settings = SettingsStore.Normalize(JsonSerializer.Deserialize<AppSettings>(await reader.ReadToEndAsync(ct), Json.Options));
        }

        var fontLib = new FontLibrary(library);
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("fonts/", StringComparison.Ordinal) && e.Name.Length > 0))
        {
            // fonts/<id> only: a valid id has no separators, so nothing can escape the folder
            if (entry.FullName != $"fonts/{entry.Name}" || !FontLibrary.IsValidId(entry.Name) || fontLib.Exists(entry.Name)) continue;
            Directory.CreateDirectory(fontLib.Dir);
            entry.ExtractToFile(fontLib.PathOf(entry.Name));
        }

        var entries = zip.Entries.Where(e => e.FullName.StartsWith("wallpapers/", StringComparison.Ordinal) && e.Name.Length > 0).ToList();
        var byId = entries.GroupBy(e => e.FullName.Split('/')[1]);
        var total = Math.Max(1, entries.Sum(e => e.Length));
        long done = 0;
        int imported = 0, skipped = 0;
        var buffer = new byte[1 << 20];

        foreach (var group in byId)
        {
            ct.ThrowIfCancellationRequested();
            var id = group.Key;
            var dir = library.DirOf(id);   // validates the id
            if (Directory.Exists(dir))
            {
                skipped++;
                done += group.Sum(e => e.Length);
                progress?.Report((done, total, $"Skipped {id} (already in library)"));
                continue;
            }
            var staging = dir + ".importing";
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var entry in group)
                {
                    var rel = entry.FullName[$"wallpapers/{id}/".Length..];
                    var target = Path.GetFullPath(Path.Combine(staging, rel));
                    if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Unsafe path in pack: {entry.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = entry.Open();
                    await using var output = File.Create(target);
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        done += read;
                        progress?.Report((done, total, entry.Name));
                    }
                }
                if (Library.ReadManifest(staging) is null) throw new InvalidDataException($"Wallpaper {id} in pack has no valid manifest.");
                Directory.Move(staging, dir);
                imported++;
            }
            catch
            {
                try { Directory.Delete(staging, recursive: true); } catch { }
                throw;
            }
        }
        progress?.Report((total, total, "Done"));
        return (new PackResult(imported, skipped, settings is not null), settings);
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(text);
    }
}
