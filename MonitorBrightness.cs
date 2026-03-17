using System.Runtime.InteropServices;

namespace AmpUp;

/// <summary>
/// Controls physical monitor brightness via DDC/CI (Monitor Control Command Set).
/// Uses the Windows High-Level Monitor API (dxva2.dll).
/// Caches physical monitor handles; auto-invalidates on failure.
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    // ── Handle cache ────────────────────────────────────────────────

    private static readonly object _cacheLock = new();
    private static List<PHYSICAL_MONITOR>? _cachedMonitors;

    private static List<PHYSICAL_MONITOR> GetCachedMonitors()
    {
        if (_cachedMonitors != null) return _cachedMonitors;

        var monitors = new List<PHYSICAL_MONITOR>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
            {
                var arr = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, arr))
                    monitors.AddRange(arr);
            }
            return true;
        }, IntPtr.Zero);

        _cachedMonitors = monitors;
        return monitors;
    }

    private static void InvalidateCache()
    {
        if (_cachedMonitors == null) return;
        foreach (var m in _cachedMonitors)
        {
            try { DestroyPhysicalMonitor(m.hPhysicalMonitor); } catch { }
        }
        _cachedMonitors = null;
    }

    // ── Throttled set (last-value-wins, 60ms interval) ──────────────

    private static float _pendingBrightness = -1f;
    private static bool _throttleRunning;
    private static readonly object _throttleLock = new();

    /// <summary>
    /// Stores the brightness value and applies it at 60ms intervals (last-value-wins).
    /// Safe to call on every knob event.
    /// </summary>
    public static void SetThrottled(float brightness)
    {
        _pendingBrightness = Math.Clamp(brightness, 0f, 1f);

        lock (_throttleLock)
        {
            if (_throttleRunning) return;
            _throttleRunning = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    float target = _pendingBrightness;
                    ApplyBrightness(target);

                    await Task.Delay(60);

                    // If value didn't change while we were applying, we're done
                    if (Math.Abs(_pendingBrightness - target) < 0.001f)
                        break;
                }
            }
            finally
            {
                lock (_throttleLock) { _throttleRunning = false; }
            }
        });
    }

    private static void ApplyBrightness(float brightness)
    {
        lock (_cacheLock)
        {
            try
            {
                var monitors = GetCachedMonitors();
                bool anyFailed = false;
                foreach (var m in monitors)
                {
                    try
                    {
                        if (GetMonitorBrightness(m.hPhysicalMonitor, out uint min, out _, out uint max))
                        {
                            uint val = (uint)(min + (max - min) * brightness);
                            SetMonitorBrightness(m.hPhysicalMonitor, val);
                        }
                        else
                        {
                            anyFailed = true;
                        }
                    }
                    catch
                    {
                        anyFailed = true;
                    }
                }
                if (anyFailed)
                    InvalidateCache();
            }
            catch
            {
                InvalidateCache();
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
                foreach (var m in monitors)
                {
                    try
                    {
                        if (GetMonitorBrightness(m.hPhysicalMonitor, out uint min, out _, out uint max))
                        {
                            uint val = (uint)(min + (max - min) * brightness);
                            SetMonitorBrightness(m.hPhysicalMonitor, val);
                        }
                        else anyFailed = true;
                    }
                    catch { anyFailed = true; }
                }
            }
            catch { anyFailed = true; }

            if (anyFailed) InvalidateCache();
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
                        InvalidateCache();
                        return -1f;
                    }
                }
            }
            catch { InvalidateCache(); }
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
    /// Release cached monitor handles. Call on app exit.
    /// </summary>
    public static void Dispose()
    {
        lock (_cacheLock)
        {
            InvalidateCache();
        }
    }
}
