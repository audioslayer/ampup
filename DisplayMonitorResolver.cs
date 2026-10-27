namespace AmpUp;

internal sealed class DisplayMonitorInfo
{
    public int Index { get; init; }
    public string DeviceName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string DevicePath { get; init; } = "";
    public bool IsPrimary { get; init; }

    public string DisplayName => !string.IsNullOrWhiteSpace(FriendlyName) ? FriendlyName : DeviceName;
    public string Label => IsPrimary ? $"{DisplayName} (Primary)" : DisplayName;
}

internal static class DisplayMonitorResolver
{
    public static List<DisplayMonitorInfo> GetMonitors()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var displayConfig = NativeMethods.GetMonitorDisplayConfigInfo();
        var monitors = new List<DisplayMonitorInfo>(screens.Length);

        for (int i = 0; i < screens.Length; i++)
        {
            displayConfig.TryGetValue(screens[i].DeviceName, out var info);
            monitors.Add(new DisplayMonitorInfo
            {
                Index = i,
                DeviceName = screens[i].DeviceName,
                FriendlyName = info?.FriendlyName ?? "",
                DevicePath = info?.DevicePath ?? "",
                IsPrimary = screens[i].Primary,
            });
        }

        return monitors;
    }

    public static int ResolveOsdMonitorIndex(OsdConfig osd)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
            return 0;

        var match = FindByIdentity(monitors, osd.MonitorDevicePath, m => m.DevicePath)
            ?? FindByIdentity(monitors, osd.MonitorFriendlyName, m => m.FriendlyName)
            ?? FindByIdentity(monitors, osd.MonitorDeviceName, m => m.DeviceName);

        if (match != null)
            return match.Index;

        if (osd.MonitorIndex >= 0 && osd.MonitorIndex < monitors.Count)
            return osd.MonitorIndex;

        return monitors.FirstOrDefault(m => m.IsPrimary)?.Index ?? 0;
    }

    public static void RememberOsdMonitor(OsdConfig osd, int monitorIndex)
    {
        var monitors = GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Index == monitorIndex);
        if (monitor == null)
            return;

        osd.MonitorIndex = monitor.Index;
        osd.MonitorDeviceName = monitor.DeviceName;
        osd.MonitorFriendlyName = monitor.FriendlyName;
        osd.MonitorDevicePath = monitor.DevicePath;
    }

    public static int ResolveScreenSyncMonitorIndex(ScreenSyncConfig cfg)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
            return 0;

        var match = FindByIdentity(monitors, cfg.MonitorDevicePath, m => m.DevicePath)
            ?? FindByIdentity(monitors, cfg.MonitorFriendlyName, m => m.FriendlyName)
            ?? FindByIdentity(monitors, cfg.MonitorDeviceName, m => m.DeviceName);

        if (match != null)
            return match.Index;

        if (cfg.MonitorIndex >= 0 && cfg.MonitorIndex < monitors.Count)
            return cfg.MonitorIndex;

        return monitors.FirstOrDefault(m => m.IsPrimary)?.Index ?? 0;
    }

    public static void RememberScreenSyncMonitor(ScreenSyncConfig cfg, int monitorIndex)
    {
        var monitor = GetMonitors().FirstOrDefault(m => m.Index == monitorIndex);
        if (monitor == null)
            return;

        cfg.MonitorIndex = monitor.Index;
        cfg.MonitorDeviceName = monitor.DeviceName;
        cfg.MonitorFriendlyName = monitor.FriendlyName;
        cfg.MonitorDevicePath = monitor.DevicePath;
    }

    private static DisplayMonitorInfo? FindByIdentity(
        List<DisplayMonitorInfo> monitors,
        string? storedValue,
        Func<DisplayMonitorInfo, string> selector)
    {
        if (string.IsNullOrWhiteSpace(storedValue))
            return null;

        return monitors.FirstOrDefault(m =>
            string.Equals(selector(m), storedValue, StringComparison.OrdinalIgnoreCase));
    }
}
