using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AmpUp.Core.Engine;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

/// <summary>
/// Govee LAN UDP sync for Ambience feature.
/// Subscribes to RgbController.OnFrameReady and mirrors device LED colors
/// to Govee LAN lights at 20 FPS.
/// </summary>
public class AmbienceSync : IDisposable
{
    private AmbienceConfig _config;
    private readonly object _lock = new();
    private bool _disposed;

    // Per-device last-sent color for delta throttling
    private readonly ConcurrentDictionary<string, (byte R, byte G, byte B)> _lastSent = new();
    private readonly ConcurrentDictionary<string, byte> _segmentEnabled = new();
    private readonly ConcurrentDictionary<string, long> _segmentKeepAliveTick = new();
    private const long SegmentKeepAliveInterval = TimeSpan.TicksPerSecond * 25; // re-enable every 25s
    private const float RoomEffectColorBoost = 1.55f;

    // Rate limiter: Govee LAN UDP per device
    // Razer binary protocol is lightweight — LedFx defaults to 40 FPS, 20 FPS is conservative and reliable.
    // colorwc JSON is heavier but 10 FPS is well within device capability.
    private readonly ConcurrentDictionary<string, long> _lastSendTick = new();
    private const long MinTicksSegment = TimeSpan.TicksPerMillisecond * 33;   // ~30 FPS for segment protocol (razer binary)
    private const long MinTicksSingle  = TimeSpan.TicksPerMillisecond * 100;  // 10 FPS for colorwc (JSON)

    // Spatial mapper for room layout mode
    private SpatialMapper? _spatialMapper;

    public AmbienceSync(AmbienceConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Set or update the spatial mapper when room layout changes.
    /// </summary>
    public void SetSpatialMapper(SpatialMapper? mapper)
    {
        _spatialMapper = mapper;
    }

    public void UpdateConfig(AmbienceConfig config)
    {
        lock (_lock) _config = config;
    }

    /// <summary>
    /// Called by RgbController.OnFrameReady every 50ms on a timer thread.
    /// byte[45]: knob0LED0 R/G/B, knob0LED1 R/G/B, ..., knob4LED2 R/G/B
    /// </summary>
    /// <summary>
    /// Tracks devices currently under manual UI control (Ambience tab).
    /// While a device IP is in this set, OnFrame sync is paused for it.
    /// </summary>
    private static readonly HashSet<string> _manualControlDevices = new();
    private static readonly Dictionary<string, DateTime> _manualControlExpiry = new();

    /// <summary>
    /// Pause sync for a device for a duration (default 10s). Called when user controls device from Ambience UI.
    /// </summary>
    public static void PauseSync(string ip, int seconds = 10)
    {
        lock (_manualControlDevices)
        {
            _manualControlDevices.Add(ip);
            _manualControlExpiry[ip] = DateTime.UtcNow.AddSeconds(seconds);
        }
    }

    /// <summary>
    /// Resume sync immediately for a device.
    /// </summary>
    public static void ResumeSync(string ip)
    {
        lock (_manualControlDevices)
        {
            _manualControlDevices.Remove(ip);
            _manualControlExpiry.Remove(ip);
        }
    }

    private static bool IsSyncPaused(string ip)
    {
        lock (_manualControlDevices)
        {
            if (!_manualControlDevices.Contains(ip)) return false;
            if (_manualControlExpiry.TryGetValue(ip, out var expiry) && DateTime.UtcNow > expiry)
            {
                _manualControlDevices.Remove(ip);
                _manualControlExpiry.Remove(ip);
                return false;
            }
            return true;
        }
    }

    public void OnFrame(byte[] linear45)
    {
        if (_disposed) return;

        AmbienceConfig cfg;
        lock (_lock) cfg = _config;

        // Only mirror LEDs when "Link to Room Ambience" is enabled
        // Skip if DreamView screen sync is active — it takes priority over LED mirroring
        if (!cfg.GoveeEnabled || !cfg.LinkToLights || cfg.GoveeDevices.Count == 0) return;
        if (cfg.ScreenSync.Enabled) return;

        foreach (var device in cfg.GoveeDevices)
        {
            if (string.IsNullOrWhiteSpace(device.Ip)) continue;
            if (!device.PoweredOn || !device.SyncWithAmpUp || device.SyncMode == "off" || IsSyncPaused(device.Ip)) continue;

            // Rate limit: skip if sent too recently
            int segCount = GetSegmentCount(device);
            bool useSegments = segCount > 0 && device.UseSegmentProtocol;
            long minTicks = useSegments ? MinTicksSegment : MinTicksSingle;
            var now = DateTime.UtcNow.Ticks;
            if (_lastSendTick.TryGetValue(device.Ip, out long lastTick) &&
                now - lastTick < minTicks)
                continue;
            string ip = device.Ip;

            if (segCount > 0 && device.UseSegmentProtocol)
            {
                // Per-segment: map Turn Up's 15 LEDs to device segments
                var segColors = new (byte R, byte G, byte B)[segCount];
                for (int s = 0; s < segCount; s++)
                {
                    int srcIdx = s * 15 / segCount;
                    int r = linear45[srcIdx * 3];
                    int g = linear45[srcIdx * 3 + 1];
                    int b = linear45[srcIdx * 3 + 2];
                    (r, g, b) = ApplySettings(r, g, b, cfg);
                    segColors[s] = ((byte)r, (byte)g, (byte)b);
                }

                // Enable segment mode + keepalive every 25s (auto-disables after ~60s)
                bool needEnable = !_segmentEnabled.ContainsKey(ip);
                if (!needEnable && _segmentKeepAliveTick.TryGetValue(ip, out long lastKa))
                    needEnable = now - lastKa > SegmentKeepAliveInterval;

                if (needEnable)
                {
                    _segmentEnabled.TryAdd(ip, 0);
                    _segmentKeepAliveTick[ip] = now;
                }

                _lastSendTick[ip] = now;
                _ = Task.Run(async () =>
                {
                    if (needEnable)
                        await SendSegmentEnable(ip, true);
                    await SendSegmentColors(ip, segColors);
                });
            }
            else
            {
                // Single color: use brightest LED
                var (r, g, b) = DeriveColor(linear45);
                (r, g, b) = ApplySettings(r, g, b, cfg);

                // Delta threshold
                if (_lastSent.TryGetValue(ip, out var prev))
                {
                    int dr = Math.Abs(r - prev.R), dg = Math.Abs(g - prev.G), db = Math.Abs(b - prev.B);
                    bool significantChange = dr > 3 || dg > 3 || db > 3;
                    bool keepalive = now - lastTick > TimeSpan.TicksPerMillisecond * 200;
                    if (!significantChange && !keepalive) continue;
                }

                _lastSent[ip] = ((byte)r, (byte)g, (byte)b);
                _lastSendTick[ip] = now;
                _ = Task.Run(() => SendGoveeColor(ip, r, g, b));
            }
        }
    }

    /// <summary>
    /// Broadcast Govee LAN discovery and return found devices.
    /// </summary>
    public async Task<List<(string Ip, string Name)>> ScanAsync(CancellationToken ct = default)
    {
        var rich = await ScanDevicesAsync(ct);
        return rich.Select(d => (d.Ip, d.Name)).ToList();
    }

    /// <summary>
    /// Broadcast Govee LAN discovery and return full device info (IP, name, SKU, MAC).
    /// </summary>
    public async Task<List<GoveeDeviceConfig>> ScanDevicesAsync(CancellationToken ct = default)
    {
        var results = new List<GoveeDeviceConfig>();

        try
        {
            // Use a single UDP client bound to port 4002 for both send and receive.
            // Govee devices respond to the sender's port, and also broadcast on 4002.
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4002));
            udp.EnableBroadcast = true;
            udp.Client.ReceiveTimeout = 5000;

            // Join multicast group so we receive responses sent to the group address
            try
            {
                udp.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
            }
            catch { }

            // Send discovery to multicast address (send twice — some devices need a nudge)
            var msg = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"scan\",\"data\":{\"account_topic\":\"reserve\"}}}");
            await udp.SendAsync(msg, msg.Length, "239.255.255.250", 4001);

            await Task.Delay(500, ct);
            await udp.SendAsync(msg, msg.Length, "239.255.255.250", 4001);

            // Also send directly to known device IPs as fallback (some networks block multicast)
            foreach (var known in _config.GoveeDevices)
            {
                if (!string.IsNullOrWhiteSpace(known.Ip))
                {
                    try
                    {
                        await udp.SendAsync(msg, msg.Length, known.Ip, 4001);
                    }
                    catch { }
                }
            }

            // Also broadcast on the local subnet (some bulbs only respond to broadcast)
            try
            {
                await udp.SendAsync(msg, msg.Length, "255.255.255.255", 4001);
            }
            catch { }

            // Collect responses for 5 seconds
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                try
                {
                    udp.Client.ReceiveTimeout = Math.Max(100, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    var result = await Task.Run(() =>
                    {
                        try
                        {
                            IPEndPoint? ep = null;
                            var data = udp.Receive(ref ep);
                            return (Data: data, Ep: ep);
                        }
                        catch
                        {
                            return (Data: (byte[]?)null, Ep: (IPEndPoint?)null);
                        }
                    }, ct);

                    if (result.Data == null || result.Ep == null) break;

                    string json = Encoding.UTF8.GetString(result.Data);
                    string ip = result.Ep.Address.ToString();
                    var (name, sku, deviceMac) = ParseScanResponse(json, ip);

                    if (!results.Any(r => r.Ip == ip))
                    {
                        results.Add(new GoveeDeviceConfig
                        {
                            Ip = ip,
                            Name = name,
                            Sku = sku,
                            DeviceId = deviceMac,
                            SyncMode = "global",
                        });
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Logger.Log($"Govee scan loop error: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee scan setup failed: {ex.Message}");
        }

        Logger.Log($"Govee scan: found {results.Count} device(s)");
        return results;
    }

    /// <summary>
    /// Called by RoomView.OnRoomFrame for room pattern effects.
    /// Sends full 15-LED frame with rate limiting and segment support.
    /// </summary>
    public void OnRoomFrame(byte[] linear45, AmbienceConfig cfg)
    {
        if (_disposed || !cfg.GoveeEnabled || cfg.GoveeDevices.Count == 0) return;

        // Collect active devices with their zone counts
        var activeDevices = new List<(GoveeDeviceConfig Dev, int Zones)>();
        foreach (var device in cfg.GoveeDevices)
        {
            if (string.IsNullOrWhiteSpace(device.Ip) || !device.PoweredOn || !device.SyncWithAmpUp || IsSyncPaused(device.Ip)) continue;
            int segs = (GetSegmentCount(device) > 0 && device.UseSegmentProtocol)
                ? GetSegmentCount(device) : 1;
            activeDevices.Add((device, segs));
        }
        if (activeDevices.Count == 0) return;

        if (cfg.SpatialSync && _spatialMapper != null && _spatialMapper.HasLayout)
        {
            // ── Room Layout spatial mode: use 3D positions for color mapping ──
            foreach (var (device, zones) in activeDevices)
            {
                bool isSeg = zones > 1 && device.UseSegmentProtocol;
                // Try IP first (room layout uses IP as DeviceId), then MAC DeviceId
                string matchId = _spatialMapper.GetDevicePosition(device.Ip) != null
                    ? device.Ip : device.DeviceId;
                var sampled = _spatialMapper.SampleForDevice(matchId, linear45, zones);
                var colors = new (int R, int G, int B)[sampled.Length];
                for (int i = 0; i < sampled.Length; i++)
                    colors[i] = ApplySettings(sampled[i].R, sampled[i].G, sampled[i].B, cfg);
                SendDeviceFrame(device, colors, isSeg);
            }
        }
        else if (cfg.SpatialSync)
        {
            // ── Linear spatial mode (no room layout): spread across devices in order ──
            int totalZones = 0;
            foreach (var (_, z) in activeDevices) totalZones += z;

            var zoneColors = SampleZoneColors(linear45, totalZones, cfg);

            int zoneOffset = 0;
            foreach (var (device, zones) in activeDevices)
            {
                var colors = new (int R, int G, int B)[zones];
                for (int i = 0; i < zones; i++)
                    colors[i] = zoneColors[zoneOffset + i];
                SendDeviceFrame(device, colors, zones > 1 && device.UseSegmentProtocol);
                zoneOffset += zones;
            }
        }
        else
        {
            // ── Mirror mode: every device shows the same effect ──
            foreach (var (device, zones) in activeDevices)
            {
                bool isSeg = zones > 1 && device.UseSegmentProtocol;
                if (isSeg)
                {
                    if (TryRenderNativeSegmentEffect(device, zones, cfg, out var nativeColors))
                    {
                        SendDeviceFrame(device, nativeColors, true);
                        continue;
                    }

                    // Segment devices: map 15 LEDs to device segments
                    var segColors = new (int R, int G, int B)[zones];

                    // Paired devices (H610A=24, H6056=15): mirror pattern on each panel
                    // so both panels show the same effect in sync
                    bool isPaired = IsPairedDevice(device.Sku);
                    int panelSegs = isPaired ? zones / 2 : zones;

                    for (int s = 0; s < zones; s++)
                    {
                        // Paired mirror: both panels show the same pattern.
                        // Reverse within each panel so they match other Govee devices.
                        int mapIdx;
                        if (isPaired)
                            mapIdx = (panelSegs - 1) - (s % panelSegs);
                        else
                            mapIdx = s;
                        int srcIdx = mapIdx * 15 / panelSegs;
                        srcIdx = Math.Clamp(srcIdx, 0, 14);
                        int r = linear45[srcIdx * 3];
                        int g = linear45[srcIdx * 3 + 1];
                        int b = linear45[srcIdx * 3 + 2];
                        (r, g, b) = ApplySettings(r, g, b, cfg);
                        segColors[s] = (r, g, b);
                    }
                    SendDeviceFrame(device, segColors, true);
                }
                else
                {
                    // Single-color: brightest LED
                    var (r, g, b) = DeriveColor(linear45);
                    (r, g, b) = ApplySettings(r, g, b, cfg);
                    SendDeviceFrame(device, new[] { (r, g, b) }, false);
                }
            }
        }
    }

    /// <summary>
    /// Sample N zone colors from 15 LEDs with smooth interpolation.
    /// </summary>
    private static (int R, int G, int B)[] SampleZoneColors(byte[] linear45, int zoneCount, AmbienceConfig cfg)
    {
        var colors = new (int R, int G, int B)[zoneCount];
        for (int z = 0; z < zoneCount; z++)
        {
            float ledPos = z * 14f / Math.Max(zoneCount - 1, 1);
            int lo = Math.Min((int)ledPos, 13);
            int hi = Math.Min(lo + 1, 14);
            float frac = ledPos - lo;

            int r = (int)(linear45[lo * 3] * (1 - frac) + linear45[hi * 3] * frac);
            int g = (int)(linear45[lo * 3 + 1] * (1 - frac) + linear45[hi * 3 + 1] * frac);
            int b = (int)(linear45[lo * 3 + 2] * (1 - frac) + linear45[hi * 3 + 2] * frac);
            (r, g, b) = ApplySettings(r, g, b, cfg);
            colors[z] = (r, g, b);
        }
        return colors;
    }

    private static bool TryRenderNativeSegmentEffect(
        GoveeDeviceConfig device,
        int segmentCount,
        AmbienceConfig cfg,
        out (int R, int G, int B)[] colors)
    {
        colors = Array.Empty<(int R, int G, int B)>();
        if (segmentCount <= 1 || string.IsNullOrWhiteSpace(cfg.RoomEffect))
            return false;
        if (!Enum.TryParse<LightEffect>(cfg.RoomEffect, true, out var effect))
            return false;

        bool supported = effect is LightEffect.Aurora or LightEffect.Ocean or LightEffect.NebulaDrift
            or LightEffect.OpalWave or LightEffect.Prism or LightEffect.Tidal or LightEffect.Vortex
            or LightEffect.Shockwave or LightEffect.Scanner or LightEffect.MeteorRain
            or LightEffect.RainbowScanner or LightEffect.ColorWave or LightEffect.FireWall
            or LightEffect.Lava or LightEffect.Waterfall or LightEffect.Matrix or LightEffect.Starfield
            or LightEffect.ColorTwinkle or LightEffect.Bloom or LightEffect.Glitch or LightEffect.DNA
            or LightEffect.AuroraVeil or LightEffect.SolarStorm or LightEffect.StarlightCanopy
            or LightEffect.PlasmaBloom or LightEffect.RippleRoom or LightEffect.PrismDrift
            or LightEffect.NebulaRain or LightEffect.ReactiveAurora or LightEffect.LiquidGlass
            or LightEffect.ChromaLayerStack;
        if (!supported) return false;

        var c1 = ParseHexColor(cfg.RoomColor1, (0, 230, 118));
        var c2 = ParseHexColor(cfg.RoomColor2, (255, 255, 255));
        float speed = 0.35f + Math.Clamp(cfg.RoomEffectSpeed, 1, 100) / 55f;
        float t = Environment.TickCount64 / 1000f * speed;
        float devicePhase = Math.Abs((device.Ip ?? device.DeviceId ?? device.Name ?? "").GetHashCode() % 997) / 997f;

        colors = new (int R, int G, int B)[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            float x = segmentCount == 1 ? 0f : i / (float)(segmentCount - 1);
            var raw = effect switch
            {
                LightEffect.Aurora => SegmentAurora(x, t, devicePhase, c1, c2),
                LightEffect.NebulaDrift => SegmentNebula(x, t, devicePhase, c1, c2),
                LightEffect.OpalWave => SegmentOpal(x, t, devicePhase),
                LightEffect.Ocean => SegmentOcean(x, t, devicePhase, c1, c2),
                LightEffect.Prism => SegmentPrism(x, t, devicePhase),
                LightEffect.Tidal => SegmentTidal(x, t, devicePhase, c1, c2),
                LightEffect.Vortex => SegmentVortex(x, t, devicePhase),
                LightEffect.Shockwave => SegmentShockwave(x, t, devicePhase, c1, c2),
                LightEffect.Scanner => SegmentScanner(x, t, c1, c2, rainbow: false),
                LightEffect.RainbowScanner => SegmentScanner(x, t, c1, c2, rainbow: true),
                LightEffect.MeteorRain => SegmentMeteor(x, t, devicePhase, c1, c2),
                LightEffect.ColorWave => SegmentColorWave(x, t, devicePhase, c1, c2),
                LightEffect.FireWall => SegmentFire(x, t, devicePhase),
                LightEffect.Lava => SegmentLava(x, t, devicePhase),
                LightEffect.Waterfall => SegmentWaterfall(x, t, devicePhase),
                LightEffect.Matrix => SegmentMatrix(x, t, devicePhase),
                LightEffect.Starfield => SegmentStarfield(x, t, devicePhase),
                LightEffect.ColorTwinkle => SegmentTwinkle(x, t, devicePhase, c1, c2),
                LightEffect.Bloom => SegmentBloom(x, t, devicePhase, c1, c2),
                LightEffect.Glitch => SegmentGlitch(x, t, devicePhase),
                LightEffect.DNA => SegmentDna(x, t, devicePhase, c1, c2),
                LightEffect.AuroraVeil => SegmentAuroraVeil(x, t, devicePhase, c1, c2),
                LightEffect.SolarStorm => SegmentSolarStorm(x, t, devicePhase, c1, c2),
                LightEffect.StarlightCanopy => SegmentStarlightCanopy(x, t, devicePhase, c1, c2),
                LightEffect.PlasmaBloom => SegmentPlasmaBloom(x, t, devicePhase, c1, c2),
                LightEffect.RippleRoom => SegmentRippleRoom(x, t, devicePhase, c1, c2),
                LightEffect.PrismDrift => SegmentPrismDrift(x, t, devicePhase, c1, c2),
                LightEffect.NebulaRain => SegmentNebulaRain(x, t, devicePhase, c1, c2),
                LightEffect.ReactiveAurora => SegmentReactiveAurora(x, t, devicePhase, c1, c2),
                LightEffect.LiquidGlass => SegmentLiquidGlass(x, t, devicePhase, c1, c2),
                LightEffect.ChromaLayerStack => SegmentChromaLayerStack(x, t, devicePhase, c1, c2),
                _ => (0, 0, 0),
            };
            colors[i] = ApplySettings(raw.Item1, raw.Item2, raw.Item3, cfg);
        }

        return true;
    }

    private static (int R, int G, int B) SegmentAurora(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float wave1 = MathF.Sin(t * 0.7f + x * 4f + phase * MathF.Tau) * 0.5f + 0.5f;
        float wave2 = MathF.Sin(t * 1.1f + x * 6f + 1.5f + phase * MathF.Tau) * 0.5f + 0.5f;
        float wave3 = MathF.Sin(t * 0.4f + x * 2f + 3f + phase * MathF.Tau) * 0.5f + 0.5f;
        float combined = (wave1 + wave2 * 0.6f + wave3 * 0.3f) / 1.9f;

        float hue = 120f + combined * 180f + MathF.Sin(t * 0.3f + x * 3f + phase * MathF.Tau) * 40f;
        hue = ((hue % 360f) + 360f) % 360f;

        float brightness = 0.15f + combined * 0.85f;
        brightness *= brightness;
        return Hsv(hue / 360f, 0.86f, Math.Clamp(brightness * 1.15f, 0f, 1f));
    }

    private static (int R, int G, int B) SegmentNebula(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float cloud = Smooth(Wave(x * 3.0f + t * 0.17f + phase) * 0.65f + Wave(x * 8.0f - t * 0.11f) * 0.35f);
        var violet = Lerp((70, 20, 145), c1, 0.35f);
        return Scale(Lerp(violet, c2, cloud), 0.45f + cloud * 0.75f);
    }

    private static (int R, int G, int B) SegmentOpal(float x, float t, float phase)
    {
        float hue = 0.47f + 0.22f * Wave(x * 2.2f - t * 0.18f + phase);
        var rgb = Hsv(hue, 0.32f, 1f);
        return Lerp((255, 210, 245), rgb, 0.65f + 0.25f * Wave(x * 6f + t));
    }

    private static (int R, int G, int B) SegmentOcean(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float wave = Smooth(Wave(x * 4.5f - t * 0.5f + phase) * 0.7f + Wave(x * 11f + t * 0.2f) * 0.3f);
        var baseColor = Lerp((0, 55, 160), (0, 230, 210), wave);
        return Lerp(baseColor, Lerp(c1, c2, wave), 0.25f);
    }

    private static (int R, int G, int B) SegmentPrism(float x, float t, float phase)
        => Hsv((x * 0.82f + t * 0.08f + phase) % 1f, 0.88f, 1f);

    private static (int R, int G, int B) SegmentTidal(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float crest = MathF.Pow(Smooth(Wave(x * 2.2f - t * 0.55f + phase)), 3.2f);
        return Lerp(Scale(c1, 0.28f), c2, crest);
    }

    private static (int R, int G, int B) SegmentVortex(float x, float t, float phase)
    {
        float spin = MathF.Abs(MathF.Sin((x * 10.0f + t * 1.6f + phase) * MathF.PI));
        return Hsv((0.72f + x * 0.25f + t * 0.04f) % 1f, 0.95f, 0.25f + spin * 0.9f);
    }

    private static (int R, int G, int B) SegmentShockwave(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float center = (t * 0.35f + phase) % 1f;
        float d = MathF.Abs(x - center);
        d = MathF.Min(d, 1f - d);
        float ring = MathF.Exp(-d * d * 95f);
        return Lerp(Scale(c1, 0.18f), c2, ring);
    }

    private static (int R, int G, int B) SegmentScanner(float x, float t, (int R, int G, int B) c1, (int R, int G, int B) c2, bool rainbow)
    {
        float pos = PingPong(t * 0.32f);
        float d = MathF.Abs(x - pos);
        float tail = MathF.Exp(-d * d * 70f);
        var color = rainbow ? Hsv((x + t * 0.08f) % 1f, 1f, 1f) : c1;
        return Lerp(Scale(c2, 0.08f), color, tail);
    }

    private static (int R, int G, int B) SegmentMeteor(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float head = (t * 0.42f + phase) % 1f;
        float d = (x - head + 1f) % 1f;
        float tail = d < 0.36f ? MathF.Pow(1f - d / 0.36f, 2.2f) : 0f;
        return Lerp(Scale(c2, 0.05f), c1, tail);
    }

    private static (int R, int G, int B) SegmentColorWave(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
        => Lerp(c1, c2, Smooth(Wave(x * 3.8f - t * 0.62f + phase)));

    private static (int R, int G, int B) SegmentFire(float x, float t, float phase)
    {
        float heat = Smooth(Wave(x * 8f + t * 1.8f + phase) * 0.5f + Wave(x * 19f - t * 1.1f) * 0.5f);
        heat = MathF.Max(heat, 1f - x * 0.35f);
        return Lerp((120, 10, 0), (255, 230, 80), heat);
    }

    private static (int R, int G, int B) SegmentLava(float x, float t, float phase)
    {
        float bubble = MathF.Pow(Smooth(Wave(x * 5.5f - t * 0.38f + phase)), 2.4f);
        return Lerp((80, 0, 0), (255, 105, 20), bubble);
    }

    private static (int R, int G, int B) SegmentWaterfall(float x, float t, float phase)
    {
        float streak = MathF.Pow(Smooth(Wave(x * 14f + t * 1.1f + phase)), 5f);
        return Lerp((0, 35, 95), (120, 235, 255), streak);
    }

    private static (int R, int G, int B) SegmentMatrix(float x, float t, float phase)
    {
        float cell = Frac(x * 23f - t * 2.1f + phase);
        float drop = cell < 0.14f ? 1f - cell / 0.14f : 0.08f;
        return Scale((0, 255, 105), drop);
    }

    private static (int R, int G, int B) SegmentStarfield(float x, float t, float phase)
    {
        float seed = Frac(MathF.Sin((x + phase) * 151.7f) * 43758.545f);
        float tw = MathF.Pow(Smooth(Wave(t * (0.6f + seed) + seed * 7f)), 8f);
        return Scale(Lerp((80, 90, 170), (255, 255, 255), tw), 0.18f + tw);
    }

    private static (int R, int G, int B) SegmentTwinkle(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float seed = Frac(MathF.Sin((x + phase) * 331.3f) * 24634.634f);
        float sparkle = MathF.Pow(Smooth(Wave(t * (0.9f + seed * 1.4f) + seed * 9f)), 10f);
        return Lerp(Scale(c1, 0.18f), c2, sparkle);
    }

    private static (int R, int G, int B) SegmentBloom(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float bloom = MathF.Pow(Smooth(Wave(x * 2.0f - t * 0.24f + phase)), 1.8f);
        return Scale(Lerp(c1, c2, bloom), 0.35f + bloom * 0.8f);
    }

    private static (int R, int G, int B) SegmentGlitch(float x, float t, float phase)
    {
        float band = Frac(MathF.Floor(x * 18f) * 0.173f + MathF.Floor(t * 8f) * 0.071f + phase);
        if (band < 0.18f) return (255, 0, 120);
        if (band < 0.32f) return (0, 240, 255);
        return Scale((80, 0, 160), 0.35f + 0.25f * Wave(x * 6f + t));
    }

    private static (int R, int G, int B) SegmentDna(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float a = MathF.Pow(Smooth(Wave(x * 5.0f + t * 0.72f + phase)), 6f);
        float b = MathF.Pow(Smooth(Wave(x * 5.0f + t * 0.72f + phase + 0.5f)), 6f);
        return Lerp(Scale(c1, a), Scale(c2, b), b / Math.Max(a + b, 0.001f));
    }

    private static (int R, int G, int B) SegmentAuroraVeil(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var aurora = SegmentAurora(x, t, phase, c1, c2);
        float veil = 0.18f + 0.22f * Wave(x * 2.8f - t * 0.18f + phase);
        return Lerp(aurora, Lerp(c1, c2, Wave(x + t * 0.05f)), veil);
    }

    private static (int R, int G, int B) SegmentSolarStorm(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var baseColor = SegmentAurora(x, t * 0.7f, phase, c1, c2);
        float arc = (t * 0.32f + phase) % 1f;
        float d = MathF.Abs(x - arc);
        d = MathF.Min(d, 1f - d);
        float flare = MathF.Exp(-d * d * 90f);
        return Lerp(baseColor, (255, 210, 60), flare);
    }

    private static (int R, int G, int B) SegmentStarlightCanopy(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var nebula = SegmentNebula(x, t, phase, c1, c2);
        float seed = Frac(MathF.Sin((x + phase) * 487.3f) * 37561.17f);
        float star = MathF.Pow(Smooth(Wave(t * (0.55f + seed) + seed * 8.7f)), 9f);
        return Lerp(Scale(nebula, 0.72f), (255, 245, 255), star);
    }

    private static (int R, int G, int B) SegmentPlasmaBloom(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float plasma = Smooth((Wave(x * 2.8f + t * 0.32f + phase) + Wave(x * 7.5f - t * 0.2f) + Wave(x * 11.0f + t * 0.12f)) / 3f);
        float bloom = 0.35f + MathF.Pow(plasma, 1.7f) * 0.85f;
        return Scale(Lerp(c1, c2, plasma), bloom);
    }

    private static (int R, int G, int B) SegmentRippleRoom(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float center = 0.5f + MathF.Sin(t * 0.24f + phase * MathF.Tau) * 0.18f;
        float radius = (t * 0.42f + phase) % 1f;
        float d = MathF.Abs(x - center);
        float ring = MathF.Exp(-MathF.Pow(d - radius * 0.72f, 2f) * 85f);
        float under = 0.12f + 0.2f * Wave(x * 2f - t * 0.12f);
        return Scale(Lerp(c1, c2, x + ring * 0.25f), Math.Clamp(under + ring, 0f, 1f));
    }

    private static (int R, int G, int B) SegmentPrismDrift(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var prism = SegmentPrism(x, t, phase);
        float shimmer = MathF.Exp(-MathF.Pow(x - ((t * 0.18f + phase) % 1f), 2f) * 75f);
        return Lerp(prism, (255, 255, 255), shimmer * 0.45f);
    }

    private static (int R, int G, int B) SegmentNebulaRain(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var nebula = SegmentNebula(x, t, phase, c1, c2);
        float streak = MathF.Pow(Smooth(Wave(x * 8.5f + t * 0.9f + phase)), 7f);
        return Lerp(nebula, c2, streak * 0.85f);
    }

    private static (int R, int G, int B) SegmentReactiveAurora(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var aurora = SegmentAurora(x, t, phase, c1, c2);
        float shimmer = MathF.Pow(Wave(x * 3.2f + t * 1.1f + phase), 2f);
        return Lerp(aurora, c2, shimmer * 0.45f);
    }

    private static (int R, int G, int B) SegmentLiquidGlass(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float caustic = MathF.Pow(Smooth(Wave(x * 4.5f - t * 0.22f + phase) * 0.65f + Wave(x * 13f + t * 0.18f) * 0.35f), 2.2f);
        var color = Lerp(c1, c2, x + caustic * 0.18f);
        return Lerp(Scale(color, 0.35f + caustic * 0.85f), (255, 255, 255), caustic * 0.16f);
    }

    private static (int R, int G, int B) SegmentChromaLayerStack(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        var wave = SegmentColorWave(x, t, phase, c1, c2);
        float pos = PingPong(t * 0.28f + phase);
        float d = MathF.Abs(x - pos);
        float scanner = MathF.Exp(-d * d * 80f);
        return Lerp(wave, Hsv((x + t * 0.08f) % 1f, 0.9f, 1f), scanner * 0.85f);
    }

    private static (int R, int G, int B) ParseHexColor(string? hex, (int R, int G, int B) fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return fallback;
        return int.TryParse(hex[..2], System.Globalization.NumberStyles.HexNumber, null, out int r)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int g)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int b)
            ? (r, g, b)
            : fallback;
    }

    private static (int R, int G, int B) Lerp((int R, int G, int B) a, (int R, int G, int B) b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return (
            (int)MathF.Round(a.R + (b.R - a.R) * t),
            (int)MathF.Round(a.G + (b.G - a.G) * t),
            (int)MathF.Round(a.B + (b.B - a.B) * t));
    }

    private static (int R, int G, int B) Scale((int R, int G, int B) c, float scale)
        => (Math.Clamp((int)MathF.Round(c.R * scale), 0, 255),
            Math.Clamp((int)MathF.Round(c.G * scale), 0, 255),
            Math.Clamp((int)MathF.Round(c.B * scale), 0, 255));

    private static float Wave(float x) => (MathF.Sin(x * MathF.Tau) + 1f) * 0.5f;
    private static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
    private static float Frac(float x) => x - MathF.Floor(x);
    private static float PingPong(float x) { x = Frac(x); return x < 0.5f ? x * 2f : 2f - x * 2f; }

    private static (int R, int G, int B) Hsv(float h, float s, float v)
    {
        h = Frac(h);
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        float c = v * s;
        float x = c * (1f - MathF.Abs((h * 6f % 2f) - 1f));
        float m = v - c;
        float r, g, b;
        int sector = (int)(h * 6f);
        switch (sector)
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }

        return (
            Math.Clamp((int)MathF.Round((r + m) * 255f), 0, 255),
            Math.Clamp((int)MathF.Round((g + m) * 255f), 0, 255),
            Math.Clamp((int)MathF.Round((b + m) * 255f), 0, 255));
    }

    /// <summary>
    /// Send a frame to a single device. Handles rate limiting, segments, and single-color.
    /// Single-color devices pre-scale brightness into RGB (1 packet instead of 2 → 10 FPS).
    /// </summary>
    private void SendDeviceFrame(GoveeDeviceConfig device, (int R, int G, int B)[] colors, bool isSegment)
    {
        string ip = device.Ip;
        if (string.IsNullOrWhiteSpace(ip)) return;
        var now = DateTime.UtcNow.Ticks;
        long minTicks = isSegment ? MinTicksSegment : MinTicksSingle;
        if (_lastSendTick.TryGetValue(ip, out long lastTick) && now - lastTick < minTicks)
            return;

        int deviceBrightness = Math.Clamp(device.BrightnessScale, 0, 100);
        if (deviceBrightness != 100)
        {
            var scaled = new (int R, int G, int B)[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                scaled[i] = (
                    Math.Clamp(colors[i].R * deviceBrightness / 100, 0, 255),
                    Math.Clamp(colors[i].G * deviceBrightness / 100, 0, 255),
                    Math.Clamp(colors[i].B * deviceBrightness / 100, 0, 255));
            }
            colors = scaled;
        }

        if (isSegment)
        {
            var segColors = new (byte R, byte G, byte B)[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                segColors[i] = ((byte)colors[i].R, (byte)colors[i].G, (byte)colors[i].B);

            bool needEnable = !_segmentEnabled.ContainsKey(ip);
            if (!needEnable && _segmentKeepAliveTick.TryGetValue(ip, out long lastKa))
                needEnable = now - lastKa > SegmentKeepAliveInterval;
            if (needEnable)
            {
                _segmentEnabled.TryAdd(ip, 0);
                _segmentKeepAliveTick[ip] = now;
            }
            _lastSendTick[ip] = now;
            _ = Task.Run(async () =>
            {
                if (needEnable)
                    await SendSegmentEnable(ip, true);
                await SendSegmentColors(ip, segColors);
            });
        }
        else
        {
            // Pre-scale brightness into RGB — single colorwc packet, no separate brightness needed
            var (r, g, b) = colors[0];

            if (_lastSent.TryGetValue(ip, out var prev))
            {
                int dr = Math.Abs(r - prev.R), dg = Math.Abs(g - prev.G), db = Math.Abs(b - prev.B);
                if (dr <= 3 && dg <= 3 && db <= 3 && now - lastTick <= TimeSpan.TicksPerMillisecond * 150)
                    return;
            }
            _lastSent[ip] = ((byte)r, (byte)g, (byte)b);
            _lastSendTick[ip] = now;
            _ = Task.Run(() => SendGoveeColor(ip, r, g, b));
        }
    }

    // ── Color derivation ────────────────────────────────────────────

    private static (int R, int G, int B) DeriveColor(byte[] linear45)
    {
        // Use the brightest LED (by total luminance) for vivid colors
        // Averaging dims effects since many LEDs may be dark during animations
        int bestIdx = 0, bestLum = 0;
        for (int i = 0; i < 15; i++)
        {
            int lum = linear45[i * 3] + linear45[i * 3 + 1] + linear45[i * 3 + 2];
            if (lum > bestLum) { bestLum = lum; bestIdx = i; }
        }
        if (bestLum > 0)
            return (linear45[bestIdx * 3], linear45[bestIdx * 3 + 1], linear45[bestIdx * 3 + 2]);

        // Fallback: average if all LEDs are dark
        int rSum = 0, gSum = 0, bSum = 0;
        for (int i = 0; i < 15; i++)
        {
            rSum += linear45[i * 3];
            gSum += linear45[i * 3 + 1];
            bSum += linear45[i * 3 + 2];
        }
        return (rSum / 15, gSum / 15, bSum / 15);
    }

    private static (int R, int G, int B) ApplySettings(int r, int g, int b, AmbienceConfig cfg)
    {
        (r, g, b) = BoostEffectColor(r, g, b);

        // Brightness scale
        r = r * cfg.BrightnessScale / 100;
        g = g * cfg.BrightnessScale / 100;
        b = b * cfg.BrightnessScale / 100;

        // Warm tone shift
        if (cfg.WarmToneShift)
        {
            r = Math.Clamp((int)(r * 1.15), 0, 255);
            b = Math.Clamp((int)(b / 1.15), 0, 255);
        }

        return (Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    private static (int R, int G, int B) BoostEffectColor(int r, int g, int b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        if (max <= 0) return (0, 0, 0);

        float boost = RoomEffectColorBoost;
        return (
            Math.Clamp((int)Math.Round(r * boost), 0, 255),
            Math.Clamp((int)Math.Round(g * boost), 0, 255),
            Math.Clamp((int)Math.Round(b * boost), 0, 255));
    }

    // ── Knob brightness control ─────────────────────────────────────

    /// <summary>
    /// Sets Govee brightness (0.0–1.0) on all configured LAN devices.
    /// Called when a knob is assigned target "govee".
    /// </summary>
    public void SetBrightness(float normalized)
    {
        if (_disposed) return;

        AmbienceConfig cfg;
        lock (_lock) cfg = _config;

        if (!cfg.GoveeEnabled || cfg.GoveeDevices.Count == 0) return;

        int value = (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 100);
        cfg.BrightnessScale = value;

        foreach (var device in cfg.GoveeDevices)
        {
            device.BrightnessScale = value;
            if (string.IsNullOrWhiteSpace(device.Ip) || !device.PoweredOn) continue;
            string ip = device.Ip;
            _ = Task.Run(() => SendGoveeBrightness(ip, value));
        }
    }

    /// <summary>
    /// Sets Govee brightness (0.0–1.0) on a single specific device by IP.
    /// Called when a knob is assigned target "govee:IP".
    /// </summary>
    /// <summary>Turn on all Govee devices that are currently powered off (user intent from knob turn).</summary>
    public void EnsureDevicesPoweredOn()
    {
        AmbienceConfig cfg;
        lock (_lock) cfg = _config;
        foreach (var device in cfg.GoveeDevices)
        {
            if (!device.PoweredOn && !string.IsNullOrWhiteSpace(device.Ip))
            {
                device.PoweredOn = true;
                string ip = device.Ip;
                _ = Task.Run(() => SendTurnAsync(ip, true));
            }
        }
    }

    /// <summary>Turn on a specific Govee device if powered off (user intent from knob turn).</summary>
    public void EnsureDevicePoweredOn(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        AmbienceConfig cfg;
        lock (_lock) cfg = _config;
        var device = cfg.GoveeDevices.FirstOrDefault(d => d.Ip == ip);
        if (device != null && !device.PoweredOn)
        {
            device.PoweredOn = true;
            _ = Task.Run(() => SendTurnAsync(ip, true));
        }
    }

    public void SetBrightnessForDevice(string ip, float normalized)
    {
        if (_disposed || string.IsNullOrWhiteSpace(ip)) return;

        // Respect device PoweredOn state
        AmbienceConfig cfg;
        lock (_lock) cfg = _config;
        var device = cfg.GoveeDevices.FirstOrDefault(d => d.Ip == ip);
        if (device != null && !device.PoweredOn) return;

        int value = (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 100);
        if (device != null)
            device.BrightnessScale = value;
        _ = Task.Run(() => SendGoveeBrightness(ip, value));
    }

    // ── LAN control commands (all send to device IP:4003) ──────────

    private const int LanControlPort = 4003;

    // Track last known power state per device IP for toggle
    private static readonly Dictionary<string, bool> _devicePowerState = new();

    /// <summary>
    /// Send a raw LAN UDP command to a Govee device.
    /// Reuses a single UdpClient for lower latency on rapid color updates.
    /// </summary>
    private static UdpClient? _sharedUdp;
    private static readonly object _udpLock = new();

    private static async Task SendLanCommand(string ip, string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        UdpClient udp;
        lock (_udpLock)
        {
            if (_sharedUdp == null)
            {
                _sharedUdp = new UdpClient();
                _sharedUdp.Client.SetSocketOption(
                    SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            udp = _sharedUdp;
        }
        try
        {
            await udp.SendAsync(data, data.Length, ip, LanControlPort);
        }
        catch
        {
            // Socket might be stale — recreate and retry
            lock (_udpLock) { try { _sharedUdp?.Dispose(); } catch { } _sharedUdp = null; }
            using var fresh = new UdpClient();
            await fresh.SendAsync(data, data.Length, ip, LanControlPort);
        }
    }

    /// <summary>
    /// Query device status via LAN UDP. Returns (onOff, brightness, r, g, b, colorTempK) or null on timeout.
    /// </summary>
    public static async Task<(bool On, int Brightness, int R, int G, int B, int ColorTempK)?> GetDeviceStatusAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        try
        {
            using var udp = new UdpClient();
            string json = "{\"msg\":{\"cmd\":\"devStatus\",\"data\":{}}}";
            byte[] data = Encoding.UTF8.GetBytes(json);
            await udp.SendAsync(data, data.Length, ip, LanControlPort);

            udp.Client.ReceiveTimeout = 2000;
            var result = await Task.Run(() =>
            {
                try
                {
                    IPEndPoint? ep = null;
                    var resp = udp.Receive(ref ep);
                    return Encoding.UTF8.GetString(resp);
                }
                catch { return null; }
            });

            if (result == null) return null;

            var obj = Newtonsoft.Json.Linq.JObject.Parse(result);
            var d = obj["msg"]?["data"];
            if (d == null) return null;

            bool on = d["onOff"]?.ToObject<int>() == 1;
            int brightness = d["brightness"]?.ToObject<int>() ?? 100;
            int r = d["color"]?["r"]?.ToObject<int>() ?? 0;
            int g = d["color"]?["g"]?.ToObject<int>() ?? 0;
            int b = d["color"]?["b"]?.ToObject<int>() ?? 0;
            int colorTemp = d["colorTemInKelvin"]?.ToObject<int>() ?? 0;

            // Update cached power state
            _devicePowerState[ip] = on;
            return (on, brightness, r, g, b, colorTemp);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee status query failed ({ip}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Toggles a Govee device on/off via LAN UDP. Uses cached power state
    /// and flips immediately — Govee's devStatus query waits up to 2s for
    /// a response that often arrives on a different socket, adding several
    /// seconds of perceived latency between button press and light change.
    /// The turn command is idempotent (firmware no-ops if already in the
    /// requested state) so a drifted cache just costs one extra press to
    /// resync in the worst case.
    /// </summary>
    public static async Task SendToggleAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            bool currentlyOn = _devicePowerState.TryGetValue(ip, out bool last) ? last : true;
            bool newState = !currentlyOn;
            _devicePowerState[ip] = newState;
            await SendTurnAsync(ip, newState);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee toggle failed ({ip}): {ex.Message}");
        }
    }

    /// <summary>
    /// Turns a Govee device on or off via LAN UDP.
    /// </summary>
    public static async Task SendTurnAsync(string ip, bool on)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            string json = $"{{\"msg\":{{\"cmd\":\"turn\",\"data\":{{\"value\":{(on ? 1 : 0)}}}}}}}";
            await SendLanCommand(ip, json);
            _devicePowerState[ip] = on;
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee turn failed ({ip}): {ex.Message}");
        }
    }

    /// <summary>
    /// Sets a Govee device to a specific color via LAN UDP.
    /// </summary>
    public static async Task SendColorAsync(string ip, byte r, byte g, byte b, int durationMs = 30)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            string json = $"{{\"msg\":{{\"cmd\":\"colorwc\",\"data\":{{\"color\":{{\"r\":{r},\"g\":{g},\"b\":{b}}},\"colorTemInKelvin\":0,\"duration\":{durationMs}}}}}}}";
            await SendLanCommand(ip, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee color failed ({ip}): {ex.Message}");
        }
    }

    /// <summary>
    /// Sets a Govee device color temperature via LAN UDP. Range: 2000-9000K.
    /// </summary>
    public static async Task SendColorTempAsync(string ip, int kelvin)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            kelvin = Math.Clamp(kelvin, 2000, 9000);
            string json = $"{{\"msg\":{{\"cmd\":\"colorwc\",\"data\":{{\"color\":{{\"r\":0,\"g\":0,\"b\":0}},\"colorTemInKelvin\":{kelvin}}}}}}}";
            await SendLanCommand(ip, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee color temp failed ({ip}): {ex.Message}");
        }
    }

    /// <summary>
    /// Sets Govee device brightness via LAN UDP. Range: 0-100.
    /// </summary>
    public static async Task SendBrightnessAsync(string ip, int value)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            value = Math.Clamp(value, 0, 100);
            string json = $"{{\"msg\":{{\"cmd\":\"brightness\",\"data\":{{\"value\":{value}}}}}}}";
            await SendLanCommand(ip, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee brightness failed ({ip}): {ex.Message}");
        }
    }

    private static async Task SendGoveeBrightness(string ip, int value)
    {
        await SendBrightnessAsync(ip, value);
    }

    // ── Per-segment protocol (Govee "razer" command) ──────────────

    private static byte XorChecksum(byte[] data, int length)
    {
        byte xor = 0;
        for (int i = 0; i < length; i++) xor ^= data[i];
        return xor;
    }

    /// <summary>
    /// Disable segment mode. Called via instance method to clear tracking state.
    /// </summary>
    public void ClearSegmentMode(string ip)
    {
        _segmentEnabled.TryRemove(ip, out _);
        _segmentKeepAliveTick.TryRemove(ip, out _);
        _ = Task.Run(() => SendSegmentEnable(ip, false));
    }

    /// <summary>
    /// Clear segment tracking state for all devices so they get re-enabled on next frame.
    /// Called when DreamSync stops (it sends its own disable commands).
    /// </summary>
    public void ClearAllSegmentTracking()
    {
        _segmentEnabled.Clear();
        _segmentKeepAliveTick.Clear();
    }

    public static async Task DisableSegmentMode(string ip)
    {
        await SendSegmentEnable(ip, false);
    }

    /// <summary>
    /// Send per-segment colors to a Govee device via LAN UDP. Handles segment enable + rate limiting.
    /// Used by VU meter music mode for direct segment control.
    /// </summary>
    public void SendSegmentFrame(string ip, (byte R, byte G, byte B)[] colors)
    {
        if (string.IsNullOrWhiteSpace(ip) || IsSyncPaused(ip)) return;
        var now = DateTime.UtcNow.Ticks;
        if (_lastSendTick.TryGetValue(ip, out long lastTick) && now - lastTick < MinTicksSegment)
            return;

        bool needEnable = !_segmentEnabled.ContainsKey(ip);
        if (!needEnable && _segmentKeepAliveTick.TryGetValue(ip, out long lastKa))
            needEnable = now - lastKa > SegmentKeepAliveInterval;
        if (needEnable)
        {
            _segmentEnabled.TryAdd(ip, 0);
            _segmentKeepAliveTick[ip] = now;
        }
        _lastSendTick[ip] = now;
        _ = Task.Run(async () =>
        {
            if (needEnable)
                await SendSegmentEnable(ip, true);
            await SendSegmentColors(ip, colors);
        });
    }

    private static async Task SendSegmentEnable(string ip, bool enable)
    {
        var pkt = new byte[] { 0xBB, 0x00, 0x01, 0xB1, (byte)(enable ? 1 : 0), 0 };
        pkt[5] = XorChecksum(pkt, 5);
        string b64 = Convert.ToBase64String(pkt);
        string json = $"{{\"msg\":{{\"cmd\":\"razer\",\"data\":{{\"pt\":\"{b64}\"}}}}}}";
        await SendLanCommand(ip, json);
    }

    private static async Task SendSegmentColors(string ip, (byte R, byte G, byte B)[] colors)
    {
        int count = colors.Length;
        int payloadLen = 2 + 1 + count * 3;
        int totalLen = 3 + payloadLen + 1;
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
        await SendLanCommand(ip, json);
    }

    private static async Task SendGoveeColor(string ip, int r, int g, int b)
    {
        await SendColorAsync(ip, (byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
    }

    // ── JSON helpers ────────────────────────────────────────────────

    /// <summary>
    /// Parse Govee LAN scan response JSON to extract sku, device MAC, and a display name.
    /// Returns (displayName, sku, deviceMac).
    /// </summary>
    public static (string Name, string Sku, string DeviceMac) ParseScanResponse(string json, string fallbackIp)
    {
        string sku = "";
        string deviceMac = "";
        try
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            var data = obj["msg"]?["data"];
            if (data != null)
            {
                sku = data["sku"]?.ToString() ?? "";
                deviceMac = data["device"]?.ToString() ?? "";
            }
        }
        catch { }

        string name = !string.IsNullOrEmpty(sku) ? GetProductName(sku) : $"Govee ({fallbackIp})";
        return (name, sku, deviceMac);
    }

    /// <summary>
    /// Get the number of individually addressable segments for a Govee device SKU.
    /// Returns 0 if the device doesn't support per-segment control.
    /// </summary>
    public static int GetSegmentCount(string? sku) => sku?.ToUpperInvariant() switch
    {
        "H6056" => 15,  // Flow Plus Light Bar (18 LEDs/bar × 2 bars, API exposes 15 segments)
        "H6057" => 15,  // Flow Plus Light Bar
        "H6046" => 12,  // RGBIC TV Light Bars
        "H6047" => 12,
        "H604A" => 20,  // RGBIC TV Backlight
        "H604B" => 20,
        "H604C" => 20,
        "H6049" => 12,  // DreamView G1
        "H6043" => 15,  // DreamView TV Backlight
        "H6062" => 10,  // Glide Wall Light
        "H610A" => 12,  // Glide Lively Wall Light (6 bars × 2 addressable segments per bar)
        "H610B" => 12,
        "H6601" => 10,  // Curtain Lights
        "H61A0" => 19,  // RGBIC Neon Rope Light (10ft = 19 segments)
        _ => 0
    };

    /// <summary>
    /// Get segment count, trying SKU first, then falling back to device name matching.
    /// </summary>
    public static int GetSegmentCount(GoveeDeviceConfig dev)
    {
        int count = GetSegmentCount(dev.Sku);
        if (count > 0) return count;

        // Fallback: match by product name (for devices added before SKU detection)
        var name = dev.Name?.ToLowerInvariant() ?? "";
        if (name.Contains("flow") && name.Contains("light bar")) return 15;
        if (name.Contains("dreamview") && name.Contains("g1")) return 12;
        if (name.Contains("tv backlight")) return 20;
        if (name.Contains("tv light bar")) return 12;
        if (name.Contains("neon rope")) return 19;                          // H61A0
        if (name.Contains("glide") && !name.Contains("lively")) return 10; // H6062 original Glide
        if (name.Contains("glide") && name.Contains("lively")) return 24; // H610A: 6 bars × 4 LEDs
        return 0;
    }

    public static bool SupportsSegments(GoveeDeviceConfig dev) => GetSegmentCount(dev) > 0;

    /// <summary>
    /// Returns true for devices that are physically two separate units (left + right)
    /// sharing one IP. In mirror mode, each panel should show the same pattern.
    /// </summary>
    public static bool IsPairedDevice(string? sku) => sku?.ToUpperInvariant() switch
    {
        "H610A" => true,  // Glide Lively: 2 panels × 12 LEDs
        "H610B" => true,
        "H6056" => true,  // Flow Plus: 2 light bars
        "H6057" => true,
        "H6046" => true,  // RGBIC TV Light Bars (pair)
        "H6047" => true,
        _ => false
    };

    /// <summary>
    /// Map Govee SKU to a friendly product name. Falls back to SKU if unknown.
    /// </summary>
    public static string GetProductName(string sku)
    {
        if (string.IsNullOrEmpty(sku)) return "";
        // Common Govee LAN-enabled models
        return sku.ToUpperInvariant() switch
        {
            "H6056" => "Glide Wall Light",
            "H6057" => "Glide Wall Light",
            "H6058" => "Glide Hexa Panels",
            "H6059" => "Glide Hexa Panels",
            "H6061" => "Glide Y Lights",
            "H6062" => "Glide Y Lights",
            "H6065" => "Glide Tri Panels",
            "H6066" => "Glide Tri Panels",
            "H6072" => "Glide Hexa Pro",
            "H6076" => "Glide Tri Pro",
            "H61A0" => "RGBIC Neon Rope Light",
            "H6601" => "LED Strip Light",
            "H6602" => "LED Strip Light",
            "H6604" => "RGBIC Strip Light",
            "H6609" => "RGBIC Strip Light",
            "H610A" => "Glide Lively Wall Light",
            "H610B" => "Glide Lively Wall Light",
            "H6110" => "RGBICWW Strip Light",
            "H6117" => "RGBICWW Strip Light",
            "H6046" => "RGBIC TV Light Bar",
            "H6047" => "RGBIC TV Light Bar",
            "H604A" => "RGBIC TV Backlight",
            "H604B" => "RGBIC TV Backlight",
            "H604C" => "RGBIC TV Backlight",
            "H604D" => "RGBICWW TV Backlight",
            "H6043" => "DreamView TV Backlight",
            "H6049" => "DreamView G1 Gaming Light",
            "H6199" => "LED Bulb",
            "H6003" => "Table Lamp",
            "H6008" => "Table Lamp",
            "H6052" => "Floor Lamp",
            "H6053" => "Floor Lamp",
            "H6054" => "Floor Lamp",
            "H7060" => "Outdoor Lights",
            "H7061" => "Outdoor Lights",
            "H7062" => "Outdoor Lights",
            "H7065" => "Outdoor String Lights",
            "H705A" => "Flood Light",
            "H705B" => "Flood Light",
            "H6089" => "Aura Table Lamp",
            "H6087" => "Aura Glow",
            "H6051" => "Lyra Floor Lamp",
            _ => sku,  // Unknown model — show raw SKU
        };
    }

    // Keep backward compat
    private static string ParseGoveeName(string json, string fallbackIp)
        => ParseScanResponse(json, fallbackIp).Name;

    public void Dispose()
    {
        _disposed = true;
    }
}
