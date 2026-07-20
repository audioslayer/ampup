using System.IO;
using System.Reflection;
using System.Text;

namespace AmpUp.Core;

public static class Logger
{
    private const long MaxLogBytes = 1_048_576;
    private static readonly string PreviousLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmpUp", "ampup.previous.log");
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmpUp", "ampup.log");
    private static readonly object _lock = new();

    // Persistent buffered writer — opened lazily on first log, flushed by a
    // ~1s timer and on process exit. FileShare.Read lets the user tail the
    // file while the app runs. If the writer ever fails (file locked, disk
    // error), logging degrades to a no-op instead of throwing into the
    // serial/N3/UI threads that call Log().
    private static StreamWriter? _writer;
    private static bool _writerFailed;
    private static bool _processExitHooked;
    private static long _bytesWritten;
    private static System.Threading.Timer? _flushTimer;

    static Logger()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);

            if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaxLogBytes)
                RotateExistingLog();

            var version = (Assembly.GetEntryAssembly() ?? typeof(Logger).Assembly)
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";
            var line = $"=== AmpUp {version} started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
            lock (_lock)
            {
                WriteLine(line);
            }
        }
        catch { /* ignore startup log failures */ }
    }

    /// <summary>Optional callback for UI log display.</summary>
    public static event Action<string>? OnLogMessage;

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
#if DEBUG
        Console.WriteLine(line);
#endif
        try { OnLogMessage?.Invoke(line); }
        catch { /* a UI log subscriber must never break application logging */ }
        lock (_lock)
        {
            WriteLine(line);
        }
    }

    /// <summary>Appends a line to the buffered writer. Callers must hold <see cref="_lock"/>.</summary>
    private static void WriteLine(string line)
    {
        try
        {
            long lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (_writer != null && _bytesWritten + lineBytes > MaxLogBytes)
                RotateOpenLog();

            var writer = EnsureWriter();
            writer?.WriteLine(line);
            if (writer != null)
                _bytesWritten += lineBytes;
        }
        catch
        {
            DisableWriter();
        }
    }

    /// <summary>Lazily opens the log writer. Callers must hold <see cref="_lock"/>.</summary>
    private static StreamWriter? EnsureWriter()
    {
        if (_writerFailed) return null;
        if (_writer != null) return _writer;

        try
        {
            var dir = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(dir);

            var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream) { AutoFlush = false };
            _bytesWritten = stream.Length;

            _flushTimer ??= new System.Threading.Timer(_ => FlushPending(), null, 1000, 1000);
            if (!_processExitHooked)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                _processExitHooked = true;
            }
            return _writer;
        }
        catch
        {
            _writerFailed = true;
            return null;
        }
    }

    private static void FlushPending()
    {
        lock (_lock)
        {
            try { _writer?.Flush(); }
            catch { DisableWriter(); }
        }
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            try
            {
                _flushTimer?.Dispose();
                _flushTimer = null;
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { /* ignore shutdown log failures */ }
            _writer = null;
            _writerFailed = true; // no reopen during teardown
        }
    }

    /// <summary>Disables file logging after a write/flush failure. Callers must hold <see cref="_lock"/>.</summary>
    private static void DisableWriter()
    {
        try { _writer?.Dispose(); } catch { }
        _writer = null;
        _writerFailed = true;
    }

    private static void RotateOpenLog()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            RotateExistingLog();
            _bytesWritten = 0;
        }
        catch
        {
            // If rotation fails, reopen the current file and keep logging.
            _writer = null;
            _bytesWritten = File.Exists(LogPath) ? new FileInfo(LogPath).Length : 0;
        }
    }

    private static void RotateExistingLog()
    {
        if (!File.Exists(LogPath)) return;
        if (File.Exists(PreviousLogPath)) File.Delete(PreviousLogPath);
        File.Move(LogPath, PreviousLogPath);
    }
}
