using Microsoft.Win32;

namespace Hanami.Platform;

/// <summary>Per-user registry state: where Hanami is installed and whether it starts with Windows.</summary>
public static class Registration
{
    private const string AppKey = @"Software\Hanami";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Hanami";

    public static string? InstallDir
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            return key?.GetValue("InstallDir") as string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey);
            if (value is null) key.DeleteValue("InstallDir", throwOnMissingValue: false);
            else key.SetValue("InstallDir", value);
        }
    }

    public static void SetStartWithWindows(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{exePath}\" --startup");
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
    }
}
