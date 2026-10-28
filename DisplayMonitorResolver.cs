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

        var match = FindMonitor(monitors, osd.MonitorDevicePath, osd.MonitorFriendlyName, osd.MonitorDeviceName);

        if (match != null)
        {
            if (string.IsNullOrWhiteSpace(osd.MonitorDevicePath) && MatchedByUniqueName(monitors, match, osd.MonitorFriendlyName))
                osd.MonitorDevicePath = match.DevicePath; // older config: pin it to the hardware path
            return match.Index;
        }

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

        var match = FindMonitor(monitors, cfg.MonitorDevicePath, cfg.MonitorFriendlyName, cfg.MonitorDeviceName);

        if (match != null)
        {
            if (string.IsNullOrWhiteSpace(cfg.MonitorDevicePath) && MatchedByUniqueName(monitors, match, cfg.MonitorFriendlyName))
                cfg.MonitorDevicePath = match.DevicePath; // older config: pin it to the hardware path
            return match.Index;
        }

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

    /// <summary>
    /// The hardware path survives power cycles and tells identical monitors apart. A friendly
    /// name only counts when one screen has it (two of the same model share it). GDI names
    /// (\\.\DISPLAY1) get renumbered when screens power off and on, so they're only trusted
    /// for configs saved before the hardware path was recorded (issue #29).
    /// </summary>
    private static DisplayMonitorInfo? FindMonitor(
        List<DisplayMonitorInfo> monitors, string? devicePath, string? friendlyName, string? deviceName)
    {
        var match = FindByIdentity(monitors, devicePath, m => m.DevicePath);
        if (match != null)
            return match;

        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            var named = monitors.Where(m => string.Equals(m.FriendlyName, friendlyName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count == 1)
                return named[0];
        }

        return string.IsNullOrWhiteSpace(devicePath)
            ? FindByIdentity(monitors, deviceName, m => m.DeviceName)
            : null;
    }

    /// <summary>True when <paramref name="monitor"/> was found by a saved friendly name no other
    /// screen shares, so its hardware path can safely be saved in place of the old identity.</summary>
    private static bool MatchedByUniqueName(List<DisplayMonitorInfo> monitors, DisplayMonitorInfo monitor, string? savedFriendlyName) =>
        !string.IsNullOrWhiteSpace(monitor.DevicePath)
        && string.Equals(monitor.FriendlyName, savedFriendlyName, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(monitor.FriendlyName)
        && monitors.Count(m => string.Equals(m.FriendlyName, monitor.FriendlyName, StringComparison.OrdinalIgnoreCase)) == 1;

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
