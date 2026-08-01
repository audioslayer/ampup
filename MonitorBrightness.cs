using System.Runtime.InteropServices;

namespace AmpUp;

/// <summary>
/// Controls physical monitor brightness via DDC/CI (Monitor Control Command Set).
/// Uses the Windows High-Level Monitor API (dxva2.dll).
/// Caches physical monitor handles; auto-invalidates on failure.
/// Supports per-monitor control via GDI device name (e.g. \\.\DISPLAY1).
/// </summary>
public static class MonitorBrightness
{
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    /// <summary>Info about a single physical monitor for UI display and targeting.</summary>
    public class MonitorInfo
    {
        /// <summary>GDI device name, e.g. \\.\DISPLAY1. Stable identifier for config.</summary>
        public string DeviceName { get; init; } = "";
        /// <summary>Friendly name from DisplayConfig, e.g. "DELL U2723QE".</summary>
        public string FriendlyName { get; init; } = "";
        /// <summary>Index into the cached handles list.</summary>
        internal int HandleIndex { get; init; }
    }

    // ── Handle cache ────────────────────────────────────────────────

    private static readonly object _cacheLock = new();
    private static List<PHYSICAL_MONITOR>? _cachedMonitors;
    private static List<string>? _cachedDeviceNames; // GDI device name per handle, same order

    // DDC/CI brightness min/max per handle, same order. Lazily filled on first use —
    // min/max are static per monitor, so the 60ms throttle loop only issues the Set
    // instead of paying a 40-100ms Get transaction before every Set.
    // Invalidated together with the handle cache (monitor changes).
    private static (uint Min, uint Max)?[]? _cachedRanges;

    /// <summary>
    /// Get the (cached) DDC/CI brightness range for the monitor at handle index.
    /// Queries the monitor once and caches the result. Callers must hold _cacheLock.
    /// </summary>
    private static bool TryGetRange(int index, IntPtr hPhysicalMonitor, out uint min, out uint max)
    {
        var ranges = _cachedRanges;
        if (ranges != null && index >= 0 && index < ranges.Length && ranges[index] is { } r)
        {
            min = r.Min;
            max = r.Max;
            return true;
        }

        if (GetMonitorBrightness(hPhysicalMonitor, out min, out _, out max))
        {
            if (ranges != null && index >= 0 && index < ranges.Length)
                ranges[index] = (min, max);
            return true;
        }
        return false;
    }

    private static void EnsureCache()
    {
        if (_cachedMonitors != null) return;

        var monitors = new List<PHYSICAL_MONITOR>();
        var deviceNames = new List<string>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            // Get GDI device name for this HMONITOR
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf<MONITORINFOEX>();
            string gdiName = "";
            if (GetMonitorInfo(hMonitor, ref info))
                gdiName = info.szDevice?.Trim('\0') ?? "";

            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
            {
                var arr = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, arr))
                {
                    foreach (var pm in arr)
                    {
                        monitors.Add(pm);
                        deviceNames.Add(gdiName);
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        _cachedMonitors = monitors;
        _cachedDeviceNames = deviceNames;
        _cachedRanges = new (uint Min, uint Max)?[monitors.Count];
    }

    private static List<PHYSICAL_MONITOR> GetCachedMonitors()
    {
        EnsureCache();
        return _cachedMonitors!;
    }

    private static void InvalidateCacheLocked()
    {
        if (_cachedMonitors == null) return;
        foreach (var m in _cachedMonitors)
        {
            try { DestroyPhysicalMonitor(m.hPhysicalMonitor); } catch { }
        }
        _cachedMonitors = null;
        _cachedDeviceNames = null;
        _cachedRanges = null;
    }

    /// <summary>
    /// Drops cached DDC/CI handles after a display topology or power-state change.
    /// Physical-monitor handles can remain non-zero after a monitor powers off,
    /// but calls through them simply return false until the handles are rebuilt.
    /// </summary>
    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            InvalidateCacheLocked();
        }
    }

    // ── Per-monitor throttled set (last-value-wins, 60ms interval) ──

    // Per-device throttle: keyed by deviceName ("" = all monitors)
    private static readonly Dictionary<string, float> _pendingBrightness = new();
    private static readonly Dictionary<string, bool> _throttleRunning = new();
    private static readonly Dictionary<string, long> _brightnessGeneration = new();
    private static readonly object _throttleLock = new();

    /// <summary>
    /// Stores the brightness value and applies it at 60ms intervals (last-value-wins).
    /// Safe to call on every knob event. Pass deviceName="" or null for all monitors.
    /// </summary>
    public static void SetThrottled(float brightness, string? deviceName = null)
    {
        var key = deviceName ?? "";
        brightness = Math.Clamp(brightness, 0f, 1f);

        lock (_throttleLock)
        {
            _pendingBrightness[key] = brightness;
            _brightnessGeneration[key] = _brightnessGeneration.GetValueOrDefault(key) + 1;
            if (_throttleRunning.TryGetValue(key, out bool running) && running) return;
            _throttleRunning[key] = true;
        }

        _ = Task.Run(() => RunBrightnessThrottleAsync(key));
    }

    private static async Task RunBrightnessThrottleAsync(string key)
    {
        long generation = -1;
        try
        {
            while (true)
            {
                float target;
                lock (_throttleLock)
                {
                    target = _pendingBrightness[key];
                    generation = _brightnessGeneration[key];
                }

                if (string.IsNullOrEmpty(key))
                    ApplyBrightnessAll(target);
                else
                    ApplyBrightnessSingle(key, target);

                await Task.Delay(60);

                lock (_throttleLock)
                {
                    if (_brightnessGeneration[key] != generation)
                        continue;

                    // The decision to stop and publishing the stopped state must
                    // be atomic with SetThrottled. Otherwise a final knob update
                    // can arrive after the decision but before the flag is cleared
                    // and be left with no worker to apply it.
                    _throttleRunning[key] = false;
                    return;
                }
            }
        }
        catch
        {
            // ApplyBrightness* handles normal DDC/CI failures internally. Keep an
            // unexpected worker failure from permanently wedging this monitor's
            // throttle. If a newer value arrived while the worker was failing,
            // hand it directly to a replacement worker under the same lock.
            bool restart;
            lock (_throttleLock)
            {
                restart = generation >= 0 && _brightnessGeneration[key] != generation;
                _throttleRunning[key] = restart;
            }
            if (restart)
                _ = Task.Run(() => RunBrightnessThrottleAsync(key));
        }
    }

    private static void ApplyBrightnessAll(float brightness)
    {
        lock (_cacheLock)
        {
            try
            {
                var monitors = GetCachedMonitors();
                bool anyFailed = false;
                for (int i = 0; i < monitors.Count; i++)
                {
                    var m = monitors[i];
                    try
                    {
                        if (TryGetRange(i, m.hPhysicalMonitor, out uint min, out uint max))
                        {
                            uint val = (uint)(min + (max - min) * brightness);
                            if (!SetMonitorBrightness(m.hPhysicalMonitor, val))
                                anyFailed = true;
                        }
                        else anyFailed = true;
                    }
                    catch { anyFailed = true; }
                }
                if (anyFailed) InvalidateCacheLocked();
            }
            catch { InvalidateCacheLocked(); }
        }
    }

    private static void ApplyBrightnessSingle(string deviceName, float brightness)
    {
        lock (_cacheLock)
        {
            // Retry once with freshly enumerated handles. A display can power
            // back on between knob events, and requiring another movement
            // would otherwise leave the requested value unapplied.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    EnsureCache();
                    for (int i = 0; i < _cachedMonitors!.Count; i++)
                    {
                        if (!string.Equals(_cachedDeviceNames![i], deviceName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var m = _cachedMonitors[i];
                        if (TryGetRange(i, m.hPhysicalMonitor, out uint min, out uint max))
                        {
                            uint val = (uint)(min + (max - min) * brightness);
                            if (SetMonitorBrightness(m.hPhysicalMonitor, val))
                                return;
                        }

                        InvalidateCacheLocked();
                        break;
                    }

                    // The configured GDI display name may have disappeared
                    // while the monitor was powered off. Re-enumerate once.
                    if (_cachedMonitors != null)
                        InvalidateCacheLocked();
                }
                catch { InvalidateCacheLocked(); }
            }
        }
    }

    /// <summary>
    /// Set brightness on all physical monitors immediately (no throttle). Value 0.0 to 1.0.
    /// </summary>
    public static void SetAll(float brightness)
    {
        lock (_cacheLock)
        {
            brightness = Math.Clamp(brightness, 0f, 1f);
            bool anyFailed = false;
            try
            {
                var monitors = GetCachedMonitors();
                for (int i = 0; i < monitors.Count; i++)
                {
                    var m = monitors[i];
                    try
                    {
                        if (TryGetRange(i, m.hPhysicalMonitor, out uint min, out uint max))
                        {
                            uint val = (uint)(min + (max - min) * brightness);
                            if (!SetMonitorBrightness(m.hPhysicalMonitor, val))
                                anyFailed = true;
                        }
                        else anyFailed = true;
                    }
                    catch { anyFailed = true; }
                }
            }
            catch { anyFailed = true; }

            if (anyFailed) InvalidateCacheLocked();
        }
    }

    /// <summary>
    /// Get current brightness of the first physical monitor. Returns 0.0-1.0, or -1 if unavailable.
    /// </summary>
    public static float GetCurrent()
    {
        lock (_cacheLock)
        {
            try
            {
                var monitors = GetCachedMonitors();
                foreach (var m in monitors)
                {
                    try
                    {
                        if (GetMonitorBrightness(m.hPhysicalMonitor, out uint min, out uint cur, out uint max))
                        {
                            if (max <= min) return 0f;
                            return (float)(cur - min) / (max - min);
                        }
                    }
                    catch
                    {
                        InvalidateCacheLocked();
                        return -1f;
                    }
                }
            }
            catch { InvalidateCacheLocked(); }
        }
        return -1f;
    }

    /// <summary>
    /// Get list of physical monitors with their names.
    /// </summary>
    public static List<string> GetMonitorNames()
    {
        lock (_cacheLock)
        {
            var names = new List<string>();
            try
            {
                var monitors = GetCachedMonitors();
                foreach (var m in monitors)
                    names.Add(m.szPhysicalMonitorDescription);
            }
            catch { }
            return names;
        }
    }

    /// <summary>
    /// Get info about all detected physical monitors (friendly names + GDI device names).
    /// Used by UI to populate the monitor sub-flyout picker.
    /// </summary>
    public static List<MonitorInfo> GetMonitorInfos()
    {
        lock (_cacheLock)
        {
            var result = new List<MonitorInfo>();
            try
            {
                EnsureCache();
                var friendlyNames = NativeMethods.GetMonitorFriendlyNames();

                for (int i = 0; i < _cachedMonitors!.Count; i++)
                {
                    var gdiName = _cachedDeviceNames![i];
                    var friendly = "";
                    if (!string.IsNullOrEmpty(gdiName))
                        friendlyNames.TryGetValue(gdiName, out friendly!);

                    // Fallback to DDC/CI description if no friendly name
                    if (string.IsNullOrEmpty(friendly))
                        friendly = _cachedMonitors[i].szPhysicalMonitorDescription;

                    // Last resort: use the GDI device name itself
                    if (string.IsNullOrEmpty(friendly))
                        friendly = gdiName;

                    result.Add(new MonitorInfo
                    {
                        DeviceName = gdiName,
                        FriendlyName = friendly ?? $"Monitor {i + 1}",
                        HandleIndex = i
                    });
                }
            }
            catch { }
            return result;
        }
    }

    /// <summary>
    /// Release cached monitor handles. Call on app exit.
    /// </summary>
    public static void Dispose()
    {
        lock (_cacheLock)
        {
            InvalidateCacheLocked();
        }
    }
}
