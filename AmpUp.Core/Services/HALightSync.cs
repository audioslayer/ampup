using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

/// <summary>
/// Pushes room effect / Screen Sync colors to Home Assistant lights without ever blocking
/// the render loop. Callers Submit() the latest color per light (cheap, any thread); a single
/// background worker wakes ~4x/sec, coalesces to the newest color, skips unchanged colors /
/// tiny deltas, and batches lights sharing a color into one light.turn_on call.
/// HA (and the Zigbee/BLE/Wi-Fi bulbs behind it) is far slower than Govee LAN, so this is
/// deliberately not real-time.
/// </summary>
public sealed class HALightSync : IDisposable
{
    /// <summary>Worker tick = hard cap of service calls per light per second (1000 / 250 = 4).</summary>
    public const int SendIntervalMs = 250;
    /// <summary>Max per-channel / brightness difference treated as "unchanged".</summary>
    private const int MinDelta = 6;
    /// <summary>Re-send an unchanged color at most this often (covers HA-side drift / lost calls).</summary>
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    private readonly record struct LightColor(byte R, byte G, byte B, int Brightness);

    private readonly Func<HAIntegration?> _getHa;
    private readonly object _lock = new();
    private readonly Dictionary<string, LightColor> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (LightColor Color, DateTime At)> _lastSent = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private bool _disposed;

    public HALightSync(Func<HAIntegration?> getHa)
    {
        _getHa = getHa;
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>
    /// Queue a color for a light. Applies the light's white balance and brightness scale.
    /// Only the latest submission per light survives until the next worker tick.
    /// </summary>
    public void Submit(HALightConfig light, byte r, byte g, byte b)
    {
        if (_disposed || string.IsNullOrWhiteSpace(light.EntityId)) return;

        int rr = r * Math.Clamp(light.WhiteBalanceR, 50, 100) / 100;
        int gg = g * Math.Clamp(light.WhiteBalanceG, 50, 100) / 100;
        int bb = b * Math.Clamp(light.WhiteBalanceB, 50, 100) / 100;

        // HA wants hue (rgb_color) and level (brightness) separately: normalize the
        // color to full value and carry intensity in brightness, scaled per light.
        // Black frames are skipped rather than turning the light off: an off/on cycle
        // through HA takes seconds and would make beat-driven effects stutter.
        int peak = Math.Max(rr, Math.Max(gg, bb));
        if (peak <= 0) return;
        int scale = Math.Clamp(light.BrightnessScale, 1, 100);
        int brightness = Math.Clamp(peak * scale / 100, 1, 255);
        var color = new LightColor(
            (byte)(rr * 255 / peak), (byte)(gg * 255 / peak), (byte)(bb * 255 / peak),
            brightness);

        lock (_lock) _pending[light.EntityId] = color;
    }

    /// <summary>Forget what was last sent so the next color goes out even if unchanged
    /// (e.g. after the user toggled power or HA changed the light).</summary>
    public void Invalidate(string entityId)
    {
        lock (_lock)
        {
            _lastSent.Remove(entityId);
            _pending.Remove(entityId);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SendIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            try
            {
                await FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"HA light sync error: {ex.Message}");
            }
        }
    }

    private async Task FlushAsync()
    {
        var ha = _getHa();
        if (ha == null) return;

        List<KeyValuePair<string, LightColor>> due;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            var now = DateTime.UtcNow;
            due = new List<KeyValuePair<string, LightColor>>();
            foreach (var kv in _pending)
            {
                if (_lastSent.TryGetValue(kv.Key, out var last)
                    && IsClose(last.Color, kv.Value)
                    && now - last.At < KeepAlive)
                    continue;
                due.Add(kv);
                _lastSent[kv.Key] = (kv.Value, now);
            }
            _pending.Clear();
        }
        if (due.Count == 0) return;

        // One call per distinct color; awaited so a slow HA naturally holds back
        // the next tick (submissions keep coalescing in _pending meanwhile).
        var calls = new List<Task>();
        foreach (var group in due.GroupBy(kv => kv.Value))
        {
            var ids = group.Select(kv => kv.Key).ToArray();
            var c = group.Key;
            calls.Add(ha.SetLightsColorAsync(ids, c.R, c.G, c.B, c.Brightness));
        }
        await Task.WhenAll(calls).ConfigureAwait(false);
    }

    private static bool IsClose(LightColor a, LightColor b)
    {
        return Math.Abs(a.R - b.R) <= MinDelta
            && Math.Abs(a.G - b.G) <= MinDelta
            && Math.Abs(a.B - b.B) <= MinDelta
            && Math.Abs(a.Brightness - b.Brightness) <= MinDelta;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _worker.Wait(500); } catch { }
        _cts.Dispose();
    }
}
