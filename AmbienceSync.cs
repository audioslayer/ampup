using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AmpUp;

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

    // Rate limiter: max 10 sends/sec per device = 1 send per 100ms
    // Our frame rate is 20 FPS (50ms tick), so skip every other frame per device
    private readonly Dictionary<string, long> _lastSendTick = new();
    private const long MinTicksBetweenSends = TimeSpan.TicksPerMillisecond * 100;

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
    public void OnFrame(byte[] linear45)
    {
        if (_disposed) return;

        AmbienceConfig cfg;
        lock (_lock) cfg = _config;

        if (!cfg.GoveeEnabled || cfg.GoveeDevices.Count == 0) return;

        foreach (var device in cfg.GoveeDevices)
        {
            if (string.IsNullOrWhiteSpace(device.Ip)) continue;

            // Always mirror the global average color across all LEDs
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
                // Send if any channel changed by more than 5, or as keepalive every 500ms
                bool significantChange = dr > 5 || dg > 5 || db > 5;
                bool keepalive = now - lastTick > TimeSpan.TicksPerMillisecond * 500;
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

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        // Also bind to port 4002 to receive responses
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Any, 4002));
        listener.Client.ReceiveTimeout = 3000;

        // Send discovery broadcast
        var msg = Encoding.UTF8.GetBytes("{\"msg\":{\"cmd\":\"scan\",\"data\":{\"account_topic\":\"reserve\"}}}");
        try
        {
            await udp.SendAsync(msg, msg.Length, "239.255.255.250", 4001);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee scan broadcast failed: {ex.Message}");
            return results;
        }

        // Collect responses for 3 seconds
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            try
            {
                listener.Client.ReceiveTimeout = Math.Max(100, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                var result = await Task.Run(() =>
                {
                    try
                    {
                        IPEndPoint? ep = null;
                        var data = listener.Receive(ref ep);
                        return (Data: data, Ep: ep);
                    }
                    catch { return (Data: (byte[]?)null, Ep: (IPEndPoint?)null); }
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
            catch { break; }
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
            if (string.IsNullOrWhiteSpace(device.Ip)) continue;
            string ip = device.Ip;
            _ = Task.Run(() => SendGoveeBrightness(ip, value));
        }
    }

    /// <summary>
    /// Sets Govee brightness (0.0–1.0) on a single specific device by IP.
    /// Called when a knob is assigned target "govee:IP".
    /// </summary>
    public void SetBrightnessForDevice(string ip, float normalized)
    {
        if (_disposed || string.IsNullOrWhiteSpace(ip)) return;
        int value = (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 100);
        _ = Task.Run(() => SendGoveeBrightness(ip, value));
    }

    // ── Static helpers for button actions ──────────────────────────

    // Track last known power state per device IP for toggle
    private static readonly Dictionary<string, bool> _devicePowerState = new();

    /// <summary>
    /// Toggles a Govee device on/off via LAN UDP. Used by button action govee_toggle.
    /// Tracks last state to alternate between on and off.
    /// </summary>
    public static async Task SendToggleAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;
        // Assume starts ON if unknown; toggle to opposite
        bool currentlyOn = _devicePowerState.TryGetValue(ip, out bool last) ? last : true;
        bool newState = !currentlyOn;
        _devicePowerState[ip] = newState;
        try
        {
            string json = $"{{\"msg\":{{\"cmd\":\"turn\",\"data\":{{\"value\":{(newState ? 1 : 0)}}}}}}}";
            byte[] data = Encoding.UTF8.GetBytes(json);
            using var udp = new UdpClient();
            await udp.SendAsync(data, data.Length, ip, 4001);
            Logger.Log($"Govee toggle ({ip}): {(newState ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee toggle failed ({ip}): {ex.Message}");
        }
    }

    /// <summary>
    /// Sets a Govee device to a specific color via LAN UDP. Used by button action govee_color.
    /// </summary>
    public static async Task SendColorAsync(string ip, byte r, byte g, byte b)
    {
        await SendGoveeColor(ip, r, g, b);
    }

    private static async Task SendGoveeBrightness(string ip, int value)
    {
        try
        {
            string json = $"{{\"msg\":{{\"cmd\":\"brightness\",\"data\":{{\"value\":{value}}}}}}}";
            byte[] data = Encoding.UTF8.GetBytes(json);
            using var udp = new UdpClient();
            await udp.SendAsync(data, data.Length, ip, 4001);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee brightness failed ({ip}): {ex.Message}");
        }
    }

    // ── Govee UDP send ──────────────────────────────────────────────

    private static async Task SendGoveeColor(string ip, int r, int g, int b)
    {
        try
        {
            string json = $"{{\"msg\":{{\"cmd\":\"colorwc\",\"data\":{{\"color\":{{\"r\":{r},\"g\":{g},\"b\":{b}}},\"colorTemInKelvin\":0}}}}}}";
            byte[] data = Encoding.UTF8.GetBytes(json);

            using var udp = new UdpClient();
            await udp.SendAsync(data, data.Length, ip, 4001);
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee send failed ({ip}): {ex.Message}");
        }
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
