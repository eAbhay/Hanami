using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Hanami.Core;
using Hanami.Platform;

namespace Hanami.App;

/// <summary>
/// The exe is its own installer. Run from anywhere (e.g. dist/), it either installs itself
/// into a folder the user picks, or — if Hanami is already installed — offers to update that copy.
/// Run from the install folder, it just starts.
/// </summary>
public static class Installer
{
    public const string QuitEventName = @"Local\Hanami.Quit";
    public const string ShowEventName = @"Local\Hanami.ShowSettings";
    public const string InstanceMutexName = @"Local\Hanami.Instance";

    public static string DefaultInstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Hanami");

    /// <summary>Returns the paths to run with, or null when this process should exit.</summary>
    public static AppPaths? Resolve(string[] args)
    {
        var devIndex = Array.IndexOf(args, "--dev");
        if (devIndex >= 0)
            return AppPaths.Dev(devIndex + 1 < args.Length && !args[devIndex + 1].StartsWith("--") ? args[devIndex + 1] : null);

        var exe = Environment.ProcessPath!;
        var installDir = Registration.InstallDir;
        var installedExe = installDir is null ? null : Path.Combine(installDir, "Hanami.exe");

        if (installedExe is not null && SamePath(exe, installedExe))
            return AppPaths.Installed(installDir!);

        if (installedExe is not null && File.Exists(installedExe))
        {
            var answer = MessageBox.Show(
                $"Hanami is installed in\n{installDir}\n\nReplace the installed copy with this version ({AppPaths.Version})?\n\n" +
                "Yes — update and restart Hanami\nNo — just open the installed Hanami",
                "Hanami", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                if (!Update(exe, installedExe)) return null;
                Launch(installedExe, "--updated");
            }
            else if (answer == MessageBoxResult.No)
            {
                Launch(installedExe);
            }
            return null;
        }

        var setup = new SetupWindow(DefaultInstallDir);
        if (setup.ShowDialog() != true) return null;
        try
        {
            Install(exe, setup.InstallDir, setup.StartWithWindows, setup.CheckForUpdates);
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            MessageBox.Show($"Couldn't install Hanami:\n{ex.Message}", "Hanami", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        Launch(Path.Combine(setup.InstallDir, "Hanami.exe"), "--first-run");
        return null;
    }

    private static void Install(string exe, string dir, bool startWithWindows, bool checkForUpdates)
    {
        Directory.CreateDirectory(dir);
        var paths = AppPaths.Installed(dir);
        File.Copy(exe, paths.ExePath, overwrite: true);
        Directory.CreateDirectory(paths.LibraryDir);

        // Seed settings with the choices made in setup; a reinstall keeps the rest of the existing file.
        var store = new SettingsStore(paths.ConfigPath);
        var exists = File.Exists(paths.ConfigPath);
        var patch = new System.Text.Json.Nodes.JsonObject();
        if (!exists || store.Current.StartWithWindows != startWithWindows) patch["startWithWindows"] = startWithWindows;
        if (!exists || store.Current.Updates.AutoCheck != checkForUpdates) patch["updates"] = new System.Text.Json.Nodes.JsonObject { ["autoCheck"] = checkForUpdates };
        if (!exists || patch.Count > 0) store.Patch(patch);

        Registration.InstallDir = dir;
        Registration.SetStartWithWindows(startWithWindows, paths.ExePath);
        Log.Info($"Installed to {dir}");
    }

    private static bool Update(string exe, string installedExe)
    {
        // Ask the running copy (if any) to quit, then wait for it to release the instance mutex.
        if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit))
        {
            quit.Set();
            quit.Dispose();
            using var mutex = new Mutex(false, InstanceMutexName);
            try { mutex.WaitOne(TimeSpan.FromSeconds(10)); mutex.ReleaseMutex(); }
            catch (AbandonedMutexException) { }
        }
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(exe, installedExe, overwrite: true);
                Log.Info($"Updated {installedExe} to {AppPaths.Version}");
                return true;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);   // the old process can hold the file for a moment after exiting
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't update Hanami:\n{ex.Message}", "Hanami", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }

    private static void Launch(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>First-run dialog: pick the install folder and whether to start with Windows.</summary>
public sealed class SetupWindow : Window
{
    private readonly TextBox _folder;
    private readonly CheckBox _startup;
    private readonly CheckBox _updates;

    public string InstallDir => _folder.Text.Trim();
    public bool StartWithWindows => _startup.IsChecked == true;
    public bool CheckForUpdates => _updates.IsChecked == true;

    public SetupWindow(string defaultDir)
    {
        Title = "Install Hanami";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/hanami.ico"));

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = "Install Hanami", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(new TextBlock
        {
            Text = "Choose where Hanami lives. Your wallpaper library (imported videos and images) is kept in a Library folder inside it.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var row = new DockPanel { LastChildFill = true };
        var browse = new Button { Content = "Browse…", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(browse, Dock.Right);
        _folder = new TextBox { Text = defaultDir, Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
        row.Children.Add(browse);
        row.Children.Add(_folder);
        root.Children.Add(row);
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Install Hanami in…", InitialDirectory = Directory.Exists(InstallDir) ? InstallDir : null };
            if (dlg.ShowDialog(this) == true) _folder.Text = Path.Combine(dlg.FolderName, "Hanami");
        };

        _startup = new CheckBox { Content = "Start Hanami when I sign in to Windows", IsChecked = true, Margin = new Thickness(0, 16, 0, 0) };
        root.Children.Add(_startup);
        _updates = new CheckBox { Content = "Tell me when updates are available (checks GitHub, sends nothing about you)", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(_updates);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var install = new Button { Content = "Install", IsDefault = true, Padding = new Thickness(20, 6, 20, 6), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(20, 6, 20, 6) };
        buttons.Children.Add(install);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        install.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(InstallDir) || !Path.IsPathFullyQualified(InstallDir))
            {
                MessageBox.Show(this, "Please choose a full folder path.", "Hanami");
                return;
            }
            DialogResult = true;
        };

        Content = root;
    }
}
