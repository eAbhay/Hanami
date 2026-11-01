using Hanami.Core;
using Microsoft.Win32;

namespace Hanami.Platform;

/// <summary>
/// Windows' light/dark setting (the "apps" theme, which is what Settings → Colors flips) and a
/// notification when it changes. Which wallpaper a theme shows is decided by <see cref="ThemeSettings"/>.
/// </summary>
public sealed class ThemeWatch : IDisposable
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public bool WindowsDark { get; private set; }

    /// <summary>Raised on the thread that receives system events, only when <see cref="WindowsDark"/> actually flips.</summary>
    public event Action? Changed;

    public ThemeWatch()
    {
        WindowsDark = Read();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var dark = Read();
        if (dark == WindowsDark) return;
        WindowsDark = dark;
        Changed?.Invoke();
    }

    private static bool Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read the Windows theme: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
}
