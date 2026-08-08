using System.Diagnostics;
using System.IO;
using System.Windows.Automation;

namespace AmpUp.Services;

/// <summary>
/// Direct controller for the Pocket Casts Windows desktop app. Playback
/// commands invoke Pocket Casts' own accessible player buttons, so they work
/// in the background and cannot accidentally control another media app.
/// </summary>
public sealed class PocketCastsIntegration
{
    private const string ProcessName = "Pocket Casts";

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

    public Task<bool> PlayPauseAsync() => ExecutePlayerControlAsync(
        "play/pause",
        name => name is "Play" or "Pause",
        useTogglePattern: true);

    public Task<bool> SkipBackAsync() => ExecutePlayerControlAsync(
        "skip back",
        name => name.StartsWith("Skip backwards", StringComparison.OrdinalIgnoreCase));

    public Task<bool> SkipForwardAsync() => ExecutePlayerControlAsync(
        "skip forward",
        name => name.StartsWith("Skip forwards", StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> ExecutePlayerControlAsync(
        string actionName,
        Func<string, bool> nameMatches,
        bool useTogglePattern = false)
    {
        try
        {
            IntPtr window = await FindOrStartWindowAsync();
            if (window == IntPtr.Zero)
            {
                Logger.Log($"Pocket Casts: cannot {actionName}; the desktop window was not found.");
                return false;
            }

            return await Task.Run(() =>
            {
                var root = AutomationElement.FromHandle(window);
                var buttons = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                for (int i = 0; i < buttons.Count; i++)
                {
                    var button = buttons[i];
                    string name = button.Current.Name ?? "";
                    if (!nameMatches(name)) continue;

                    if (useTogglePattern
                        && button.TryGetCurrentPattern(TogglePattern.Pattern, out object toggleObject))
                    {
                        ((TogglePattern)toggleObject).Toggle();
                        Logger.Log($"Pocket Casts: {actionName} invoked on its '{name}' control.");
                        return true;
                    }

                    if (!useTogglePattern
                        && button.TryGetCurrentPattern(InvokePattern.Pattern, out object invokeObject))
                    {
                        ((InvokePattern)invokeObject).Invoke();
                        Logger.Log($"Pocket Casts: {actionName} invoked on its '{name}' control.");
                        return true;
                    }
                }

                Logger.Log($"Pocket Casts: cannot {actionName}; its player control was not found.");
                return false;
            });
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
