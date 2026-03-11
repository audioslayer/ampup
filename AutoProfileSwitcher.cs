using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AmpUp;

public class AutoSwitchConfig
{
    public bool Enabled { get; set; } = false;
    public List<AutoSwitchRule> Rules { get; set; } = new();
    public bool RevertToDefault { get; set; } = true;
    public string DefaultProfile { get; set; } = "Default";
}

public class AutoSwitchRule
{
    public string ProcessName { get; set; } = "";
    public string ProfileName { get; set; } = "";
}

public class AutoProfileSwitcher
{
    public event Action<string>? OnProfileSwitchRequested;

    private AutoSwitchConfig _config;
    private string? _lastRequestedProfile;
    private DateTime _lastSwitchTime = DateTime.MinValue;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);

    public AutoProfileSwitcher(AutoSwitchConfig config)
    {
        _config = config;
    }

    public void UpdateConfig(AutoSwitchConfig config)
    {
        _config = config;
    }

    public void Poll()
    {
        if (!_config.Enabled) return;

        // Debounce — don't process switches faster than the cooldown
        if (DateTime.UtcNow - _lastSwitchTime < Cooldown) return;

        string? processName = GetForegroundProcessName();
        string? targetProfile = ResolveProfile(processName);

        if (targetProfile == null) return;
        if (targetProfile == _lastRequestedProfile) return;

        _lastRequestedProfile = targetProfile;
        _lastSwitchTime = DateTime.UtcNow;

        Logger.Log($"[AutoProfileSwitcher] Foreground: '{processName}' → profile '{targetProfile}'");
        OnProfileSwitchRequested?.Invoke(targetProfile);
    }

    private string? ResolveProfile(string? processName)
    {
        if (processName != null)
        {
            foreach (var rule in _config.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ProcessName) || string.IsNullOrWhiteSpace(rule.ProfileName))
                    continue;

                if (processName.Contains(rule.ProcessName, StringComparison.OrdinalIgnoreCase))
                    return rule.ProfileName;
            }
        }

        // No rule matched
        if (_config.RevertToDefault)
            return _config.DefaultProfile;

        return null;
    }

    private static string? GetForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch (ArgumentException)
        {
            // Process already exited
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Access denied (e.g. elevated system process)
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"[AutoProfileSwitcher] GetForegroundProcessName error: {ex.Message}");
            return null;
        }
    }
}
