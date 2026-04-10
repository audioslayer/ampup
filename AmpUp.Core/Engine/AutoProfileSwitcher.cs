using AmpUp.Core;
using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

public class AutoProfileSwitcher
{
    public event Action<string>? OnProfileSwitchRequested;

    private AutoSwitchConfig _config;
    private readonly Func<string?> _getForegroundProcessName;
    private string? _lastRequestedProfile;
    private DateTime _lastSwitchTime = DateTime.MinValue;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(500);

    public AutoProfileSwitcher(AutoSwitchConfig config, Func<string?> getForegroundProcessName)
    {
        _config = config;
        _getForegroundProcessName = getForegroundProcessName;
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

        string? processName = _getForegroundProcessName();
        string? targetProfile = ResolveProfile(processName);

        if (targetProfile == null) return;
        if (targetProfile == _lastRequestedProfile) return;

        _lastRequestedProfile = targetProfile;
        _lastSwitchTime = DateTime.UtcNow;

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
}
