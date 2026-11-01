using System.Text;
using System.Text.Json.Nodes;

namespace Hanami.Core;

/// <summary>A font file in Library/fonts/ (FontInfo in docs/CONTRACTS.md).</summary>
public sealed record FontInfo(string Id, string Name, string Src, long SizeBytes);

/// <summary>
/// Fonts the user imported for the screensaver clock, stored as Library/fonts/&lt;id&gt; where the id is
/// the sanitized file name. Ids also arrive from packs and settings files, so every entry point
/// validates them: no separators, no "..", only the allowed extensions.
/// </summary>
public sealed class FontLibrary
{
    public static readonly string[] Extensions = [".ttf", ".otf", ".woff", ".woff2"];
    public const string SystemPrefix = "system:", LibraryPrefix = "library:";
    private const int MaxBaseLength = 60;

    private readonly Library _library;

    public FontLibrary(Library library) => _library = library;

    public string Dir => Path.Combine(_library.Root, "fonts");

    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxBaseLength + 8 || id.Contains("..")) return false;
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.IndexOfAny(['/', '\\']) >= 0) return false;
        if (id.Trim() != id || id.StartsWith('.')) return false;
        return Array.IndexOf(Extensions, Path.GetExtension(id).ToLowerInvariant()) >= 0
            && Path.GetFileNameWithoutExtension(id).Length > 0;
    }

    /// <summary>Whether a Settings.screensaver.clock.font value is well-formed (the file may still be missing).</summary>
    public static bool IsValidSetting(string? font) => font is null
        || (font.StartsWith(SystemPrefix, StringComparison.Ordinal) && font.Length > SystemPrefix.Length
            && font.Length <= 120 && font.All(c => !char.IsControl(c)))
        || (font.StartsWith(LibraryPrefix, StringComparison.Ordinal) && IsValidId(font[LibraryPrefix.Length..]));

    /// <summary>A safe, unused id for a font file picked by the user, or null when the extension is not allowed.</summary>
    public string? NewId(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (Array.IndexOf(Extensions, ext) < 0) return null;
        var sb = new StringBuilder();
        foreach (var c in Path.GetFileNameWithoutExtension(sourcePath))
            sb.Append(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '(' or ')' ? c : '_');
        var name = sb.ToString().Trim().TrimStart('.');
        if (name.Length > MaxBaseLength) name = name[..MaxBaseLength].TrimEnd();
        if (name.Length == 0) name = "font";
        var id = name + ext;
        for (var n = 2; File.Exists(PathOf(id)); n++) id = $"{name}-{n}{ext}";
        return id;
    }

    public string PathOf(string id) =>
        IsValidId(id) ? Path.Combine(Dir, id) : throw new ArgumentException($"Invalid font id: {id}");

    public bool Exists(string id) => IsValidId(id) && File.Exists(PathOf(id));

    public FontInfo Import(string sourcePath)
    {
        var id = NewId(sourcePath)
            ?? throw new InvalidOperationException($"Unsupported font type: {Path.GetExtension(sourcePath)}");
        Directory.CreateDirectory(Dir);
        File.Copy(sourcePath, PathOf(id));
        return Info(id);
    }

    public void Delete(string id)
    {
        var path = PathOf(id);
        if (File.Exists(path)) File.Delete(path);
    }

    public List<FontInfo> List() => !Directory.Exists(Dir) ? [] : Directory.EnumerateFiles(Dir)
        .Select(f => Path.GetFileName(f)!).Where(n => IsValidId(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .Select(Info).ToList();

    private FontInfo Info(string id) =>
        new(id, Path.GetFileNameWithoutExtension(id), $"{Library.Origin}/fonts/{Uri.EscapeDataString(id)}", new FileInfo(PathOf(id)).Length);

    /// <summary>
    /// The clock config a screensaver page receives: the settings with <c>font</c> resolved to
    /// <c>{id, family, src?}</c>. A missing or unknown font resolves to null, i.e. the bundled Tilt Warp.
    /// </summary>
    public JsonObject ClockMessage(ClockSettings c)
    {
        JsonObject? font = null;
        if (c.Font is { } f && f.StartsWith(SystemPrefix, StringComparison.Ordinal))
            font = new JsonObject { ["id"] = f, ["family"] = f[SystemPrefix.Length..] };
        else if (c.Font is { } l && l.StartsWith(LibraryPrefix, StringComparison.Ordinal) && Exists(l[LibraryPrefix.Length..]))
        {
            var info = Info(l[LibraryPrefix.Length..]);
            font = new JsonObject { ["id"] = l, ["family"] = info.Name, ["src"] = info.Src };
        }
        return new JsonObject
        {
            ["x"] = c.X, ["y"] = c.Y, ["size"] = c.Size, ["font"] = font,
            ["dateFont"] = c.DateFont, ["showDate"] = c.ShowDate,
        };
    }
}
