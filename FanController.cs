using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WolfMixer;

public class FanControlSensor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float Value { get; set; }
    public bool IsControl { get; set; }
}

public class FanController : IDisposable
{
    private HttpClient? _http;
    private FanControlConfig _config;
    private bool _available = false;

    public bool IsAvailable => _available;

    public FanController(FanControlConfig config)
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
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public void UpdateConfig(FanControlConfig config)
    {
        _config = config;
        InitClient();
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (_http == null) return false;

        try
        {
            var resp = await _http.GetAsync("/api/sensors");
            _available = resp.IsSuccessStatusCode;
            return _available;
        }
        catch (Exception ex)
        {
            Logger.Log($"FanControl connection test failed: {ex.Message}");
            _available = false;
            return false;
        }
    }

    public async Task<List<FanControlSensor>> GetControllersAsync()
    {
        var result = new List<FanControlSensor>();
        if (_http == null) return result;

        try
        {
            var resp = await _http.GetAsync("/api/controls");
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            var items = JArray.Parse(json);

            foreach (var item in items)
            {
                result.Add(new FanControlSensor
                {
                    Id = item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Value = item["value"]?.Value<float>() ?? 0f,
                    IsControl = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"FanControl GetControllers failed: {ex.Message}");
        }

        return result;
    }

    public async Task<List<FanControlSensor>> GetSensorsAsync()
    {
        var result = new List<FanControlSensor>();
        if (_http == null) return result;

        try
        {
            var resp = await _http.GetAsync("/api/sensors");
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            var items = JArray.Parse(json);

            foreach (var item in items)
            {
                result.Add(new FanControlSensor
                {
                    Id = item["id"]?.ToString() ?? "",
                    Name = item["name"]?.ToString() ?? "",
                    Value = item["value"]?.Value<float>() ?? 0f,
                    IsControl = false
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"FanControl GetSensors failed: {ex.Message}");
        }

        return result;
    }

    public async Task SetSpeedAsync(string controlId, float value)
    {
        if (_http == null) return;

        try
        {
            int percent = (int)(Math.Clamp(value, 0f, 1f) * 100);
            var body = JsonConvert.SerializeObject(new { value = percent, @override = true });
            var content = new System.Net.Http.StringContent(
                body, System.Text.Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"/api/fanControl/{controlId}")
            {
                Content = content
            };

            var resp = await _http.SendAsync(request);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"FanControl SetSpeed {controlId} returned {resp.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"FanControl SetSpeed {controlId} failed: {ex.Message}");
        }
    }

    public static string ParseTarget(string target)
    {
        var idx = target.IndexOf(':');
        return idx >= 0 ? target.Substring(idx + 1) : target;
    }

    public void Dispose()
    {
        _http?.Dispose();
    }
}
