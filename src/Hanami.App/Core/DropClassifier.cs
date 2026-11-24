using System.IO.Compression;
using Hanami.Core.Pets;

namespace Hanami.Core;

/// <summary>What a dropped file or folder is, so the right import can take it.</summary>
public enum DropKind { Photo, Video, PetPack, Rejected }

/// <summary>One sorted drop: its kind, and for a rejected one a readable reason.</summary>
public sealed record DropItem(string Path, DropKind Kind, string? Reason = null)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
}

/// <summary>
/// Sorts dropped paths into photos, videos, pet packs and rejects. Pure over the file system: a zip is only looked at by the
/// names of its entries (never extracted), a folder by the files it holds. A pet pack is what the pet import recognises: a
/// Petdex <c>pet.json</c>, or Shimeji <c>shime&lt;N&gt;.png</c> images.
/// </summary>
public static class DropClassifier
{
    public static IReadOnlyList<DropItem> Classify(IEnumerable<string> paths) => paths.Select(Classify).ToList();

    public static DropItem Classify(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return PetPackReader.HasPetFiles(path) ? new(path, DropKind.PetPack)
                    : new(path, DropKind.Rejected, "this folder has no Petdex pet (pet.json) or Shimeji images (shime1.png …)");
            if (!File.Exists(path)) return new(path, DropKind.Rejected, "it couldn't be found");

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (Library.ImageExtensions.Contains(ext)) return new(path, DropKind.Photo);
            if (Library.VideoExtensions.Contains(ext)) return new(path, DropKind.Video);
            if (ext == ".zip")
                return ZipHasPets(path) ? new(path, DropKind.PetPack)
                    : new(path, DropKind.Rejected, "this zip has no Petdex pet (pet.json) or Shimeji images (shime1.png …)");
            return new(path, DropKind.Rejected, string.IsNullOrEmpty(ext) ? "it isn't a photo, video or pet pack" : $"{ext} files aren't supported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(path, DropKind.Rejected, "it couldn't be read");
        }
    }

    /// <summary>Whether a zip holds a pet, judged by entry names alone.</summary>
    private static bool ZipHasPets(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries.Take(PetPackReader.MaxZipEntries))
            if (entry.Name.Equals(PetdexReader.ManifestFile, StringComparison.OrdinalIgnoreCase) || entry.Name.Equals(ShimejiBundle.AnimationFile, StringComparison.OrdinalIgnoreCase)
                || PetPackReader.FrameNumber(entry.Name) is not null)
                return true;
        return false;
    }
}
