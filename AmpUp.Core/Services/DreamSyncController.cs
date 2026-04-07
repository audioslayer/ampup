using System.Net.Sockets;
using System.Text;
using AmpUp.Core;
using AmpUp.Core.Models;
using AmpUp.Core.Interfaces;
using AmpUp.Core.Engine;

namespace AmpUp.Core.Services;

/// <summary>
/// DreamView / Screen Sync orchestrator.
/// Captures screen zones via an injected IScreenCapture, averages colors per zone, and pushes them
/// to configured Govee LAN devices via UDP. Capture runs at user-selected fps
/// (15/30/60) for smooth color averaging, but UDP sends are capped at 30fps
/// (Govee LED hardware can't transition faster — sending more causes flicker).
///
/// Architecture:
///   - One background thread runs the capture+send loop
///   - IScreenCapture handles platform-specific screen capture + zone sampling
///   - A persistent UdpClient per device avoids per-frame socket allocation
///   - Delta threshold suppresses sends when color hasn't changed enough
/// </summary>
public class DreamSyncController : IDisposable
{
    private ScreenSyncConfig _config;
    private AmbienceConfig _ambience;
    private readonly IScreenCapture _capture;
    private readonly object _lock = new();
    private bool _disposed;
    private ScreenSpatialMapper? _spatialMapper;

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

    // Raised each frame with current zone colors — used by the live preview in RoomView
    public event Action<(byte R, byte G, byte B)[]>? OnZoneColors;

    // Raised each frame with the 2D zone grid — used by ScreenEdgeControl for spatial preview
    public event Action<(byte R, byte G, byte B)[,], int, int>? OnZoneGrid;

    // Status text for UI display (e.g. "Syncing at 30fps" / "Stopped")
    public string Status { get; private set; } = "Stopped";

    public bool IsRunning => _running;

    public DreamSyncController(ScreenSyncConfig config, AmbienceConfig ambience, IScreenCapture capture)
    {
        _config = config;
        _ambience = ambience;
        _capture = capture;
    }

    public void UpdateConfig(ScreenSyncConfig config, AmbienceConfig ambience)
    {
        bool wasEnabled;
        lock (_lock)
        {
            wasEnabled = _config.Enabled;
            _config = config;
            _ambience = ambience;
        }

        // Only start/stop on actual Enabled state transitions — not every config save
        if (config.Enabled && !wasEnabled && !_running)
            Start();
        else if (!config.Enabled && wasEnabled && _running)
            Stop();
    }

    public void SetSpatialMapper(ScreenSpatialMapper? mapper)
    {
        lock (_lock) { _spatialMapper = mapper; }
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
        // Don't disable segments here — room effects may be using them.
        // Segments auto-timeout on the device after ~60s without keepalive.
        // Clear our tracking so they get re-enabled if DreamSync restarts.
        _segmentEnabled.Clear();
        _segmentEnableTick.Clear();
        _lastSegmentColors.Clear();
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
                // Determine content crop bounds
                ContentBounds? contentCrop = cfg.CropBlackBars ? cfg.ContentBounds : null;

                // Try 2D grid capture first (preferred — enables spatial mapping)
                int gridCols = cfg.ZoneCount;
                int gridRows = 3; // top / middle / bottom
                var grid = _capture.CaptureZoneGrid(cfg.MonitorIndex, gridCols, gridRows, contentCrop);

                // Flat zone array (for legacy path + UI preview)
                (byte R, byte G, byte B)[]? zones = null;

                if (grid != null)
                {
                    // Flatten 2D grid to 1D for backward compat (average rows per column)
                    zones = FlattenGridToHorizontal(grid, gridCols, gridRows);
                }
                else
                {
                    // Mac fallback: use legacy 1D capture
                    zones = _capture.CaptureZones(cfg.MonitorIndex, cfg.ZoneCount, cfg.CropBlackBars);
                }

                if (zones != null)
                {
                    // Boost saturation on flat zones (used for UI preview + legacy path)
                    for (int i = 0; i < zones.Length; i++)
                    {
                        var (r, g, b) = zones[i];
                        zones[i] = BoostSaturation(r, g, b, cfg.Saturation);
                    }

                    // Also boost saturation on the 2D grid if available
                    if (grid != null)
                    {
                        for (int row = 0; row < gridRows; row++)
                            for (int col = 0; col < gridCols; col++)
                            {
                                var (r, g, b) = grid[row, col];
                                grid[row, col] = BoostSaturation(r, g, b, cfg.Saturation);
                            }
                    }

                    // Notify UI for live preview (fire and forget — UI marshal on receipt)
                    OnZoneColors?.Invoke(zones);
                    if (grid != null)
                        OnZoneGrid?.Invoke(grid, gridCols, gridRows);

                    // Snapshot spatial mapper under lock
                    ScreenSpatialMapper? spatialMapper;
                    lock (_lock) { spatialMapper = _spatialMapper; }

                    // Check if any device needs a FullScreen capture (no crop)
                    bool needFullScreen = false;
                    foreach (var mapping in cfg.DeviceMappings)
                    {
                        if (mapping.CropMode == DeviceCropMode.FullScreen && !mapping.UseAutoSpatial)
                        {
                            needFullScreen = true;
                            break;
                        }
                    }
                    (byte R, byte G, byte B)[]? fullScreenZones = null;
                    if (needFullScreen)
                        fullScreenZones = _capture.CaptureZones(cfg.MonitorIndex, cfg.ZoneCount, false);

                    // Push to each configured Govee device (capped at 30fps — hardware limit)
                    bool sendThisFrame = _sendThrottle.ElapsedMilliseconds >= (1000 / MaxSendFps);
                    if (sendThisFrame && amb.GoveeEnabled && amb.GoveeDevices.Count > 0)
                    {
                        foreach (var mapping in cfg.DeviceMappings)
                        {
                            if (string.IsNullOrWhiteSpace(mapping.DeviceIp)) continue;

                            var dev = amb.GoveeDevices.FirstOrDefault(d => d.Ip == mapping.DeviceIp);
                            if (dev == null || !dev.PoweredOn) continue;

                            int segCount = AmbienceSync.GetSegmentCount(dev);
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

                                // Map screen zones to device segments (side-aware for edge glow)
                                (byte R, byte G, byte B)[] segColors;

                                if (mapping.UseAutoSpatial && spatialMapper != null && grid != null)
                                {
                                    // ── Spatial mapping path (2D grid) ──
                                    string deviceKey = dev.Ip ?? mapping.DeviceIp;
                                    var region = spatialMapper.GetRegion(deviceKey);
                                    if (region.HasValue)
                                    {
                                        segColors = MapZonesToSegmentsSpatial(grid, gridCols, gridRows, region.Value, segCount);
                                    }
                                    else
                                    {
                                        // Fallback if device not in spatial layout
                                        segColors = MapZonesToSegments(zones, segCount, mapping.Side);
                                    }
                                }
                                else if (AmbienceSync.IsPairedDevice(dev.Sku))
                                {
                                    // Paired device (legacy): first half = right screen edge, second half = left
                                    // (H610A wiring: segments 0-5 = right panel, 6-11 = left panel)
                                    int half = segCount / 2;
                                    var effectiveZones = (mapping.CropMode == DeviceCropMode.FullScreen && fullScreenZones != null)
                                        ? fullScreenZones : zones;
                                    var leftColors = MapZonesToSegments(effectiveZones, half, ZoneSide.Right);
                                    var rightColors = MapZonesToSegments(effectiveZones, segCount - half, ZoneSide.Left);
                                    segColors = new (byte R, byte G, byte B)[segCount];
                                    // Reverse first panel to match physical orientation
                                    for (int si = 0; si < half; si++)
                                        segColors[si] = leftColors[half - 1 - si];
                                    Array.Copy(rightColors, 0, segColors, half, segCount - half);
                                }
                                else
                                {
                                    var effectiveZones = (mapping.CropMode == DeviceCropMode.FullScreen && fullScreenZones != null)
                                        ? fullScreenZones : zones;
                                    segColors = MapZonesToSegments(effectiveZones, segCount, mapping.Side);
                                }

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
                                var effectiveZones = (mapping.CropMode == DeviceCropMode.FullScreen && fullScreenZones != null)
                                    ? fullScreenZones : zones;
                                var color = SampleColorForSide(effectiveZones, effectiveZones.Length, mapping.Side);
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

    // ── Grid helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Flatten a 2D zone grid to a 1D horizontal array by averaging rows per column.
    /// Used for backward compatibility with OnZoneColors event and legacy mapping path.
    /// </summary>
    private static (byte R, byte G, byte B)[] FlattenGridToHorizontal(
        (byte R, byte G, byte B)[,] grid, int cols, int rows)
    {
        var result = new (byte R, byte G, byte B)[cols];
        for (int c = 0; c < cols; c++)
        {
            int r = 0, g = 0, b = 0;
            for (int row = 0; row < rows; row++)
            {
                r += grid[row, c].R;
                g += grid[row, c].G;
                b += grid[row, c].B;
            }
            result[c] = ((byte)(r / rows), (byte)(g / rows), (byte)(b / rows));
        }
        return result;
    }

    /// <summary>
    /// Map a 2D zone grid to device segments using a spatial screen region.
    /// Vertical edge devices (Left/Right): segments map top→bottom across rows.
    /// Horizontal edge devices (Top/Bottom): segments map left→right across cols.
    /// Full: segments spread left→right, averaging all rows in range.
    /// </summary>
    private static (byte R, byte G, byte B)[] MapZonesToSegmentsSpatial(
        (byte R, byte G, byte B)[,] grid, int cols, int rows,
        ScreenSpatialMapper.ScreenRegion region, int segmentCount)
    {
        var result = new (byte R, byte G, byte B)[segmentCount];

        // Convert normalized region bounds to grid indices (clamped)
        int colStart = Math.Clamp((int)(region.XStart * cols), 0, cols - 1);
        int colEnd = Math.Clamp((int)Math.Ceiling(region.XEnd * cols), 1, cols);
        int rowStart = Math.Clamp((int)(region.YStart * rows), 0, rows - 1);
        int rowEnd = Math.Clamp((int)Math.Ceiling(region.YEnd * rows), 1, rows);

        int colRange = Math.Max(colEnd - colStart, 1);
        int rowRange = Math.Max(rowEnd - rowStart, 1);

        bool vertical = region.PrimaryEdge == ZoneSide.Left || region.PrimaryEdge == ZoneSide.Right;

        if (vertical)
        {
            // Segments map top→bottom across rows, averaging cols in range
            for (int seg = 0; seg < segmentCount; seg++)
            {
                float segRowStart = rowStart + (float)seg / segmentCount * rowRange;
                float segRowEnd = rowStart + (float)(seg + 1) / segmentCount * rowRange;

                int r = 0, g = 0, b = 0, count = 0;
                for (int row = (int)segRowStart; row < (int)Math.Ceiling(segRowEnd) && row < rowEnd; row++)
                {
                    for (int col = colStart; col < colEnd; col++)
                    {
                        r += grid[row, col].R;
                        g += grid[row, col].G;
                        b += grid[row, col].B;
                        count++;
                    }
                }
                if (count > 0)
                    result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
        }
        else
        {
            // Horizontal (Top/Bottom/Full): segments map left→right across cols, averaging rows in range
            for (int seg = 0; seg < segmentCount; seg++)
            {
                float segColStart = colStart + (float)seg / segmentCount * colRange;
                float segColEnd = colStart + (float)(seg + 1) / segmentCount * colRange;

                int r = 0, g = 0, b = 0, count = 0;
                for (int col = (int)segColStart; col < (int)Math.Ceiling(segColEnd) && col < colEnd; col++)
                {
                    for (int row = rowStart; row < rowEnd; row++)
                    {
                        r += grid[row, col].R;
                        g += grid[row, col].G;
                        b += grid[row, col].B;
                        count++;
                    }
                }
                if (count > 0)
                    result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
        }

        return result;
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

    /// <summary>
    /// Apply HSV saturation multiplier to an RGB color. Clamps to 0-255.
    /// </summary>
    private static (byte R, byte G, byte B) BoostSaturation(byte r, byte g, byte b, float saturation)
    {
        if (Math.Abs(saturation - 1.0f) < 0.01f) return (r, g, b);

        // RGB → HSV
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        float h = 0, s = 0, v = max;
        if (max > 0) s = delta / max;
        if (delta > 0)
        {
            if (max == rf) h = (gf - bf) / delta % 6;
            else if (max == gf) h = (bf - rf) / delta + 2;
            else h = (rf - gf) / delta + 4;
            h /= 6;
            if (h < 0) h += 1;
        }

        // Boost saturation
        s = Math.Clamp(s * saturation, 0f, 1f);

        // HSV → RGB
        float c = v * s;
        float x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        float m = v - c;

        float ro, go, bo;
        int hi = (int)(h * 6);
        switch (hi % 6)
        {
            case 0: ro = c; go = x; bo = 0; break;
            case 1: ro = x; go = c; bo = 0; break;
            case 2: ro = 0; go = c; bo = x; break;
            case 3: ro = 0; go = x; bo = c; break;
            case 4: ro = x; go = 0; bo = c; break;
            default: ro = c; go = 0; bo = x; break;
        }

        return (
            (byte)Math.Clamp((ro + m) * 255, 0, 255),
            (byte)Math.Clamp((go + m) * 255, 0, 255),
            (byte)Math.Clamp((bo + m) * 255, 0, 255)
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
        (byte R, byte G, byte B)[] zones, int segmentCount, ZoneSide side = ZoneSide.Full)
    {
        var result = new (byte R, byte G, byte B)[segmentCount];
        int zoneCount = zones.Length;

        // Side-aware: only sample from the relevant portion of screen zones
        int zoneStart = 0, zoneEnd = zoneCount;
        switch (side)
        {
            case ZoneSide.Left:
                zoneEnd = Math.Max(zoneCount / 4, 1); // leftmost 25% of screen
                break;
            case ZoneSide.Right:
                zoneStart = zoneCount - Math.Max(zoneCount / 4, 1); // rightmost 25%
                break;
            case ZoneSide.Top:
                zoneEnd = Math.Max(zoneCount / 4, 1);
                break;
            case ZoneSide.Bottom:
                zoneStart = zoneCount - Math.Max(zoneCount / 4, 1);
                break;
        }
        int sideZoneCount = zoneEnd - zoneStart;

        for (int seg = 0; seg < segmentCount; seg++)
        {
            // Proportional mapping within the side's zone range
            float start = zoneStart + (float)seg / segmentCount * sideZoneCount;
            float end = zoneStart + (float)(seg + 1) / segmentCount * sideZoneCount;

            int r = 0, g = 0, b = 0, count = 0;
            for (int z = (int)start; z < (int)Math.Ceiling(end) && z < zoneEnd; z++)
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
