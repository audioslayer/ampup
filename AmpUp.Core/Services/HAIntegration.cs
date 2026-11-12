using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AmpUp.Core;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

public class HAEntity
{
    public string EntityId { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Domain => EntityId.Contains('.') ? EntityId.Split('.')[0] : "";
}

/// <summary>Light entity details for the Room > Devices discovery dialog. Every attribute is optional.</summary>
public class HALightInfo
{
    public string EntityId { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Area { get; set; } = "";
    public string State { get; set; } = ""; // "on" | "off" | "unavailable" | ...
    public bool IsOn => string.Equals(State, "on", StringComparison.OrdinalIgnoreCase);
    public bool IsUnavailable => string.Equals(State, "unavailable", StringComparison.OrdinalIgnoreCase);
    public bool SupportsColor { get; set; }
    public bool SupportsColorTemp { get; set; }
    public bool SupportsBrightness { get; set; }
    public int? Brightness { get; set; } // 0-255 when on
    public (byte R, byte G, byte B)? Rgb { get; set; }
}

public class HAIntegration : IDisposable
{
    private HttpClient? _http;
    private HomeAssistantConfig _config;
    private bool _available = false;

    public bool IsAvailable => _available;
    public List<HAEntity> CachedEntities { get; private set; } = new();

    public HAIntegration(HomeAssistantConfig config)
    {
        _config = config;
        InitClient();
    }

    private void InitClient()
    {
        _http?.Dispose();
        _http = null;
        _available = false;

        if (!_config.Enabled || string.IsNullOrWhiteSpace(_config.Url))
            return;

        _http = new HttpClient
        {
            BaseAddress = new Uri(_config.Url.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(15)
        };

        if (!string.IsNullOrWhiteSpace(_config.Token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.Token.Trim());
        }

        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void UpdateConfig(HomeAssistantConfig config)
    {
        _config = config;
        InitClient();
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (_http == null) return false;

        try
        {
            using var resp = await _http.GetAsync("/api/");
            _available = resp.IsSuccessStatusCode;
            return _available;
        }
        catch (Exception ex)
        {
            Logger.Log($"HA connection test failed: {ex.Message}");
            _available = false;
            return false;
        }
    }

    public async Task<List<HAEntity>> GetEntitiesAsync(string? domain = null)
    {
        var result = new List<HAEntity>();
        if (_http == null) return result;

        try
        {
            using var resp = await _http.GetAsync("/api/states");
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            var states = JArray.Parse(json);

            foreach (var state in states)
            {
                var entityId = state["entity_id"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(entityId)) continue;

                if (domain != null && !entityId.StartsWith(domain + "."))
                    continue;

                var attrs = state["attributes"] as JObject;
                var friendlyName = attrs?["friendly_name"]?.ToString() ?? entityId;

                result.Add(new HAEntity
                {
                    EntityId = entityId,
                    FriendlyName = friendlyName
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"HA GetEntities failed: {ex.Message}");
        }

        return result;
    }

    private async Task CallServiceInternalAsync(string domain, string service, object data)
    {
        if (_http == null) return;

        try
        {
            var json = JsonConvert.SerializeObject(data);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"/api/services/{domain}/{service}", content);

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"HA service call {domain}.{service} returned {resp.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"HA service call {domain}.{service} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fire-and-forget service call — sends HTTP request without waiting for HA's full response.
    /// Used by knob controls where HA's slow device confirmation (2-10s) would block the throttle loop.
    /// </summary>
    private void CallServiceFireAndForget(string domain, string service, object data)
        => _ = CallServiceFireAndForgetAsync(domain, service, data);

    private async Task CallServiceFireAndForgetAsync(string domain, string service, object data)
    {
        if (_http == null) return;
        try
        {
            var json = JsonConvert.SerializeObject(data);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            // Don't await — let it fly
            using var response = await _http.PostAsync($"/api/services/{domain}/{service}", content);
            if (!response.IsSuccessStatusCode)
                Logger.Log($"HA fire-and-forget {domain}.{service} returned {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger.Log($"HA fire-and-forget {domain}.{service} error: {ex.Message}");
        }
    }

    // Knob controls (fire-and-forget for responsiveness)

    public Task SetLightBrightnessAsync(string entityId, float value)
    {
        CallServiceFireAndForget("light", "turn_on", new
        {
            entity_id = entityId,
            brightness = (int)(Math.Clamp(value, 0f, 1f) * 255)
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Set a light's RGB color and brightness in one call (for room sync).
    /// </summary>
    public void SetLightColorFireAndForget(string entityId, byte r, byte g, byte b, int brightness = 255)
    {
        CallServiceFireAndForget("light", "turn_on", new
        {
            entity_id = entityId,
            rgb_color = new[] { (int)r, (int)g, (int)b },
            brightness,
        });
    }

    public Task SetMediaVolumeAsync(string entityId, float value)
    {
        CallServiceFireAndForget("media_player", "volume_set", new
        {
            entity_id = entityId,
            volume_level = Math.Clamp(value, 0f, 1f)
        });
        return Task.CompletedTask;
    }

    public Task SetCoverPositionAsync(string entityId, float value)
    {
        CallServiceFireAndForget("cover", "set_cover_position", new
        {
            entity_id = entityId,
            position = (int)(Math.Clamp(value, 0f, 1f) * 100)
        });
        return Task.CompletedTask;
    }

    public Task SetFanSpeedAsync(string entityId, float value)
    {
        CallServiceFireAndForget("fan", "turn_on", new
        {
            entity_id = entityId,
            percentage = (int)(Math.Clamp(value, 0f, 1f) * 100)
        });
        return Task.CompletedTask;
    }

    // Button actions

    public async Task ToggleEntityAsync(string entityId)
    {
        await CallServiceInternalAsync("homeassistant", "toggle", new
        {
            entity_id = entityId
        });
    }

    public async Task ActivateSceneAsync(string entityId)
    {
        await CallServiceInternalAsync("scene", "turn_on", new
        {
            entity_id = entityId
        });
    }

    public async Task SetLightColorAsync(string entityId, byte r, byte g, byte b, int brightness = 255)
    {
        await CallServiceInternalAsync("light", "turn_on", new
        {
            entity_id = entityId,
            rgb_color = new[] { (int)r, (int)g, (int)b },
            brightness = Math.Clamp(brightness, 1, 255),
        });
    }

    /// <summary>
    /// Set several lights to the same RGB color in a single light.turn_on call (room sync batching).
    /// </summary>
    public async Task SetLightsColorAsync(string[] entityIds, byte r, byte g, byte b, int brightness = 255)
    {
        if (entityIds.Length == 0) return;
        await CallServiceInternalAsync("light", "turn_on", new
        {
            entity_id = entityIds,
            rgb_color = new[] { (int)r, (int)g, (int)b },
            brightness = Math.Clamp(brightness, 1, 255),
        });
    }

    /// <summary>
    /// Read on/off + brightness (0-255) for light entities in one /api/states request.
    /// Entities missing from HA are omitted. Returns null when HA could not be reached.
    /// </summary>
    public async Task<Dictionary<string, (bool On, int Brightness)>?> GetLightStatesAsync(IEnumerable<string> entityIds)
    {
        if (_http == null) return null;
        var wanted = new HashSet<string>(entityIds, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, (bool On, int Brightness)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var resp = await _http.GetAsync("/api/states");
            if (!resp.IsSuccessStatusCode) return null;
            var states = JArray.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var state in states)
            {
                var entityId = state["entity_id"]?.ToString() ?? "";
                if (!wanted.Contains(entityId)) continue;
                bool on = string.Equals(state["state"]?.ToString(), "on", StringComparison.OrdinalIgnoreCase);
                int brightness = state["attributes"]?["brightness"]?.Type == JTokenType.Integer
                    ? (int)state["attributes"]!["brightness"]!
                    : (on ? 255 : 0);
                result[entityId] = (on, Math.Clamp(brightness, 0, 255));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"HA GetLightStates failed: {ex.Message}");
            return null;
        }
        return result;
    }

    /// <summary>
    /// All light.* entities with capabilities and current state, plus their HA area when the
    /// template API can resolve it (areas aren't in /api/states). Missing attributes are tolerated.
    /// </summary>
    public async Task<List<HALightInfo>> GetLightDetailsAsync()
    {
        var result = new List<HALightInfo>();
        if (_http == null) return result;

        try
        {
            using var resp = await _http.GetAsync("/api/states");
            if (!resp.IsSuccessStatusCode) return result;
            var states = JArray.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var state in states)
            {
                var entityId = state["entity_id"]?.ToString() ?? "";
                if (!entityId.StartsWith("light.")) continue;
                var attrs = state["attributes"] as JObject;
                var info = new HALightInfo
                {
                    EntityId = entityId,
                    FriendlyName = attrs?["friendly_name"]?.ToString() ?? entityId,
                    State = state["state"]?.ToString() ?? "",
                };

                if (attrs?["supported_color_modes"] is JArray modes)
                {
                    foreach (var m in modes)
                    {
                        switch (m.ToString())
                        {
                            case "rgb": case "rgbw": case "rgbww": case "hs": case "xy":
                                info.SupportsColor = true; info.SupportsBrightness = true; break;
                            case "color_temp":
                                info.SupportsColorTemp = true; info.SupportsBrightness = true; break;
                            case "brightness": case "white":
                                info.SupportsBrightness = true; break;
                        }
                    }
                }

                if (attrs?["brightness"]?.Type == JTokenType.Integer)
                    info.Brightness = Math.Clamp((int)attrs["brightness"]!, 0, 255);
                if (attrs?["rgb_color"] is JArray rgb && rgb.Count >= 3
                    && rgb.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                    info.Rgb = ((byte)Math.Clamp((int)rgb[0], 0, 255),
                                (byte)Math.Clamp((int)rgb[1], 0, 255),
                                (byte)Math.Clamp((int)rgb[2], 0, 255));
                result.Add(info);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"HA GetLightDetails failed: {ex.Message}");
            return result;
        }

        // Areas: best effort through the template API (older HA / restricted tokens just skip).
        try
        {
            const string template =
                "{% for s in states.light %}{{ s.entity_id }}\t{{ area_name(s.entity_id) or '' }}\n{% endfor %}";
            var json = JsonConvert.SerializeObject(new { template });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("/api/template", content);
            if (resp.IsSuccessStatusCode)
            {
                var areas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in (await resp.Content.ReadAsStringAsync()).Split('\n'))
                {
                    var parts = line.Split('\t');
                    if (parts.Length == 2 && parts[1].Trim().Length > 0)
                        areas[parts[0].Trim()] = parts[1].Trim();
                }
                foreach (var info in result)
                    if (areas.TryGetValue(info.EntityId, out var area))
                        info.Area = area;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"HA light areas unavailable: {ex.Message}");
        }

        return result;
    }

    public async Task SetLightPowerAsync(string entityId, bool on)
    {
        await CallServiceInternalAsync("light", on ? "turn_on" : "turn_off", new
        {
            entity_id = entityId
        });
    }

    public async Task SetLightColorTemperatureAsync(string entityId, int kelvin, int brightness = 255)
    {
        await CallServiceInternalAsync("light", "turn_on", new
        {
            entity_id = entityId,
            color_temp_kelvin = Math.Clamp(kelvin, 2000, 9000),
            brightness = Math.Clamp(brightness, 1, 255),
        });
    }

    public async Task CallServiceAsync(string domain, string service, string entityId)
    {
        await CallServiceInternalAsync(domain, service, new
        {
            entity_id = entityId
        });
    }

    /// <summary>
    /// Handles a knob target like "ha_light:light.office_lamp" with a 0.0-1.0 value.
    /// Routes to the correct service call based on the target prefix.
    /// </summary>
    public async Task HandleKnobAsync(string target, float value)
    {
        var (type, entityId) = ParseTarget(target);

        switch (type)
        {
            case "light":
                await SetLightBrightnessAsync(entityId, value);
                break;
            case "media":
                await SetMediaVolumeAsync(entityId, value);
                break;
            case "cover":
                await SetCoverPositionAsync(entityId, value);
                break;
            case "fan":
                await SetFanSpeedAsync(entityId, value);
                break;
            default:
                Logger.Log($"HA unknown knob target type: {type} for {target}");
                break;
        }
    }

    /// <summary>
    /// Parse target string like "ha_light:light.office_lamp" → (type, entityId).
    /// Returns ("light", "light.office_lamp") so the type can route to the right method.
    /// </summary>
    public static (string type, string entityId) ParseTarget(string target)
    {
        var parts = target.Split(':', 2);
        if (parts.Length == 2 && parts[0].StartsWith("ha_"))
            return (parts[0].Substring(3), parts[1]); // "light", "light.entity_id"
        return ("", target);
    }

    public async Task<bool> RefreshEntitiesAsync()
    {
        var connected = await TestConnectionAsync();
        if (!connected)
        {
            CachedEntities = new List<HAEntity>();
            return false;
        }

        CachedEntities = await GetEntitiesAsync();
        return true;
    }

    public List<HAEntity> GetCachedEntitiesByDomain(string domain)
    {
        return CachedEntities.Where(e => e.Domain == domain).ToList();
    }

    public List<HAEntity> GetCachedEntitiesByDomains(string[] domains)
    {
        return CachedEntities.Where(e => domains.Contains(e.Domain)).ToList();
    }

    public void Dispose()
    {
        _http?.Dispose();
    }
}
