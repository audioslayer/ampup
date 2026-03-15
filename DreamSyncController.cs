using System.Net.Sockets;
using System.Text;

namespace AmpUp;

/// <summary>
/// DreamView / Screen Sync orchestrator.
/// Captures screen zones via GDI, averages colors per zone, and pushes them
/// to configured Govee LAN devices via UDP. Capture runs at user-selected fps
/// (15/30/60) for smooth color averaging, but UDP sends are capped at 30fps
/// (Govee LED hardware can't transition faster — sending more causes flicker).
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

    // Per-device last-sent color for delta suppression (single-color fallback)
    private readonly Dictionary<string, (byte R, byte G, byte B)> _lastSent = new();

    // Per-segment protocol state
    private readonly HashSet<string> _segmentEnabled = new();        // devices with segment mode active
    private readonly Dictionary<string, long> _segmentEnableTick = new(); // keepalive timer
    private readonly Dictionary<string, (byte R, byte G, byte B)[]> _lastSegmentColors = new();
    private const long SegmentKeepaliveMs = 30_000; // re-send enable every 30s (device times out ~60s)

    // Cap UDP send rate at 30fps (hardware can't transition faster — sending more causes flicker)
    private const int MaxSendFps = 30;
    private readonly System.Diagnostics.Stopwatch _sendThrottle = System.Diagnostics.Stopwatch.StartNew();

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
        DisableAllSegments();
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

                    // Push to each configured Govee device (capped at 30fps — hardware limit)
                    bool sendThisFrame = _sendThrottle.ElapsedMilliseconds >= (1000 / MaxSendFps);
                    if (sendThisFrame && amb.GoveeEnabled && amb.GoveeDevices.Count > 0)
                    {
                        foreach (var mapping in cfg.DeviceMappings)
                        {
                            if (string.IsNullOrWhiteSpace(mapping.DeviceIp)) continue;

                            var dev = amb.GoveeDevices.FirstOrDefault(d => d.Ip == mapping.DeviceIp);
                            if (dev == null) continue;

                            int segCount = AmbienceSync.GetSegmentCount(dev.Sku);
                            bool useSegments = segCount > 0 && dev.UseSegmentProtocol;

                            if (useSegments)
                            {
                                // ── Per-segment path ──
                                // Enable segment mode if not yet active or keepalive expired
                                long nowMs = Environment.TickCount64;
                                if (!_segmentEnabled.Contains(mapping.DeviceIp) ||
                                    nowMs - (_segmentEnableTick.GetValueOrDefault(mapping.DeviceIp)) > SegmentKeepaliveMs)
                                {
                                    SendSegmentEnable(mapping.DeviceIp, true);
                                    _segmentEnabled.Add(mapping.DeviceIp);
                                    _segmentEnableTick[mapping.DeviceIp] = nowMs;
                                }

                                // Map screen zones to device segments
                                var segColors = MapZonesToSegments(zones, segCount);
                                for (int s = 0; s < segColors.Length; s++)
                                    segColors[s] = ApplyBrightness(segColors[s], amb.BrightnessScale);

                                // Delta check across all segments
                                if (SegmentColorsChanged(mapping.DeviceIp, segColors, cfg.Sensitivity))
                                {
                                    _lastSegmentColors[mapping.DeviceIp] = segColors;
                                    SendSegmentColors(mapping.DeviceIp, segColors);
                                }
                            }
                            else
                            {
                                // ── Single-color fallback (colorwc) ──
                                var color = SampleColorForSide(zones, cfg.ZoneCount, mapping.Side);
                                color = ApplyBrightness(color, amb.BrightnessScale);

                                int sensitivity = cfg.Sensitivity;
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
                    }

                    if (sendThisFrame)
                        _sendThrottle.Restart();
                    Status = $"Syncing at {fps}fps (send ≤{MaxSendFps}fps)";
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

    // ── Per-segment protocol (Govee "razer" command) ─────────────────────────

    private static byte XorChecksum(byte[] data, int length)
    {
        byte xor = 0;
        for (int i = 0; i < length; i++) xor ^= data[i];
        return xor;
    }

    private void SendSegmentEnable(string ip, bool enable)
    {
        // Binary: BB 00 01 B1 [01=enable/00=disable] [xor]
        var pkt = new byte[] { 0xBB, 0x00, 0x01, 0xB1, (byte)(enable ? 1 : 0), 0 };
        pkt[5] = XorChecksum(pkt, 5);

        string b64 = Convert.ToBase64String(pkt);
        string json = $"{{\"msg\":{{\"cmd\":\"razer\",\"data\":{{\"pt\":\"{b64}\"}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        if (!_udpClients.TryGetValue(ip, out var udp))
        {
            udp = new UdpClient();
            _udpClients[ip] = udp;
        }
        _ = udp.SendAsync(data, data.Length, ip, LanControlPort);
        Logger.Log($"DreamSync: segment {(enable ? "enabled" : "disabled")} for {ip}");
    }

    private void SendSegmentColors(string ip, (byte R, byte G, byte B)[] colors)
    {
        // Binary: BB [len_hi] [len_lo] B0 00 [count] [R G B]... [xor]
        int count = colors.Length;
        int payloadLen = 2 + 1 + count * 3; // B0 00 + count + RGB data
        int totalLen = 3 + payloadLen + 1;   // header(3) + payload + checksum
        var pkt = new byte[totalLen];

        pkt[0] = 0xBB;
        pkt[1] = (byte)(payloadLen >> 8);
        pkt[2] = (byte)(payloadLen & 0xFF);
        pkt[3] = 0xB0;
        pkt[4] = 0x00;
        pkt[5] = (byte)count;

        for (int i = 0; i < count; i++)
        {
            pkt[6 + i * 3] = colors[i].R;
            pkt[7 + i * 3] = colors[i].G;
            pkt[8 + i * 3] = colors[i].B;
        }
        pkt[totalLen - 1] = XorChecksum(pkt, totalLen - 1);

        string b64 = Convert.ToBase64String(pkt);
        string json = $"{{\"msg\":{{\"cmd\":\"razer\",\"data\":{{\"pt\":\"{b64}\"}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        if (!_udpClients.TryGetValue(ip, out var udp))
        {
            udp = new UdpClient();
            _udpClients[ip] = udp;
        }
        _ = udp.SendAsync(data, data.Length, ip, LanControlPort);
    }

    /// <summary>
    /// Map N screen zones to M device segments by proportional grouping.
    /// </summary>
    private static (byte R, byte G, byte B)[] MapZonesToSegments(
        (byte R, byte G, byte B)[] zones, int segmentCount)
    {
        var result = new (byte R, byte G, byte B)[segmentCount];
        int zoneCount = zones.Length;

        for (int seg = 0; seg < segmentCount; seg++)
        {
            // Proportional mapping: which zones contribute to this segment
            float start = (float)seg / segmentCount * zoneCount;
            float end = (float)(seg + 1) / segmentCount * zoneCount;

            int r = 0, g = 0, b = 0, count = 0;
            for (int z = (int)start; z < (int)Math.Ceiling(end) && z < zoneCount; z++)
            {
                r += zones[z].R;
                g += zones[z].G;
                b += zones[z].B;
                count++;
            }
            if (count > 0)
                result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
        return result;
    }

    private bool SegmentColorsChanged(string ip, (byte R, byte G, byte B)[] colors, int sensitivity)
    {
        if (!_lastSegmentColors.TryGetValue(ip, out var prev) || prev.Length != colors.Length)
            return true;
        for (int i = 0; i < colors.Length; i++)
        {
            if (Math.Abs(colors[i].R - prev[i].R) > sensitivity ||
                Math.Abs(colors[i].G - prev[i].G) > sensitivity ||
                Math.Abs(colors[i].B - prev[i].B) > sensitivity)
                return true;
        }
        return false;
    }

    private void DisableAllSegments()
    {
        foreach (var ip in _segmentEnabled)
        {
            try { SendSegmentEnable(ip, false); } catch { }
        }
        _segmentEnabled.Clear();
        _segmentEnableTick.Clear();
        _lastSegmentColors.Clear();
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
