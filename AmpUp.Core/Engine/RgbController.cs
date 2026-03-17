using AmpUp.Core.Models;
using AmpUp.Core;

namespace AmpUp.Core.Engine;

/// <summary>
/// Controls the Turn Up device's RGB lighting via serial.
/// Protocol: 48-byte frame — FE 05 [45 bytes of RGB data] FF
/// Each knob has 3 LEDs, each LED has R/G/B = 5 knobs x 3 LEDs x 3 bytes = 45 data bytes.
/// Supports multiple lighting effects per knob with smooth 20 FPS animation.
/// </summary>
public class RgbController : IDisposable
{
    private Action<byte[], int, int>? _writeBytes;
    private Func<bool>? _isPortOpen;
    private readonly byte[] _colorMsg = new byte[48];
    private readonly byte[] _linearColors = new byte[45]; // pre-gamma, post-brightness — for external sync
    private System.Threading.Timer? _refreshTimer;

    /// <summary>
    /// Called after each frame is computed with pre-gamma linear RGB for all 15 LEDs.
    /// byte[45]: knob0LED0 R/G/B, knob0LED1 R/G/B, ... knob4LED2 R/G/B
    /// </summary>
    public Action<byte[]>? OnFrameReady;

    // State tracking
    private readonly float[] _knobPositions = new float[5];
    private bool _micMuted;
    private bool _masterMuted;
    private int _brightness = 100; // 0-100 global brightness
    private volatile List<LightConfig> _lights = new();
    private int _animTick; // incremented every timer tick (50ms)
    private Func<float[]>? _getAudioBands;
    private volatile GlobalLightConfig? _globalLight;

    // Sparkle state per knob
    private readonly int[] _sparkleLed = new int[5];
    private readonly int[] _sparkleTick = new int[5];
    private readonly int[] _sparkleNext = new int[5];

    // Candle smoothed brightness state per knob (3 LEDs each)
    private readonly float[,] _candleCurrent = new float[5, 3];
    private readonly float[,] _candleTarget = new float[5, 3];

    // Stack state per knob
    private readonly int[] _stackLit = new int[5]; // how many LEDs are lit (0-3)
    private readonly int[] _stackTick = new int[5];

    // ProgramMute state per knob — written from polling timer, read from animation timer
    private readonly object _stateLock = new();
    private readonly Dictionary<int, bool> _programMuteStates = new();
    private readonly Dictionary<int, bool> _appGroupMuteStates = new();

    // DeviceSelect state: current default output device ID
    private string _defaultOutputDeviceId = "";

    // Global scanner state
    private float _scannerPos;
    private int _scannerDir = 1;

    // Rainbow scanner state (separate to avoid conflict with Scanner)
    private float _rainbowScannerPos;
    private int _rainbowScannerDir = 1;

    // SparkleRain state: up to 5 simultaneous sparkles across 15 LEDs
    private struct GlobalSparkle { public int Idx; public int Age; public bool Active; }
    private readonly GlobalSparkle[] _globalSparkles = new GlobalSparkle[5];

    // FireWall smoothed brightness per global LED (0-14)
    private readonly float[] _fireWallCurrent = new float[15];
    private readonly float[] _fireWallTarget = new float[15];

    // DualRacer state: two racers at different positions
    private float _racerPos1;      // color1 racer position (left→right)
    private float _racerPos2 = 14f;// color2 racer position (right→left)

    // Lightning state
    private int _lightningCenter = -1;   // current strike center LED
    private int _lightningAge;           // ticks since strike started
    private int _lightningNextCountdown; // ticks until next strike

    // Fillup state
    private int _fillupCount;       // number of LEDs lit (0-15)
    private int _fillupDir = 1;     // 1=filling, -1=draining
    private int _fillupTick;        // ticks since last change
    private bool _fillupPaused;     // true when at full or empty

    // Rainfall state: up to 6 active drops
    private struct RaindropState { public float Pos; public float Speed; public bool Active; public int SplashAge; }
    private readonly RaindropState[] _raindrops = new RaindropState[6];
    private int _raindropNextTick;  // countdown to next spawn

    // Police state: tracks the double-flash sub-pattern
    private int _policeSubTick;     // position within flash cadence cycle

    // Preview color override — when set, all 15 LEDs show this color (for color picker live preview)
    private volatile byte[]? _previewColor; // null = normal, byte[3] = {R,G,B}

    // Random number generator for stochastic effects
    private static readonly Random _rng = new();

    // Transition state
    private ProfileTransition _transitionEffect = ProfileTransition.None;
    private int _transitionTick = -1;  // -1 = no transition active
    private const int TransitionDuration = 20; // 20 ticks = 1 second at 20fps
    private struct TransitionColor { public byte R, G, B; }
    private TransitionColor _transitionColor = new() { R = 0, G = 230, B = 118 };

    // Per-channel gamma correction tables — default 1.0 (linear, no correction).
    // The official Turn Up app sends raw RGB with no gamma. Users can adjust
    // per-channel via Settings → LED Calibration if their LEDs need correction.
    private byte[] _gammaR = BuildGammaTable(1.0);
    private byte[] _gammaG = BuildGammaTable(1.0);
    private byte[] _gammaB = BuildGammaTable(1.0);

    private static byte[] BuildGammaTable(double gamma)
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[i] = (byte)(Math.Pow(i / 255.0, gamma) * 255.0 + 0.5);
        return table;
    }

    /// <summary>Update per-channel gamma curves. Called on config load/change.</summary>
    public void SetGamma(double gammaR, double gammaG, double gammaB)
    {
        _gammaR = BuildGammaTable(Math.Clamp(gammaR, 0.5, 4.0));
        _gammaG = BuildGammaTable(Math.Clamp(gammaG, 0.5, 4.0));
        _gammaB = BuildGammaTable(Math.Clamp(gammaB, 0.5, 4.0));
    }

    public RgbController()
    {
        _colorMsg[0] = 0xFE;   // start
        _colorMsg[1] = 0x05;   // command: set colors
        _colorMsg[47] = 0xFF;  // end
    }

    public void SetOutput(Action<byte[], int, int>? write, Func<bool>? isOpen)
    {
        _writeBytes = write;
        _isPortOpen = isOpen;

        // Start or stop the refresh timer based on connection state
        if (write != null && isOpen?.Invoke() == true)
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
    /// Update the current default output device ID for the DeviceSelect effect.
    /// </summary>
    public void SetDefaultOutputDevice(string deviceId) => _defaultOutputDeviceId = deviceId ?? "";

    /// <summary>
    /// Update per-knob program mute state for the ProgramMute effect.
    /// </summary>
    public void SetProgramMuted(int knobIdx, bool muted)
    {
        if (knobIdx >= 0 && knobIdx < 5)
        {
            lock (_stateLock) _programMuteStates[knobIdx] = muted;
        }
    }

    /// <summary>
    /// Update per-knob app group mute state for the AppGroupMute effect.
    /// allMuted=true when every app in the group is muted (or none are found).
    /// </summary>
    public void SetAppGroupMuted(int knobIdx, bool allMuted)
    {
        if (knobIdx >= 0 && knobIdx < 5)
        {
            lock (_stateLock) _appGroupMuteStates[knobIdx] = allMuted;
        }
    }

    /// <summary>
    /// Set or clear preview color override. When set (non-null), all 15 LEDs
    /// display this color (with brightness + gamma), overriding normal effects.
    /// Used by the color picker dialog for live hardware preview.
    /// Pass null to resume normal effect rendering.
    /// </summary>
    public void SetPreviewColor(byte r, byte g, byte b)
    {
        _previewColor = new byte[] { r, g, b };
    }

    /// <summary>
    /// Clear the preview color override. Resumes normal effect rendering.
    /// </summary>
    public void ClearPreviewColor()
    {
        _previewColor = null;
    }

    /// <summary>
    /// Set global brightness (0-100). Applied as final multiplier on all RGB values.
    /// The hardware has a dead zone below ~33% where LEDs can't display,
    /// so we remap 1-100% to 33-100% device brightness. 0% = off.
    /// </summary>
    /// <summary>
    /// Get the current rendered color for a knob (pre-gamma, post-brightness).
    /// Returns the color of LED 0 (center LED) which represents the knob's current color.
    /// </summary>
    public (byte R, byte G, byte B) GetCurrentColor(int knobIdx)
    {
        if (knobIdx < 0 || knobIdx > 4) return (0, 0, 0);
        int offset = knobIdx * 9; // LED 0 of this knob
        return (_linearColors[offset], _linearColors[offset + 1], _linearColors[offset + 2]);
    }

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
    /// Set or clear the audio bands provider used by the AudioReactive effect.
    /// The provider should return a float[] with 5 smoothed frequency band levels.
    /// </summary>
    public void SetAudioBandsProvider(Func<float[]>? provider) => _getAudioBands = provider;

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

        byte gr = _gammaR[Math.Clamp(r, 0, 255)];
        byte gg = _gammaG[Math.Clamp(g, 0, 255)];
        byte gb = _gammaB[Math.Clamp(b, 0, 255)];

        for (int led = 0; led < 3; led++)
        {
            _colorMsg[knobIdx * 9 + led * 3 + 2] = gr;
            _colorMsg[knobIdx * 9 + led * 3 + 3] = gg;
            _colorMsg[knobIdx * 9 + led * 3 + 4] = gb;

            // Store pre-gamma linear values for external sync (Ambience)
            _linearColors[knobIdx * 9 + led * 3 + 0] = (byte)Math.Clamp(r, 0, 255);
            _linearColors[knobIdx * 9 + led * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
            _linearColors[knobIdx * 9 + led * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
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

        _colorMsg[knobIdx * 9 + ledIdx * 3 + 2] = _gammaR[Math.Clamp(r, 0, 255)];
        _colorMsg[knobIdx * 9 + ledIdx * 3 + 3] = _gammaG[Math.Clamp(g, 0, 255)];
        _colorMsg[knobIdx * 9 + ledIdx * 3 + 4] = _gammaB[Math.Clamp(b, 0, 255)];

        // Store pre-gamma linear values for external sync (Ambience)
        _linearColors[knobIdx * 9 + ledIdx * 3 + 0] = (byte)Math.Clamp(r, 0, 255);
        _linearColors[knobIdx * 9 + ledIdx * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
        _linearColors[knobIdx * 9 + ledIdx * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
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
        if (_writeBytes == null || _isPortOpen?.Invoke() != true) return;

        try
        {
            _writeBytes.Invoke(_colorMsg, 0, _colorMsg.Length);
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

        // Preview color override — used by color picker for live hardware preview
        var preview = _previewColor;
        if (preview != null)
        {
            for (int knob = 0; knob < 5; knob++)
                SetColor(knob, preview[0], preview[1], preview[2]);
            Send();
            OnFrameReady?.Invoke(_linearColors);
            return; // skip normal effects during preview
        }

        if (_transitionTick >= 0)
        {
            RenderTransition();
            _transitionTick++;
            if (_transitionTick >= TransitionDuration)
                _transitionTick = -1; // transition complete
            Send();
            OnFrameReady?.Invoke(_linearColors);
            return; // skip normal effects during transition
        }

        UpdateEffects();
        ApplyDisabledKnobs();
        Send();
        OnFrameReady?.Invoke(_linearColors);
    }

    /// <summary>
    /// Zero out LEDs for knobs that are disabled in global lighting config.
    /// </summary>
    private void ApplyDisabledKnobs()
    {
        var gl = _globalLight;
        if (gl == null || !gl.Enabled || gl.DisabledKnobs.Count == 0) return;

        foreach (var k in gl.DisabledKnobs)
        {
            if (k < 0 || k > 4) continue;
            for (int led = 0; led < 3; led++)
            {
                int offset = k * 9 + led * 3;
                _colorMsg[offset + 2] = 0;
                _colorMsg[offset + 3] = 0;
                _colorMsg[offset + 4] = 0;
                _linearColors[offset + 0] = 0;
                _linearColors[offset + 1] = 0;
                _linearColors[offset + 2] = 0;
            }
        }
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

    // Global-spanning effects treat all 15 LEDs as one strip
    private static readonly HashSet<LightEffect> SpanningEffects = new()
    {
        LightEffect.Scanner, LightEffect.MeteorRain, LightEffect.ColorWave, LightEffect.Segments,
        LightEffect.TheaterChase, LightEffect.RainbowScanner, LightEffect.SparkleRain,
        LightEffect.BreathingSync, LightEffect.FireWall,
        LightEffect.DualRacer, LightEffect.Lightning, LightEffect.Fillup, LightEffect.Ocean,
        LightEffect.Collision, LightEffect.DNA, LightEffect.Rainfall, LightEffect.PoliceLights,
    };

    /// <summary>
    /// Compute LED colors for all knobs based on their configured effect.
    /// </summary>
    private void UpdateEffects()
    {
        // Global lighting mode — apply one config to all 5 knobs
        if (_globalLight != null && _globalLight.Enabled)
        {
            // Global-spanning effects render across all 15 LEDs as one strip
            if (SpanningEffects.Contains(_globalLight.Effect))
            {
                ApplyGlobalSpanEffect(_globalLight);
                return;
            }

            for (int k = 0; k < 5; k++)
            {
                // When gradient colors are set, map each knob to its gradient position
                var (kr, kg, kb) = GetGradientColor(_globalLight, k / 4f);
                var light = new LightConfig
                {
                    Idx = k,
                    Effect = _globalLight.Effect,
                    R = kr, G = kg, B = kb,
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

            case LightEffect.PositionBlend:
                EffectPositionBlend(k, light, pos);
                break;
            case LightEffect.PositionBlendMute:
                EffectPositionBlendMute(k, light, pos);
                break;

            case LightEffect.PingPong:
                EffectPingPong(k, light);
                break;

            case LightEffect.Stack:
                EffectStack(k, light);
                break;

            case LightEffect.Wave:
                EffectWave(k, light);
                break;

            case LightEffect.Candle:
                EffectCandle(k, light);
                break;

            case LightEffect.Wheel:
                EffectWheel(k, light);
                break;

            case LightEffect.RainbowWheel:
                EffectRainbowWheel(k, light);
                break;

            case LightEffect.ProgramMute:
                EffectProgramMute(k, light);
                break;

            case LightEffect.AppGroupMute:
                EffectAppGroupMute(k, light);
                break;

            case LightEffect.DeviceSelect:
                EffectDeviceSelect(k, light);
                break;

            // Global-spanning effects: when used per-knob, fall back to simple behavior
            case LightEffect.Scanner:
            case LightEffect.MeteorRain:
            case LightEffect.ColorWave:
            case LightEffect.Segments:
            case LightEffect.TheaterChase:
            case LightEffect.RainbowScanner:
            case LightEffect.SparkleRain:
            case LightEffect.BreathingSync:
            case LightEffect.FireWall:
            case LightEffect.DualRacer:
            case LightEffect.Lightning:
            case LightEffect.Fillup:
            case LightEffect.Ocean:
            case LightEffect.Collision:
            case LightEffect.DNA:
            case LightEffect.Rainfall:
            case LightEffect.PoliceLights:
                // Per-knob fallback: just show color1
                SetColor(k, light.R, light.G, light.B);
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

        // Gradient fill: color1 at bottom → color2 at top
        bool[] leds = { led0On, led1On, led2On };
        for (int led = 0; led < 3; led++)
        {
            if (leds[led])
            {
                float t = led / 2f;
                int r = Math.Clamp((int)(light.R + (light.R2 - light.R) * t), 0, 255);
                int g = Math.Clamp((int)(light.G + (light.G2 - light.G) * t), 0, 255);
                int b = Math.Clamp((int)(light.B + (light.B2 - light.B) * t), 0, 255);
                SetColor(k, led, r, g, b);
            }
            else
            {
                SetColor(k, led, 0, 0, 0);
            }
        }
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
        // Spread rainbow across all 15 LEDs individually (5 knobs × 3 LEDs)
        // Each LED gets its own hue for a smooth gradient across the whole device
        for (int led = 0; led < 3; led++)
        {
            int globalIdx = k * 3 + led;              // 0-14 across all knobs
            float hue = ((_animTick * 2f) + globalIdx * 24f) % 360f;  // 24° per LED = full rainbow across 15 LEDs
            var (r, g, b) = HsvToRgb(hue, 1f, 1f);
            SetColor(k, led, r, g, b);
        }
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
    /// Mic status: unmuted = solid color1, muted = slow blink on color2.
    /// </summary>
    private void EffectMicStatus(int k, LightConfig light)
    {
        if (_micMuted)
            ApplyMutedBlink(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Master device mute: unmuted = solid color1, muted = slow blink on color2.
    /// </summary>
    private void EffectDeviceMute(int k, LightConfig light)
    {
        if (_masterMuted)
            ApplyMutedBlink(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Slow blink for muted state. Smooth sine transition between 30% and 100% brightness
    /// with a 2-second period — clearly signals muted without being jarring.
    /// </summary>
    private void ApplyMutedBlink(int k, int r, int g, int b)
    {
        // 2-second blink cycle (40 ticks at 20fps)
        const float periodTicks = 40f;
        float angle = (_animTick % (int)periodTicks) / periodTicks * MathF.PI * 2f;
        // Smooth oscillation between 30% and 100% brightness
        float brightness = 0.65f + 0.35f * MathF.Sin(angle);

        SetColor(k,
            Math.Clamp((int)(r * brightness), 0, 255),
            Math.Clamp((int)(g * brightness), 0, 255),
            Math.Clamp((int)(b * brightness), 0, 255));
    }

    /// <summary>
    /// Audio-reactive effect. Uses FFT band levels from the audio bands provider.
    /// EffectSpeed acts as sensitivity (50 = 1x, 1 = 0.02x, 100 = 2x).
    /// R/G/B = idle/base color, R2/G2/B2 = peak/loud color.
    /// </summary>
    private void EffectAudioReactive(int k, LightConfig light)
    {
        var bands = _getAudioBands?.Invoke();
        if (bands == null || bands.Length < 5)
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
                level = Math.Clamp(bands[1] * sensitivity, 0f, 1f);
                break;

            case ReactiveMode.SpectrumBands:
                // Each knob = its own frequency band (0=sub-bass .. 4=treble)
                level = Math.Clamp(bands[Math.Clamp(k, 0, 4)] * sensitivity, 0f, 1f);
                break;

            case ReactiveMode.ColorShift:
                // Average all bands for overall energy
                float avg = 0f;
                for (int b = 0; b < 5; b++) avg += bands[b];
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
        float baseAngle = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * MathF.PI * 2f;

        // Each LED breathes with slight phase offset for a ripple feel
        for (int led = 0; led < 3; led++)
        {
            float angle = baseAngle + led * 0.4f; // subtle wave across the 3 LEDs
            float brightness = (MathF.Sin(angle) + 1f) / 2f;
            brightness *= brightness;
            // Blend from color1 toward color2 at peak brightness
            float t = led / 2f;
            int r = (int)((light.R + (light.R2 - light.R) * t) * brightness);
            int g = (int)((light.G + (light.G2 - light.G) * t) * brightness);
            int b = (int)((light.B + (light.B2 - light.B) * t) * brightness);
            SetColor(k, led, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }
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
            // Head glows white-hot, tail shows color1→color2 gradient
            float ledT = led / 2f;
            float whiteBlend = dist == 0 ? 0.3f : 0f;
            int cr = (int)(light.R + (light.R2 - light.R) * ledT);
            int cg = (int)(light.G + (light.G2 - light.G) * ledT);
            int cb = (int)(light.B + (light.B2 - light.B) * ledT);
            SetColor(k, led,
                Math.Clamp((int)((cr * (1f - whiteBlend) + 255 * whiteBlend) * brightness), 0, 255),
                Math.Clamp((int)((cg * (1f - whiteBlend) + 255 * whiteBlend) * brightness), 0, 255),
                Math.Clamp((int)((cb * (1f - whiteBlend) + 255 * whiteBlend) * brightness), 0, 255));
        }
    }

    /// <summary>
    /// Random LED briefly flashes white then fades. Base = color1 at 15% brightness.
    /// </summary>
    private void EffectSparkle(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        int interval = Math.Max(1, 20 - speed / 5); // ticks between sparkles

        // Set gradient base dim color on each LED (color1 → color2)
        for (int led = 0; led < 3; led++)
        {
            float t = led / 2f;
            int baseR = (int)((light.R + (light.R2 - light.R) * t) * 0.15f);
            int baseG = (int)((light.G + (light.G2 - light.G) * t) * 0.15f);
            int baseB = (int)((light.B + (light.B2 - light.B) * t) * 0.15f);
            SetColor(k, led, baseR, baseG, baseB);
        }

        // Manage sparkle timing
        _sparkleTick[k]++;
        if (_sparkleTick[k] >= _sparkleNext[k])
        {
            _sparkleLed[k] = _rng.Next(3);
            _sparkleTick[k] = 0;
            _sparkleNext[k] = interval + _rng.Next(Math.Max(1, interval));
        }

        // Apply sparkle with decay — color2 flash that fades to base
        int age = _sparkleTick[k];
        if (age < 5)
        {
            float sparkBright = age < 2 ? 1.0f : 1.0f - (age - 2) / 3f;
            int sLed = _sparkleLed[k];
            // Sparkle flashes color2 blended with white at peak
            float whiteBlend = sparkBright * 0.4f;
            int sr = Math.Clamp((int)((light.R2 * (1f - whiteBlend) + 255 * whiteBlend) * sparkBright), 0, 255);
            int sg = Math.Clamp((int)((light.G2 * (1f - whiteBlend) + 255 * whiteBlend) * sparkBright), 0, 255);
            int sb = Math.Clamp((int)((light.B2 * (1f - whiteBlend) + 255 * whiteBlend) * sparkBright), 0, 255);
            SetColor(k, sLed, sr, sg, sb);
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

    /// <summary>
    /// Like PositionFill but lit LEDs blend from color1 (bottom) to color2 (top).
    /// LED 0 = color1, LED 1 = midpoint, LED 2 = color2. Unlit LEDs are off.
    /// </summary>
    private void EffectPositionBlend(int k, LightConfig light, float pos)
    {
        bool led0On = pos >= (1f / 6f);
        bool led1On = pos >= 0.5f;
        bool led2On = pos >= (5f / 6f);

        bool[] leds = { led0On, led1On, led2On };
        for (int led = 0; led < 3; led++)
        {
            if (leds[led])
            {
                float t = led / 2f; // 0, 0.5, 1.0
                int r = Math.Clamp((int)(light.R + (light.R2 - light.R) * t), 0, 255);
                int g = Math.Clamp((int)(light.G + (light.G2 - light.G) * t), 0, 255);
                int b = Math.Clamp((int)(light.B + (light.B2 - light.B) * t), 0, 255);
                SetColor(k, led, r, g, b);
            }
            else
            {
                SetColor(k, led, 0, 0, 0);
            }
        }
    }

    /// <summary>
    /// PositionBlend while the master output device is unmuted.
    /// When muted: all 3 LEDs show a dim version of color2 as a clear mute indicator.
    /// color1 = low end (unmuted), color2 = high end (unmuted) / mute color.
    /// </summary>
    private void EffectPositionBlendMute(int k, LightConfig light, float pos)
    {
        if (_masterMuted)
        {
            // All LEDs dim mute color (color2 at 40% brightness)
            int r = (int)(light.R2 * 0.4f);
            int g = (int)(light.G2 * 0.4f);
            int b = (int)(light.B2 * 0.4f);
            SetColor(k, 0, r, g, b);
            SetColor(k, 1, r, g, b);
            SetColor(k, 2, r, g, b);
        }
        else
        {
            EffectPositionBlend(k, light, pos);
        }
    }

    // --- New per-knob 3-LED effects ---

    /// <summary>
    /// Bright dot bounces back and forth across 3 LEDs (0→1→2→1→0...).
    /// Color1 = dot, Color2 = dim background.
    /// </summary>
    private void EffectPingPong(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 1.5f - (speed / 100f * 1.3f);
        float periodTicks = periodSec / 0.05f;
        float t = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1);

        // Bounce: 0→1→2→1→0 maps to t=0..1 via triangle wave over 4 steps
        float pos = t * 4f; // 0..4
        float ledPos;
        if (pos < 1f) ledPos = pos;          // 0→1
        else if (pos < 2f) ledPos = 1f + (pos - 1f); // 1→2
        else if (pos < 3f) ledPos = 2f - (pos - 2f); // 2→1
        else ledPos = 1f - (pos - 3f);               // 1→0

        for (int led = 0; led < 3; led++)
        {
            float dist = Math.Abs(led - ledPos);
            float brightness = Math.Max(0f, 1f - dist);
            brightness *= brightness; // sharpen the falloff

            int r = (int)(light.R * brightness + light.R2 * 0.08f * (1f - brightness));
            int g = (int)(light.G * brightness + light.G2 * 0.08f * (1f - brightness));
            int b = (int)(light.B * brightness + light.B2 * 0.08f * (1f - brightness));
            SetColor(k, led, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }
    }

    /// <summary>
    /// LEDs build up one by one (0, then 0+1, then 0+1+2), pause, all off, repeat.
    /// Color1 = lit LED color.
    /// </summary>
    private void EffectStack(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        int interval = Math.Max(2, 30 - speed * 28 / 100); // ticks per step

        _stackTick[k]++;
        if (_stackTick[k] >= interval)
        {
            _stackTick[k] = 0;
            _stackLit[k]++;
            if (_stackLit[k] > 4) // 3 lit states + 1 pause + 1 off
                _stackLit[k] = 0;
        }

        int litCount = Math.Min(_stackLit[k], 3);
        for (int led = 0; led < 3; led++)
        {
            if (led < litCount)
            {
                // Gradient across lit LEDs — color1 → color2
                float t = led / 2f;
                int r = Math.Clamp((int)(light.R + (light.R2 - light.R) * t), 0, 255);
                int g = Math.Clamp((int)(light.G + (light.G2 - light.G) * t), 0, 255);
                int b = Math.Clamp((int)(light.B + (light.B2 - light.B) * t), 0, 255);
                SetColor(k, led, r, g, b);
            }
            else
            {
                SetColor(k, led, 0, 0, 0);
            }
        }
    }

    /// <summary>
    /// Sine wave of brightness travels across 3 LEDs. Each LED offset 120° in phase.
    /// Color1 = wave color.
    /// </summary>
    private void EffectWave(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 2.0f - (speed / 100f * 1.8f);
        float periodTicks = periodSec / 0.05f;
        float baseAngle = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * MathF.PI * 2f;

        for (int led = 0; led < 3; led++)
        {
            float angle = baseAngle + led * (MathF.PI * 2f / 3f); // 120° offset per LED
            float brightness = (MathF.Sin(angle) + 1f) / 2f;
            brightness *= brightness; // make peaks brighter, troughs darker

            // Gradient from color1 → color2 across the 3 LEDs
            float t = led / 2f;
            int r = (int)((light.R + (light.R2 - light.R) * t) * brightness);
            int g = (int)((light.G + (light.G2 - light.G) * t) * brightness);
            int b = (int)((light.B + (light.B2 - light.B) * t) * brightness);
            SetColor(k, led, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }
    }

    /// <summary>
    /// Smooth organic candle flicker. Each LED has independently smoothed random brightness.
    /// Color1 = base flame, Color2 = ember/peak tint.
    /// </summary>
    private void EffectCandle(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float smoothing = 0.03f + (speed / 100f) * 0.15f; // 0.03 (calm) to 0.18 (windy)

        for (int led = 0; led < 3; led++)
        {
            // Move toward target
            _candleCurrent[k, led] += (_candleTarget[k, led] - _candleCurrent[k, led]) * smoothing;

            // Pick new target when close enough
            if (Math.Abs(_candleCurrent[k, led] - _candleTarget[k, led]) < 0.05f)
                _candleTarget[k, led] = 0.2f + (float)_rng.NextDouble() * 0.8f;

            float bright = _candleCurrent[k, led];
            float ember = bright * bright; // more ember at higher brightness
            int r = Math.Clamp((int)(light.R * bright + (light.R2 - light.R) * ember * 0.3f), 0, 255);
            int g = Math.Clamp((int)(light.G * bright + (light.G2 - light.G) * ember * 0.3f), 0, 255);
            int b = Math.Clamp((int)(light.B * bright + (light.B2 - light.B) * ember * 0.3f), 0, 255);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Single bright dot rotates around 3 LEDs (0→1→2→0...) with a dim trailing fade.
    /// Color1 = dot color. Speed controls rotation rate.
    /// </summary>
    private void EffectWheel(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 1.5f - (speed / 100f * 1.3f); // 1.5s to 0.2s per full rotation
        float periodTicks = periodSec / 0.05f;
        float t = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1);

        float headPos = t * 3f; // 0..3 wrapping
        for (int led = 0; led < 3; led++)
        {
            float dist = ((led - headPos % 3f) + 3f) % 3f; // 0 = head, wraps
            float brightness;
            if (dist < 0.5f) brightness = 1.0f;           // head
            else if (dist < 1.5f) brightness = 0.3f;      // 1 behind
            else brightness = 0.05f;                       // 2 behind (dim tail)

            // Head blends toward color2, tail stays color1
            float blendT = dist < 0.5f ? 1f : dist < 1.5f ? 0.5f : 0f;
            int r = (int)((light.R + (light.R2 - light.R) * blendT) * brightness);
            int g = (int)((light.G + (light.G2 - light.G) * blendT) * brightness);
            int b = (int)((light.B + (light.B2 - light.B) * blendT) * brightness);
            SetColor(k, led, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
        }
    }

    /// <summary>
    /// Each of the 3 LEDs shows tightly-spaced rainbow hues (~40° apart) rotating over time.
    /// Like RainbowCycle but with closer hues — feels like a spinning color wheel on a single knob.
    /// </summary>
    private void EffectRainbowWheel(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float baseHue = (_animTick * (0.5f + speed / 100f * 2.5f)) % 360f;
        for (int led = 0; led < 3; led++)
        {
            float hue = (baseHue + led * 40f) % 360f; // 40° between LEDs (vs 120° for RainbowCycle)
            var (r, g, b) = HsvToRgb(hue, 1f, 1f);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Show color1 when the tracked program is NOT muted, color2 when muted or not found.
    /// </summary>
    private void EffectProgramMute(int k, LightConfig light)
    {
        bool muted;
        lock (_stateLock) muted = _programMuteStates.GetValueOrDefault(k, true); // default to muted color if unknown
        if (muted)
            SetColor(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Show color1 when any app in the knob's app group is unmuted, color2 when all are muted or none found.
    /// </summary>
    private void EffectAppGroupMute(int k, LightConfig light)
    {
        bool allMuted;
        lock (_stateLock) allMuted = _appGroupMuteStates.GetValueOrDefault(k, false); // default to unmuted/live appearance
        if (allMuted)
            SetColor(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Shows a different color based on which audio output device is currently the Windows default.
    /// Each DeviceColorEntry maps a device ID to a color.
    /// If no mapping matches, falls back to color1 (R/G/B).
    /// </summary>
    private void EffectDeviceSelect(int k, LightConfig light)
    {
        if (light.DeviceColors != null)
        {
            foreach (var entry in light.DeviceColors)
            {
                if (!string.IsNullOrEmpty(entry.DeviceId) &&
                    entry.DeviceId.Equals(_defaultOutputDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    SetColor(k, entry.R, entry.G, entry.B);
                    return;
                }
            }
        }
        // Fallback: color1
        SetColor(k, light.R, light.G, light.B);
    }

    // --- Global-spanning effects (all 15 LEDs as one strip) ---

    /// <summary>Set a single LED by global index (0-14). Applies brightness and gamma.</summary>
    private void SetGlobalLed(int globalIdx, int r, int g, int b)
    {
        int knob = globalIdx / 3;
        int led = globalIdx % 3;
        if (knob >= 0 && knob < 5)
            SetColor(knob, led, r, g, b);
    }

    private void ApplyGlobalSpanEffect(GlobalLightConfig gl)
    {
        switch (gl.Effect)
        {
            case LightEffect.Scanner:       GlobalScanner(gl); break;
            case LightEffect.MeteorRain:    GlobalMeteor(gl); break;
            case LightEffect.ColorWave:     GlobalColorWave(gl); break;
            case LightEffect.Segments:      GlobalSegments(gl); break;
            case LightEffect.TheaterChase:  GlobalTheaterChase(gl); break;
            case LightEffect.RainbowScanner:GlobalRainbowScanner(gl); break;
            case LightEffect.SparkleRain:   GlobalSparkleRain(gl); break;
            case LightEffect.BreathingSync: GlobalBreathingSync(gl); break;
            case LightEffect.FireWall:      GlobalFireWall(gl); break;
            case LightEffect.DualRacer:     GlobalDualRacer(gl); break;
            case LightEffect.Lightning:     GlobalLightning(gl); break;
            case LightEffect.Fillup:        GlobalFillup(gl); break;
            case LightEffect.Ocean:         GlobalOcean(gl); break;
            case LightEffect.Collision:     GlobalCollision(gl); break;
            case LightEffect.DNA:           GlobalDNA(gl); break;
            case LightEffect.Rainfall:      GlobalRainfall(gl); break;
            case LightEffect.PoliceLights:  GlobalPoliceLights(gl); break;
        }
    }

    /// <summary>
    /// Cylon/KITT scanner: bright dot with fading tail sweeps back and forth across 15 LEDs.
    /// </summary>
    private void GlobalScanner(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float step = 0.1f + (speed / 100f) * 0.5f; // LEDs per tick

        _scannerPos += step * _scannerDir;
        if (_scannerPos >= 14f) { _scannerPos = 14f; _scannerDir = -1; }
        if (_scannerPos <= 0f) { _scannerPos = 0f; _scannerDir = 1; }

        for (int i = 0; i < 15; i++)
        {
            float dist = Math.Abs(i - _scannerPos);
            float brightness = Math.Max(0f, 1f - dist / 3f); // tail spans ~3 LEDs
            brightness *= brightness;

            // Use gradient color at each LED position for colorful scanner
            var (cr, cg, cb) = GetGradientColor(gl, i / 14f);
            int r = Math.Clamp((int)(cr * brightness), 0, 255);
            int g = Math.Clamp((int)(cg * brightness), 0, 255);
            int b = Math.Clamp((int)(cb * brightness), 0, 255);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Bright meteor with long fading tail shoots across all 15 LEDs, wraps around.
    /// </summary>
    private void GlobalMeteor(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float headPos = (_animTick * (0.2f + speed / 100f * 0.6f)) % 20f; // wraps at 20 for tail room

        for (int i = 0; i < 15; i++)
        {
            float dist = headPos - i;
            if (dist < 0) dist += 20f; // wrap distance

            float brightness;
            if (dist < 0.5f) brightness = 1f;           // head
            else if (dist < 6f) brightness = 1f - dist / 6f; // tail decay
            else brightness = 0f;

            brightness *= brightness; // sharper falloff

            // Use gradient color per LED — meteor trails through the whole palette
            var (cr, cg, cb) = GetGradientColor(gl, i / 14f);
            // Head glows white-hot, tail shows full gradient color
            float whiteBlend = brightness > 0.8f ? (brightness - 0.8f) / 0.2f * 0.5f : 0f;
            int r = Math.Clamp((int)((cr * (1f - whiteBlend) + 255 * whiteBlend) * brightness), 0, 255);
            int g = Math.Clamp((int)((cg * (1f - whiteBlend) + 255 * whiteBlend) * brightness), 0, 255);
            int b = Math.Clamp((int)((cb * (1f - whiteBlend) + 255 * whiteBlend) * brightness), 0, 255);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Scrolling gradient of color1→color2 across all 15 LEDs. Smooth and flowing.
    /// </summary>
    private void GlobalColorWave(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float offset = _animTick * (0.05f + speed / 100f * 0.3f);

        for (int i = 0; i < 15; i++)
        {
            // Sine wave creates smooth wave position: 0→1→0 across the strip
            float t = (MathF.Sin((i / 15f + offset) * MathF.PI * 2f) + 1f) / 2f;
            var (r, g, b) = GetGradientColor(gl, t);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Rotating barber-pole of alternating color1/color2 bands across all 15 LEDs.
    /// </summary>
    private void GlobalSegments(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float offset = _animTick * (0.05f + speed / 100f * 0.25f);
        float bandWidth = 3f; // LEDs per band

        for (int i = 0; i < 15; i++)
        {
            float pos = (i + offset) / bandWidth;
            float t = (MathF.Sin(pos * MathF.PI) + 1f) / 2f; // smooth alternation
            var (r, g, b) = GetGradientColor(gl, t);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Every 3rd LED is lit, the lit pattern shifts one position each speed-tick.
    /// Color1 = lit color, Color2 = dim background.
    /// </summary>
    private void GlobalTheaterChase(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        int ticksPerStep = Math.Max(1, 20 - speed * 19 / 100); // 20 ticks (slow) to 1 tick (fast)
        int offset = (_animTick / ticksPerStep) % 3;

        for (int i = 0; i < 15; i++)
        {
            bool lit = (i % 3) == offset;
            if (lit)
            {
                var (r, g, b) = GetGradientColor(gl, i / 14f);
                SetGlobalLed(i, r, g, b);
            }
            else
            {
                var (r, g, b) = GetGradientColor(gl, i / 14f);
                SetGlobalLed(i, (int)(r * 0.05f), (int)(g * 0.05f), (int)(b * 0.05f));
            }
        }
    }

    /// <summary>
    /// Like Scanner but the sweep dot color cycles through rainbow hues based on position.
    /// Tail fades to dark. Speed controls sweep speed.
    /// </summary>
    private void GlobalRainbowScanner(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float step = 0.1f + (speed / 100f) * 0.5f;

        _rainbowScannerPos += step * _rainbowScannerDir;
        if (_rainbowScannerPos >= 14f) { _rainbowScannerPos = 14f; _rainbowScannerDir = -1; }
        if (_rainbowScannerPos <= 0f)  { _rainbowScannerPos = 0f;  _rainbowScannerDir = 1; }

        // Map head position (0-14) → hue (0-360)
        float headHue = _rainbowScannerPos / 14f * 360f;

        for (int i = 0; i < 15; i++)
        {
            float dist = Math.Abs(i - _rainbowScannerPos);
            float brightness = Math.Max(0f, 1f - dist / 3f);
            brightness *= brightness;

            float hue = (headHue + (i - _rainbowScannerPos) * 5f + 360f) % 360f;
            var (r, g, b) = HsvToRgb(hue, 1f, brightness);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Random LEDs across all 15 flash bright and fade out. Multiple sparkles simultaneously.
    /// Color1 = sparkle color, dim base = color1 at 10%.
    /// </summary>
    private void GlobalSparkleRain(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        // spawn chance per tick: speed=1 → ~2% chance, speed=100 → ~25% chance
        float spawnChance = 0.02f + (speed / 100f) * 0.23f;

        // Try to spawn new sparkles in empty slots
        for (int s = 0; s < _globalSparkles.Length; s++)
        {
            if (!_globalSparkles[s].Active && (float)_rng.NextDouble() < spawnChance)
            {
                _globalSparkles[s].Active = true;
                _globalSparkles[s].Idx = _rng.Next(15);
                _globalSparkles[s].Age = 0;
            }
        }

        // Render dim base on all LEDs (from gradient at each position)
        for (int i = 0; i < 15; i++)
        {
            var (br, bg, bb) = GetGradientColor(gl, i / 14f);
            SetGlobalLed(i, (int)(br * 0.10f), (int)(bg * 0.10f), (int)(bb * 0.10f));
        }

        // Age and render active sparkles
        for (int s = 0; s < _globalSparkles.Length; s++)
        {
            if (!_globalSparkles[s].Active) continue;

            int age = _globalSparkles[s].Age;
            float sparkBright = age < 2 ? 1.0f : Math.Max(0f, 1.0f - (age - 2) / 4f);

            if (sparkBright <= 0f)
            {
                _globalSparkles[s].Active = false;
                continue;
            }

            int idx = _globalSparkles[s].Idx;
            var (cr, cg, cb) = GetGradientColor(gl, idx / 14f);
            // Blend toward white at peak
            int sr = Math.Clamp((int)(cr * sparkBright + 255 * sparkBright * 0.4f), 0, 255);
            int sg = Math.Clamp((int)(cg * sparkBright + 255 * sparkBright * 0.4f), 0, 255);
            int sb = Math.Clamp((int)(cb * sparkBright + 255 * sparkBright * 0.4f), 0, 255);
            SetGlobalLed(idx, sr, sg, sb);

            _globalSparkles[s].Age++;
        }
    }

    /// <summary>
    /// Sine wave of brightness travels across all 15 LEDs. Each LED offset 24° in phase.
    /// Color1 = wave color. Speed controls rate.
    /// </summary>
    private void GlobalBreathingSync(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float periodSec = 4.0f - (speed / 100f) * 3.6f; // 4.0s to 0.4s
        float periodTicks = periodSec / 0.05f;
        float baseAngle = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * MathF.PI * 2f;

        const float phaseOffsetPerLed = MathF.PI * 2f / 15f; // 24° per LED

        for (int i = 0; i < 15; i++)
        {
            float angle = baseAngle + i * phaseOffsetPerLed;
            float brightness = (MathF.Sin(angle) + 1f) / 2f;
            brightness *= brightness; // squared easing — organic breathing feel
            var (r, g, b) = GetGradientColor(gl, i / 14f);
            SetGlobalLed(i, (int)(r * brightness), (int)(g * brightness), (int)(b * brightness));
        }
    }

    /// <summary>
    /// Fire effect across all 15 LEDs as one continuous flame wall.
    /// Adjacent LEDs have correlated brightness for realism.
    /// Color1 = base flame, Color2 = ember tint.
    /// </summary>
    private void GlobalFireWall(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float smoothing = 0.03f + (speed / 100f) * 0.15f; // 0.03 (calm) to 0.18 (windy)

        for (int i = 0; i < 15; i++)
        {
            // Smooth toward target
            _fireWallCurrent[i] += (_fireWallTarget[i] - _fireWallCurrent[i]) * smoothing;

            // Pick new target when close, biased toward neighbor's value for correlated flicker
            if (Math.Abs(_fireWallCurrent[i] - _fireWallTarget[i]) < 0.05f)
            {
                float neighborBias = 0f;
                int neighbors = 0;
                if (i > 0)  { neighborBias += _fireWallCurrent[i - 1]; neighbors++; }
                if (i < 14) { neighborBias += _fireWallCurrent[i + 1]; neighbors++; }
                float neighborAvg = neighbors > 0 ? neighborBias / neighbors : 0.5f;

                // Target = 70% random + 30% neighbor influence
                float random = 0.2f + (float)_rng.NextDouble() * 0.8f;
                _fireWallTarget[i] = random * 0.7f + neighborAvg * 0.3f;
            }

            float bright = _fireWallCurrent[i];
            float ember = bright * bright;
            // Use gradient: base color from gradient position, ember tint blends toward end color
            var (baseR, baseG, baseB) = GetGradientColor(gl, i / 14f);
            var (emberR, emberG, emberB) = GetGradientColor(gl, 1f);
            int r = Math.Clamp((int)(baseR * bright + (emberR - baseR) * ember * 0.3f), 0, 255);
            int g = Math.Clamp((int)(baseG * bright + (emberG - baseG) * ember * 0.3f), 0, 255);
            int b = Math.Clamp((int)(baseB * bright + (emberB - baseB) * ember * 0.3f), 0, 255);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Two dots racing in opposite directions with 3-LED fading tails. Colors blend additively on overlap.
    /// Color1 = left→right racer, Color2 = right→left racer.
    /// </summary>
    private void GlobalDualRacer(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float step = 0.08f + (speed / 100f) * 0.42f; // 0.08 to 0.5 LEDs/tick

        _racerPos1 = (_racerPos1 + step + 15f) % 15f;
        _racerPos2 = (_racerPos2 - step + 15f) % 15f;

        for (int i = 0; i < 15; i++)
        {
            // Distance from each racer (wrap-aware)
            float d1 = Math.Min(Math.Abs(i - _racerPos1), 15f - Math.Abs(i - _racerPos1));
            float d2 = Math.Min(Math.Abs(i - _racerPos2), 15f - Math.Abs(i - _racerPos2));

            float b1 = Math.Max(0f, 1f - d1 / 3f);
            float b2 = Math.Max(0f, 1f - d2 / 3f);
            b1 *= b1;
            b2 *= b2;

            // Additive blend — overlap creates brighter mix
            int r = Math.Clamp((int)(gl.R * b1 + gl.R2 * b2), 0, 255);
            int g = Math.Clamp((int)(gl.G * b1 + gl.G2 * b2), 0, 255);
            int b = Math.Clamp((int)(gl.B * b1 + gl.B2 * b2), 0, 255);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Random dramatic lightning strikes. A bright flash starts at a random LED and
    /// cascades outward in both directions fading rapidly. Dim color1 glow between strikes.
    /// </summary>
    private void GlobalLightning(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        // Strike frequency: speed=1 → every ~60 ticks, speed=100 → every ~8 ticks
        int strikeInterval = Math.Max(8, 60 - (speed * 52 / 100));

        // Dim ambient base — gradient across the strip
        for (int i = 0; i < 15; i++)
        {
            var (br, bg, bb) = GetGradientColor(gl, i / 14f);
            SetGlobalLed(i, (int)(br * 0.05f), (int)(bg * 0.05f), (int)(bb * 0.05f));
        }

        // Count down to next strike
        if (_lightningCenter < 0)
        {
            _lightningNextCountdown--;
            if (_lightningNextCountdown <= 0)
            {
                _lightningCenter = _rng.Next(15);
                _lightningAge = 0;
                _lightningNextCountdown = strikeInterval + _rng.Next(strikeInterval / 2);
            }
            return;
        }

        // Render active strike — 8 tick lifetime with fast cascade decay
        const int strikeDuration = 8;
        if (_lightningAge >= strikeDuration)
        {
            _lightningCenter = -1;
            _lightningNextCountdown = strikeInterval + _rng.Next(strikeInterval / 2);
            return;
        }

        // Cascade radius grows by 1 per tick, brightness decays
        float ageT = _lightningAge / (float)strikeDuration;
        float peakBright = 1f - ageT;
        int cascadeRadius = _lightningAge + 1;

        for (int i = 0; i < 15; i++)
        {
            int dist = Math.Abs(i - _lightningCenter);
            if (dist <= cascadeRadius)
            {
                float ledBright = peakBright * (1f - dist / (float)(cascadeRadius + 1));
                ledBright *= ledBright;
                // White-hot core + gradient color tint at the edges
                var (cr, cg, cb) = GetGradientColor(gl, i / 14f);
                float whiteBlend = ledBright > 0.5f ? 0.7f : 0.3f;
                int r = Math.Clamp((int)((255 * whiteBlend + cr * (1f - whiteBlend)) * ledBright), 0, 255);
                int g = Math.Clamp((int)((255 * whiteBlend + cg * (1f - whiteBlend)) * ledBright), 0, 255);
                int b = Math.Clamp((int)((255 * whiteBlend + cb * (1f - whiteBlend)) * ledBright), 0, 255);
                SetGlobalLed(i, r, g, b);
            }
        }

        _lightningAge++;
    }

    /// <summary>
    /// LEDs fill left-to-right one by one (color1), pause when full, drain right-to-left, pause, repeat.
    /// Leading edge glows brighter. Speed controls fill/drain rate.
    /// </summary>
    private void GlobalFillup(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        int ticksPerStep = Math.Max(1, 20 - (speed * 18 / 100)); // 20 ticks (slow) to 2 ticks (fast)
        int pauseTicks = ticksPerStep * 4; // pause at endpoints

        _fillupTick++;

        if (_fillupPaused)
        {
            if (_fillupTick >= pauseTicks)
            {
                _fillupTick = 0;
                _fillupPaused = false;
            }
        }
        else if (_fillupTick >= ticksPerStep)
        {
            _fillupTick = 0;
            _fillupCount += _fillupDir;

            if (_fillupCount >= 15)
            {
                _fillupCount = 15;
                _fillupDir = -1;
                _fillupPaused = true;
            }
            else if (_fillupCount <= 0)
            {
                _fillupCount = 0;
                _fillupDir = 1;
                _fillupPaused = true;
            }
        }

        for (int i = 0; i < 15; i++)
        {
            if (i < _fillupCount)
            {
                // Leading edge gets bright white-hot flash
                bool isLeading = (_fillupDir == 1 && i == _fillupCount - 1) ||
                                 (_fillupDir == -1 && i == _fillupCount);
                float bright = isLeading ? 1.4f : 1.0f;
                float whiteBlend = isLeading ? 0.4f : 0f;
                var (cr, cg, cb) = GetGradientColor(gl, i / 14f);
                SetGlobalLed(i,
                    Math.Clamp((int)((cr * (1f - whiteBlend) + 255 * whiteBlend) * bright), 0, 255),
                    Math.Clamp((int)((cg * (1f - whiteBlend) + 255 * whiteBlend) * bright), 0, 255),
                    Math.Clamp((int)((cb * (1f - whiteBlend) + 255 * whiteBlend) * bright), 0, 255));
            }
            else
            {
                SetGlobalLed(i, 0, 0, 0);
            }
        }
    }

    /// <summary>
    /// Rolling ocean waves via overlapping sine functions. Color1 = water, Color2 = whitecaps at peaks.
    /// </summary>
    private void GlobalOcean(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.02f + speed / 100f * 0.1f);

        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f; // 0..1 across the strip
            // Three overlapping waves at different frequencies and speeds
            float wave = 0.5f
                + 0.35f * MathF.Sin(x * MathF.PI * 2f - t * 1.7f)
                + 0.25f * MathF.Sin(x * MathF.PI * 3.5f - t * 2.3f + 1.2f)
                + 0.15f * MathF.Sin(x * MathF.PI * 5.5f - t * 3.1f + 0.7f);

            wave = Math.Clamp(wave, 0f, 1f);

            // Whitecaps: blend in end-of-gradient color at high wave values
            float capBlend = Math.Max(0f, (wave - 0.7f) / 0.3f);
            float waterBright = 0.2f + wave * 0.8f;

            var (wr, wg, wb) = GetGradientColor(gl, x); // water color from gradient position
            var (cr, cg, cb) = GetGradientColor(gl, 1f); // cap color from gradient end
            int r = Math.Clamp((int)(wr * waterBright * (1f - capBlend) + cr * capBlend), 0, 255);
            int g = Math.Clamp((int)(wg * waterBright * (1f - capBlend) + cg * capBlend), 0, 255);
            int b = Math.Clamp((int)(wb * waterBright * (1f - capBlend) + cb * capBlend), 0, 255);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Two pulses start from opposite ends (LED 0 and LED 14), race toward the center,
    /// collide at LED 7 with a white flash that expands, then fade. Repeat.
    /// Color1 = left pulse, Color2 = right pulse. Collision = white.
    /// </summary>
    private void GlobalCollision(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        // Cycle: approach (40 ticks) + collision (8 ticks) + fade (12 ticks) = 60 ticks, scaled by speed
        int cycleTicks = Math.Max(20, 80 - (speed * 60 / 100));
        int approachTicks = cycleTicks * 2 / 3;
        int collisionTicks = 8;
        int fadeTicks = cycleTicks - approachTicks - collisionTicks;

        int phase = _animTick % (approachTicks + collisionTicks + fadeTicks);

        // Clear to off
        for (int i = 0; i < 15; i++)
            SetGlobalLed(i, 0, 0, 0);

        if (phase < approachTicks)
        {
            // Approach: pulses move toward center
            float t = phase / (float)approachTicks; // 0..1
            float pos1 = t * 7f;                    // 0 → 7
            float pos2 = 14f - t * 7f;              // 14 → 7

            for (int i = 0; i < 15; i++)
            {
                float d1 = Math.Abs(i - pos1);
                float d2 = Math.Abs(i - pos2);
                float b1 = Math.Max(0f, 1f - d1 / 2.5f); b1 *= b1;
                float b2 = Math.Max(0f, 1f - d2 / 2.5f); b2 *= b2;
                int r = Math.Clamp((int)(gl.R * b1 + gl.R2 * b2), 0, 255);
                int g = Math.Clamp((int)(gl.G * b1 + gl.G2 * b2), 0, 255);
                int b = Math.Clamp((int)(gl.B * b1 + gl.B2 * b2), 0, 255);
                SetGlobalLed(i, r, g, b);
            }
        }
        else if (phase < approachTicks + collisionTicks)
        {
            // Collision: white flash expands outward from center
            int colAge = phase - approachTicks;
            float bright = 1f - colAge / (float)collisionTicks;
            int radius = colAge + 1;

            for (int i = 0; i < 15; i++)
            {
                int dist = Math.Abs(i - 7);
                if (dist <= radius)
                {
                    float ledBright = bright * (1f - dist / (float)(radius + 1));
                    ledBright = Math.Max(0f, ledBright);
                    int r = Math.Clamp((int)(255 * ledBright), 0, 255);
                    SetGlobalLed(i, r, r, r); // white
                }
            }
        }
        else
        {
            // Fade: gradient residual glow dims to off
            int fadeAge = phase - approachTicks - collisionTicks;
            float bright = 1f - fadeAge / (float)Math.Max(fadeTicks, 1);
            bright = Math.Max(0f, bright) * 0.3f; // dim residual glow
            for (int i = 0; i < 15; i++)
            {
                var (cr, cg, cb) = GetGradientColor(gl, i / 14f);
                SetGlobalLed(i, (int)(cr * bright), (int)(cg * bright), (int)(cb * bright));
            }
        }
    }

    /// <summary>
    /// Two interleaving sine waves traveling in opposite directions — double helix.
    /// Color1 = strand 1, Color2 = strand 2. Overlap blends the colors.
    /// </summary>
    private void GlobalDNA(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.04f + speed / 100f * 0.2f);

        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f * MathF.PI * 4f; // ~2 full cycles across strip

            // Strand 1 moves forward, strand 2 moves backward with 180° phase offset
            float s1 = (MathF.Sin(x - t) + 1f) / 2f;
            float s2 = (MathF.Sin(x + t + MathF.PI) + 1f) / 2f;

            // Sharpen to make distinct bright bands
            s1 = s1 * s1;
            s2 = s2 * s2;

            // Blend: each strand contributes its color from gradient
            var (c1r, c1g, c1b) = GetGradientColor(gl, i / 14f);           // strand 1: position-based
            var (c2r, c2g, c2b) = GetGradientColor(gl, 1f - i / 14f);      // strand 2: reversed
            int r = Math.Clamp((int)(c1r * s1 + c2r * s2), 0, 255);
            int g = Math.Clamp((int)(c1g * s1 + c2g * s2), 0, 255);
            int b = Math.Clamp((int)(c1b * s1 + c2b * s2), 0, 255);

            // Ensure minimum glow even in dark spots
            float minBright = 0.04f;
            int minR = (int)(c1r * minBright);
            int minG = (int)(c1g * minBright);
            int minB = (int)(c1b * minBright);
            SetGlobalLed(i, Math.Max(r, minR), Math.Max(g, minG), Math.Max(b, minB));
        }
    }

    /// <summary>
    /// Drops streak from LED 14 → 0 with short fading tails. Splash at LED 0 on impact.
    /// Color1 = drop, Color2 = splash. Multiple drops at varying speeds for depth.
    /// </summary>
    private void GlobalRainfall(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float baseSpeed = 0.2f + (speed / 100f) * 0.6f; // LEDs/tick
        int spawnInterval = Math.Max(3, 20 - (speed * 16 / 100));

        // Dim gradient base across the strip
        for (int i = 0; i < 15; i++)
        {
            var (br, bg, bb) = GetGradientColor(gl, i / 14f);
            SetGlobalLed(i, (int)(br * 0.04f), (int)(bg * 0.04f), (int)(bb * 0.04f));
        }

        // Try to spawn new drop
        _raindropNextTick--;
        if (_raindropNextTick <= 0)
        {
            _raindropNextTick = spawnInterval + _rng.Next(spawnInterval);
            for (int s = 0; s < _raindrops.Length; s++)
            {
                if (!_raindrops[s].Active)
                {
                    _raindrops[s].Active = true;
                    _raindrops[s].Pos = 14f;
                    _raindrops[s].Speed = baseSpeed * (0.6f + (float)_rng.NextDouble() * 0.8f);
                    _raindrops[s].SplashAge = -1;
                    break;
                }
            }
        }

        // Update and render drops
        for (int s = 0; s < _raindrops.Length; s++)
        {
            if (!_raindrops[s].Active) continue;

            if (_raindrops[s].SplashAge >= 0)
            {
                // Render splash at LED 0
                int age = _raindrops[s].SplashAge;
                float splashBright = Math.Max(0f, 1f - age / 5f);
                int splashRadius = Math.Min(age + 1, 3);
                for (int i = 0; i < splashRadius && i < 15; i++)
                {
                    var (cr, cg, cb) = GetGradientColor(gl, i / 14f);
                    // Splash blends toward white at peak
                    float whiteBlend = splashBright * 0.5f;
                    int sr = Math.Clamp((int)((cr * (1f - whiteBlend) + 255 * whiteBlend) * splashBright), 0, 255);
                    int sg = Math.Clamp((int)((cg * (1f - whiteBlend) + 255 * whiteBlend) * splashBright), 0, 255);
                    int sb = Math.Clamp((int)((cb * (1f - whiteBlend) + 255 * whiteBlend) * splashBright), 0, 255);
                    SetGlobalLed(i, sr, sg, sb);
                }
                _raindrops[s].SplashAge++;
                if (_raindrops[s].SplashAge > 6)
                    _raindrops[s].Active = false;
                continue;
            }

            // Move drop
            _raindrops[s].Pos -= _raindrops[s].Speed;

            if (_raindrops[s].Pos < 0f)
            {
                _raindrops[s].Pos = 0f;
                _raindrops[s].SplashAge = 0;
                continue;
            }

            // Draw drop with 2-LED tail — gradient colored at each position
            for (int tail = 0; tail < 3; tail++)
            {
                int ledIdx = (int)(_raindrops[s].Pos) + tail;
                if (ledIdx < 0 || ledIdx >= 15) continue;
                float bright = tail == 0 ? 1f : tail == 1 ? 0.35f : 0.08f;
                var (cr, cg, cb) = GetGradientColor(gl, ledIdx / 14f);
                SetGlobalLed(ledIdx,
                    Math.Clamp((int)(cr * bright), 0, 255),
                    Math.Clamp((int)(cg * bright), 0, 255),
                    Math.Clamp((int)(cb * bright), 0, 255));
            }
        }
    }

    /// <summary>
    /// Police/emergency double-flash: LEDs 0-7 flash color1, LEDs 8-14 flash color2, then swap.
    /// Double-flash cadence: flash-flash-pause.
    /// </summary>
    private void GlobalPoliceLights(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        // Full double-flash cycle: 2 flashes + pause = ~10 ticks at speed 50
        int flashLen = Math.Max(1, 5 - (speed * 4 / 100));  // on ticks per flash
        int gapLen = flashLen;                                // off between double flashes
        int pauseLen = flashLen * 3;                         // pause before swap
        int halfCycle = flashLen + gapLen + flashLen + pauseLen; // one color's turn
        int fullCycle = halfCycle * 2;                        // total cycle

        _policeSubTick = (_policeSubTick + 1) % fullCycle;

        bool leftOn = false;
        bool rightOn = false;

        int posInHalf = _policeSubTick % halfCycle;
        bool firstHalf = _policeSubTick < halfCycle;

        // In each half-cycle: flash, gap, flash, pause
        if (posInHalf < flashLen)
            leftOn = true;
        else if (posInHalf < flashLen + gapLen)
            leftOn = false;
        else if (posInHalf < flashLen + gapLen + flashLen)
            leftOn = true;
        else
            leftOn = false;

        // Second half: opposite side flashes
        bool secondHalf = !firstHalf;
        int posInSecond = _policeSubTick - halfCycle;
        if (secondHalf)
        {
            if (posInSecond < flashLen)
                rightOn = true;
            else if (posInSecond < flashLen + gapLen)
                rightOn = false;
            else if (posInSecond < flashLen + gapLen + flashLen)
                rightOn = true;
            else
                rightOn = false;
        }

        // Render: LEDs 0-7 = color1, LEDs 8-14 = color2
        for (int i = 0; i < 8; i++)
        {
            bool on = firstHalf ? leftOn : false;
            SetGlobalLed(i, on ? gl.R : 0, on ? gl.G : 0, on ? gl.B : 0);
        }
        for (int i = 8; i < 15; i++)
        {
            bool on = secondHalf ? rightOn : false;
            SetGlobalLed(i, on ? gl.R2 : 0, on ? gl.G2 : 0, on ? gl.B2 : 0);
        }
    }

    // --- Profile transition renderer ---

    /// <summary>
    /// Render one frame of the active profile switch transition.
    /// </summary>
    /// <summary>
    /// Get the base hue from the transition color for multi-color derivations.
    /// </summary>
    private float TransitionBaseHue()
    {
        HsvFromRgb(_transitionColor.R, _transitionColor.G, _transitionColor.B, out float h, out _, out _);
        return h;
    }

    private void RenderTransition()
    {
        float t = _transitionTick / (float)TransitionDuration; // 0..1
        float baseHue = TransitionBaseHue();

        switch (_transitionEffect)
        {
            case ProfileTransition.Flash:
            {
                // 3 flashes — primary color with complementary accent on alternating flashes
                float flashPhase = t * 3f;
                int flashNum = (int)flashPhase;
                float flashCycle = flashPhase - MathF.Floor(flashPhase);
                bool flashOn = flashCycle < 0.5f;
                for (int k = 0; k < 5; k++)
                {
                    if (flashOn)
                    {
                        // Alternate: flash 0,2 = profile color, flash 1 = complementary
                        if (flashNum == 1)
                        {
                            var (cr, cg, cb) = HsvToRgb((baseHue + 180f) % 360f, 0.9f, 1f);
                            SetColor(k, cr, cg, cb);
                        }
                        else
                        {
                            SetColor(k, _transitionColor.R, _transitionColor.G, _transitionColor.B);
                        }
                    }
                    else
                        SetColor(k, 0, 0, 0);
                }
                break;
            }

            case ProfileTransition.Cascade:
            {
                // Cascade with analogous color gradient across knobs
                float cascadePhase = t * 2f;
                if (cascadePhase <= 1f)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        float knobT = cascadePhase * 5f - k;
                        float bright = Math.Clamp(knobT, 0f, 1f);
                        // Each knob gets a slightly shifted hue (±30° spread)
                        float knobHue = (baseHue - 30f + k * 15f + 360f) % 360f;
                        var (r, g, b) = HsvToRgb(knobHue, 0.9f, bright);
                        SetColor(k, r, g, b);
                    }
                }
                else
                {
                    float fade = 1f - (cascadePhase - 1f);
                    for (int k = 0; k < 5; k++)
                    {
                        float knobHue = (baseHue - 30f + k * 15f + 360f) % 360f;
                        var (r, g, b) = HsvToRgb(knobHue, 0.9f, fade);
                        SetColor(k, r, g, b);
                    }
                }
                break;
            }

            case ProfileTransition.RainbowSweep:
            {
                // Rainbow wave seeded from profile hue, accelerates then fades
                float rainbowSpeed = 5f + t * 20f;
                float fadeOut = t > 0.7f ? (1f - t) / 0.3f : 1f;
                for (int k = 0; k < 5; k++)
                {
                    float hue = (baseHue + _transitionTick * rainbowSpeed + k * 72f) % 360f;
                    var (r, g, b) = HsvToRgb(hue, 1f, fadeOut);
                    SetColor(k, r, g, b);
                }
                break;
            }

            case ProfileTransition.Ripple:
            {
                // Center-out ripple: knob 2 lights first, then 1&3, then 0&4
                // Uses profile color at center, complementary at edges
                float ripplePhase = t * 2.5f;
                for (int k = 0; k < 5; k++)
                {
                    int dist = Math.Abs(k - 2); // 0,1,2 distance from center
                    float arrival = dist * 0.3f; // when this knob starts
                    float knobT = Math.Clamp(ripplePhase - arrival, 0f, 1f);
                    float fade = t > 0.6f ? (1f - t) / 0.4f : 1f;
                    float bright = knobT * fade;

                    // Shift hue by distance from center (profile → complementary)
                    float knobHue = (baseHue + dist * 60f) % 360f;
                    var (r, g, b) = HsvToRgb(knobHue, 0.85f, bright);
                    SetColor(k, r, g, b);
                }
                break;
            }

            case ProfileTransition.ColorBurst:
            {
                // Explosion: all LEDs flash white then burst into triadic colors
                if (t < 0.15f)
                {
                    // Initial white flash
                    float flash = t / 0.15f;
                    int w = (int)(255 * flash);
                    for (int k = 0; k < 5; k++)
                        SetColor(k, w, w, w);
                }
                else
                {
                    // Burst into triadic colors from profile hue, then fade
                    float burstT = (t - 0.15f) / 0.85f;
                    float fade = burstT > 0.5f ? (1f - burstT) / 0.5f : 1f;
                    for (int k = 0; k < 5; k++)
                    {
                        // Triadic: base, +120°, +240°, then analogous fills
                        float knobHue = (baseHue + k * 72f) % 360f;
                        // Pulsing saturation for sparkle effect
                        float pulse = 0.7f + 0.3f * MathF.Sin(burstT * 20f + k * 1.2f);
                        var (r, g, b) = HsvToRgb(knobHue, pulse, fade);
                        SetColor(k, r, g, b);
                    }
                }
                break;
            }

            case ProfileTransition.Wipe:
            {
                // Left-to-right wipe with trailing analogous gradient
                float wipePos = t * 7f; // position of wipe front (0..7, overshoots for trail)
                float fade = t > 0.7f ? (1f - t) / 0.3f : 1f;
                for (int k = 0; k < 5; k++)
                {
                    float dist = wipePos - k;
                    if (dist < 0f)
                    {
                        SetColor(k, 0, 0, 0); // not yet reached
                    }
                    else
                    {
                        // Trail: recently wiped knobs are bright, older ones dim
                        float trail = Math.Clamp(1f - dist * 0.25f, 0.15f, 1f) * fade;
                        // Analogous gradient: leading edge = profile color, trail shifts hue
                        float knobHue = (baseHue + dist * 20f) % 360f;
                        var (r, g, b) = HsvToRgb(knobHue, 0.9f, trail);
                        SetColor(k, r, g, b);
                    }
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

    /// <summary>
    /// Get interpolated color at position 0.0-1.0 across gradient colors.
    /// Falls back to color1/color2 blend if no gradient colors set.
    /// </summary>
    private static (int r, int g, int b) GetGradientColor(GlobalLightConfig cfg, float position)
    {
        var colors = cfg.GradientColors;
        if (colors == null || colors.Count < 2)
        {
            float t = Math.Clamp(position, 0f, 1f);
            return (
                Math.Clamp((int)(cfg.R + (cfg.R2 - cfg.R) * t), 0, 255),
                Math.Clamp((int)(cfg.G + (cfg.G2 - cfg.G) * t), 0, 255),
                Math.Clamp((int)(cfg.B + (cfg.B2 - cfg.B) * t), 0, 255)
            );
        }

        position = Math.Clamp(position, 0f, 1f);
        float segment = position * (colors.Count - 1);
        int idx = Math.Min((int)segment, colors.Count - 2);
        float t2 = segment - idx;

        var c1 = ParseHexColor(colors[idx]);
        var c2 = ParseHexColor(colors[idx + 1]);
        return (
            Math.Clamp((int)(c1.r + (c2.r - c1.r) * t2), 0, 255),
            Math.Clamp((int)(c1.g + (c2.g - c1.g) * t2), 0, 255),
            Math.Clamp((int)(c1.b + (c2.b - c1.b) * t2), 0, 255)
        );
    }

    private static (int r, int g, int b) ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return (0, 0, 0);
        return (
            Convert.ToInt32(hex[0..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16)
        );
    }
}
