using System.IO;
using System.Reflection;

namespace AmpUp.Core;

public static class Logger
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmpUp", "ampup.log");
    private static readonly object _lock = new();

    static Logger()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);

            // Rotate: delete log if > 1MB
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_048_576)
                File.Delete(LogPath);

            var version = typeof(Logger).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";
            var line = $"=== AmpUp {version} started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { /* ignore startup log failures */ }
    }

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(line);
        lock (_lock)
        {
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { /* ignore log failures */ }
        }
    }
}
