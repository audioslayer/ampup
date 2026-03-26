using System.Net.Http;
using System.Text;

namespace AmpUp.Core.Services;

/// <summary>
/// Best-effort Corsair iCUE integration via the iCUE 5+ local REST API (port 60222).
/// Does not require bundling the iCUE SDK DLL — iCUE must be installed and running.
/// Sends the 15-LED RGB colors from Turn Up hardware to all connected iCUE channels.
/// </summary>
public class CorsairSync : IDisposable
{
    private const string BaseUrl = "http://localhost:60222/api/v1";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private bool _available;
    private bool _disposed;

    public bool IsAvailable => _available && !_disposed;

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _available = false;
        _http.Dispose();
    }
}
