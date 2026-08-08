using System.Diagnostics;
using System.IO;

namespace AmpUp.Services;

/// <summary>
/// Direct controller for the Pocket Casts Windows desktop app. Playback
/// commands are sent to Pocket Casts' own window with WM_APPCOMMAND, so they
/// work in the background and cannot accidentally control another media app.
/// </summary>
public sealed class PocketCastsIntegration
{
    private const string ProcessName = "Pocket Casts";
    private const uint WmAppCommand = 0x0319;
    private const int AppCommandMediaNextTrack = 11;
    private const int AppCommandMediaPreviousTrack = 12;
    private const int AppCommandMediaPlayPause = 14;

    public async Task<bool> OpenAsync()
    {
        IntPtr window = await FindOrStartWindowAsync();
        if (window == IntPtr.Zero)
        {
            Logger.Log("Pocket Casts: desktop window was not found.");
            return false;
        }

        NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
        NativeMethods.SwitchToThisWindow(window, true);
        Logger.Log("Pocket Casts: opened the desktop app.");
        return true;
    }

    public Task<bool> PlayPauseAsync() =>
        SendAppCommandAsync("play/pause", AppCommandMediaPlayPause);

    public Task<bool> SkipBackAsync() =>
        SendAppCommandAsync("skip back", AppCommandMediaPreviousTrack);

    public Task<bool> SkipForwardAsync() =>
        SendAppCommandAsync("skip forward", AppCommandMediaNextTrack);

    private static async Task<bool> SendAppCommandAsync(string actionName, int command)
    {
        try
        {
            IntPtr window = await FindOrStartWindowAsync();
            if (window == IntPtr.Zero)
            {
                Logger.Log($"Pocket Casts: cannot {actionName}; the desktop window was not found.");
                return false;
            }

            NativeMethods.SendMessage(
                window,
                WmAppCommand,
                window,
                new IntPtr(command << 16));
            Logger.Log($"Pocket Casts: {actionName} sent directly to the desktop app.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Pocket Casts: {actionName} failed - {ex.Message}");
            return false;
        }
    }

    private static async Task<IntPtr> FindOrStartWindowAsync()
    {
        IntPtr existing = FindWindow();
        if (existing != IntPtr.Zero) return existing;

        string? executable = FindExecutable();
        if (string.IsNullOrWhiteSpace(executable)) return IntPtr.Zero;

        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(100);
            existing = FindWindow();
            if (existing != IntPtr.Zero) return existing;
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindWindow()
    {
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
            }
            finally
            {
                process.Dispose();
            }
        }
        return IntPtr.Zero;
    }

    private static string? FindExecutable()
    {
        try
        {
            string installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "pocket_casts_desktop");
            if (!Directory.Exists(installRoot)) return null;

            return Directory.EnumerateFiles(installRoot, "Pocket Casts.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Log($"Pocket Casts: executable discovery failed - {ex.Message}");
            return null;
        }
    }
}
