using System.Runtime.InteropServices;

namespace WolfMixer;

/// <summary>
/// Controls physical monitor brightness via DDC/CI (Monitor Control Command Set).
/// Uses the Windows High-Level Monitor API (dxva2.dll).
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

    private static List<PHYSICAL_MONITOR> GetPhysicalMonitors()
    {
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
        return monitors;
    }

    /// <summary>
    /// Set brightness on all physical monitors. Value 0.0 to 1.0.
    /// </summary>
    public static void SetAll(float brightness)
    {
        var monitors = GetPhysicalMonitors();
        foreach (var m in monitors)
        {
            try
            {
                if (GetMonitorBrightness(m.hPhysicalMonitor, out uint min, out uint cur, out uint max))
                {
                    uint val = (uint)(min + (max - min) * Math.Clamp(brightness, 0f, 1f));
                    SetMonitorBrightness(m.hPhysicalMonitor, val);
                }
            }
            catch { }
            finally
            {
                DestroyPhysicalMonitor(m.hPhysicalMonitor);
            }
        }
    }

    /// <summary>
    /// Get current brightness of the first physical monitor. Returns 0.0-1.0, or -1 if unavailable.
    /// </summary>
    public static float GetCurrent()
    {
        var monitors = GetPhysicalMonitors();
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
            catch { }
            finally
            {
                DestroyPhysicalMonitor(m.hPhysicalMonitor);
            }
        }
        return -1f;
    }

    /// <summary>
    /// Get list of physical monitors with their names.
    /// </summary>
    public static List<string> GetMonitorNames()
    {
        var names = new List<string>();
        var monitors = GetPhysicalMonitors();
        foreach (var m in monitors)
        {
            names.Add(m.szPhysicalMonitorDescription);
            DestroyPhysicalMonitor(m.hPhysicalMonitor);
        }
        return names;
    }
}
