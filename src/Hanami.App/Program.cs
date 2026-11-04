using System.Windows;
using System.Windows.Threading;
using Hanami.App;
using Hanami.Core;

namespace Hanami;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        // The uninstall helper (a temp copy of the exe) and the Apps & features entry's "Uninstall" run before anything else.
        if (args.Contains(Uninstaller.HelperArg)) return Uninstaller.RunHelper(args);
        if (args.Contains("--uninstall"))
        {
            var dir = Hanami.Platform.Registration.InstallDir ?? Path.GetDirectoryName(Environment.ProcessPath)!;
            Uninstaller.PromptAndStart(null, dir);
            return 0;
        }

        AppPaths? paths;
        try { paths = Installer.Resolve(args); }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show(ex.Message, "Hanami", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        if (paths is null) return 0;

        using var instance = new Mutex(true, Installer.InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            // Already running: bring its settings window up instead of starting a second copy.
            if (EventWaitHandle.TryOpenExisting(Installer.ShowEventName, out var show)) { show.Set(); show.Dispose(); }
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ShowEventName);
        using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.QuitEventName);

        var openSettings = args.Contains("--first-run") || (!args.Contains("--startup") && !args.Contains("--updated"));
        var feedIndex = Array.IndexOf(args, "--update-feed");   // dev only: UpdateService ignores it elsewhere
        var updateFeed = feedIndex >= 0 && feedIndex + 1 < args.Length ? args[feedIndex + 1] : null;
        var host = new HanamiHost(paths, openSettings, updateFeed);
        StartSignalListener(showEvent, app.Dispatcher, host.OpenSettings);
        StartSignalListener(quitEvent, app.Dispatcher, host.Quit);

        app.Startup += async (_, _) =>
        {
            try { await host.StartAsync(); }
            catch (Exception ex)
            {
                Log.Error("Hanami failed to start", ex);
                MessageBox.Show($"Hanami failed to start:\n{ex.Message}\n\nLog: {Log.FilePath}", "Hanami", MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown(1);
            }
        };
        var code = app.Run();
        instance.ReleaseMutex();
        return code;
    }

    private static void StartSignalListener(EventWaitHandle handle, Dispatcher ui, Action action)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                handle.WaitOne();
                ui.BeginInvoke(action);
            }
        }) { IsBackground = true, Name = "Hanami signal listener" };
        thread.Start();
    }
}
