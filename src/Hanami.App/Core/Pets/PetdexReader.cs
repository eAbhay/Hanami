using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hanami.Core.Pets;

/// <summary>
/// Reads a Petdex pet (petdex.dev): a zip or folder holding <c>pet.json</c> and the sprite sheet it names. Only those two
/// files are read, by file name; nothing from an entry's path is ever written. The sheet is sliced into one PNG per cell
/// that has art and stored through <see cref="PetLibrary.Add"/> as a pack of kind "petdex".
/// </summary>
public static class PetdexReader
{
    public const string ManifestFile = "pet.json";
    public const long MaxZipBytes = 20L * 1024 * 1024, MaxSheetBytes = 10L * 1024 * 1024;
    private const int MaxJsonBytes = 256 * 1024, MaxPets = 50, MaxDepth = 2;

    private sealed class PetJson
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
        public string? SpritesheetPath { get; set; }
    }

    // ---- folders ---------------------------------------------------------------------------------

    /// <summary>Imports every Petdex pet in a folder (a pet's own folder, or one holding several, like ~/.codex/pets); null when there is no pet.json.</summary>
    public static PetImportResult? ImportFolder(PetLibrary library, string folder, PetSource? source = null)
    {
        var dirs = FindFolders(folder).Take(MaxPets).ToList();
        if (dirs.Count == 0) return null;
        var result = new PetImportResult(new(), new());
        foreach (var dir in dirs)
        {
            var label = Path.GetFileName(dir.TrimEnd('\\', '/'));
            try
            {
                var json = ReadText(Path.Combine(dir, ManifestFile));
                var sheetName = SheetFileName(json);
                var sheetPath = sheetName is null ? null : Path.Combine(dir, sheetName);
                if (sheetPath is null || !File.Exists(sheetPath)) { result.Rejected.Add($"“{label}”: its sprite sheet is missing"); continue; }
                if (new FileInfo(sheetPath).Length > MaxSheetBytes) { result.Rejected.Add($"“{label}”: its sprite sheet is larger than {MaxSheetBytes / 1024 / 1024} MB"); continue; }
                Adopt(library, json, File.ReadAllBytes(sheetPath), label, source, result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Rejected.Add($"“{label}”: couldn't be read ({ex.Message})");
            }
        }
        return result;
    }

    private static IEnumerable<string> FindFolders(string root, int depth = 0)
    {
        if (File.Exists(Path.Combine(root, ManifestFile))) { yield return root; yield break; }
        if (depth >= MaxDepth) yield break;
        List<string> subs;
        try { subs = Directory.EnumerateDirectories(root).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var d in subs)
            foreach (var found in FindFolders(d, depth + 1)) yield return found;
    }

    private static string ReadText(string path)
    {
        if (new FileInfo(path).Length > MaxJsonBytes) throw new IOException("pet.json is too large");
        return File.ReadAllText(path);
    }

    // ---- zips ------------------------------------------------------------------------------------

    /// <summary>Imports the Petdex pets in a zip; null when it holds no pet.json.</summary>
    public static PetImportResult? ImportZip(PetLibrary library, string zipPath, PetSource? source = null)
    {
        var result = new PetImportResult(new(), new());
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries.Take(PetPackReader.MaxZipEntries).ToList();
            var manifests = entries.Where(IsManifest).Take(MaxPets).ToList();
            if (manifests.Count == 0) return null;
            if (new FileInfo(zipPath).Length > MaxZipBytes)
            {
                result.Rejected.Add($"“{Path.GetFileName(zipPath)}” is larger than {MaxZipBytes / 1024 / 1024} MB");
                return result;
            }

            foreach (var manifest in manifests)
            {
                var dir = DirOf(manifest.FullName);
                var label = dir.Length == 0 ? Path.GetFileNameWithoutExtension(zipPath) : dir.Split('/')[^1];
                if (manifest.Length > MaxJsonBytes) { result.Rejected.Add($"“{label}”: pet.json is too large"); continue; }
                string json;
                using (var reader = new StreamReader(manifest.Open())) json = reader.ReadToEnd();
                var sheetName = SheetFileName(json);
                // The sheet is looked up by file name among the files next to pet.json; its path inside the zip is never used.
                var sheet = sheetName is null ? null : entries.FirstOrDefault(e => e.Name.Length > 0 && DirOf(e.FullName) == dir
                    && e.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
                if (sheet is null) { result.Rejected.Add($"“{label}”: its sprite sheet is missing"); continue; }
                if (sheet.Length > MaxSheetBytes) { result.Rejected.Add($"“{label}”: its sprite sheet is larger than {MaxSheetBytes / 1024 / 1024} MB"); continue; }
                using var ms = new MemoryStream();
                using (var input = sheet.Open()) input.CopyTo(ms);
                Adopt(library, json, ms.ToArray(), label, source, result);
            }
        }
        catch (InvalidDataException)
        {
            result.Rejected.Add($"“{Path.GetFileName(zipPath)}” isn't a readable zip file.");
        }
        return result;
    }

    private static bool IsManifest(ZipArchiveEntry e) =>
        e.Name.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase) && !e.FullName.Replace('\\', '/').Split('/').Contains("..");

    private static string DirOf(string fullName)
    {
        var path = fullName.Replace('\\', '/');
        return path[..(path.LastIndexOf('/') + 1)].TrimEnd('/');
    }

    // ---- one pet ---------------------------------------------------------------------------------

    /// <summary>The file name of the sprite sheet pet.json names (its folder part dropped), or null when it isn't an image name.</summary>
    internal static string? SheetFileName(string json)
    {
        try
        {
            var pet = JsonSerializer.Deserialize<PetJson>(json, Json.Options);
            var name = (pet?.SpritesheetPath ?? "spritesheet.webp").Replace('\\', '/');
            name = name[(name.LastIndexOf('/') + 1)..].Trim();
            return name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ? name : null;
        }
        catch (JsonException) { return null; }
    }

    private static void Adopt(PetLibrary library, string json, byte[] sheetBytes, string label, PetSource? source, PetImportResult result)
    {
        string name;
        try
        {
            var pet = JsonSerializer.Deserialize<PetJson>(json, Json.Options);
            name = FirstText(pet?.DisplayName, source?.Slug, pet?.Id, label);
        }
        catch (JsonException) { result.Rejected.Add($"“{label}”: pet.json isn't valid"); return; }

        var decoded = DecodeSheet(sheetBytes, out var error);
        if (decoded is null) { result.Rejected.Add($"“{label}”: {error}"); return; }
        var (pixels, width, height, bitmap) = decoded.Value;

        var sheet = PetdexLayout.Analyze(pixels, width, height, out error);
        if (sheet is null) { result.Rejected.Add($"“{label}”: {error}"); return; }
        var built = PetdexLayout.Build(sheet, out error);
        if (built is null) { result.Rejected.Add($"“{label}”: {error}"); return; }

        var frames = new Dictionary<int, byte[]>();
        foreach (var n in built.Frames)
        {
            var row = (n - 1) / PetdexLayout.Columns;
            var col = (n - 1) % PetdexLayout.Columns;
            var cell = new CroppedBitmap(bitmap, new Int32Rect(col * PetdexLayout.CellWidth, row * PetdexLayout.CellHeight, PetdexLayout.CellWidth, PetdexLayout.CellHeight));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(cell));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            frames[n] = ms.ToArray();
        }

        var check = PetPackReader.Validate(frames, built.Mapping);
        if (check.Error is { } problem) { result.Rejected.Add($"“{label}”: {problem}"); return; }
        result.Imported.Add(library.Add(name, frames, PetdexLayout.CellWidth, PetdexLayout.CellHeight, built.Mapping,
            PetPackManifest.KindPetdex, source, built.Abilities));
    }

    private static string FirstText(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "Pet";

    /// <summary>Decodes a sheet (WebP or PNG, through the system's image codecs) to 32-bit BGRA pixels.</summary>
    private static (byte[] Pixels, int Width, int Height, BitmapSource Bitmap)? DecodeSheet(byte[] bytes, out string? error)
    {
        error = null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var first = decoder.Frames[0];
            var (w, h) = (first.PixelWidth, first.PixelHeight);
            if (PetdexLayout.RowsFor(w, h) is null)   // checked before converting, so a huge image is never expanded
            {
                error = $"the sprite sheet is {w}×{h}, not {PetdexLayout.SheetWidth}×{PetdexLayout.RowsV1 * PetdexLayout.CellHeight} or {PetdexLayout.SheetWidth}×{PetdexLayout.RowsV2 * PetdexLayout.CellHeight}";
                return null;
            }
            var converted = new FormatConvertedBitmap(first, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            var pixels = new byte[w * h * 4];
            converted.CopyPixels(pixels, w * 4, 0);
            return (pixels, w, h, converted);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            error = "Windows can't read its sprite sheet (WebP needs the “Web Media Extensions” from the Microsoft Store)";
            return null;
        }
    }
}
