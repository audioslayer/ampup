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
                IntPtr foregroundBefore = NativeMethods.GetForegroundWindow();
                bool wasMinimized = NativeMethods.IsIconic(window);
                bool shielded = TryMakeWindowTransparent(window, foregroundBefore, out IntPtr originalExStyle);

                try
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
                            RestorePreviousWindow(window, foregroundBefore, wasMinimized);
                            Logger.Log($"Pocket Casts: {actionName} invoked on its '{name}' control.");
                            return true;
                        }

                        if (!useTogglePattern
                            && button.TryGetCurrentPattern(InvokePattern.Pattern, out object invokeObject))
                        {
                            ((InvokePattern)invokeObject).Invoke();
                            RestorePreviousWindow(window, foregroundBefore, wasMinimized);
                            Logger.Log($"Pocket Casts: {actionName} invoked on its '{name}' control.");
                            return true;
                        }
                    }

                    Logger.Log($"Pocket Casts: cannot {actionName}; its player control was not found.");
                    return false;
                }
                finally
                {
                    if (shielded)
                        NativeMethods.SetWindowLongPtr(window, NativeMethods.GWL_EXSTYLE, originalExStyle);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Pocket Casts: {actionName} failed - {ex.Message}");
            return false;
        }
    }

    private static bool TryMakeWindowTransparent(
        IntPtr pocketCastsWindow,
        IntPtr foregroundBefore,
        out IntPtr originalExStyle)
    {
        originalExStyle = NativeMethods.GetWindowLongPtr(pocketCastsWindow, NativeMethods.GWL_EXSTYLE);

        // No shield is needed while the user is already working in Pocket Casts.
        // Avoid changing a window that already owns custom layered-window state,
        // since its original alpha cannot be inferred safely.
        if (foregroundBefore == pocketCastsWindow
            || (originalExStyle.ToInt64() & NativeMethods.WS_EX_LAYERED) != 0)
            return false;

        var transparentStyle = new IntPtr(originalExStyle.ToInt64() | NativeMethods.WS_EX_LAYERED);
        NativeMethods.SetWindowLongPtr(
            pocketCastsWindow,
            NativeMethods.GWL_EXSTYLE,
            transparentStyle);

        if (NativeMethods.SetLayeredWindowAttributes(
            pocketCastsWindow,
            0,
            0,
            NativeMethods.LWA_ALPHA))
            return true;

        NativeMethods.SetWindowLongPtr(
            pocketCastsWindow,
            NativeMethods.GWL_EXSTYLE,
            originalExStyle);
        return false;
    }

    private static void RestorePreviousWindow(
        IntPtr pocketCastsWindow,
        IntPtr foregroundBefore,
        bool wasMinimized)
    {
        // Chromium's accessibility provider can activate its window while a
        // player button is invoked. Give that activation time to settle, then
        // put the user's original window back exactly where it was.
        Thread.Sleep(100);

        if (wasMinimized)
            NativeMethods.ShowWindow(pocketCastsWindow, NativeMethods.SW_MINIMIZE);

        if (foregroundBefore != IntPtr.Zero && foregroundBefore != pocketCastsWindow)
            NativeMethods.SwitchToThisWindow(foregroundBefore, true);
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
