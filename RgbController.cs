using System.IO.Ports;

namespace AmpUp;

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
    private AudioAnalyzer? _audioAnalyzer;
    private GlobalLightConfig? _globalLight;

    // Sparkle state per knob
    private readonly int[] _sparkleLed = new int[5];
    private readonly int[] _sparkleTick = new int[5];
    private readonly int[] _sparkleNext = new int[5];

    // Random number generator for stochastic effects
    private static readonly Random _rng = new();

    // Transition state
    private ProfileTransition _transitionEffect = ProfileTransition.None;
    private int _transitionTick = -1;  // -1 = no transition active
    private const int TransitionDuration = 60; // 60 ticks = 3 seconds at 20fps
    private struct TransitionColor { public byte R, G, B; }
    private TransitionColor _transitionColor = new() { R = 0, G = 230, B = 118 };

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
    /// The hardware has a dead zone below ~33% where LEDs can't display,
    /// so we remap 1-100% to 33-100% device brightness. 0% = off.
    /// </summary>
    public void SetBrightness(int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        // Remap: 0=off, 1-100 → 33-100 (hardware minimum threshold)
        _brightness = pct == 0 ? 0 : 33 + pct * 67 / 100;
    }

    /// <summary>
    /// Store reference to current light configs (called when config changes).
    /// </summary>
    public void UpdateConfig(List<LightConfig> lights) => _lights = lights;

    /// <summary>
    /// Update the global lighting override config.
    /// </summary>
    public void UpdateGlobalConfig(GlobalLightConfig? config) => _globalLight = config;

    /// <summary>
    /// Set or clear the audio analyzer used by the AudioReactive effect.
    /// </summary>
    public void SetAudioAnalyzer(AudioAnalyzer? analyzer) => _audioAnalyzer = analyzer;

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

        if (_transitionTick >= 0)
        {
            RenderTransition();
            _transitionTick++;
            if (_transitionTick >= TransitionDuration)
                _transitionTick = -1; // transition complete
            Send();
            return; // skip normal effects during transition
        }

        UpdateEffects();
        Send();
    }

    /// <summary>
    /// Start a transition animation. Called when profile switches.
    /// </summary>
    public void PlayTransition(ProfileTransition effect, int r = 0, int g = 230, int b = 118)
    {
        if (effect == ProfileTransition.None) return;
        _transitionEffect = effect;
        _transitionColor = new TransitionColor { R = (byte)r, G = (byte)g, B = (byte)b };
        _transitionTick = 0;
        Logger.Log($"RGB transition: {effect}");
    }

    /// <summary>
    /// Compute LED colors for all knobs based on their configured effect.
    /// </summary>
    private void UpdateEffects()
    {
        // Global lighting mode — apply one config to all 5 knobs
        if (_globalLight != null && _globalLight.Enabled)
        {
            for (int k = 0; k < 5; k++)
            {
                var light = new LightConfig
                {
                    Idx = k,
                    Effect = _globalLight.Effect,
                    R = _globalLight.R, G = _globalLight.G, B = _globalLight.B,
                    R2 = _globalLight.R2, G2 = _globalLight.G2, B2 = _globalLight.B2,
                    EffectSpeed = _globalLight.EffectSpeed,
                    ReactiveMode = _globalLight.ReactiveMode,
                };
                ApplyEffect(k, light);
            }
            return;
        }

        foreach (var light in _lights)
        {
            int k = light.Idx;
            if (k < 0 || k > 4) continue;
            ApplyEffect(k, light);
        }
    }

    /// <summary>
    /// Apply the configured effect for a single knob.
    /// </summary>
    private void ApplyEffect(int k, LightConfig light)
    {
        float rawPos = _knobPositions[k];
        // Remap: below 15% = off, 15-100% → 0-100% (hardware LED dead zone)
        float pos = rawPos < 0.15f ? 0f : (rawPos - 0.15f) / 0.85f;

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

            case LightEffect.AudioReactive:
                EffectAudioReactive(k, light);
                break;

            case LightEffect.Breathing:
                EffectBreathing(k, light);
                break;

            case LightEffect.Fire:
                EffectFire(k, light);
                break;

            case LightEffect.Comet:
                EffectComet(k, light);
                break;

            case LightEffect.Sparkle:
                EffectSparkle(k, light);
                break;

            case LightEffect.GradientFill:
                EffectGradientFill(k, light);
                break;
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

    /// <summary>
    /// Audio-reactive effect. Uses FFT band levels from AudioAnalyzer.
    /// EffectSpeed acts as sensitivity (50 = 1x, 1 = 0.02x, 100 = 2x).
    /// R/G/B = idle/base color, R2/G2/B2 = peak/loud color.
    /// </summary>
    private void EffectAudioReactive(int k, LightConfig light)
    {
        if (_audioAnalyzer == null)
        {
            SetColor(k, light.R, light.G, light.B);
            return;
        }

        float sensitivity = light.EffectSpeed / 50f; // 1=0.02x, 50=1x, 100=2x
        float level;

        switch (light.ReactiveMode)
        {
            case ReactiveMode.BeatPulse:
                // Bass (band 1) drives all knobs simultaneously
                level = Math.Clamp(_audioAnalyzer.SmoothedBands[1] * sensitivity, 0f, 1f);
                break;

            case ReactiveMode.SpectrumBands:
                // Each knob = its own frequency band (0=sub-bass .. 4=treble)
                level = Math.Clamp(_audioAnalyzer.SmoothedBands[Math.Clamp(k, 0, 4)] * sensitivity, 0f, 1f);
                break;

            case ReactiveMode.ColorShift:
                // Average all bands for overall energy
                float avg = 0f;
                for (int b = 0; b < 5; b++) avg += _audioAnalyzer.SmoothedBands[b];
                avg /= 5f;
                level = Math.Clamp(avg * sensitivity, 0f, 1f);

                // Shift hue of base color by up to +120° at peak, value from 0.2 to 1.0
                HsvFromRgb(light.R, light.G, light.B, out float h, out float s, out _);
                float newHue = (h + level * 120f) % 360f;
                float newVal = 0.2f + level * 0.8f;
                var (cr, cg, cb) = HsvToRgb(newHue, Math.Max(s, 0.7f), newVal);
                SetColor(k, cr, cg, cb);
                return;

            default:
                level = 0f;
                break;
        }

        // BeatPulse / SpectrumBands: lerp between base and peak color
        int r = (int)(light.R + (light.R2 - light.R) * level);
        int g = (int)(light.G + (light.G2 - light.G) * level);
        int b2 = (int)(light.B + (light.B2 - light.B) * level);
        SetColor(k, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b2, 0, 255));
    }

    // --- New effects ---

    /// <summary>
    /// Smooth sine-wave brightness fade in/out. Like the Apple sleep indicator.
    /// EffectSpeed 1 = 4s period, 100 = 0.4s period.
    /// </summary>
    private void EffectBreathing(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 4.0f - (speed / 100f * 3.6f); // 4.0s down to 0.4s
        float periodTicks = periodSec / 0.05f;
        float angle = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * MathF.PI * 2f;
        float brightness = (MathF.Sin(angle) + 1f) / 2f;
        // Square for smoother, more organic look at low end
        brightness *= brightness;
        SetColor(k, (int)(light.R * brightness), (int)(light.G * brightness), (int)(light.B * brightness));
    }

    /// <summary>
    /// Randomized warm flickering across the 3 LEDs. Each LED gets independent brightness.
    /// </summary>
    private void EffectFire(int k, LightConfig light)
    {
        for (int led = 0; led < 3; led++)
        {
            float flicker = 0.3f + (float)_rng.NextDouble() * 0.7f;
            // Warm flicker: blend toward color2 at high brightness for ember glow
            // Color1 = base flame, Color2 = bright ember/tip color
            float emberBlend = flicker * flicker; // more ember at higher flicker
            int r = Math.Clamp((int)(light.R * flicker + (light.R2 - light.R) * emberBlend * 0.4f), 0, 255);
            int g = Math.Clamp((int)(light.G * flicker + (light.G2 - light.G) * emberBlend * 0.4f), 0, 255);
            int b = Math.Clamp((int)(light.B * flicker + (light.B2 - light.B) * emberBlend * 0.4f), 0, 255);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Bright pixel chases across 3 LEDs with a fading tail.
    /// Head = full brightness, mid = 35%, tail = 8%.
    /// </summary>
    private void EffectComet(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 1.5f - (speed / 100f * 1.3f); // 1.5s to 0.2s per sweep
        float periodTicks = periodSec / 0.05f;
        int phase = (int)(_animTick % (int)Math.Max(periodTicks, 1));
        float t = phase / Math.Max(periodTicks, 1); // 0..1 across sweep
        int headLed = (int)(t * 3f) % 3;

        for (int led = 0; led < 3; led++)
        {
            int dist = (led - headLed + 3) % 3; // 0=head, 1=mid, 2=tail
            float brightness = dist switch
            {
                0 => 1.0f,
                1 => 0.35f,
                _ => 0.08f,
            };
            SetColor(k, led,
                (int)(light.R * brightness),
                (int)(light.G * brightness),
                (int)(light.B * brightness));
        }
    }

    /// <summary>
    /// Random LED briefly flashes white then fades. Base = color1 at 15% brightness.
    /// </summary>
    private void EffectSparkle(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        int interval = Math.Max(1, 20 - speed / 5); // ticks between sparkles

        // Set base dim color on all LEDs
        int baseR = (int)(light.R * 0.15f);
        int baseG = (int)(light.G * 0.15f);
        int baseB = (int)(light.B * 0.15f);
        for (int led = 0; led < 3; led++)
            SetColor(k, led, baseR, baseG, baseB);

        // Manage sparkle timing
        _sparkleTick[k]++;
        if (_sparkleTick[k] >= _sparkleNext[k])
        {
            _sparkleLed[k] = _rng.Next(3);
            _sparkleTick[k] = 0;
            _sparkleNext[k] = interval + _rng.Next(Math.Max(1, interval));
        }

        // Apply sparkle with decay (bright for 2 ticks, fade over 3 more)
        int age = _sparkleTick[k];
        if (age < 5)
        {
            float sparkBright = age < 2 ? 1.0f : 1.0f - (age - 2) / 3f;
            int sLed = _sparkleLed[k];
            int sr = (int)(255 * sparkBright + baseR * (1 - sparkBright));
            int sg = (int)(255 * sparkBright + baseG * (1 - sparkBright));
            int sb = (int)(255 * sparkBright + baseB * (1 - sparkBright));
            SetColor(k, sLed, Math.Clamp(sr, 0, 255), Math.Clamp(sg, 0, 255), Math.Clamp(sb, 0, 255));
        }
    }

    /// <summary>
    /// Static gradient from color1 to color2 across 3 LEDs (no animation).
    /// LED 0 = color1, LED 1 = midpoint blend, LED 2 = color2.
    /// </summary>
    private void EffectGradientFill(int k, LightConfig light)
    {
        for (int led = 0; led < 3; led++)
        {
            float t = led / 2f; // 0, 0.5, 1.0
            int r = (int)(light.R + (light.R2 - light.R) * t);
            int g = (int)(light.G + (light.G2 - light.G) * t);
            int b = (int)(light.B + (light.B2 - light.B) * t);
            SetColor(k, led, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }
    }

    // --- Profile transition renderer ---

    /// <summary>
    /// Render one frame of the active profile switch transition.
    /// </summary>
    private void RenderTransition()
    {
        float t = _transitionTick / (float)TransitionDuration; // 0..1

        switch (_transitionEffect)
        {
            case ProfileTransition.Flash:
            {
                // 3 flashes over the full duration
                float flashPhase = t * 3f;
                float flashCycle = flashPhase - MathF.Floor(flashPhase);
                bool flashOn = flashCycle < 0.5f;
                for (int k = 0; k < 5; k++)
                {
                    if (flashOn)
                        SetColor(k, _transitionColor.R, _transitionColor.G, _transitionColor.B);
                    else
                        SetColor(k, 0, 0, 0);
                }
                break;
            }

            case ProfileTransition.Cascade:
            {
                float cascadePhase = t * 2f; // 0..2 (first half = cascade in, second half = fade out)
                if (cascadePhase <= 1f)
                {
                    // Cascade in: each knob lights up at 0.2 intervals
                    for (int k = 0; k < 5; k++)
                    {
                        float knobT = cascadePhase * 5f - k;
                        float bright = Math.Clamp(knobT, 0f, 1f);
                        SetColor(k,
                            (int)(_transitionColor.R * bright),
                            (int)(_transitionColor.G * bright),
                            (int)(_transitionColor.B * bright));
                    }
                }
                else
                {
                    // Fade out: all knobs fade together
                    float fade = 1f - (cascadePhase - 1f);
                    for (int k = 0; k < 5; k++)
                    {
                        SetColor(k,
                            (int)(_transitionColor.R * fade),
                            (int)(_transitionColor.G * fade),
                            (int)(_transitionColor.B * fade));
                    }
                }
                break;
            }

            case ProfileTransition.RainbowSweep:
            {
                // Fast rainbow wave that accelerates then fades out in the last 30%
                float rainbowSpeed = 5f + t * 20f;
                float fadeOut = t > 0.7f ? (1f - t) / 0.3f : 1f;
                for (int k = 0; k < 5; k++)
                {
                    float hue = (_transitionTick * rainbowSpeed + k * 72f) % 360f;
                    var (r, g, b) = HsvToRgb(hue, 1f, fadeOut);
                    SetColor(k, r, g, b);
                }
                break;
            }
        }
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

    /// <summary>
    /// Convert RGB (0-255 each) to HSV. H = 0-360, S = 0-1, V = 0-1.
    /// </summary>
    private static void HsvFromRgb(int r, int g, int b, out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;
        v = max;
        s = max > 0 ? delta / max : 0f;
        if (delta == 0f) { h = 0f; return; }
        if (max == rf)       h = 60f * (((gf - bf) / delta) % 6f);
        else if (max == gf)  h = 60f * (((bf - rf) / delta) + 2f);
        else                 h = 60f * (((rf - gf) / delta) + 4f);
        if (h < 0f) h += 360f;
    }
}
