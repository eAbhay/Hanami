namespace Hanami.Core;

/// <summary>Tiny append-only log at %LocalAppData%\Hanami\hanami.log (rotated at 1 MB).</summary>
public static class Log
{
    private static readonly object Gate = new();
    /// <summary>Settable so tests can point the log at a temp file instead of the user's real one.</summary>
    public static string FilePath { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hanami", "hanami.log");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex is null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000) File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, line);
            }
            catch { /* logging must never take the app down */ }
        }
    }
}
