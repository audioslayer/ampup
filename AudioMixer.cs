using AmpUp.Core.Engine;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AmpUp;

public class AudioMixer : IDisposable
{
    // P/Invoke declarations consolidated in NativeMethods.cs

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceNotificationClient _deviceNotificationClient;
    private readonly Dictionary<int, int> _lastValues = new();
    private readonly object _lock = new();      // guards _sessions / _sessionsByPid dict access
    private readonly object _enumLock = new();  // guards _enumerator — accessed from multiple threads
    private readonly object _lastValuesLock = new();
    private System.Threading.Timer? _pollTimer;
    private int _refreshInProgress;
    private long _lastRefreshErrorLogTicks;
    private string _lastRefreshError = "";
    private volatile bool _disposed;
    private const float NonZeroVolumeThreshold = 0.0001f;

    /// <summary>Fires when Windows adds, removes, activates, or changes the default audio endpoint.</summary>
    public event Action? AudioDevicesChanged;

    public AudioMixer()
    {
        _deviceNotificationClient = new DeviceNotificationClient(this);
        try { _enumerator.RegisterEndpointNotificationCallback(_deviceNotificationClient); }
        catch (Exception ex) { Logger.Log($"Audio device watcher registration failed: {ex.Message}"); }
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly AudioMixer _owner;

        public DeviceNotificationClient(AudioMixer owner) => _owner = owner;

        private void Notify()
        {
            if (!_owner._disposed)
                _owner.AudioDevicesChanged?.Invoke();
        }

        // Never dispose Core Audio objects synchronously inside an IMMNotificationClient
        // callback. Windows can be holding its endpoint-notification lock here; disposing
        // an endpoint may then wait on that same lock and deadlock every AudioMixer caller.
        // The app event is debounced and performs invalidation after this callback returns.
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Notify();
        public void OnDeviceAdded(string pwstrDeviceId) => Notify();
        public void OnDeviceRemoved(string deviceId) => Notify();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Notify();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    // Map of processName (lowercase) -> AudioSessionControl
    private Dictionary<string, AudioSessionControl> _sessions = new();

    /// <summary>
    /// Fuzzy process name match: strips spaces so "Apple Music" matches "AppleMusic".
    /// Allocation-free char walk — equivalent to
    /// processName.Replace(" ", "").Contains(search.Replace(" ", "")), case-insensitive.
    /// Called per session per VU tick, so no string allocs allowed here.
    /// </summary>
    private static bool FuzzyContains(string processName, string search)
    {
        // First non-space needle char — an empty/all-space needle matches anything
        int nStart = 0;
        while (nStart < search.Length && search[nStart] == ' ') nStart++;
        if (nStart >= search.Length) return true;

        for (int start = 0; start < processName.Length; start++)
        {
            if (processName[start] == ' ') continue;

            int h = start, n = nStart;
            while (true)
            {
                while (n < search.Length && search[n] == ' ') n++;
                if (n >= search.Length) return true; // consumed the whole needle
                while (h < processName.Length && processName[h] == ' ') h++;
                if (h >= processName.Length) break;
                if (char.ToLowerInvariant(processName[h]) != char.ToLowerInvariant(search[n])) break;
                h++; n++;
            }
        }
        return false;
    }

    /// <summary>
    /// Lowercases without allocating when the string is already lowercase —
    /// the common case for config target strings, checked per VU tick.
    /// </summary>
    private static string ToLowerNoAlloc(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (s[i] != char.ToLowerInvariant(s[i])) return s.ToLowerInvariant();
        return s;
    }
    // Map of processId -> AudioSessionControl (for active_window lookups)
    private Dictionary<uint, AudioSessionControl> _sessionsByPid = new();

    public void Start()
    {
        RefreshSessions();
        _pollTimer = new System.Threading.Timer(_ => RefreshSessions(), null, 2000, 2000);
    }

    public void RefreshNow() => RefreshSessions();

    private MMDevice? _renderDevice; // kept alive so session COM objects remain valid
    private MMDevice? _masterPeakDevice; // dedicated persistent device for master peak metering
    private MMDevice? _micPeakDevice; // dedicated persistent device for mic peak metering
    private MMDevice? _masterControlDevice; // persistent default render endpoint (Role.Console) for SetVolume("master")
    private MMDevice? _micControlDevice; // persistent default capture endpoint for SetVolume("mic")
    private AudioSessionControl? _systemPeakSession; // PID-0 System Sounds session for peak metering
    private readonly Dictionary<string, MMDevice> _devicePeakCache = new(); // deviceId -> metering device (guarded by _enumLock)
    private string? _defaultRenderId; // last-seen default render endpoint ID (guarded by _enumLock)
    private string? _defaultCaptureId; // last-seen default capture endpoint ID (guarded by _enumLock)

    // PID -> process name cache. Process.GetProcessById snapshots the entire
    // system process table per call, so resolve each PID once and reuse across
    // the 2s refresh ticks. Entries for PIDs no longer in the session list are
    // evicted each refresh so the cache can't grow unbounded.
    private readonly Dictionary<int, string> _pidNameCache = new();
    private readonly object _pidNameLock = new();

    // Distinct AudioSessionControl wrappers from the last refresh — disposed on swap
    private List<AudioSessionControl> _sessionWrappers = new();

    private void RefreshSessions()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0) return;

        MMDevice? pendingDevice = null;
        List<AudioSessionControl>? pendingWrappers = null;

        try
        {
            MMDevice? device;
            lock (_enumLock)
            {
                if (_disposed) return;
                device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            pendingDevice = device;

            // Drop cached endpoint objects if the default device changed — otherwise
            // the master/mic knob + VU caches keep pointing at the old endpoint.
            InvalidateStaleEndpointCaches(device!);

            var newSessions = new Dictionary<string, AudioSessionControl>();
            var newPidSessions = new Dictionary<uint, AudioSessionControl>();
            var newWrappers = new List<AudioSessionControl>();
            pendingWrappers = newWrappers;
            var seenPids = new HashSet<int>();

            var sessionMgr = device!.AudioSessionManager;
            var sessions = sessionMgr.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                bool stored = false;
                try
                {
                    var pid = (int)s.GetProcessID;
                    if (pid == 0) continue;
                    seenPids.Add(pid);
                    var rawName = ResolvePidName(pid);
                    if (rawName == null) continue; // process gone
                    var name = rawName.ToLowerInvariant();
                    // Use compound key (name:pid) to store ALL sessions per process
                    // (apps like Discord create multiple: voice, screenshare, notifications)
                    var sessionKey = $"{name}:{pid}";
                    if (!newSessions.ContainsKey(sessionKey))
                    {
                        newSessions[sessionKey] = s;
                        stored = true;
                    }
                    // Also store by plain name for backward compat (first wins)
                    if (!newSessions.ContainsKey(name))
                    {
                        newSessions[name] = s;
                        stored = true;
                    }

                    // Also index by WASAPI display name — catches UWP/packaged apps
                    // where audio runs in a helper process (e.g. AMPLibraryAgent for Apple Music)
                    try
                    {
                        var displayName = s.DisplayName;
                        if (!string.IsNullOrEmpty(displayName))
                        {
                            var dnKey = displayName.ToLowerInvariant();
                            if (!newSessions.ContainsKey(dnKey))
                            {
                                newSessions[dnKey] = s;
                                stored = true;
                            }
                        }
                    }
                    catch { }

                    var upid = (uint)pid;
                    if (!newPidSessions.ContainsKey(upid))
                    {
                        newPidSessions[upid] = s;
                        stored = true;
                    }
                }
                catch { }
                finally
                {
                    if (stored)
                        newWrappers.Add(s);
                    else
                        try { s.Dispose(); } catch { }
                }
            }

            // Evict cached names for PIDs that no longer have sessions
            lock (_pidNameLock)
            {
                if (_pidNameCache.Count > 0)
                {
                    List<int>? stale = null;
                    foreach (var p in _pidNameCache.Keys)
                        if (!seenPids.Contains(p)) (stale ??= new List<int>()).Add(p);
                    if (stale != null)
                        foreach (var p in stale) _pidNameCache.Remove(p);
                }
            }

            // Swap atomically — old device + old session wrappers disposed after new sessions are live
            lock (_lock)
            {
                var oldWrappers = _sessionWrappers;
                _sessions = newSessions;
                _sessionsByPid = newPidSessions;
                _sessionWrappers = newWrappers;
                var oldDevice = _renderDevice;
                _renderDevice = device;
                pendingDevice = null;
                pendingWrappers = null;
                try { oldDevice?.Dispose(); } catch { }
                // Sessions are re-enumerated fresh each refresh (the SessionCollection
                // indexer creates a new wrapper per access), so nothing from the previous
                // refresh is carried forward — every old wrapper is safe to dispose.
                // AudioSessionControl.Dispose only unregisters event callbacks (we register
                // none) and suppresses the finalizer; the underlying COM object stays alive
                // for any view still briefly holding a reference from GetSessionForProcess.
                foreach (var w in oldWrappers)
                    try { w.Dispose(); } catch { }
            }
        }
        catch (Exception ex)
        {
            // Don't clear _sessions on failure — keep stale data rather than empty
            LogRefreshError(ex.Message);
        }
        finally
        {
            if (pendingWrappers != null)
            {
                foreach (var wrapper in pendingWrappers)
                    try { wrapper.Dispose(); } catch { }
            }
            try { pendingDevice?.Dispose(); } catch { }
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }

    private void LogRefreshError(string message)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long lastTicks = Interlocked.Read(ref _lastRefreshErrorLogTicks);
        if (!string.Equals(message, _lastRefreshError, StringComparison.Ordinal)
            || lastTicks == 0
            || nowTicks - lastTicks >= TimeSpan.FromMinutes(5).Ticks)
        {
            _lastRefreshError = message;
            Interlocked.Exchange(ref _lastRefreshErrorLogTicks, nowTicks);
            Logger.Log($"Session refresh error: {message} (identical errors suppressed for 5 minutes)");
        }
    }

    /// <summary>
    /// PID -> process name with a persistent cache (raw casing — callers lowercase
    /// as needed). Returns null if the process is gone.
    /// </summary>
    private string? ResolvePidName(int pid)
    {
        lock (_pidNameLock)
        {
            if (_pidNameCache.TryGetValue(pid, out var cached)) return cached;
        }
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            var name = proc.ProcessName;
            lock (_pidNameLock) _pidNameCache[pid] = name;
            return name;
        }
        catch { return null; }
    }

    /// <summary>
    /// Compares the current default render/capture endpoint IDs against the last
    /// refresh and drops the cached control/metering endpoints on change, so a
    /// default-device switch (cycle_output, Windows settings) re-acquires the new
    /// endpoint within one 2s refresh tick.
    /// </summary>
    private void InvalidateStaleEndpointCaches(MMDevice currentRenderDefault)
    {
        string? renderId = null;
        try { renderId = currentRenderDefault.ID; } catch { }

        string? captureId = null;
        try
        {
            MMDevice? mic;
            lock (_enumLock) mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            using (mic) { captureId = mic?.ID; }
        }
        catch { } // no default capture device — leave capture caches as-is

        lock (_enumLock)
        {
            if (renderId != null && _defaultRenderId != null && renderId != _defaultRenderId)
            {
                try { _masterPeakDevice?.Dispose(); } catch { }
                _masterPeakDevice = null;
                try { _masterControlDevice?.Dispose(); } catch { }
                _masterControlDevice = null;
                try { _systemPeakSession?.Dispose(); } catch { }
                _systemPeakSession = null;
            }
            if (renderId != null) _defaultRenderId = renderId;

            if (captureId != null && _defaultCaptureId != null && captureId != _defaultCaptureId)
            {
                try { _micPeakDevice?.Dispose(); } catch { }
                _micPeakDevice = null;
                try { _micControlDevice?.Dispose(); } catch { }
                _micControlDevice = null;
            }
            if (captureId != null) _defaultCaptureId = captureId;
        }
    }

    /// <summary>
    /// Full pipeline: raw 0-1023 -> 0-1 -> curve -> range clamp -> final 0-1 volume.
    /// Delegates to <see cref="VolumePipeline.ComputeVolume(int, KnobConfig)"/>.
    /// </summary>
    private static float ComputeVolume(int rawValue, KnobConfig knob)
        => VolumePipeline.ComputeVolume(rawValue, knob);

    internal static void SetRenderEndpointVolume(AudioEndpointVolume endpointVolume, float volume)
    {
        endpointVolume.MasterVolumeLevelScalar = volume;

        // Some Bluetooth endpoints flip endpoint mute when volume reaches 0 and
        // do not automatically clear it when the scalar is raised again.
        if (volume > NonZeroVolumeThreshold && endpointVolume.Mute)
        {
            endpointVolume.Mute = false;
        }
    }

    public void SetVolume(KnobConfig knob, int rawValue, int? debounceKeyOverride = null)
    {
        // Debounce — skip if change < 5
        int debounceKey = debounceKeyOverride ?? knob.Idx;
        lock (_lastValuesLock)
        {
            if (_lastValues.TryGetValue(debounceKey, out int last) && Math.Abs(rawValue - last) < 5)
            {
                return;
            }
            _lastValues[debounceKey] = rawValue;
        }

        float vol = ComputeVolume(rawValue, knob);

        try
        {
            var target = ToLowerNoAlloc(knob.Target);

            if (target == "master")
            {
                // Persistent control endpoint — activating a fresh MMDevice per knob
                // event is expensive. Invalidated on failure, device change (see
                // InvalidateStaleEndpointCaches) and session lock (InvalidatePeakDevice).
                lock (_enumLock)
                {
                    try
                    {
                        _masterControlDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                        SetRenderEndpointVolume(_masterControlDevice.AudioEndpointVolume, vol);
                    }
                    catch
                    {
                        // Endpoint went stale — drop it so the next knob event re-acquires
                        try { _masterControlDevice?.Dispose(); } catch { }
                        _masterControlDevice = null;
                        throw;
                    }
                }
                return;
            }

            if (target == "mic")
            {
                lock (_enumLock)
                {
                    try
                    {
                        _micControlDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        _micControlDevice.AudioEndpointVolume.MasterVolumeLevelScalar = vol;
                    }
                    catch
                    {
                        try { _micControlDevice?.Dispose(); } catch { }
                        _micControlDevice = null;
                        throw;
                    }
                }
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

        lock (_enumLock)
        {
            try
            {
                var device = GetOrCacheDeviceEndpoint(deviceId, dataFlow);
                if (device == null) return;
                if (dataFlow == DataFlow.Render)
                {
                    SetRenderEndpointVolume(device.AudioEndpointVolume, vol);
                }
                else
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = vol;
                }
            }
            catch (Exception ex)
            {
                if (_devicePeakCache.Remove(deviceId, out var stale))
                {
                    try { stale.Dispose(); } catch { }
                }
                Logger.Log($"SetDeviceVolume error: {ex.Message}");
            }
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

        lock (_enumLock)
        {
            try
            {
                var device = GetOrCacheDeviceEndpoint(deviceId, DataFlow.Render);
                if (device != null)
                    device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
            }
            catch (Exception ex)
            {
                if (_devicePeakCache.Remove(deviceId, out var stale))
                {
                    try { stale.Dispose(); } catch { }
                }
                Logger.Log($"ToggleOutputDeviceMute error: {ex.Message}");
            }
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
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
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
            var target = ToLowerNoAlloc(knob.Target);

            if (target == "master")
            {
                // MixerView reads volume every 75ms. Reuse the same endpoint as
                // peak metering instead of activating a fresh MMDevice each tick;
                // repeated activation leaks native MMDevices property-store handles
                // until a GC finalizer pass and eventually stalls long-running apps.
                lock (_enumLock)
                {
                    try
                    {
                        _masterPeakDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        return _masterPeakDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                    }
                    catch
                    {
                        try { _masterPeakDevice?.Dispose(); } catch { }
                        _masterPeakDevice = null;
                        return 0f;
                    }
                }
            }

            if (target == "mic")
            {
                lock (_enumLock)
                {
                    try
                    {
                        _micPeakDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        return _micPeakDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                    }
                    catch
                    {
                        try { _micPeakDevice?.Dispose(); } catch { }
                        _micPeakDevice = null;
                        return 0f;
                    }
                }
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
        lock (_enumLock)
        {
            try
            {
                var device = GetOrCacheDeviceEndpoint(deviceId, dataFlow);
                if (device == null) return 0f;
                return device.AudioEndpointVolume.MasterVolumeLevelScalar;
            }
            catch
            {
                if (_devicePeakCache.Remove(deviceId, out var stale))
                {
                    try { stale.Dispose(); } catch { }
                }
                return 0f;
            }
        }
    }

    /// <summary>
    /// Returns a persistent endpoint for a specific device. Both volume and peak
    /// reads share this cache because MixerView requests them back-to-back every
    /// 75ms. Caller must hold <see cref="_enumLock"/>.
    /// </summary>
    private MMDevice? GetOrCacheDeviceEndpoint(string deviceId, DataFlow dataFlow)
    {
        if (_devicePeakCache.TryGetValue(deviceId, out var cached))
            return cached;

        var devices = _enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        MMDevice? match = null;
        if (devices != null)
        {
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                bool isMatch = false;
                try { isMatch = match == null && device.ID == deviceId; } catch { }
                if (isMatch)
                    match = device;
                else
                    try { device.Dispose(); } catch { }
            }
        }

        if (match != null)
            _devicePeakCache[deviceId] = match;
        return match;
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
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
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
            var target = ToLowerNoAlloc(knob.Target);

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
                // Same persistent-device pattern as the master path — re-activating
                // the capture endpoint per VU tick is expensive.
                lock (_enumLock)
                {
                    try
                    {
                        _micPeakDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                        return _micPeakDevice.AudioMeterInformation.MasterPeakValue;
                    }
                    catch
                    {
                        // Device may have changed — recreate on next call
                        try { _micPeakDevice?.Dispose(); } catch { }
                        _micPeakDevice = null;
                        return 0f;
                    }
                }
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
                // System Sounds session (PID 0) cached off the persistent master peak
                // device — re-activating the endpoint + enumerating sessions per VU
                // tick is expensive. Invalidated on read failure, device change and
                // session lock alongside _masterPeakDevice.
                lock (_enumLock)
                {
                    try
                    {
                        if (_systemPeakSession == null)
                        {
                            _masterPeakDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                            var sessionMgr = _masterPeakDevice.AudioSessionManager;
                            sessionMgr.RefreshSessions();
                            var sessions = sessionMgr.Sessions;
                            for (int i = 0; i < sessions.Count; i++)
                            {
                                var s = sessions[i];
                                bool keep = false;
                                try { keep = _systemPeakSession == null && s.GetProcessID == 0; } catch { }
                                if (keep)
                                    _systemPeakSession = s;
                                else
                                    try { s.Dispose(); } catch { }
                            }
                        }
                        if (_systemPeakSession != null)
                            return _systemPeakSession.AudioMeterInformation.MasterPeakValue;
                    }
                    catch
                    {
                        try { _systemPeakSession?.Dispose(); } catch { }
                        _systemPeakSession = null;
                    }
                    return 0f;
                }
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
        lock (_enumLock)
        {
            // Persistent per-device metering cache — a full EnumerateAudioEndPoints
            // per VU tick is expensive. Evicted on read failure and session lock.
            if (_devicePeakCache.TryGetValue(deviceId, out var cached))
            {
                try
                {
                    return cached.AudioMeterInformation.MasterPeakValue;
                }
                catch
                {
                    // Device removed/changed — re-resolve on next call
                    try { cached.Dispose(); } catch { }
                    _devicePeakCache.Remove(deviceId);
                    return 0f;
                }
            }

            try
            {
                var match = GetOrCacheDeviceEndpoint(deviceId, dataFlow);
                if (match != null)
                    return match.AudioMeterInformation.MasterPeakValue;
            }
            catch { }
            return 0f;
        }
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
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
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
                    using var dev = renderDevices[i];
                    result.Add((dev.ID, dev.FriendlyName, true));
                }
            }

            MMDeviceCollection? captureDevices;
            lock (_enumLock) captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            if (captureDevices != null)
            {
                for (int i = 0; i < captureDevices.Count; i++)
                {
                    using var dev = captureDevices[i];
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

            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
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
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
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
                    string? procName = ResolvePidName(pid);
                    if (procName == null) continue;

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
            using (dev) output = dev?.FriendlyName ?? "Unknown";
        }
        catch { }
        try
        {
            MMDevice? mic;
            lock (_enumLock) mic = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            using (mic) input = mic?.FriendlyName ?? "Unknown";
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
            using (dev)
            {
            float vol = dev.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool muted = dev.AudioEndpointVolume.Mute;
            return (vol, muted);
            }
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
            try { _micPeakDevice?.Dispose(); } catch { }
            _micPeakDevice = null;
            try { _masterControlDevice?.Dispose(); } catch { }
            _masterControlDevice = null;
            try { _micControlDevice?.Dispose(); } catch { }
            _micControlDevice = null;
            try { _systemPeakSession?.Dispose(); } catch { }
            _systemPeakSession = null;
            foreach (var dev in _devicePeakCache.Values)
                try { dev.Dispose(); } catch { }
            _devicePeakCache.Clear();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Dispose();
        _pollTimer = null;
        lock (_lock)
        {
            _sessions.Clear();
            _sessionsByPid.Clear();
            foreach (var w in _sessionWrappers)
                try { w.Dispose(); } catch { }
            _sessionWrappers = new List<AudioSessionControl>();
        }
        lock (_enumLock)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_deviceNotificationClient); } catch { }
            try { _renderDevice?.Dispose(); } catch { }
            try { _masterPeakDevice?.Dispose(); } catch { }
            try { _micPeakDevice?.Dispose(); } catch { }
            try { _masterControlDevice?.Dispose(); } catch { }
            try { _micControlDevice?.Dispose(); } catch { }
            try { _systemPeakSession?.Dispose(); } catch { }
            _renderDevice = null;
            _masterPeakDevice = null;
            _micPeakDevice = null;
            _masterControlDevice = null;
            _micControlDevice = null;
            _systemPeakSession = null;
            foreach (var dev in _devicePeakCache.Values)
                try { dev.Dispose(); } catch { }
            _devicePeakCache.Clear();
            _enumerator.Dispose();
        }
    }
}
