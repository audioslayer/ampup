using System.Net.Http;
using System.Text;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

public class CorsairDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "fan", "pump", "cooler", "headset", etc.
}

/// <summary>
/// Best-effort Corsair iCUE integration via the iCUE 5+ local REST API (port 60222).
/// Does not require bundling the iCUE SDK DLL — iCUE must be installed and running.
/// Sends the 15-LED RGB colors from Turn Up hardware to all connected iCUE channels.
/// Also provides fan speed and lighting/mural control.
/// </summary>
public class CorsairSync : IDisposable
{
    private const string BaseUrl = "http://localhost:60222/api/v1";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private bool _available;
    private bool _disposed;

    public bool IsAvailable => _available && !_disposed;

    /// <summary>Discovered iCUE devices after calling GetDevicesAsync.</summary>
    public List<CorsairDevice> Devices { get; private set; } = new();

    public CorsairSync()
    {
        _http = new HttpClient { Timeout = HttpTimeout };
    }

    /// <summary>
    /// Start the integration: check iCUE is running and the REST API is reachable.
    /// Sets IsAvailable accordingly. Safe to call multiple times.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        _ = Task.Run(CheckAvailabilityAsync);
    }

    private async Task CheckAvailabilityAsync()
    {
        if (_disposed) { _available = false; return; }
        try
        {
            // Quick process check first (cheap)
            bool iCueRunning = System.Diagnostics.Process.GetProcesses()
                .Any(p => p.ProcessName.IndexOf("iCUE", StringComparison.OrdinalIgnoreCase) >= 0
                       || p.ProcessName.IndexOf("iCue", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!iCueRunning) { _available = false; return; }

            // Confirm REST API is reachable
            var resp = await _http.GetAsync($"{BaseUrl}/profiles").ConfigureAwait(false);
            _available = resp.IsSuccessStatusCode;
            Logger.Log($"CorsairSync: iCUE REST API {(_available ? "available" : $"returned {(int)resp.StatusCode}")}");
        }
        catch
        {
            _available = false;
        }
    }

    public void Stop()
    {
        _available = false;
    }

    /// <summary>
    /// Send 15 RGB values (5 knobs × 3 LEDs × 3 bytes R,G,B — same layout as Turn Up LED frame)
    /// to all connected iCUE Commander / Link channels via the iCUE REST lighting API.
    /// Failures are silent and set IsAvailable = false until the next Start() call.
    /// </summary>
    public void SyncColors(byte[] rgbColors)
    {
        if (!IsAvailable || rgbColors == null || rgbColors.Length < 45) return;

        // Build a simple JSON array of {ledIndex, r, g, b} objects
        // iCUE REST API PUT /api/v1/lighting body: { "leds": [ { "id": N, "r": R, "g": G, "b": B } ] }
        var sb = new StringBuilder();
        sb.Append("{\"leds\":[");
        for (int i = 0; i < 15; i++)
        {
            int offset = i * 3;
            byte r = rgbColors[offset];
            byte g = rgbColors[offset + 1];
            byte b = rgbColors[offset + 2];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"id\":{i},\"r\":{r},\"g\":{g},\"b\":{b}}}");
        }
        sb.Append("]}");

        // Fire-and-forget — do not await, do not block the RGB frame loop
        _ = SendColorsAsync(sb.ToString());
    }

    private async Task SendColorsAsync(string json)
    {
        if (_disposed) return;
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PutAsync($"{BaseUrl}/lighting", content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"CorsairSync: PUT /lighting returned {(int)resp.StatusCode} — disabling");
                _available = false;
            }
        }
        catch
        {
            _available = false;
        }
    }

    // ── Device discovery ──────────────────────────────────────────────

    /// <summary>
    /// Fetch connected iCUE devices from GET /api/v1/devices.
    /// Returns empty list if iCUE is not running. Results also stored in Devices property.
    /// </summary>
    public async Task<List<CorsairDevice>> GetDevicesAsync()
    {
        if (_disposed) return new();
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/devices").ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return new();

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            // Parse: [{"id":"...","model":"...","type":"..."}, ...]
            // Use simple manual parse to avoid heavy dependencies
            var devices = ParseDeviceList(json);
            Devices = devices;
            return devices;
        }
        catch (Exception ex)
        {
            Logger.Log($"CorsairSync: GetDevicesAsync failed — {ex.Message}");
            return new();
        }
    }

    private static List<CorsairDevice> ParseDeviceList(string json)
    {
        var result = new List<CorsairDevice>();
        try
        {
            // Simple JSON array parse — avoids Newtonsoft dependency in Core
            // Matches: {"id":"...","model":"...","type":"..."}
            var items = SplitJsonArray(json);
            foreach (var item in items)
            {
                var id   = ExtractJsonString(item, "id");
                var name = ExtractJsonString(item, "model");
                if (string.IsNullOrEmpty(name)) name = ExtractJsonString(item, "name");
                var type = ExtractJsonString(item, "type");
                if (!string.IsNullOrEmpty(id))
                    result.Add(new CorsairDevice { Id = id, Name = name, Type = type.ToLowerInvariant() });
            }
        }
        catch { }
        return result;
    }

    // ── Effects ───────────────────────────────────────────────────────

    /// <summary>
    /// Fetch available iCUE effects/murals from GET /api/v1/effects.
    /// Returns effect names. Empty list if unavailable.
    /// </summary>
    public async Task<List<string>> GetEffectsAsync()
    {
        if (_disposed) return new();
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/effects").ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return new();

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseEffectList(json);
        }
        catch (Exception ex)
        {
            Logger.Log($"CorsairSync: GetEffectsAsync failed — {ex.Message}");
            return new();
        }
    }

    private static List<string> ParseEffectList(string json)
    {
        var result = new List<string>();
        try
        {
            var items = SplitJsonArray(json);
            foreach (var item in items)
            {
                var name = ExtractJsonString(item, "name");
                if (string.IsNullOrEmpty(name)) name = ExtractJsonString(item, "id");
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
        }
        catch { }
        return result;
    }

    /// <summary>Apply a named iCUE effect/mural via PUT /api/v1/effects/{effectId}.</summary>
    public async Task ApplyEffectAsync(string effectId)
    {
        if (_disposed || string.IsNullOrEmpty(effectId)) return;
        try
        {
            var resp = await _http.PutAsync($"{BaseUrl}/effects/{Uri.EscapeDataString(effectId)}", null)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                Logger.Log($"CorsairSync: ApplyEffect '{effectId}' returned {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger.Log($"CorsairSync: ApplyEffect failed — {ex.Message}");
        }
    }

    // ── Static color ──────────────────────────────────────────────────

    /// <summary>Send a single static color to all discovered devices' LEDs.</summary>
    public async Task SetStaticColorAllAsync(byte r, byte g, byte b)
    {
        if (_disposed) return;
        // Send to the lighting endpoint — paint all 15 LEDs the same color
        var sb = new StringBuilder();
        sb.Append("{\"leds\":[");
        for (int i = 0; i < 15; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"id\":{i},\"r\":{r},\"g\":{g},\"b\":{b}}}");
        }
        sb.Append("]}");
        await SendStaticAsync(sb.ToString()).ConfigureAwait(false);
    }

    private async Task SendStaticAsync(string json)
    {
        if (_disposed) return;
        try
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PutAsync($"{BaseUrl}/lighting", content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                Logger.Log($"CorsairSync: SetStaticColor returned {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger.Log($"CorsairSync: SetStaticColor failed — {ex.Message}");
        }
    }

    // ── Fan speed ─────────────────────────────────────────────────────

    /// <summary>
    /// Set fan speed (0-100%) for a specific device.
    /// Tries PUT /api/v1/fans/{deviceId} first, then POST /api/v1/devices/{deviceId}/fan as fallback.
    /// Failures are silent (logged only). Fire-and-forget friendly.
    /// </summary>
    public async Task SetFanSpeedAsync(string deviceId, int percent)
    {
        if (_disposed || string.IsNullOrEmpty(deviceId)) return;
        percent = Math.Clamp(percent, 0, 100);
        string body = $"{{\"speed\":{percent}}}";
        try
        {
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.PutAsync($"{BaseUrl}/fans/{Uri.EscapeDataString(deviceId)}", content)
                .ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return;

            Logger.Log($"CorsairSync: PUT /fans/{deviceId} returned {(int)resp.StatusCode}, trying fallback");

            // Fallback: POST /api/v1/devices/{deviceId}/fan
            var content2 = new StringContent(body, Encoding.UTF8, "application/json");
            var resp2 = await _http.PostAsync($"{BaseUrl}/devices/{Uri.EscapeDataString(deviceId)}/fan", content2)
                .ConfigureAwait(false);
            if (!resp2.IsSuccessStatusCode)
                Logger.Log($"CorsairSync: fan fallback also failed {(int)resp2.StatusCode} — device={deviceId}");
        }
        catch (Exception ex)
        {
            Logger.Log($"CorsairSync: SetFanSpeedAsync failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Audio-reactive fan speed: maps audioLevel (0-1) to fan % between FanMinPercent and FanMaxPercent,
    /// then calls SetFanSpeedAsync on all devices matching the given type filter.
    /// Fire-and-forget — non-blocking.
    /// </summary>
    public void SyncAudioReactive(float audioLevel, CorsairConfig cfg, string typeFilter = "")
    {
        if (!IsAvailable || Devices.Count == 0) return;
        float clamped = Math.Clamp(audioLevel, 0f, 1f);
        int percent = (int)(cfg.FanMinPercent + clamped * (cfg.FanMaxPercent - cfg.FanMinPercent));
        percent = Math.Clamp(percent, 0, 100);

        foreach (var device in Devices)
        {
            bool matches = string.IsNullOrEmpty(typeFilter)
                || device.Type.Contains(typeFilter, StringComparison.OrdinalIgnoreCase);
            if (!matches) continue;
            _ = SetFanSpeedAsync(device.Id, percent);
        }
    }

    // ── JSON helpers (no Newtonsoft dependency) ───────────────────────

    private static List<string> SplitJsonArray(string json)
    {
        var result = new List<string>();
        json = json.Trim();
        if (!json.StartsWith('[')) return result;
        json = json[1..];
        if (json.EndsWith(']')) json = json[..^1];

        int depth = 0;
        int start = 0;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']') depth--;
            else if (c == ',' && depth == 0)
            {
                result.Add(json[start..i].Trim());
                start = i + 1;
            }
        }
        var last = json[start..].Trim();
        if (!string.IsNullOrEmpty(last)) result.Add(last);
        return result;
    }

    private static string ExtractJsonString(string json, string key)
    {
        var search = $"\"{key}\":\"";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return "";
        int start = idx + search.Length;
        int end = json.IndexOf('"', start);
        if (end < 0) return "";
        return json[start..end];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _available = false;
        _http.Dispose();
    }
}
