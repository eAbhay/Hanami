namespace Hanami.Core;

/// <summary>When the Windows lock screen picture has to be written again (docs/CONTRACTS.md, "Lock screen").</summary>
public static class LockImagePolicy
{
    /// <summary>
    /// Identifies what the picture shows: the primary monitor's screensaver wallpaper, the theme slot it came from and
    /// when its saved still was last written (a newly chosen frame changes it).
    /// </summary>
    public static string Signature(string? wallpaperId, bool dark, long stillWrittenTicks) =>
        $"{wallpaperId ?? "-"}|{(dark ? "dark" : "light")}|{stillWrittenTicks}";

    /// <summary>Write the picture only when the option is on and the signature differs from the one last written (null = never).</summary>
    public static bool ShouldWrite(bool enabled, string? lastSignature, string signature) =>
        enabled && lastSignature != signature;
}
