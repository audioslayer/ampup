using System.IO.Ports;

namespace WolfMixer;

/// <summary>
/// Controls the Turn Up device's RGB lighting via serial.
/// Protocol: 48-byte frame — FE 05 [45 bytes of RGB data] FF
/// Each knob has 3 LEDs, each LED has R/G/B = 5 knobs x 3 LEDs x 3 bytes = 45 data bytes.
/// Supports multiple lighting effects per knob with smooth 20 FPS animation.
/// </summary>
public class RgbController : IDisposable
{
    private SerialPort? _port;
    private readonly byte[] _colorMsg = new byte[48];
    private System.Threading.Timer? _refreshTimer;

    // State tracking
    private readonly float[] _knobPositions = new float[5];
    private bool _micMuted;
    private bool _masterMuted;
    private int _brightness = 100; // 0-100 global brightness
    private List<LightConfig> _lights = new();
    private int _animTick; // incremented every timer tick (50ms)

    // Gamma correction table from the original Turn Up firmware.
    private static readonly byte[] Gamma8 = {
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,
        1,1,1,1,1,1,1,1,1,2,2,2,2,2,2,2,
        2,3,3,3,3,3,3,3,4,4,4,4,4,5,5,5,
        5,6,6,6,6,7,7,7,7,8,8,8,9,9,9,10,
        10,10,11,11,11,12,12,13,13,13,14,14,15,15,16,16,
        17,17,18,18,19,19,20,20,21,21,22,22,23,24,24,25,
        25,26,27,27,28,29,29,30,31,32,32,33,34,35,35,36,
        37,38,39,39,40,41,42,43,44,45,46,47,48,49,50,50,
        51,52,54,55,56,57,58,59,60,61,62,63,64,66,67,68,
        69,70,72,73,74,75,77,78,79,81,82,83,85,86,87,89,
        90,92,93,95,96,98,99,101,102,104,105,107,109,110,112,114,
        115,117,119,120,122,124,126,127,129,131,133,135,137,138,140,142,
        144,146,148,150,152,154,156,158,160,162,164,167,169,171,173,175,
        177,180,182,184,186,189,191,193,196,198,200,203,205,208,210,213,
        215,218,220,223,225,228,231,233,236,239,241,244,247,249,252,255
    };

    public RgbController()
    {
        _colorMsg[0] = 0xFE;   // start
        _colorMsg[1] = 0x05;   // command: set colors
        _colorMsg[47] = 0xFF;  // end
    }

    public void SetPort(SerialPort? port)
    {
        _port = port;

        // Start or stop the refresh timer based on connection state
        if (port != null && port.IsOpen)
        {
            _refreshTimer?.Dispose();
            _refreshTimer = new System.Threading.Timer(_ => Tick(), null, 50, 50);
        }
        else
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }
    }

    // --- Public state setters ---

    /// <summary>
    /// Update knob position (0.0 to 1.0) for effect calculations.
    /// </summary>
    public void SetKnobPosition(int idx, float pos)
    {
        if (idx >= 0 && idx < 5)
            _knobPositions[idx] = Math.Clamp(pos, 0f, 1f);
    }

    /// <summary>
    /// Update mic muted state for the MicStatus effect.
    /// </summary>
    public void SetMicMuted(bool muted) => _micMuted = muted;

    /// <summary>
    /// Update master muted state for the DeviceMute effect.
    /// </summary>
    public void SetMasterMuted(bool muted) => _masterMuted = muted;

    /// <summary>
    /// Set global brightness (0-100). Applied as final multiplier on all RGB values.
    /// </summary>
    public void SetBrightness(int pct) => _brightness = Math.Clamp(pct, 0, 100);

    /// <summary>
    /// Store reference to current light configs (called when config changes).
    /// </summary>
    public void UpdateConfig(List<LightConfig> lights) => _lights = lights;

    // --- Color setting ---

    /// <summary>
    /// Set one knob's color. All 3 LEDs on that knob get the same color.
    /// Applies brightness and gamma. Does NOT send.
    /// </summary>
    public void SetColor(int knobIdx, int r, int g, int b)
    {
        if (knobIdx < 0 || knobIdx > 4) return;

        // Apply brightness
        r = r * _brightness / 100;
        g = g * _brightness / 100;
        b = b * _brightness / 100;

        byte gr = Gamma8[Math.Clamp(r, 0, 255)];
        byte gg = Gamma8[Math.Clamp(g, 0, 255)];
        byte gb = Gamma8[Math.Clamp(b, 0, 255)];

        for (int led = 0; led < 3; led++)
        {
            _colorMsg[knobIdx * 9 + led * 3 + 2] = gr;
            _colorMsg[knobIdx * 9 + led * 3 + 3] = gg;
            _colorMsg[knobIdx * 9 + led * 3 + 4] = gb;
        }
    }

    /// <summary>
    /// Set a single LED on a knob. Applies brightness and gamma. Does NOT send.
    /// </summary>
    public void SetColor(int knobIdx, int ledIdx, int r, int g, int b)
    {
        if (knobIdx < 0 || knobIdx > 4) return;
        if (ledIdx < 0 || ledIdx > 2) return;

        // Apply brightness
        r = r * _brightness / 100;
        g = g * _brightness / 100;
        b = b * _brightness / 100;

        _colorMsg[knobIdx * 9 + ledIdx * 3 + 2] = Gamma8[Math.Clamp(r, 0, 255)];
        _colorMsg[knobIdx * 9 + ledIdx * 3 + 3] = Gamma8[Math.Clamp(g, 0, 255)];
        _colorMsg[knobIdx * 9 + ledIdx * 3 + 4] = Gamma8[Math.Clamp(b, 0, 255)];
    }

    /// <summary>
    /// Apply all colors from config into the buffer and send once.
    /// </summary>
    public void ApplyColors(List<LightConfig> lights)
    {
        _lights = lights;
        UpdateEffects();
        Send();
    }

    /// <summary>
    /// Send the current color buffer to the device.
    /// </summary>
    public void Send()
    {
        if (_port == null || !_port.IsOpen) return;

        try
        {
            _port.Write(_colorMsg, 0, _colorMsg.Length);
        }
        catch { }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }

    // --- Animation engine ---

    /// <summary>
    /// Called every 50ms by the refresh timer. Updates effects and sends frame.
    /// </summary>
    private void Tick()
    {
        _animTick++;
        UpdateEffects();
        Send();
    }

    /// <summary>
    /// Compute LED colors for all knobs based on their configured effect.
    /// </summary>
    private void UpdateEffects()
    {
        foreach (var light in _lights)
        {
            int k = light.Idx;
            if (k < 0 || k > 4) continue;

            float pos = _knobPositions[k];

            switch (light.Effect)
            {
                case LightEffect.SingleColor:
                    EffectSingleColor(k, light, pos);
                    break;

                case LightEffect.ColorBlend:
                    EffectColorBlend(k, light, pos);
                    break;

                case LightEffect.PositionFill:
                    EffectPositionFill(k, light, pos);
                    break;

                case LightEffect.Blink:
                    EffectBlink(k, light);
                    break;

                case LightEffect.Pulse:
                    EffectPulse(k, light);
                    break;

                case LightEffect.RainbowWave:
                    EffectRainbowWave(k);
                    break;

                case LightEffect.RainbowCycle:
                    EffectRainbowCycle(k);
                    break;

                case LightEffect.MicStatus:
                    EffectMicStatus(k, light);
                    break;

                case LightEffect.DeviceMute:
                    EffectDeviceMute(k, light);
                    break;
            }
        }
    }

    // --- Effect implementations ---

    /// <summary>
    /// All 3 LEDs = color1 scaled by knob position.
    /// </summary>
    private void EffectSingleColor(int k, LightConfig light, float pos)
    {
        int r = (int)(light.R * pos);
        int g = (int)(light.G * pos);
        int b = (int)(light.B * pos);
        SetColor(k, r, g, b);
    }

    /// <summary>
    /// Lerp between color1 (at 0%) and color2 (at 100%) based on knob position.
    /// </summary>
    private void EffectColorBlend(int k, LightConfig light, float pos)
    {
        int r = (int)(light.R + (light.R2 - light.R) * pos);
        int g = (int)(light.G + (light.G2 - light.G) * pos);
        int b = (int)(light.B + (light.B2 - light.B) * pos);
        SetColor(k, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    /// <summary>
    /// LEDs fill left-to-right as knob increases.
    /// LED 0 ON if pos >= 1/6, LED 1 ON if pos >= 1/2, LED 2 ON if pos >= 5/6.
    /// </summary>
    private void EffectPositionFill(int k, LightConfig light, float pos)
    {
        bool led0On = pos >= (1f / 6f);
        bool led1On = pos >= 0.5f;
        bool led2On = pos >= (5f / 6f);

        SetColor(k, 0, led0On ? light.R : 0, led0On ? light.G : 0, led0On ? light.B : 0);
        SetColor(k, 1, led1On ? light.R : 0, led1On ? light.G : 0, led1On ? light.B : 0);
        SetColor(k, 2, led2On ? light.R : 0, led2On ? light.G : 0, led2On ? light.B : 0);
    }

    /// <summary>
    /// Alternate between color1 and color2 at a rate determined by EffectSpeed.
    /// Speed=1 -> 2s period, Speed=100 -> 0.1s period.
    /// </summary>
    private void EffectBlink(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 2.0f - (speed / 100f * 1.9f); // 2.0s to 0.1s
        float periodTicks = periodSec / 0.05f; // convert to 50ms ticks
        bool useColor1 = (_animTick % (int)Math.Max(periodTicks, 1)) < (periodTicks / 2f);

        if (useColor1)
            SetColor(k, light.R, light.G, light.B);
        else
            SetColor(k, light.R2, light.G2, light.B2);
    }

    /// <summary>
    /// Smooth sine-wave oscillation between color1 and color2.
    /// </summary>
    private void EffectPulse(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 2.0f - (speed / 100f * 1.9f);
        float periodTicks = periodSec / 0.05f;
        float angle = (float)(_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * MathF.PI * 2f;
        float t = (MathF.Sin(angle) + 1f) / 2f; // 0.0 to 1.0

        int r = (int)(light.R + (light.R2 - light.R) * t);
        int g = (int)(light.G + (light.G2 - light.G) * t);
        int b = (int)(light.B + (light.B2 - light.B) * t);
        SetColor(k, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    /// <summary>
    /// HSV rainbow across all knobs. Each knob offset by 72 degrees (360/5).
    /// Hue animates over time. All 3 LEDs on a knob share the same color.
    /// </summary>
    private void EffectRainbowWave(int k)
    {
        float hue = ((_animTick * 2f) + k * 72f) % 360f;
        var (r, g, b) = HsvToRgb(hue, 1f, 1f);
        SetColor(k, r, g, b);
    }

    /// <summary>
    /// Each of the 3 LEDs on a knob gets a different hue offset (0, 120, 240).
    /// Hue animates over time.
    /// </summary>
    private void EffectRainbowCycle(int k)
    {
        float baseHue = (_animTick * 2f) % 360f;
        for (int led = 0; led < 3; led++)
        {
            float hue = (baseHue + led * 120f) % 360f;
            var (r, g, b) = HsvToRgb(hue, 1f, 1f);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Show color1 when mic is NOT muted, color2 when muted.
    /// </summary>
    private void EffectMicStatus(int k, LightConfig light)
    {
        if (_micMuted)
            SetColor(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Show color1 when master is NOT muted, color2 when muted.
    /// </summary>
    private void EffectDeviceMute(int k, LightConfig light)
    {
        if (_masterMuted)
            SetColor(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    // --- Helpers ---

    /// <summary>
    /// Convert HSV to RGB. H = 0-360, S = 0-1, V = 0-1. Returns (r, g, b) each 0-255.
    /// </summary>
    private static (int r, int g, int b) HsvToRgb(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f; // normalize to 0-360
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r1, g1, b1;
        if (h < 60f)       { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180f) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0; b1 = c; }
        else               { r1 = c; g1 = 0; b1 = x; }

        return (
            (int)((r1 + m) * 255f + 0.5f),
            (int)((g1 + m) * 255f + 0.5f),
            (int)((b1 + m) * 255f + 0.5f)
        );
    }
}
