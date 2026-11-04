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

    /// <summary>Size pictures are reduced to before comparing (RGB, 3 bytes a pixel). Decoding is done by the platform wrapper.</summary>
    public const int SampleWidth = 32, SampleHeight = 20;

    /// <summary>Largest mean difference per channel at which two samples count as the same picture (right ones scored 0.2 to 0.7, the nearest wrong one 45).</summary>
    public const double MatchThreshold = 15;

    /// <summary>Mean absolute difference per channel (0 to 255) between two samples; <see cref="double.MaxValue"/> when they can't be compared.</summary>
    public static double MeanDifference(byte[] a, byte[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return double.MaxValue;
        long sum = 0;
        for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return (double)sum / a.Length;
    }

    /// <summary>Whether Windows shows the picture that was set (Windows re-encodes it, so only a close match counts).</summary>
    public static bool SamePicture(byte[] expected, byte[] actual) => MeanDifference(expected, actual) <= MatchThreshold;

    /// <summary>Write the picture only when the option is on and the signature differs from the one last written (null = never).</summary>
    public static bool ShouldWrite(bool enabled, string? lastSignature, string signature) =>
        enabled && lastSignature != signature;
}
