using Microsoft.Win32;

namespace Hanami.Platform;

/// <summary>Per-user registry and Start menu state: where Hanami is installed, whether it starts with Windows, and its uninstall entry.</summary>
public static class Registration
{
    private const string AppKey = @"Software\Hanami";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Hanami";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Hanami";

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

    // ---- Start menu shortcut and the Apps & features entry ---------------------------------------

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hanami.lnk");

    /// <summary>
    /// Makes Hanami show up in the Start menu and in Settings → Apps, with an Uninstall action (which runs
    /// <c>Hanami.exe --uninstall</c>). Safe to call on every start: it only rewrites what changed.
    /// </summary>
    public static void RegisterApp(string exePath, string version)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Hanami");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Hanami");
            key.SetValue("InstallLocation", Path.GetDirectoryName(exePath)!);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        CreateShortcut(StartMenuShortcut, exePath);
    }

    /// <summary>Removes the Start menu shortcut, the uninstall entry, the start-with-Windows entry and Hanami's own registry key.</summary>
    public static void Unregister()
    {
        try { File.Delete(StartMenuShortcut); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)) run?.DeleteValue(RunValue, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
    }

    private static void CreateShortcut(string lnk, string exePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(lnk);
            if ((string)link.TargetPath == exePath) return;   // already right
            link.TargetPath = exePath;
            link.WorkingDirectory = Path.GetDirectoryName(exePath);
            link.IconLocation = exePath;
            link.Description = "Hanami live wallpapers";
            link.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Hanami.Core.Log.Warn($"Could not create the Start menu shortcut: {ex.Message}");
        }
    }
}
