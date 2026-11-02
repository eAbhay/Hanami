namespace Hanami.Core;

public enum LockTrigger
{
    /// <summary>A screensaver (not a preview) just started.</summary>
    ScreensaverStart,
    /// <summary>The option was turned on, or the screensaver's wallpaper or the theme changed.</summary>
    SettingsChange,
}

/// <summary>When the Windows lock screen picture has to be photographed from the screensaver again (docs/CONTRACTS.md, "Lock screen").</summary>
public static class LockImagePolicy
{
    /// <summary>Identifies what the picture shows: the primary monitor's screensaver wallpaper and the theme slot it came from.</summary>
    public static string Signature(string? wallpaperId, bool dark) => $"{wallpaperId ?? "-"}|{(dark ? "dark" : "light")}";

    /// <summary>
    /// A screensaver start always refreshes a video or built-in scene (they move, so the picture is a new frame); an image only when its
    /// signature changed. A settings or theme change refreshes only when the signature changed. Never when the option is off.
    /// </summary>
    public static bool ShouldCapture(bool enabled, LockTrigger trigger, string? lastSignature, string signature, string wallpaperType)
    {
        if (!enabled) return false;
        var changed = lastSignature != signature;
        return trigger switch
        {
            LockTrigger.ScreensaverStart => changed || wallpaperType is "video" or "builtin",
            _ => changed,
        };
    }
}
