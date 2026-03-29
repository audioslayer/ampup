using System.Net;
using System.Net.Sockets;
using System.Text;
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
    private readonly Dictionary<string, (byte R, byte G, byte B)> _lastSent = new();

    // Rate limiter: Govee LAN max ~10 sends/sec, but we can push closer to that limit
    // Send up to 30 FPS — matches DreamSync rate, safe for all known Govee LAN devices
    private readonly Dictionary<string, long> _lastSendTick = new();
    private const long MinTicksBetweenSends = TimeSpan.TicksPerMillisecond * 33; // ~30 FPS cap

    public AmbienceSync(AmbienceConfig config)
    {
        _config = config;
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

            // Skip devices that are off, under manual control, or with sync disabled
            if (!device.PoweredOn || device.SyncMode == "off" || IsSyncPaused(device.Ip)) continue;

            // Mirror the global average color across all LEDs
            var (r, g, b) = DeriveColor(linear45);
            (r, g, b) = ApplySettings(r, g, b, cfg);

            // Rate limit: skip if sent too recently
            var now = DateTime.UtcNow.Ticks;
            if (_lastSendTick.TryGetValue(device.Ip, out long lastTick) &&
                now - lastTick < MinTicksBetweenSends)
                continue;

            // Delta threshold: skip if color barely changed
            if (_lastSent.TryGetValue(device.Ip, out var prev))
            {
                int dr = Math.Abs(r - prev.R);
                int dg = Math.Abs(g - prev.G);
                int db = Math.Abs(b - prev.B);
                // Send if any channel changed by more than 1, or as keepalive every 200ms
                bool significantChange = dr > 1 || dg > 1 || db > 1;
                bool keepalive = now - lastTick > TimeSpan.TicksPerMillisecond * 200;
                if (!significantChange && !keepalive) continue;
            }

            _lastSent[device.Ip] = ((byte)r, (byte)g, (byte)b);
            _lastSendTick[device.Ip] = now;

            string ip = device.Ip;
            _ = Task.Run(() => SendGoveeColor(ip, r, g, b));
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
            Logger.Log("Govee scan: bound to port 4002");

            // Join multicast group so we receive responses sent to the group address
            try
            {
                udp.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
                Logger.Log("Govee scan: joined multicast group 239.255.255.250");
            }
            catch (Exception mex)
            {
                Logger.Log($"Govee scan: multicast join failed (non-fatal): {mex.Message}");
            }

            // Send discovery to multicast address (send twice — some devices need a nudge)
            var msg = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"scan\",\"data\":{\"account_topic\":\"reserve\"}}}");
            await udp.SendAsync(msg, msg.Length, "239.255.255.250", 4001);
            Logger.Log("Govee scan: multicast sent to 239.255.255.250:4001");

            await Task.Delay(500, ct);
            await udp.SendAsync(msg, msg.Length, "239.255.255.250", 4001);
            Logger.Log("Govee scan: multicast re-sent to 239.255.255.250:4001");

            // Also send directly to known device IPs as fallback (some networks block multicast)
            foreach (var known in _config.GoveeDevices)
            {
                if (!string.IsNullOrWhiteSpace(known.Ip))
                {
                    try
                    {
                        await udp.SendAsync(msg, msg.Length, known.Ip, 4001);
                        Logger.Log($"Govee scan: unicast sent to {known.Ip}:4001");
                    }
                    catch { }
                }
            }

            // Also broadcast on the local subnet (some bulbs only respond to broadcast)
            try
            {
                await udp.SendAsync(msg, msg.Length, "255.255.255.255", 4001);
                Logger.Log("Govee scan: broadcast sent to 255.255.255.255:4001");
            }
            catch (Exception bex)
            {
                Logger.Log($"Govee scan: broadcast failed (non-fatal): {bex.Message}");
            }

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
                        catch (Exception rx)
                        {
                            if (rx is not System.Net.Sockets.SocketException)
                                Logger.Log($"Govee scan receive error: {rx.Message}");
                            return (Data: (byte[]?)null, Ep: (IPEndPoint?)null);
                        }
                    }, ct);

                    if (result.Data == null || result.Ep == null) break;

                    string json = Encoding.UTF8.GetString(result.Data);
                    string ip = result.Ep.Address.ToString();
                    Logger.Log($"Govee scan response from {ip}: {json}");
                    var (name, sku, deviceMac) = ParseScanResponse(json, ip);
                    Logger.Log($"Govee parsed: name={name}, sku={sku}, mac={deviceMac}");

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

    // ── Color derivation ────────────────────────────────────────────

    private static (int R, int G, int B) DeriveColor(byte[] linear45)
    {
        // Average all 15 LEDs (5 knobs × 3 LEDs) for a global room color
        int rSum = 0, gSum = 0, bSum = 0;
        for (int i = 0; i < 15; i++)
        {
            rSum += linear45[i * 3 + 0];
            gSum += linear45[i * 3 + 1];
            bSum += linear45[i * 3 + 2];
        }
        return (rSum / 15, gSum / 15, bSum / 15);
    }

    private static (int R, int G, int B) ApplySettings(int r, int g, int b, AmbienceConfig cfg)
    {
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

        foreach (var device in cfg.GoveeDevices)
        {
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
        _ = Task.Run(() => SendGoveeBrightness(ip, value));
    }

    // ── LAN control commands (all send to device IP:4003) ──────────

    private const int LanControlPort = 4003;

    // Track last known power state per device IP for toggle
    private static readonly Dictionary<string, bool> _devicePowerState = new();

    /// <summary>
    /// Send a raw LAN UDP command to a Govee device.
    /// </summary>
    private static async Task SendLanCommand(string ip, string json)
    {
        byte[] data = Encoding.UTF8.GetBytes(json);
        using var udp = new UdpClient();
        await udp.SendAsync(data, data.Length, ip, LanControlPort);
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
            Logger.Log($"Govee status ({ip}): on={on} bright={brightness} rgb=({r},{g},{b}) temp={colorTemp}K");
            return (on, brightness, r, g, b, colorTemp);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee status query failed ({ip}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Toggles a Govee device on/off via LAN UDP. Queries real state first, falls back to cached state.
    /// </summary>
    public static async Task SendToggleAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            // Try to get real state first
            var status = await GetDeviceStatusAsync(ip);
            bool currentlyOn;
            if (status.HasValue)
            {
                currentlyOn = status.Value.On;
            }
            else
            {
                // Fallback to cached state; assume ON if unknown
                currentlyOn = _devicePowerState.TryGetValue(ip, out bool last) ? last : true;
            }

            bool newState = !currentlyOn;
            _devicePowerState[ip] = newState;
            await SendTurnAsync(ip, newState);
            Logger.Log($"Govee toggle ({ip}): {(newState ? "on" : "off")}");
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
    public static async Task SendColorAsync(string ip, byte r, byte g, byte b)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        try
        {
            string json = $"{{\"msg\":{{\"cmd\":\"colorwc\",\"data\":{{\"color\":{{\"r\":{r},\"g\":{g},\"b\":{b}}},\"colorTemInKelvin\":0,\"duration\":0}}}}}}";
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
        "H6056" => 6,   // Flow Plus Light Bar
        "H6057" => 6,   // Flow Plus Light Bar
        "H6046" => 12,  // RGBIC TV Light Bars
        "H6047" => 12,
        "H604A" => 20,  // RGBIC TV Backlight
        "H604B" => 20,
        "H604C" => 20,
        "H6049" => 12,  // DreamView G1
        "H6043" => 15,  // DreamView TV Backlight
        "H6062" => 10,  // Glide Wall Light
        "H6601" => 10,  // Curtain Lights
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
        if (name.Contains("flow") && name.Contains("light bar")) return 6;
        if (name.Contains("dreamview") && name.Contains("g1")) return 12;
        if (name.Contains("tv backlight")) return 20;
        if (name.Contains("tv light bar")) return 12;
        if (name.Contains("glide")) return 10;
        return 0;
    }

    public static bool SupportsSegments(GoveeDeviceConfig dev) => GetSegmentCount(dev) > 0;

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
            "H6601" => "LED Strip Light",
            "H6602" => "LED Strip Light",
            "H6604" => "RGBIC Strip Light",
            "H6609" => "RGBIC Strip Light",
            "H610A" => "RGBIC Strip Light Pro",
            "H610B" => "RGBIC Strip Light Pro",
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
