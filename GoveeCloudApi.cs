using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AmpUp;

// Data models
public class GoveeDeviceInfo
{
    public string Device { get; set; } = "";
    public string Sku { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
    // Full capability JSON for extracting scene options, segment info, etc.
    public JArray? RawCapabilities { get; set; }
}

public class GoveeDeviceState
{
    public bool Online { get; set; }
    public bool On { get; set; }
    public int Brightness { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public int ColorTemp { get; set; }
}

public class GoveeScene
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
}

public class GoveeCapability
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("instance")]
    public string Instance { get; set; } = "";

    [JsonProperty("value")]
    public object Value { get; set; } = 0;
}

public class GoveeCloudApi : IDisposable
{
    private const string BaseUrl = "https://openapi.api.govee.com";
    private const int MinRequestIntervalMs = 600;

    private readonly HttpClient _http;
    private string _apiKey;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly SemaphoreSlim _rateLock = new(1, 1);

    public GoveeCloudApi(string apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient();
        _http.BaseAddress = new Uri(BaseUrl);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void UpdateApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }

    // ── Rate limiter ─────────────────────────────────────────────────────────

    private async Task ThrottleAsync()
    {
        await _rateLock.WaitAsync();
        try
        {
            var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
            if (elapsed < MinRequestIntervalMs)
                await Task.Delay((int)(MinRequestIntervalMs - elapsed));
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _rateLock.Release();
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("Govee-API-Key", _apiKey);
        if (body != null)
        {
            var json = JsonConvert.SerializeObject(body);
            req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
        return req;
    }

    // ── API calls ─────────────────────────────────────────────────────────────

    public async Task<List<GoveeDeviceInfo>> GetDevicesAsync()
    {
        try
        {
            await ThrottleAsync();
            using var req = BuildRequest(HttpMethod.Get, "/router/api/v1/user/devices");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Govee] GetDevices failed: {resp.StatusCode}");
                return new();
            }

            var json = await resp.Content.ReadAsStringAsync();
            var root = JObject.Parse(json);
            var data = root["data"] as JArray ?? new JArray();
            var result = new List<GoveeDeviceInfo>();

            foreach (var item in data)
            {
                var caps = new List<string>();
                if (item["capabilities"] is JArray capArr)
                    foreach (var cap in capArr)
                        if (cap["type"]?.ToString() is string t)
                            caps.Add(t);

                result.Add(new GoveeDeviceInfo
                {
                    Device = item["device"]?.ToString() ?? "",
                    Sku = item["sku"]?.ToString() ?? "",
                    DeviceName = item["deviceName"]?.ToString() ?? "",
                    Capabilities = caps,
                    RawCapabilities = item["capabilities"] as JArray,
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Govee] GetDevicesAsync exception: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> ControlDeviceAsync(string device, string sku, GoveeCapability capability)
    {
        try
        {
            await ThrottleAsync();
            var body = new
            {
                requestId = Guid.NewGuid().ToString(),
                payload = new
                {
                    sku,
                    device,
                    capability
                }
            };
            using var req = BuildRequest(HttpMethod.Post, "/router/api/v1/device/control", body);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Govee] ControlDevice failed: {resp.StatusCode}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Govee] ControlDeviceAsync exception: {ex.Message}");
            return false;
        }
    }

    public async Task<GoveeDeviceState?> GetDeviceStateAsync(string device, string sku)
    {
        try
        {
            await ThrottleAsync();
            var body = new
            {
                requestId = Guid.NewGuid().ToString(),
                payload = new { sku, device }
            };
            using var req = BuildRequest(HttpMethod.Post, "/router/api/v1/device/state", body);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Govee] GetDeviceState failed: {resp.StatusCode}");
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            var root = JObject.Parse(json);
            var caps = root["payload"]?["capabilities"] as JArray ?? new JArray();

            var state = new GoveeDeviceState();
            foreach (var cap in caps)
            {
                var instance = cap["instance"]?.ToString();
                var value = cap["state"]?["value"];
                if (value == null) continue;

                switch (instance)
                {
                    case "online":
                        state.Online = value.ToObject<bool>();
                        break;
                    case "powerSwitch":
                        state.On = value.ToObject<int>() == 1;
                        break;
                    case "brightness":
                        state.Brightness = value.ToObject<int>();
                        break;
                    case "colorRgb":
                        var rgb = value.ToObject<int>();
                        state.R = (rgb >> 16) & 0xFF;
                        state.G = (rgb >> 8) & 0xFF;
                        state.B = rgb & 0xFF;
                        break;
                    case "colorTemperatureK":
                        state.ColorTemp = value.ToObject<int>();
                        break;
                }
            }

            return state;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Govee] GetDeviceStateAsync exception: {ex.Message}");
            return null;
        }
    }

    public async Task<List<GoveeScene>> GetDynamicScenesAsync(string device, string sku)
    {
        return await GetScenesInternalAsync(device, sku, "dynamic");
    }

    public async Task<List<GoveeScene>> GetDiyScenesAsync(string device, string sku)
    {
        return await GetScenesInternalAsync(device, sku, "diy");
    }

    private async Task<List<GoveeScene>> GetScenesInternalAsync(string device, string sku, string category)
    {
        try
        {
            await ThrottleAsync();
            using var req = BuildRequest(HttpMethod.Get,
                $"/router/api/v1/device/scenes?device={Uri.EscapeDataString(device)}&sku={Uri.EscapeDataString(sku)}");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[Govee] GetScenes({category}) failed: {resp.StatusCode}");
                return new();
            }

            var json = await resp.Content.ReadAsStringAsync();
            var root = JObject.Parse(json);
            var result = new List<GoveeScene>();

            // Scenes are nested under payload.capabilities[].parameters.options
            var caps = root["payload"]?["capabilities"] as JArray ?? new JArray();
            foreach (var cap in caps)
            {
                var capType = cap["type"]?.ToString() ?? "";
                bool isDiy = capType.Contains("diy", StringComparison.OrdinalIgnoreCase);
                bool isDynamic = capType.Contains("dynamic", StringComparison.OrdinalIgnoreCase);

                bool matches = category == "diy" ? isDiy : isDynamic;
                if (!matches) continue;

                var options = cap["parameters"]?["options"] as JArray ?? new JArray();
                foreach (var opt in options)
                {
                    result.Add(new GoveeScene
                    {
                        Id = opt["value"]?.ToString() ?? "",
                        Name = opt["name"]?.ToString() ?? "",
                        Category = category
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Govee] GetScenesInternalAsync({category}) exception: {ex.Message}");
            return new();
        }
    }

    /// <summary>
    /// Extract scenes from device's raw capabilities (no extra API call needed).
    /// </summary>
    public static List<GoveeScene> ExtractScenesFromCapabilities(JArray? capabilities, string category = "dynamic")
    {
        var result = new List<GoveeScene>();
        if (capabilities == null) return result;

        foreach (var cap in capabilities)
        {
            var capType = cap["type"]?.ToString() ?? "";
            bool isDiy = capType.Contains("diy", StringComparison.OrdinalIgnoreCase);
            bool isDynamic = capType.Contains("dynamic", StringComparison.OrdinalIgnoreCase);

            bool matches = category == "diy" ? isDiy : isDynamic;
            if (!matches) continue;

            var options = cap["parameters"]?["options"] as JArray ?? new JArray();
            foreach (var opt in options)
            {
                result.Add(new GoveeScene
                {
                    Id = opt["value"]?.ToString() ?? "",
                    Name = opt["name"]?.ToString() ?? "",
                    Category = category,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Extract segment count from device's raw capabilities.
    /// </summary>
    public static int ExtractSegmentCount(JArray? capabilities)
    {
        if (capabilities == null) return 0;

        foreach (var cap in capabilities)
        {
            var instance = cap["instance"]?.ToString() ?? "";
            if (instance == "segmentedColorRgb" || instance == "segmentedBrightness")
            {
                // Look for segment array length in parameters
                var fields = cap["parameters"]?["fields"] as JArray;
                if (fields != null)
                {
                    foreach (var field in fields)
                    {
                        if (field["fieldName"]?.ToString() == "segment")
                        {
                            var range = field["range"];
                            if (range != null)
                                return range["max"]?.ToObject<int>() ?? 0;
                            var options = field["options"] as JArray;
                            if (options != null)
                                return options.Count;
                        }
                    }
                }
            }
        }
        return 0;
    }

    /// <summary>
    /// Cross-reference LAN-discovered devices with Cloud API devices to populate friendly names.
    /// Updates GoveeDeviceConfig.Name with the Cloud API deviceName when SKU matches.
    /// </summary>
    public static void EnrichLanDevicesWithCloudNames(List<GoveeDeviceConfig> lanDevices, List<GoveeDeviceInfo> cloudDevices)
    {
        foreach (var lan in lanDevices)
        {
            // Match by device ID (MAC) first, then by SKU
            var match = cloudDevices.FirstOrDefault(c =>
                !string.IsNullOrEmpty(lan.DeviceId) && c.Device == lan.DeviceId)
                ?? cloudDevices.FirstOrDefault(c =>
                    !string.IsNullOrEmpty(lan.Sku) && c.Sku == lan.Sku);

            if (match != null && !string.IsNullOrWhiteSpace(match.DeviceName))
                lan.Name = match.DeviceName;
        }
    }

    // ── Command builders ──────────────────────────────────────────────────────

    public static GoveeCapability TurnOnOff(bool on) => new()
    {
        Type = "devices.capabilities.on_off",
        Instance = "powerSwitch",
        Value = on ? 1 : 0
    };

    public static GoveeCapability SetBrightness(int percent) => new()
    {
        Type = "devices.capabilities.range",
        Instance = "brightness",
        Value = Math.Clamp(percent, 1, 100)
    };

    public static GoveeCapability SetColor(int r, int g, int b) => new()
    {
        Type = "devices.capabilities.color_setting",
        Instance = "colorRgb",
        Value = ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF)
    };

    public static GoveeCapability SetColorTemp(int kelvin) => new()
    {
        Type = "devices.capabilities.color_setting",
        Instance = "colorTemperatureK",
        Value = Math.Clamp(kelvin, 2000, 9000)
    };

    public static GoveeCapability SetScene(string sceneId, string sceneName) => new()
    {
        Type = "devices.capabilities.dynamic_scene",
        Instance = "lightScene",
        Value = new { id = sceneId, name = sceneName }
    };

    public static GoveeCapability SetDiyScene(string sceneId) => new()
    {
        Type = "devices.capabilities.dynamic_scene",
        Instance = "diyScene",
        Value = new { id = sceneId }
    };

    public static GoveeCapability SetSegmentColor(int segmentIndex, int r, int g, int b) => new()
    {
        Type = "devices.capabilities.segment_color_setting",
        Instance = "segmentedColorRgb",
        Value = new
        {
            segment = new[] { segmentIndex },
            rgb = ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF)
        }
    };

    public static GoveeCapability SetMusicMode(int musicModeId, int sensitivity) => new()
    {
        Type = "devices.capabilities.music_setting",
        Instance = "musicMode",
        Value = new
        {
            musicMode = musicModeId,
            sensitivity = Math.Clamp(sensitivity, 0, 100)
        }
    };

    public void Dispose()
    {
        _http.Dispose();
        _rateLock.Dispose();
    }
}
