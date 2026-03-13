using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AmpUp;

public class HAEntity
{
    public string EntityId { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Domain => EntityId.Contains('.') ? EntityId.Split('.')[0] : "";
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
            var resp = await _http.GetAsync("/api/");
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
            var resp = await _http.GetAsync("/api/states");
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
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"/api/services/{domain}/{service}", content);

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
    {
        if (_http == null) return;
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            // Don't await — let it fly
            _http.PostAsync($"/api/services/{domain}/{service}", content).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Logger.Log($"HA fire-and-forget {domain}.{service} failed: {t.Exception?.InnerException?.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);
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
