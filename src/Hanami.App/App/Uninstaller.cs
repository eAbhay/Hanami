using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hanami.Core;
using Hanami.Platform;

namespace Hanami.App;

/// <summary>How much an uninstall removes.</summary>
public enum UninstallChoice { Cancel, KeepData, DeleteData }

/// <summary>
/// Uninstalling the installed copy. The running exe can't delete itself, so the choice is made in
/// <see cref="Prompt"/>, then a copy of the exe from a temp folder does the work (<see cref="RunHelper"/>) after the app has quit.
/// "Data" is what the user made: the wallpaper and pet libraries (<c>Library</c>) and the settings (<c>config</c>).
/// The app itself, its extracted web files, the Start menu shortcut, the start-with-Windows entry, the Apps &amp; features
/// entry and the browser cache always go.
/// </summary>
public static class Uninstaller
{
    public const string HelperArg = "--uninstall-run";

    /// <summary>Asks what to do; when the user confirms, starts the helper and returns true (the caller then quits).</summary>
    public static bool PromptAndStart(Window? owner, string installDir)
    {
        var choice = Prompt(owner, installDir);
        if (choice == UninstallChoice.Cancel) return false;
        Start(installDir, choice == UninstallChoice.DeleteData);
        return true;
    }

    private static void Start(string installDir, bool deleteData)
    {
        var temp = Path.Combine(Path.GetTempPath(), "HanamiUninstall-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        var helper = Path.Combine(temp, "Hanami.exe");
        File.Copy(Environment.ProcessPath!, helper, overwrite: true);
        var psi = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = temp };
        psi.ArgumentList.Add(HelperArg);
        psi.ArgumentList.Add(deleteData ? "delete" : "keep");
        psi.ArgumentList.Add(installDir);
        Process.Start(psi);
        Log.Info($"Uninstall started ({(deleteData ? "deleting" : "keeping")} data in {installDir})");
    }

    private static UninstallChoice Prompt(Window? owner, string installDir)
    {
        var result = UninstallChoice.Cancel;
        var window = new Window
        {
            Title = "Uninstall Hanami",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hanami.ico")),
        };
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Uninstall Hanami", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(new TextBlock
        {
            Text = "Hanami will close and be removed: the app, its Start menu shortcut and its start-with-Windows entry.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        root.Children.Add(new TextBlock
        {
            Text = $"What about your data (imported wallpapers, pet packs and settings, kept in {installDir})?",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Keep it, and installing Hanami again into the same folder brings everything back. Delete it, and it is gone for good.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        Button Make(string text, UninstallChoice choice, bool isDefault = false, bool isCancel = false) =>
            new() { Content = text, IsDefault = isDefault, IsCancel = isCancel, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        var keep = Make("Keep my data", UninstallChoice.KeepData, isDefault: true);
        var delete = Make("Delete my data too", UninstallChoice.DeleteData);
        var cancel = Make("Cancel", UninstallChoice.Cancel, isCancel: true);
        keep.Click += (_, _) => { result = UninstallChoice.KeepData; window.Close(); };
        delete.Click += (_, _) =>
        {
            var sure = MessageBox.Show(window, "This permanently deletes your imported wallpapers, pet packs and settings. Delete them?", "Hanami",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (sure != MessageBoxResult.Yes) return;
            result = UninstallChoice.DeleteData;
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        buttons.Children.Add(keep);
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        window.Content = root;
        window.ShowDialog();
        return result;
    }

    // ---- the helper (a temp copy of the exe) -----------------------------------------------------

    /// <summary>Entry point of the helper: <c>Hanami.exe --uninstall-run keep|delete &lt;installDir&gt;</c>. Returns the exit code.</summary>
    public static int RunHelper(string[] args)
    {
        var i = Array.IndexOf(args, HelperArg);
        if (i < 0 || i + 2 >= args.Length) return 2;
        var deleteData = args[i + 1] == "delete";
        var installDir = args[i + 2];
        try
        {
            StopRunningApp();
            Registration.Unregister();
            var problems = RemoveFiles(installDir, deleteData);
            RemoveAppData(deleteData);
            Log.Info($"Uninstalled ({(deleteData ? "data deleted" : "data kept")}){(problems.Count > 0 ? $"; left behind: {string.Join(", ", problems)}" : "")}");
            var text = deleteData ? "Hanami has been uninstalled and your data was deleted."
                : $"Hanami has been uninstalled. Your wallpapers and settings were kept in\n{installDir}\n\nInstall Hanami again into that folder to get them back.";
            if (problems.Count > 0) text += $"\n\nCouldn't remove: {string.Join(", ", problems)}. You can delete it yourself.";
            MessageBox.Show(text, "Hanami", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Uninstall failed", ex);
            MessageBox.Show($"Couldn't finish uninstalling Hanami:\n{ex.Message}", "Hanami", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            RemoveSelf();
        }
    }

    /// <summary>Asks a running Hanami to quit and waits until it has let go of its files.</summary>
    private static void StopRunningApp()
    {
        if (EventWaitHandle.TryOpenExisting(Installer.QuitEventName, out var quit)) { quit.Set(); quit.Dispose(); }
        using var mutex = new Mutex(false, Installer.InstanceMutexName);
        try { if (mutex.WaitOne(TimeSpan.FromSeconds(15))) mutex.ReleaseMutex(); }
        catch (AbandonedMutexException) { }
        Thread.Sleep(500);   // WebView2 helper processes take a moment to let go
    }

    /// <summary>Removes the app's files from the install folder (and the data too when asked); the folder itself goes if it ends up empty.</summary>
    private static List<string> RemoveFiles(string installDir, bool deleteData)
    {
        var problems = new List<string>();
        // Only ever touch what Hanami put there: a folder the user picked may hold other things.
        var targets = new List<string> { "Hanami.exe", "app" };
        if (deleteData) targets.AddRange(["Library", "config"]);
        foreach (var name in targets)
        {
            var path = Path.Combine(installDir, name);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else if (File.Exists(path)) File.Delete(path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= 20) { problems.Add(name); break; }
                    Thread.Sleep(250);
                }
            }
        }
        try { if (Directory.Exists(installDir) && !Directory.EnumerateFileSystemEntries(installDir).Any()) Directory.Delete(installDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return problems;
    }

    /// <summary>The browser cache always goes; the log folder goes with the data.</summary>
    private static void RemoveAppData(bool deleteData)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hanami");
        foreach (var path in deleteData ? new[] { root } : new[] { Path.Combine(root, "WebView2") })
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Deletes this temp copy once it has exited.</summary>
    private static void RemoveSelf()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath)!;
            if (!Path.GetFileName(dir).StartsWith("HanamiUninstall-", StringComparison.Ordinal)) return;
            // Not started inside the folder: a process can't delete its own working directory.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{dir}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
    }
}
