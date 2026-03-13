using System.Net.Sockets;
using System.Text;

namespace AmpUp;

/// <summary>
/// DreamView / Screen Sync orchestrator.
/// Captures screen zones via GDI, averages colors per zone, and pushes them
/// to configured Govee LAN devices via UDP at up to 60fps.
///
/// Architecture:
///   - One background thread runs the capture+send loop
///   - ScreenCapture handles GDI BitBlt + zone sampling
///   - A persistent UdpClient per device avoids per-frame socket allocation
///   - Delta threshold suppresses sends when color hasn't changed enough
/// </summary>
public class DreamSyncController : IDisposable
{
    private ScreenSyncConfig _config;
    private AmbienceConfig _ambience;
    private readonly ScreenCapture _capture = new();
    private readonly object _lock = new();
    private bool _disposed;

    // Running state
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _running;

    // Per-device persistent UDP sockets (avoids allocation per frame)
    private readonly Dictionary<string, UdpClient> _udpClients = new();
    private const int LanControlPort = 4003;

    // Per-device last-sent color for delta suppression
    private readonly Dictionary<string, (byte R, byte G, byte B)> _lastSent = new();

    // Raised each frame with current zone colors — used by the live preview in AmbienceView
    public event Action<(byte R, byte G, byte B)[]>? OnZoneColors;

    // Status text for UI display (e.g. "Syncing at 30fps" / "Stopped")
    public string Status { get; private set; } = "Stopped";

    public bool IsRunning => _running;

    public DreamSyncController(ScreenSyncConfig config, AmbienceConfig ambience)
    {
        _config = config;
        _ambience = ambience;
    }

    public void UpdateConfig(ScreenSyncConfig config, AmbienceConfig ambience)
    {
        lock (_lock)
        {
            _config = config;
            _ambience = ambience;
        }

        // If enabled state changed, start/stop accordingly
        if (config.Enabled && !_running)
            Start();
        else if (!config.Enabled && _running)
            Stop();
    }

    // ── Start / Stop ─────────────────────────────────────────────────────────

    public void Start()
    {
        if (_disposed || _running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoop(_cts.Token));
        Logger.Log("DreamSync: started");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _cts?.Cancel();
        try { _loopTask?.Wait(2000); } catch { }
        _loopTask = null;
        _cts?.Dispose();
        _cts = null;
        Status = "Stopped";
        Logger.Log("DreamSync: stopped");
    }

    // ── Capture loop ─────────────────────────────────────────────────────────

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScreenSyncConfig cfg;
            AmbienceConfig amb;
            lock (_lock) { cfg = _config; amb = _ambience; }

            int fps = cfg.TargetFps switch { 60 => 60, 15 => 15, _ => 30 };
            int delayMs = 1000 / fps;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Capture all zones from the configured monitor
                var zones = _capture.CaptureZones(cfg.MonitorIndex, cfg.ZoneCount);
                if (zones != null)
                {
                    // Boost saturation on each zone
                    for (int i = 0; i < zones.Length; i++)
                    {
                        var (r, g, b) = zones[i];
                        zones[i] = ScreenCapture.BoostSaturation(r, g, b, cfg.Saturation);
                    }

                    // Notify UI for live preview (fire and forget — UI marshal on receipt)
                    OnZoneColors?.Invoke(zones);

                    // Push to each configured Govee device
                    if (amb.GoveeEnabled && amb.GoveeDevices.Count > 0)
                    {
                        foreach (var mapping in cfg.DeviceMappings)
                        {
                            if (string.IsNullOrWhiteSpace(mapping.DeviceIp)) continue;

                            // Find the device in ambience config — must be enabled (not "off")
                            var dev = amb.GoveeDevices.FirstOrDefault(d => d.Ip == mapping.DeviceIp);
                            if (dev == null) continue;

                            // Sample the correct zone color for this device's side
                            var color = SampleColorForSide(zones, cfg.ZoneCount, mapping.Side);

                            // Apply brightness scale from ambience config
                            color = ApplyBrightness(color, amb.BrightnessScale);

                            // Delta check — only send if color changed enough
                            int sensitivity = cfg.Sensitivity; // 1-20
                            if (_lastSent.TryGetValue(mapping.DeviceIp, out var prev))
                            {
                                int dr = Math.Abs(color.R - prev.R);
                                int dg = Math.Abs(color.G - prev.G);
                                int db = Math.Abs(color.B - prev.B);
                                if (dr <= sensitivity && dg <= sensitivity && db <= sensitivity)
                                    continue;
                            }

                            _lastSent[mapping.DeviceIp] = color;
                            SendColorFast(mapping.DeviceIp, color.R, color.G, color.B);
                        }
                    }

                    Status = $"Syncing at {fps}fps";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DreamSync loop error: {ex.Message}");
            }

            // Sleep for the remainder of the frame interval
            int elapsed = (int)sw.ElapsedMilliseconds;
            int remaining = delayMs - elapsed;
            if (remaining > 1)
            {
                try { await Task.Delay(remaining, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        Status = "Stopped";
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Map a ZoneSide to an averaged color from the zone array.
    /// Left = average of left half of zones, Right = right half, Full = all zones.
    /// </summary>
    private static (byte R, byte G, byte B) SampleColorForSide(
        (byte R, byte G, byte B)[] zones, int zoneCount, ZoneSide side)
    {
        int start = 0, end = zoneCount;
        switch (side)
        {
            case ZoneSide.Left:
                end = zoneCount / 2;
                break;
            case ZoneSide.Right:
                start = zoneCount / 2;
                break;
            case ZoneSide.Top:
                end = zoneCount / 2;
                break;
            case ZoneSide.Bottom:
                start = zoneCount / 2;
                break;
            // Full: use all zones
        }

        end = Math.Max(end, start + 1); // guard against 0-length
        int r = 0, g = 0, b = 0, count = 0;
        for (int i = start; i < end && i < zones.Length; i++)
        {
            r += zones[i].R;
            g += zones[i].G;
            b += zones[i].B;
            count++;
        }
        if (count == 0) return (0, 0, 0);
        return ((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static (byte R, byte G, byte B) ApplyBrightness((byte R, byte G, byte B) c, int scale)
    {
        return (
            (byte)(c.R * scale / 100),
            (byte)(c.G * scale / 100),
            (byte)(c.B * scale / 100)
        );
    }

    // ── UDP send (persistent socket, fire-and-forget) ─────────────────────────

    private void SendColorFast(string ip, byte r, byte g, byte b)
    {
        // Build JSON payload
        string json = $"{{\"msg\":{{\"cmd\":\"colorwc\",\"data\":{{\"color\":{{\"r\":{r},\"g\":{g},\"b\":{b}}},\"colorTemInKelvin\":0}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        // Get or create a persistent UDP socket for this device
        if (!_udpClients.TryGetValue(ip, out var udp))
        {
            udp = new UdpClient();
            _udpClients[ip] = udp;
        }

        // Fire and forget — don't await; capture loop shouldn't block on network I/O
        _ = udp.SendAsync(data, data.Length, ip, LanControlPort);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _capture.Dispose();
        foreach (var udp in _udpClients.Values)
        {
            try { udp.Dispose(); } catch { }
        }
        _udpClients.Clear();
    }
}
