namespace Hanami.Core;

/// <summary>What the tray shows while a version is offered: the menu item's text, whether clicking it can start an install, and the tooltip line.</summary>
public sealed record UpdatePrompt(string MenuText, bool CanInstall, string TipLine);

/// <summary>
/// The rule behind the tray icon's dot, its menu items and tooltip (the General tab's dot follows the same rule: <c>update.version</c> is set).
/// While a version is offered they stay, through checking, downloading and failing, until the user updates or skips. See docs/CONTRACTS.md, "Updates".
/// </summary>
public static class UpdateAttention
{
    /// <summary>The tray tooltip is cut to this length; Windows has allowed more only on some versions.</summary>
    public const int MaxTipChars = 63;

    /// <summary>What to show for the update state, or null when no version is offered (nothing to show).</summary>
    public static UpdatePrompt? For(string status, string? version, double progress)
    {
        if (string.IsNullOrEmpty(version)) return null;
        var offered = $"Update available: install {version}";
        var tip = $"Update {version} available";
        return status switch
        {
            "available" => new UpdatePrompt(offered, true, tip),
            "checking" => new UpdatePrompt(offered, false, tip),   // InstallAsync would ignore the click until the check ends
            "failed" => new UpdatePrompt($"Update failed: try {version} again", true, $"Update {version} failed. Right-click to retry."),
            "downloading" when progress >= 1 => new UpdatePrompt($"Installing {version}…", false, $"Installing update {version}. Hanami will restart."),
            "downloading" => new UpdatePrompt($"Downloading {version}… {Percent(progress)}%", false, $"Downloading update {version}: {Percent(progress)}%"),
            _ => null,
        };
    }

    private static int Percent(double progress) => (int)(Math.Clamp(progress, 0, 1) * 100);

    /// <summary>The tooltip: the update line comes first, so cutting a long status never loses it.</summary>
    public static string Tooltip(string status, UpdatePrompt? prompt)
    {
        var tip = prompt is null ? $"Hanami — {status}" : $"{prompt.TipLine}\nHanami — {status}";
        return tip.Length > MaxTipChars ? tip[..MaxTipChars] : tip;
    }

    /// <summary>The dot's top-left corner and diameter on a square tray icon: in the top-right corner, at any icon size still big enough to see.</summary>
    public static (int X, int Y, int Diameter) DotBounds(int iconSize)
    {
        var diameter = Math.Min(iconSize, Math.Max(7, (int)Math.Round(iconSize * 0.45)));
        return (iconSize - diameter, 0, diameter);
    }
}
