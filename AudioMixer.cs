using NAudio.CoreAudioApi;

namespace AmpUp;

public class AudioMixer : IDisposable
{
    // P/Invoke declarations consolidated in NativeMethods.cs

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<int, int> _lastValues = new();
    private readonly object _lock = new();      // guards _sessions / _sessionsByPid dict access
    private readonly object _enumLock = new();  // guards _enumerator — accessed from multiple threads
    private System.Threading.Timer? _pollTimer;

    // Map of processName (lowercase) -> AudioSessionControl
    private Dictionary<string, AudioSessionControl> _sessions = new();
    // Map of processId -> AudioSessionControl (for active_window lookups)
    private Dictionary<uint, AudioSessionControl> _sessionsByPid = new();

    public void Start()
    {
        RefreshSessions();
        _pollTimer = new System.Threading.Timer(_ => RefreshSessions(), null, 2000, 2000);
    }

    private void RefreshSessions()
    {
        try
        {
            lock (_lock)
            {
                _sessions.Clear();
                _sessionsByPid.Clear();
                MMDevice? device;
                lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                using var _dev = device;
                var sessionMgr = device!.AudioSessionManager;
                var sessions = sessionMgr.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        var pid = (int)s.GetProcessID;
                        if (pid == 0) continue;
                        var proc = System.Diagnostics.Process.GetProcessById(pid);
                        var name = proc.ProcessName.ToLowerInvariant();
                        if (!_sessions.ContainsKey(name))
                            _sessions[name] = s;
                        var upid = (uint)pid;
                        if (!_sessionsByPid.ContainsKey(upid))
                            _sessionsByPid[upid] = s;
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Session refresh error: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply response curve to a 0-1 raw volume value.
    /// </summary>
    private static float ApplyCurve(float raw, ResponseCurve curve)
    {
        return curve switch
        {
            ResponseCurve.Logarithmic => (float)(Math.Log10(1.0 + raw * 9.0) / Math.Log10(10.0)),
            ResponseCurve.Exponential => raw * raw,
            _ => raw // Linear
        };
    }

    /// <summary>
    /// Remap a 0-1 curved value into the MinVolume..MaxVolume range (both 0-100), returning 0-1.
    /// </summary>
    private static float ApplyVolumeRange(float curved, int minVolume, int maxVolume)
    {
        float min = Math.Clamp(minVolume, 0, 100) / 100f;
        float max = Math.Clamp(maxVolume, 0, 100) / 100f;
        if (max <= min) max = min + 0.01f; // safety
        return min + curved * (max - min);
    }

    /// <summary>
    /// Full pipeline: raw 0-1023 -> 0-1 -> curve -> range clamp -> final 0-1 volume.
    /// </summary>
    private static float ComputeVolume(int rawValue, KnobConfig knob)
    {
        float raw = Math.Clamp(rawValue / 1023f, 0f, 1f);
        float curved = ApplyCurve(raw, knob.Curve);
        float vol = ApplyVolumeRange(curved, knob.MinVolume, knob.MaxVolume);
        return Math.Clamp(vol, 0f, 1f);
    }

    public void SetVolume(KnobConfig knob, int rawValue)
    {
        // Debounce — skip if change < 5
        if (_lastValues.TryGetValue(knob.Idx, out int last) && Math.Abs(rawValue - last) < 5)
        {
            return;
        }
        _lastValues[knob.Idx] = rawValue;

        float vol = ComputeVolume(rawValue, knob);
        Logger.Log($"Knob {knob.Idx} ({knob.Label}) raw={rawValue} vol={vol:P0} target={knob.Target}");

        try
        {
            var target = knob.Target.ToLowerInvariant();

            if (target == "master")
            {
                MMDevice? device;
                lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                using (device) { device!.AudioEndpointVolume.MasterVolumeLevelScalar = vol; }
                return;
            }

            if (target == "mic")
            {
                MMDevice? mic;
                lock (_enumLock) mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                using (mic) { mic!.AudioEndpointVolume.MasterVolumeLevelScalar = vol; }
                return;
            }

            if (target == "output_device")
            {
                SetDeviceVolume(knob.DeviceId, DataFlow.Render, vol);
                return;
            }

            if (target == "input_device")
            {
                SetDeviceVolume(knob.DeviceId, DataFlow.Capture, vol);
                return;
            }

            if (target == "active_window")
            {
                SetActiveWindowVolume(vol);
                return;
            }

            // Multi-app group target
            if (target == "apps")
            {
                lock (_lock)
                {
                    foreach (var appName in knob.Apps)
                    {
                        var app = appName.ToLowerInvariant();
                        foreach (var kv in _sessions)
                        {
                            if (kv.Key.Contains(app))
                                try { kv.Value.SimpleAudioVolume.Volume = vol; } catch { }
                        }
                    }
                }
                return;
            }

            lock (_lock)
            {
                if (target == "system")
                {
                    // System Sounds has PID 0 — scan sessions on the default device directly
                    try
                    {
                        MMDevice? device;
                        lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        using (device)
                        {
                            var sessions = device!.AudioSessionManager.Sessions;
                            for (int i = 0; i < sessions.Count; i++)
                            {
                                var s = sessions[i];
                                if (s.GetProcessID == 0)
                                {
                                    s.SimpleAudioVolume.Volume = vol;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                    return;
                }

                if (target == "any")
                {
                    var first = _sessions.Values.FirstOrDefault();
                    if (first != null) first.SimpleAudioVolume.Volume = vol;
                    return;
                }

                // Match by process name substring
                var match = _sessions.FirstOrDefault(kv => kv.Key.Contains(target));
                if (match.Value != null)
                    match.Value.SimpleAudioVolume.Volume = vol;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"SetVolume error for {knob.Label}: {ex.Message}");
        }
    }

    private void SetDeviceVolume(string deviceId, DataFlow dataFlow, float vol)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            Logger.Log($"SetDeviceVolume: no deviceId configured");
            return;
        }

        try
        {
            MMDeviceCollection? devices;
            lock (_enumLock) devices = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var dev = devices[i];
                    if (dev.ID == deviceId)
                    {
                        dev.AudioEndpointVolume.MasterVolumeLevelScalar = vol;
                        return;
                    }
                }
            }
            Logger.Log($"SetDeviceVolume: device not found: {deviceId}");
        }
        catch (Exception ex)
        {
            Logger.Log($"SetDeviceVolume error: {ex.Message}");
        }
    }

    private void SetActiveWindowVolume(float vol)
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            lock (_lock)
            {
                if (_sessionsByPid.TryGetValue(pid, out var session))
                {
                    session.SimpleAudioVolume.Volume = vol;
                    return;
                }
            }

            // If not found by exact PID, try to find by process name
            // (some apps like Chrome have child processes)
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                var name = proc.ProcessName.ToLowerInvariant();
                lock (_lock)
                {
                    if (_sessions.TryGetValue(name, out var sessionByName))
                    {
                        sessionByName.SimpleAudioVolume.Volume = vol;
                    }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            Logger.Log($"SetActiveWindowVolume error: {ex.Message}");
        }
    }

    public float GetVolume(KnobConfig knob)
    {
        try
        {
            var target = knob.Target.ToLowerInvariant();

            if (target == "master")
            {
                MMDevice? device;
                lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                using (device) { return device!.AudioEndpointVolume.MasterVolumeLevelScalar; }
            }

            if (target == "mic")
            {
                MMDevice? mic;
                lock (_enumLock) mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                using (mic) { return mic!.AudioEndpointVolume.MasterVolumeLevelScalar; }
            }

            if (target == "output_device")
            {
                return GetDeviceVolume(knob.DeviceId, DataFlow.Render);
            }

            if (target == "input_device")
            {
                return GetDeviceVolume(knob.DeviceId, DataFlow.Capture);
            }

            if (target == "active_window")
            {
                return GetActiveWindowVolume();
            }

            if (target == "system")
            {
                try
                {
                    MMDevice? device;
                    lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    using (device)
                    {
                        var sessions = device!.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var s = sessions[i];
                            if (s.GetProcessID == 0)
                                return s.SimpleAudioVolume.Volume;
                        }
                    }
                }
                catch { }
                return 0f;
            }

            if (target == "apps")
            {
                lock (_lock)
                {
                    float maxVol = 0f;
                    foreach (var appName in knob.Apps)
                    {
                        var app = appName.ToLowerInvariant();
                        foreach (var kv in _sessions)
                        {
                            if (kv.Key.Contains(app))
                                try { maxVol = Math.Max(maxVol, kv.Value.SimpleAudioVolume.Volume); } catch { }
                        }
                    }
                    return maxVol;
                }
            }

            if (target == "any")
            {
                lock (_lock)
                {
                    var first = _sessions.Values.FirstOrDefault();
                    if (first != null) return first.SimpleAudioVolume.Volume;
                }
                return 0f;
            }

            // Process name substring match
            lock (_lock)
            {
                var match = _sessions.FirstOrDefault(kv => kv.Key.Contains(target));
                if (match.Value != null) return match.Value.SimpleAudioVolume.Volume;
            }
        }
        catch { }
        return 0f;
    }

    private float GetDeviceVolume(string deviceId, DataFlow dataFlow)
    {
        if (string.IsNullOrEmpty(deviceId)) return 0f;
        try
        {
            MMDeviceCollection? devices;
            lock (_enumLock) devices = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var dev = devices[i];
                    if (dev.ID == deviceId)
                        return dev.AudioEndpointVolume.MasterVolumeLevelScalar;
                }
            }
        }
        catch { }
        return 0f;
    }

    private float GetActiveWindowVolume()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0f;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return 0f;

            lock (_lock)
            {
                if (_sessionsByPid.TryGetValue(pid, out var session))
                    return session.SimpleAudioVolume.Volume;
            }

            // Fallback: match by process name
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                var name = proc.ProcessName.ToLowerInvariant();
                lock (_lock)
                {
                    if (_sessions.TryGetValue(name, out var sessionByName))
                        return sessionByName.SimpleAudioVolume.Volume;
                }
            }
            catch { }
        }
        catch { }
        return 0f;
    }

    public float GetPeakLevel(KnobConfig knob)
    {
        try
        {
            var target = knob.Target.ToLowerInvariant();

            if (target == "master")
            {
                MMDevice? device;
                lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                using (device) { return device!.AudioMeterInformation.MasterPeakValue; }
            }

            if (target == "mic")
            {
                MMDevice? mic;
                lock (_enumLock) mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                using (mic) { return mic!.AudioMeterInformation.MasterPeakValue; }
            }

            if (target == "output_device")
            {
                return GetPeakLevelForDevice(knob.DeviceId, DataFlow.Render);
            }

            if (target == "input_device")
            {
                return GetPeakLevelForDevice(knob.DeviceId, DataFlow.Capture);
            }

            if (target == "active_window")
            {
                return GetActiveWindowPeakLevel();
            }

            if (target == "system")
            {
                try
                {
                    MMDevice? device;
                    lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    using (device)
                    {
                        var sessionMgr = device!.AudioSessionManager;
                        var sessions = sessionMgr.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var s = sessions[i];
                            if (s.GetProcessID == 0)
                                return s.AudioMeterInformation.MasterPeakValue;
                        }
                    }
                }
                catch { }
                return 0f;
            }

            if (target == "apps")
            {
                lock (_lock)
                {
                    float maxPeak = 0f;
                    foreach (var appName in knob.Apps)
                    {
                        var app = appName.ToLowerInvariant();
                        foreach (var kv in _sessions)
                        {
                            if (kv.Key.Contains(app))
                                try { maxPeak = Math.Max(maxPeak, kv.Value.AudioMeterInformation.MasterPeakValue); } catch { }
                        }
                    }
                    return maxPeak;
                }
            }

            if (target == "any")
            {
                lock (_lock)
                {
                    var first = _sessions.Values.FirstOrDefault();
                    if (first != null) return first.AudioMeterInformation.MasterPeakValue;
                }
                return 0f;
            }

            // Process name substring match
            lock (_lock)
            {
                var match = _sessions.FirstOrDefault(kv => kv.Key.Contains(target));
                if (match.Value != null) return match.Value.AudioMeterInformation.MasterPeakValue;
            }
        }
        catch { }
        return 0f;
    }

    private float GetPeakLevelForDevice(string deviceId, DataFlow dataFlow)
    {
        if (string.IsNullOrEmpty(deviceId)) return 0f;
        try
        {
            MMDeviceCollection? devices;
            lock (_enumLock) devices = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var dev = devices[i];
                    if (dev.ID == deviceId)
                        return dev.AudioMeterInformation.MasterPeakValue;
                }
            }
        }
        catch { }
        return 0f;
    }

    private float GetActiveWindowPeakLevel()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0f;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return 0f;

            lock (_lock)
            {
                if (_sessionsByPid.TryGetValue(pid, out var session))
                    return session.AudioMeterInformation.MasterPeakValue;
            }

            // Fallback: match by process name
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                var name = proc.ProcessName.ToLowerInvariant();
                lock (_lock)
                {
                    if (_sessions.TryGetValue(name, out var sessionByName))
                        return sessionByName.AudioMeterInformation.MasterPeakValue;
                }
            }
            catch { }
        }
        catch { }
        return 0f;
    }

    /// <summary>
    /// Returns all active audio endpoints (output and input devices).
    /// </summary>
    public List<(string Id, string Name, bool IsOutput)> GetAudioDevices()
    {
        var result = new List<(string Id, string Name, bool IsOutput)>();
        try
        {
            MMDeviceCollection? renderDevices;
            lock (_enumLock) renderDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (renderDevices != null)
            {
                for (int i = 0; i < renderDevices.Count; i++)
                {
                    var dev = renderDevices[i];
                    result.Add((dev.ID, dev.FriendlyName, true));
                }
            }

            MMDeviceCollection? captureDevices;
            lock (_enumLock) captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            if (captureDevices != null)
            {
                for (int i = 0; i < captureDevices.Count; i++)
                {
                    var dev = captureDevices[i];
                    result.Add((dev.ID, dev.FriendlyName, false));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"GetAudioDevices error: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Returns the process name of the current foreground window, or empty string if unavailable.
    /// </summary>
    public string GetActiveWindowProcessName()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";

            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Returns a list of process names that currently have active audio sessions.
    /// </summary>
    public List<string> GetRunningAudioApps()
    {
        lock (_lock)
        {
            return _sessions.Keys.OrderBy(k => k).ToList();
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _enumerator.Dispose();
    }
}
