using System.Reflection;
using Hanami.Core;

namespace Hanami.App;

/// <summary>Where everything lives. In --dev mode the repo's devroot/ stands in for the install folder
/// and web/ is served straight from the repo, so edits to HTML/JS show up on reload.</summary>
public sealed class AppPaths
{
    public string InstallDir { get; }
    public string WebDir { get; }
    public bool IsDev { get; }

    public string LibraryDir => Path.Combine(InstallDir, "Library");
    public string ConfigPath => Path.Combine(InstallDir, "config", "settings.json");
    public string ExePath => Path.Combine(InstallDir, "Hanami.exe");

    /// <summary>Browser profile/cache. Kept out of the install folder so it is never exported.</summary>
    public static string WebViewDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hanami", "WebView2");

    public static string Version =>
        typeof(AppPaths).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private AppPaths(string installDir, string webDir, bool isDev)
    {
        InstallDir = installDir;
        WebDir = webDir;
        IsDev = isDev;
    }

    public static AppPaths Installed(string installDir) => new(installDir, Path.Combine(installDir, "app", "web"), false);

    public static AppPaths Dev(string? repoRoot)
    {
        repoRoot ??= FindRepoRoot() ?? throw new InvalidOperationException("--dev: could not find the repo (web/wallpaper) above the exe.");
        return new AppPaths(Path.Combine(repoRoot, "devroot"), Path.Combine(repoRoot, "web"), true);
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "web", "wallpaper"))) return dir.FullName;
        return null;
    }

    /// <summary>Copies the web/ tree embedded in the exe to app/web, once per build.</summary>
    public void ExtractWebAssets()
    {
        if (IsDev) return;
        var asm = typeof(AppPaths).Assembly;
        var stamp = asm.ManifestModule.ModuleVersionId.ToString();
        var stampFile = Path.Combine(WebDir, ".build");
        if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp) return;

        if (Directory.Exists(WebDir)) Directory.Delete(WebDir, recursive: true);
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("web/", StringComparison.Ordinal)))
        {
            var target = Path.Combine(WebDir, name["web/".Length..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = asm.GetManifestResourceStream(name)!;
            using var output = File.Create(target);
            input.CopyTo(output);
        }
        File.WriteAllText(stampFile, stamp);
        Log.Info($"Extracted web assets to {WebDir}");
    }
}
