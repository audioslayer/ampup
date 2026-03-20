using AmpUp.Core;
using AmpUp.Core.Models;
using AmpUp.Core.Engine;

namespace AmpUp.Mac;

public class MacAudioMixer : IDisposable
{
    private readonly MacAudioBridge _bridge = new();
    private readonly object _lock = new();
    private Timer? _pollTimer;
    private Dictionary<string, List<int>> _appsByName = new(); // lowercase name → PIDs
    private readonly HashSet<int> _tappedPids = new();
    private readonly int[] _lastRaw = new int[5];
    private bool _disposed;

    public void Start()
    {
        RefreshApps();
        _pollTimer = new Timer(_ => RefreshApps(), null, 2000, 2000);
    }

    private void RefreshApps()
    {
        try
        {
            var apps = _bridge.GetRunningAudioApps();
            var dict = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var app in apps)
            {
                // Use last component of bundle ID as name (com.google.Chrome.helper → chrome)
                var name = app.Name;
                var parts = name.Split('.');
                if (parts.Length >= 2)
                    name = parts[^2]; // second to last (e.g. "Chrome" from com.google.Chrome.helper)

                var lower = name.ToLowerInvariant();
                if (!dict.ContainsKey(lower))
                    dict[lower] = new List<int>();
                dict[lower].Add(app.Pid);

                // Also index by full bundle ID
                var fullLower = app.Name.ToLowerInvariant();
                if (!dict.ContainsKey(fullLower))
                    dict[fullLower] = new List<int>();
                dict[fullLower].Add(app.Pid);
            }
            lock (_lock) { _appsByName = dict; }
        }
        catch (Exception ex)
        {
            Logger.Log($"MacAudioMixer refresh error: {ex.Message}");
        }
    }

    public void SetVolume(KnobConfig knob, int rawValue)
    {
        // Debounce
        if (knob.Idx >= 0 && knob.Idx < 5)
        {
            if (Math.Abs(rawValue - _lastRaw[knob.Idx]) < 5) return;
            _lastRaw[knob.Idx] = rawValue;
        }

        float volume = VolumePipeline.ComputeVolume(rawValue, knob);
        var target = knob.Target?.ToLowerInvariant() ?? "none";

        switch (target)
        {
            case "none":
                break;

            case "master":
                _bridge.MasterVolume = volume;
                break;

            case "apps":
                // App group — control multiple apps
                foreach (var appName in knob.Apps)
                {
                    SetAppVolumeByName(appName, volume);
                }
                break;

            default:
                // Single app target (spotify, chrome, discord, etc.)
                SetAppVolumeByName(target, volume);
                break;
        }
    }

    private void SetAppVolumeByName(string name, float volume)
    {
        List<int>? pids;
        lock (_lock)
        {
            _appsByName.TryGetValue(name.ToLowerInvariant(), out pids);
        }

        if (pids == null || pids.Count == 0) return;

        foreach (var pid in pids)
        {
            // Lazily create tap
            if (!_tappedPids.Contains(pid))
            {
                if (_bridge.CreateTap(pid))
                    _tappedPids.Add(pid);
                else
                    continue;
            }
            _bridge.SetAppVolume(pid, volume);
        }
    }

    public float GetPeakLevel(KnobConfig knob)
    {
        var target = knob.Target?.ToLowerInvariant() ?? "none";
        if (target == "master") return 0; // TODO: master peak
        if (target == "none") return 0;

        List<int>? pids = null;
        if (target == "apps")
        {
            float maxPeak = 0;
            foreach (var app in knob.Apps)
            {
                lock (_lock) { _appsByName.TryGetValue(app.ToLowerInvariant(), out pids); }
                if (pids != null)
                    foreach (var pid in pids)
                        maxPeak = Math.Max(maxPeak, _bridge.GetAppPeak(pid));
            }
            return maxPeak;
        }

        lock (_lock) { _appsByName.TryGetValue(target, out pids); }
        if (pids == null || pids.Count == 0) return 0;
        return _bridge.GetAppPeak(pids[0]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        _bridge.Cleanup();
        _tappedPids.Clear();
    }
}
