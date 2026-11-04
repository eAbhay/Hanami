using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.UserProfile;

using Hanami.Core;

namespace Hanami.Platform;

/// <summary>What the lock screen picture code needs, so nothing but this wrapper touches Windows (tests use a fake).</summary>
public interface ILockScreenImage
{
    /// <summary>Makes <paramref name="path"/> the Windows lock screen picture. Null on success, else the reason it failed.</summary>
    Task<string?> SetAsync(string path);

    /// <summary>Saves the current lock screen picture to <paramref name="path"/>. False when Windows has none to give (e.g. Spotlight).</summary>
    Task<bool> BackupAsync(string path);

    /// <summary>The picture in <paramref name="path"/> reduced to <see cref="LockImagePolicy.SampleWidth"/>×<see cref="LockImagePolicy.SampleHeight"/> RGB for comparing; null when it can't be read.</summary>
    Task<byte[]?> SampleAsync(string path);
}

/// <summary>
/// The real thing, <c>Windows.System.UserProfile.LockScreen</c>. Must be called on the UI thread. Never throws:
/// failures come back as a message. Don't call it from tests, and not on the user's lock screen without asking.
/// </summary>
public sealed class LockScreenImage : ILockScreenImage
{
    public async Task<string?> SetAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            await LockScreen.SetImageFileAsync(file);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Setting the lock screen picture failed: {ex.Message}");
            return ex.Message;
        }
    }

    public async Task<bool> BackupAsync(string path)
    {
        try
        {
            using var stream = LockScreen.GetImageStream();
            if (stream is null) return false;
            using var input = stream.AsStreamForRead();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using var output = File.Create(path);
            await input.CopyToAsync(output);
            return output.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Backing up the lock screen picture failed: {ex.Message}");
            return false;
        }
    }

    public Task<byte[]?> SampleAsync(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = LockImagePolicy.SampleWidth;
            bmp.DecodePixelHeight = LockImagePolicy.SampleHeight;
            bmp.UriSource = new Uri(Path.GetFullPath(path));
            bmp.EndInit();
            var rgb = new FormatConvertedBitmap(bmp, PixelFormats.Rgb24, null, 0);
            var pixels = new byte[LockImagePolicy.SampleWidth * LockImagePolicy.SampleHeight * 3];
            rgb.CopyPixels(pixels, LockImagePolicy.SampleWidth * 3, 0);
            return Task.FromResult<byte[]?>(pixels);
        }
        catch (Exception ex)
        {
            Log.Warn($"Reading a lock screen picture to compare failed: {ex.Message}");
            return Task.FromResult<byte[]?>(null);
        }
    }
}
