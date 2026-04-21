using AmpUp.Core.Engine;
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

    /// <summary>
    /// Fuzzy process name match: strips spaces so "Apple Music" matches "AppleMusic".
    /// Both strings should already be lowercase.
    /// </summary>
    private static bool FuzzyContains(string processName, string search)
        => processName.Contains(search)
        || processName.Replace(" ", "").Contains(search.Replace(" ", ""));
    // Map of processId -> AudioSessionControl (for active_window lookups)
    private Dictionary<uint, AudioSessionControl> _sessionsByPid = new();

    public void Start()
    {
        RefreshSessions();
        _pollTimer = new System.Threading.Timer(_ => RefreshSessions(), null, 2000, 2000);
    }

    private MMDevice? _renderDevice; // kept alive so session COM objects remain valid
    private MMDevice? _masterPeakDevice; // dedicated persistent device for master peak metering

    private void RefreshSessions()
    {
        try
        {
            MMDevice? device;
            lock (_enumLock) device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var newSessions = new Dictionary<string, AudioSessionControl>();
            var newPidSessions = new Dictionary<uint, AudioSessionControl>();

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
                    // Use compound key (name:pid) to store ALL sessions per process
                    // (apps like Discord create multiple: voice, screenshare, notifications)
                    var sessionKey = $"{name}:{pid}";
                    if (!newSessions.ContainsKey(sessionKey))
                        newSessions[sessionKey] = s;
                    // Also store by plain name for backward compat (first wins)
                    if (!newSessions.ContainsKey(name))
                        newSessions[name] = s;

                    // Also index by WASAPI display name — catches UWP/packaged apps
                    // where audio runs in a helper process (e.g. AMPLibraryAgent for Apple Music)
                    try
                    {
                        var displayName = s.DisplayName;
                        if (!string.IsNullOrEmpty(displayName))
                        {
                            var dnKey = displayName.ToLowerInvariant();
                            if (!newSessions.ContainsKey(dnKey))
                                newSessions[dnKey] = s;
                        }
                    }
                    catch { }

                    var upid = (uint)pid;
                    if (!newPidSessions.ContainsKey(upid))
                        newPidSessions[upid] = s;
                }
                catch { }
            }

            // Swap atomically — old device disposed after new sessions are live
            lock (_lock)
            {
                _sessions = newSessions;
                _sessionsByPid = newPidSessions;
                var oldDevice = _renderDevice;
                _renderDevice = device;
                oldDevice?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Don't clear _sessions on failure — keep stale data rather than empty
            Logger.Log($"Session refresh error: {ex.Message}");
        }
    }

    /// <summary>
    /// Full pipeline: raw 0-1023 -> 0-1 -> curve -> range clamp -> final 0-1 volume.
    /// Delegates to <see cref="VolumePipeline.ComputeVolume(int, KnobConfig)"/>.
    /// </summary>
    private static float ComputeVolume(int rawValue, KnobConfig knob)
        => VolumePipeline.ComputeVolume(rawValue, knob);

    public void SetVolume(KnobConfig knob, int rawValue)
    {
        // Debounce — skip if change < 5
        if (_lastValues.TryGetValue(knob.Idx, out int last) && Math.Abs(rawValue - last) < 5)
        {
            return;
        }
        _lastValues[knob.Idx] = rawValue;

        float vol = ComputeVolume(rawValue, knob);

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
                            if (FuzzyContains(kv.Key, app))
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

                // Match ALL sessions by process name substring (apps like Discord
                // create multiple sessions — voice, screenshare, notifications)
                foreach (var kv in _sessions)
                {
                    if (FuzzyContains(kv.Key, target))
                        try { kv.Value.SimpleAudioVolume.Volume = vol; } catch { }
                }
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
                    using var dev = devices[i];
                    if (dev.ID == deviceId)
                    {
                        dev.AudioEndpointVolume.MasterVolumeLevelScalar = vol;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"SetDeviceVolume error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets volume on a specific audio output device by its device ID.
    /// Used by Device Groups to control audio device volume alongside lights.
    /// </summary>
    public void SetOutputDeviceVolume(string deviceId, float volume)
    {
        SetDeviceVolume(deviceId, DataFlow.Render, Math.Clamp(volume, 0f, 1f));
    }

    /// <summary>
    /// Toggles mute on a specific audio output device by its device ID.
    /// Used by Device Groups to toggle audio device mute alongside lights.
    /// </summary>
    public void ToggleOutputDeviceMute(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return;
        }

        try
        {
            MMDeviceCollection? devices;
            lock (_enumLock) devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    using var dev = devices[i];
                    if (dev.ID == deviceId)
                    {
                        dev.AudioEndpointVolume.Mute = !dev.AudioEndpointVolume.Mute;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ToggleOutputDeviceMute error: {ex.Message}");
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
                            if (FuzzyContains(kv.Key, app))
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

            // Process name substring match — return max volume across all matching sessions
            // (apps like Discord have multiple sessions: voice, screenshare, notifications)
            lock (_lock)
            {
                float maxVol = -1f;
                foreach (var kv in _sessions)
                {
                    if (FuzzyContains(kv.Key, target))
                        try { maxVol = Math.Max(maxVol, kv.Value.SimpleAudioVolume.Volume); } catch { }
                }
                if (maxVol >= 0) return maxVol;
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
                    using var dev = devices[i];
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

            // Skip AmpUp's own window — user is just looking at the mixer
            int myPid = Environment.ProcessId;
            if (pid == myPid) return -1f; // -1 signals "no valid active window"

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
                // Use a dedicated persistent device for peak metering
                // (separate from _renderDevice which gets swapped during RefreshSessions).
                // All reads and writes to _masterPeakDevice go through _enumLock to avoid a
                // TOCTOU race between GetPeakLevel (UI timer) and InvalidatePeakDevice
                // (session-lock handler on a different thread).
                lock (_enumLock)
                {
                    if (_masterPeakDevice == null)
                        _masterPeakDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    try
                    {
                        return _masterPeakDevice.AudioMeterInformation.MasterPeakValue;
                    }
                    catch
                    {
                        // Device may have changed — recreate on next call
                        try { _masterPeakDevice?.Dispose(); } catch { }
                        _masterPeakDevice = null;
                        return 0f;
                    }
                }
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
                            if (FuzzyContains(kv.Key, app))
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
                var match = _sessions.FirstOrDefault(kv => FuzzyContains(kv.Key, target));
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
                    using var dev = devices[i];
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

            // Skip AmpUp's own window
            if (pid == Environment.ProcessId) return 0f;

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
    /// Friendly name of the foreground window's audio session — WASAPI
    /// DisplayName when available (e.g. "Apple Music" instead of the
    /// helper process "AMPLibraryAgent"), falling back to ProcessName.
    /// Returns "" if no session resolves. Mirrors the lookup path used
    /// by SetActiveWindowVolume so the OSD label reflects what's actually
    /// being controlled.
    /// </summary>
    public string GetActiveWindowDisplayName()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "";

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return "";

            // Skip AmpUp — matches GetActiveWindowVolume's behavior so the
            // OSD doesn't helpfully announce "AmpUp" when the user is just
            // testing a knob from inside the config UI.
            if (pid == Environment.ProcessId) return "";

            AudioSessionControl? session = null;
            lock (_lock)
            {
                _sessionsByPid.TryGetValue(pid, out session);
            }

            if (session != null)
            {
                try
                {
                    var dn = session.DisplayName;
                    if (!string.IsNullOrWhiteSpace(dn)) return dn;
                }
                catch { }
            }

            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
            catch { return ""; }
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Returns the raw AudioSessionControl for a given process name (case-insensitive).
    /// Used by AudioDashView to hold session references for live peak polling.
    /// </summary>
    public AudioSessionControl? GetSessionForProcess(string processName)
    {
        lock (_lock)
        {
            var key = processName.ToLowerInvariant();
            if (_sessions.TryGetValue(key, out var s)) return s;
            // Fuzzy fallback
            var match = _sessions.FirstOrDefault(kv => FuzzyContains(kv.Key, key));
            return match.Value;
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

    public record SessionInfo(
        string ProcessName,
        int Pid,
        float Volume,
        float Peak,
        bool Muted,
        string DisplayName
    );

    /// <summary>
    /// Returns live info for all active audio sessions — used by the Audio Dashboard.
    /// Deduplicated by process name (first session wins).
    /// </summary>
    public List<SessionInfo> GetAllSessionsInfo()
    {
        var result = new List<SessionInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            foreach (var kv in _sessions)
            {
                var s = kv.Value;
                try
                {
                    int pid = (int)s.GetProcessID;
                    if (pid == 0) continue;

                    // Use process name for dedup — skip display name entries (dupes)
                    string procName;
                    try { procName = System.Diagnostics.Process.GetProcessById(pid).ProcessName; }
                    catch { continue; }

                    if (!seen.Add(procName.ToLowerInvariant())) continue;

                    float vol = s.SimpleAudioVolume.Volume;
                    float peak = 0f;
                    try { peak = s.AudioMeterInformation.MasterPeakValue; } catch { }
                    bool muted = false;
                    try { muted = s.SimpleAudioVolume.Mute; } catch { }
                    string displayName = "";
                    try { displayName = s.DisplayName; } catch { }
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = procName;

                    result.Add(new SessionInfo(procName, pid, vol, peak, muted, displayName));
                }
                catch { }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns (outputName, inputName) friendly names of current default audio endpoints.
    /// </summary>
    public (string Output, string Input) GetDefaultDeviceNames()
    {
        string output = "Unknown", input = "Unknown";
        try
        {
            MMDevice? dev;
            lock (_enumLock) dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            output = dev?.FriendlyName ?? "Unknown";
            dev?.Dispose();
        }
        catch { }
        try
        {
            MMDevice? mic;
            lock (_enumLock) mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            input = mic?.FriendlyName ?? "Unknown";
            mic?.Dispose();
        }
        catch { }
        return (output, input);
    }

    /// <summary>
    /// Returns (masterVolume 0-1, isMuted) for the default render endpoint.
    /// </summary>
    public (float Volume, bool Muted) GetMasterInfo()
    {
        try
        {
            MMDevice? dev;
            lock (_enumLock) dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (dev == null) return (0f, false);
            float vol = dev.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool muted = dev.AudioEndpointVolume.Mute;
            dev.Dispose();
            return (vol, muted);
        }
        catch { return (0f, false); }
    }

    /// <summary>
    /// Drops the persistent master peak device so it is re-acquired on the next poll.
    /// Call this when the Windows session is locked — WASAPI invalidates device COM objects
    /// during lock, and holding stale handles causes COMExceptions on the poll thread.
    /// </summary>
    public void InvalidatePeakDevice()
    {
        lock (_enumLock)
        {
            try { _masterPeakDevice?.Dispose(); } catch { }
            _masterPeakDevice = null;
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _enumerator.Dispose();
    }
}
