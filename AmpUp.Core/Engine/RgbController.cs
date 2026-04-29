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
    private readonly int[] _knobBrightness = { 100, 100, 100, 100, 100 }; // per-knob 0-100
    private int _muteBrightnessPct = 15; // 0-100, dim level when muted (Issue #9)
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

    // Drip state per knob
    private readonly float[] _dripPos = new float[5]; // drop position 0-2
    private readonly int[] _dripPhase = new int[5]; // 0=forming, 1=falling, 2=splash, 3=pause
    private readonly int[] _dripTick = new int[5];

    // Starfield state (global)
    private readonly float[] _starBright = new float[15];
    private readonly float[] _starTarget = new float[15];

    // AudioPositionBlend crossfade: 0=idle effect, 1=AudioReactive
    private readonly float[] _audioBlendFade = new float[5];
    private float _audioBlendFadeGlobal; // global-level crossfade for spanning effects

    // Shadow buffer for AudioPositionBlend global mode — stores idle effect frame
    private readonly byte[] _idleBuffer = new byte[48]; // same layout as _colorMsg

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
    // Per-knob preview: only one knob shows preview color, others render normally
    private volatile int _previewKnobIdx = -1; // -1 = all knobs (legacy), 0-4 = specific knob

    // Screen sync override: when set, replaces normal effect rendering with screen colors
    // byte[45] = 15 LEDs × 3 (R,G,B), same layout as _linearColors
    private volatile byte[]? _screenSyncColors;

    // Random number generator for stochastic effects
    private static readonly Random _rng = new();

    // Palette system — resolved once per tick for global, or per-knob for individual effects
    private volatile List<ColorPalette>? _customPalettes;
    private ColorPalette? _globalPalette; // cached for current tick

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

    private static void RgbToHsv(int r, int g, int b, out float h, out float s, out float v)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;
        v = max;
        s = max == 0 ? 0 : delta / max;
        if (delta == 0) { h = 0; return; }
        if (max == rf) h = 60f * (((gf - bf) / delta) % 6f);
        else if (max == gf) h = 60f * (((bf - rf) / delta) + 2f);
        else h = 60f * (((rf - gf) / delta) + 4f);
        if (h < 0) h += 360f;
    }

    private static void HsvToRgb(float h, float s, float v, out int r, out int g, out int b)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float rf, gf, bf;
        if (h < 60) { rf = c; gf = x; bf = 0; }
        else if (h < 120) { rf = x; gf = c; bf = 0; }
        else if (h < 180) { rf = 0; gf = c; bf = x; }
        else if (h < 240) { rf = 0; gf = x; bf = c; }
        else if (h < 300) { rf = x; gf = 0; bf = c; }
        else { rf = c; gf = 0; bf = x; }
        r = Math.Clamp((int)((rf + m) * 255f + 0.5f), 0, 255);
        g = Math.Clamp((int)((gf + m) * 255f + 0.5f), 0, 255);
        b = Math.Clamp((int)((bf + m) * 255f + 0.5f), 0, 255);
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
        => SetOutput(write, isOpen, 50);

    public void SetOutput(Action<byte[], int, int>? write, Func<bool>? isOpen, int refreshMs)
    {
        _writeBytes = write;
        _isPortOpen = isOpen;
        refreshMs = Math.Clamp(refreshMs, 16, 1000);

        // Start or stop the refresh timer based on connection state
        if (write != null && isOpen?.Invoke() == true)
        {
            _refreshTimer?.Dispose();
            _refreshTimer = new System.Threading.Timer(_ => Tick(), null, refreshMs, refreshMs);
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
    public void SetPreviewColor(byte r, byte g, byte b, int knobIdx = -1)
    {
        _previewKnobIdx = knobIdx;
        _previewColor = new byte[] { r, g, b };
    }

    /// <summary>
    /// Clear the preview color override. Resumes normal effect rendering.
    /// </summary>
    public void ClearPreviewColor()
    {
        _previewColor = null;
        _previewKnobIdx = -1;
    }

    /// <summary>
    /// Set screen sync colors on the Turn Up LEDs. Overrides normal effect rendering.
    /// Pass a 45-byte array (15 LEDs × R,G,B) or null to resume normal effects.
    /// </summary>
    public void SetScreenSyncColors(byte[]? colors)
    {
        _screenSyncColors = colors;
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
    /// Set the dim brightness level for muted LEDs (0-100%).
    /// When a knob is muted (ProgramMute/AppGroupMute), LEDs show the primary color
    /// at this brightness instead of turning off. (Issue #9)
    /// </summary>
    public void SetMuteBrightness(int pct)
    {
        _muteBrightnessPct = Math.Clamp(pct, 0, 100);
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
    /// Set the list of user-created custom palettes (from config).
    /// </summary>
    public void UpdateCustomPalettes(List<ColorPalette>? palettes) => _customPalettes = palettes;

    /// <summary>
    /// Resolve a palette by name — checks built-in, then custom, then falls back to
    /// generating a 2-stop palette from the legacy color1/color2 fields.
    /// </summary>
    private ColorPalette ResolvePalette(string? name, int r, int g, int b, int r2, int g2, int b2)
    {
        if (!string.IsNullOrEmpty(name))
        {
            if (BuiltInPalettes.ByName.TryGetValue(name, out var builtIn))
                return builtIn;
            var custom = _customPalettes;
            if (custom != null)
            {
                var found = custom.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
            }
        }
        // Fallback: legacy 2-color palette
        return ColorPalette.FromTwoColors(r, g, b, r2, g2, b2);
    }

    /// <summary>
    /// Set or clear the audio bands provider used by the AudioReactive effect.
    /// The provider should return a float[] with 5 smoothed frequency band levels.
    /// </summary>
    public void SetAudioBandsProvider(Func<float[]>? provider) => _getAudioBands = provider;

    // --- Color setting ---

    /// <summary>
    /// Scale RGB by a brightness factor while preserving hue and saturation. (Issue #10)
    /// Works in HSV space: keeps H+S constant, scales V by brightnessScale.
    /// Pure black (0,0,0) stays black.
    /// </summary>
    private static (int r, int g, int b) ScaleColorHsv(int r, int g, int b, float brightnessScale)
    {
        if (r == 0 && g == 0 && b == 0) return (0, 0, 0);
        RgbToHsv(r, g, b, out float h, out float s, out float v);
        v = Math.Clamp(v * brightnessScale, 0f, 1f);
        HsvToRgb(h, s, v, out int ro, out int go, out int bo);
        return (ro, go, bo);
    }

    /// <summary>
    /// Set one knob's color. All 3 LEDs on that knob get the same color.
    /// Applies brightness (HSV-based to preserve hue) and gamma. Does NOT send.
    /// </summary>
    public void SetColor(int knobIdx, int r, int g, int b)
    {
        if (knobIdx < 0 || knobIdx > 4) return;

        // Apply global + per-knob brightness in HSV space to preserve hue (Issue #10)
        int kb = _knobBrightness[knobIdx];
        float scale = _brightness * kb / 10000f;
        (r, g, b) = ScaleColorHsv(r, g, b, scale);

        byte gr = _gammaR[Math.Clamp(r, 0, 255)];
        byte gg = _gammaG[Math.Clamp(g, 0, 255)];
        byte gb = _gammaB[Math.Clamp(b, 0, 255)];

        for (int led = 0; led < 3; led++)
        {
            _colorMsg[knobIdx * 9 + led * 3 + 2] = gr;
            _colorMsg[knobIdx * 9 + led * 3 + 3] = gg;
            _colorMsg[knobIdx * 9 + led * 3 + 4] = gb;

            // Store pre-gamma linear values for external sync (Ambience)
            _linearColors[knobIdx * 9 + led * 3 + 0] = (byte)r;
            _linearColors[knobIdx * 9 + led * 3 + 1] = (byte)g;
            _linearColors[knobIdx * 9 + led * 3 + 2] = (byte)b;
        }
    }

    /// <summary>
    /// Set a single LED on a knob. Applies brightness (HSV-based to preserve hue) and gamma. Does NOT send.
    /// </summary>
    public void SetColor(int knobIdx, int ledIdx, int r, int g, int b)
    {
        if (knobIdx < 0 || knobIdx > 4) return;
        if (ledIdx < 0 || ledIdx > 2) return;

        // Apply global + per-knob brightness in HSV space to preserve hue (Issue #10)
        int kb = _knobBrightness[knobIdx];
        float scale = _brightness * kb / 10000f;
        (r, g, b) = ScaleColorHsv(r, g, b, scale);

        _colorMsg[knobIdx * 9 + ledIdx * 3 + 2] = _gammaR[Math.Clamp(r, 0, 255)];
        _colorMsg[knobIdx * 9 + ledIdx * 3 + 3] = _gammaG[Math.Clamp(g, 0, 255)];
        _colorMsg[knobIdx * 9 + ledIdx * 3 + 4] = _gammaB[Math.Clamp(b, 0, 255)];

        // Store pre-gamma linear values for external sync (Ambience)
        _linearColors[knobIdx * 9 + ledIdx * 3 + 0] = (byte)r;
        _linearColors[knobIdx * 9 + ledIdx * 3 + 1] = (byte)g;
        _linearColors[knobIdx * 9 + ledIdx * 3 + 2] = (byte)b;
    }

    /// <summary>
    /// Apply all colors from config into the buffer and send once.
    /// </summary>
    public void ApplyColors(List<LightConfig> lights)
    {
        _lights = lights;
        // Cache per-knob brightness for fast lookup in SetColor
        foreach (var l in lights)
            if (l.Idx >= 0 && l.Idx < 5)
                _knobBrightness[l.Idx] = Math.Clamp(l.Brightness, 0, 100);
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
        int previewIdx = _previewKnobIdx;
        if (preview != null && previewIdx == -1)
        {
            // Global preview: all knobs show preview color
            for (int knob = 0; knob < 5; knob++)
                SetColor(knob, preview[0], preview[1], preview[2]);
            Send();
            OnFrameReady?.Invoke(_linearColors);
            return;
        }

        // Screen sync override: push screen capture colors directly to Turn Up LEDs
        var screenSync = _screenSyncColors;
        if (screenSync != null && screenSync.Length == 45 && preview == null)
        {
            for (int k = 0; k < 5; k++)
                for (int led = 0; led < 3; led++)
                {
                    int offset = k * 9 + led * 3;
                    SetColor(k, led, screenSync[offset], screenSync[offset + 1], screenSync[offset + 2]);
                }
            Send();
            OnFrameReady?.Invoke(_linearColors);
            return;
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

        // Per-knob preview: override only the target knob, others render normally
        if (preview != null && previewIdx >= 0 && previewIdx < 5)
            SetColor(previewIdx, preview[0], preview[1], preview[2]);

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
    }

    // Global-spanning effects treat all 15 LEDs as one strip
    private static readonly HashSet<LightEffect> SpanningEffects = new()
    {
        LightEffect.Scanner, LightEffect.MeteorRain, LightEffect.ColorWave, LightEffect.Segments,
        LightEffect.TheaterChase, LightEffect.RainbowScanner, LightEffect.SparkleRain,
        LightEffect.BreathingSync, LightEffect.FireWall,
        LightEffect.DualRacer, LightEffect.Lightning, LightEffect.Fillup, LightEffect.Ocean,
        LightEffect.Collision, LightEffect.DNA, LightEffect.Rainfall, LightEffect.PoliceLights,
        LightEffect.Aurora, LightEffect.Matrix, LightEffect.Starfield,
        LightEffect.Equalizer, LightEffect.Waterfall, LightEffect.Lava, LightEffect.VuWave,
        LightEffect.NebulaDrift,
        LightEffect.Vortex, LightEffect.Shockwave, LightEffect.Tidal,
        LightEffect.Prism, LightEffect.EmberDrift, LightEffect.Glitch,
        LightEffect.OpalWave, LightEffect.Bloom, LightEffect.ColorTwinkle,
        LightEffect.AuroraVeil, LightEffect.SolarStorm, LightEffect.StarlightCanopy,
        LightEffect.PlasmaBloom, LightEffect.RippleRoom, LightEffect.PrismDrift,
        LightEffect.NebulaRain, LightEffect.ReactiveAurora, LightEffect.LiquidGlass,
        LightEffect.ChromaLayerStack,
    };

    /// <summary>
    /// Compute LED colors for all knobs based on their configured effect.
    /// </summary>
    private void UpdateEffects()
    {
        // Global lighting mode — apply one config to all 5 knobs
        if (_globalLight != null && _globalLight.Enabled)
        {
            // Resolve palette once per tick for global mode
            _globalPalette = ResolvePalette(
                _globalLight.PaletteName,
                _globalLight.R, _globalLight.G, _globalLight.B,
                _globalLight.R2, _globalLight.G2, _globalLight.B2);

            // AudioPositionBlend in global mode: render idle effect, crossfade with audio
            if (_globalLight.Effect == LightEffect.AudioPositionBlend)
            {
                RenderGlobalAudioBlend(_globalLight);
                _globalPalette = null;
                return;
            }

            // Global-spanning effects render across all 15 LEDs as one strip
            if (SpanningEffects.Contains(_globalLight.Effect))
            {
                ApplyGlobalSpanEffect(_globalLight);
                _globalPalette = null;
                return;
            }

            for (int k = 0; k < 5; k++)
            {
                // Map each knob to its palette position for per-knob color
                var (kr, kg, kb) = GetGradientColor(_globalLight, k / 4f);
                var light = new LightConfig
                {
                    Idx = k,
                    Effect = _globalLight.Effect,
                    R = kr, G = kg, B = kb,
                    R2 = _globalLight.R2, G2 = _globalLight.G2, B2 = _globalLight.B2,
                    EffectSpeed = _globalLight.EffectSpeed,
                    ReactiveMode = _globalLight.ReactiveMode,
                    PaletteName = _globalLight.PaletteName,
                };
                ApplyEffect(k, light);
            }
            _globalPalette = null;
            return;
        }

        _globalPalette = null;
        foreach (var light in _lights)
        {
            int k = light.Idx;
            if (k < 0 || k > 4) continue;
            _knobBrightness[k] = Math.Clamp(light.Brightness, 0, 100);
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
                EffectPositionFill(k, light, rawPos); // Use raw position (no dead zone) like Turn Up source
                break;

            case LightEffect.CycleFill:
                EffectCycleFill(k, light, rawPos);
                break;

            case LightEffect.RainbowFill:
                EffectRainbowFill(k, light, rawPos);
                break;

            case LightEffect.Blink:
                EffectBlink(k, light);
                break;

            case LightEffect.Pulse:
                EffectPulse(k, light);
                break;

            case LightEffect.RainbowWave:
                EffectRainbowWave(k, light.EffectSpeed);
                break;

            case LightEffect.RainbowCycle:
                EffectRainbowCycle(k, light.EffectSpeed);
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
                EffectPositionBlend(k, light, rawPos);
                break;
            case LightEffect.PositionBlendMute:
                EffectPositionBlendMute(k, light, rawPos);
                break;
            case LightEffect.AudioPositionBlend:
                EffectAudioPositionBlend(k, light, rawPos);
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

            case LightEffect.Heartbeat:
                EffectHeartbeat(k, light);
                break;

            case LightEffect.Plasma:
                EffectPlasma(k, light);
                break;

            case LightEffect.Drip:
                EffectDrip(k, light);
                break;

            // Global-spanning effects: when used per-knob, fall back to simple behavior
            case LightEffect.Aurora:
            case LightEffect.Matrix:
            case LightEffect.Starfield:
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
            case LightEffect.NebulaDrift:
                // Per-knob fallback: just show color1
                SetColor(k, light.R, light.G, light.B);
                break;
        }
    }

    // --- Effect implementations ---

    /// <summary>
    /// All 3 LEDs = color1 (static, not affected by knob position).
    /// Matches original Turn Up behavior: SingleColor is always full-strength
    /// regardless of knob position. This avoids color shift on low-intensity
    /// colors like grey/white where hardware red LEDs overpower at low currents.
    /// </summary>
    private void EffectSingleColor(int k, LightConfig light, float pos)
    {
        SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Sample palette at knob position. With 2-color palette = legacy lerp.
    /// With multi-color palette = smooth gradient through all stops.
    /// </summary>
    private void EffectColorBlend(int k, LightConfig light, float pos)
    {
        var (r, g, b) = GetKnobPaletteColor(light, pos);
        SetColor(k, r, g, b);
    }

    /// <summary>
    /// Smooth progressive fill — each LED fades in across its third of the range.
    /// Matches Turn Up source: LED 0 fades 0-33%, LED 1 fades 33-66%, LED 2 fades 66-100%.
    /// </summary>
    private void EffectPositionFill(int k, LightConfig light, float pos)
    {
        float pct = pos * 100f;

        // LED 0: fades in from 0-33%, full above 33%
        float led0 = pct < 33f ? pct / 33f : 1f;
        // LED 1: off until 33%, fades in 33-66%, full above 66%
        float led1 = pct < 33f ? 0f : pct < 66f ? (pct - 33f) / 33f : 1f;
        // LED 2: off until 66%, fades in 66-100%
        float led2 = pct > 66f ? (pct - 66f) / 34f : 0f;

        SetColor(k, 0, (int)(light.R * led0), (int)(light.G * led0), (int)(light.B * led0));
        SetColor(k, 1, (int)(light.R * led1), (int)(light.G * led1), (int)(light.B * led1));
        SetColor(k, 2, (int)(light.R * led2), (int)(light.G * led2), (int)(light.B * led2));
    }

    /// <summary>
    /// Smooth progressive fill with animated color cycling between color1 and color2.
    /// Fill level follows knob position, color oscillates over time.
    /// </summary>
    private void EffectCycleFill(int k, LightConfig light, float pos)
    {
        float pct = pos * 100f;

        float led0 = pct < 33f ? pct / 33f : 1f;
        float led1 = pct < 33f ? 0f : pct < 66f ? (pct - 33f) / 33f : 1f;
        float led2 = pct > 66f ? (pct - 66f) / 34f : 0f;

        // Cycle color between color1 and color2 over time
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 4.0f - (speed / 100f * 3.5f); // 4s to 0.5s
        float periodTicks = periodSec / 0.05f;
        float t = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1);
        float blend = (float)(Math.Sin(t * Math.PI * 2) * 0.5 + 0.5); // smooth 0→1→0

        var (r, g, b) = GetKnobPaletteColor(light, blend);

        SetColor(k, 0, (int)(r * led0), (int)(g * led0), (int)(b * led0));
        SetColor(k, 1, (int)(r * led1), (int)(g * led1), (int)(b * led1));
        SetColor(k, 2, (int)(r * led2), (int)(g * led2), (int)(b * led2));
    }

    /// <summary>
    /// Smooth progressive fill with rainbow colors cycling through the filled LEDs.
    /// Fill level follows knob position, hue shifts over time.
    /// </summary>
    private void EffectRainbowFill(int k, LightConfig light, float pos)
    {
        float pct = pos * 100f;

        float led0 = pct < 33f ? pct / 33f : 1f;
        float led1 = pct < 33f ? 0f : pct < 66f ? (pct - 33f) / 33f : 1f;
        float led2 = pct > 66f ? (pct - 66f) / 34f : 0f;

        // Rainbow hue shifts over time, each LED offset by 120°
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 5.0f - (speed / 100f * 4.5f); // 5s to 0.5s
        float periodTicks = periodSec / 0.05f;
        float baseHue = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * 360f;

        float[] brightness = { led0, led1, led2 };
        for (int led = 0; led < 3; led++)
        {
            float hue = (baseHue + led * 120f) % 360f;
            HsvToRgb(hue, 1f, brightness[led], out int r, out int g, out int b);
            SetColor(k, led, r, g, b);
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
        float phase = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1);
        // Step through palette in discrete jumps (blink, not smooth)
        float t = phase < 0.5f ? 0f : 1f;
        var (r, g, b) = GetKnobPaletteColor(light, t);
        SetColor(k, r, g, b);
    }

    /// <summary>
    /// Smooth sine-wave oscillation between color1 and color2.
    /// </summary>
    private void EffectPulse(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float periodSec = 2.0f - (speed / 100f * 1.9f);
        float periodTicks = periodSec / 0.05f;
        float baseAngle = (float)(_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1) * MathF.PI * 2f;

        for (int led = 0; led < 3; led++)
        {
            // Phase offset per LED creates a ripple wave across the 3 LEDs
            float angle = baseAngle - led * 0.5f;
            float t = (MathF.Sin(angle) + 1f) / 2f;

            var (r, g, b) = GetKnobPaletteColor(light, t);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// HSV rainbow across all knobs. Each knob offset by 72 degrees (360/5).
    /// Hue animates over time. All 3 LEDs on a knob share the same color.
    /// </summary>
    private void EffectRainbowWave(int k, int speed = 50)
    {
        float rate = 0.5f + (speed / 100f) * 3.5f; // 0.5 to 4.0 degrees per tick
        for (int led = 0; led < 3; led++)
        {
            int globalIdx = k * 3 + led;
            float hue = ((_animTick * rate) + globalIdx * 24f) % 360f;
            var (r, g, b) = HsvToRgb(hue, 1f, 1f);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Each of the 3 LEDs on a knob gets a different hue offset (0, 120, 240).
    /// Hue animates over time. Speed controls rotation rate.
    /// </summary>
    private void EffectRainbowCycle(int k, int speed = 50)
    {
        float rate = 0.5f + (speed / 100f) * 3.5f;
        float baseHue = (_animTick * rate) % 360f;
        for (int led = 0; led < 3; led++)
        {
            float hue = (baseHue + led * 120f) % 360f;
            var (r, g, b) = HsvToRgb(hue, 1f, 1f);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Mic status: unmuted = solid color1, muted = solid color2.
    /// </summary>
    private void EffectMicStatus(int k, LightConfig light)
    {
        if (_micMuted)
            SetColor(k, light.R2, light.G2, light.B2);
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Master device mute: unmuted = solid color1, muted = solid color2.
    /// </summary>
    private void EffectDeviceMute(int k, LightConfig light)
    {
        if (_masterMuted)
            SetColor(k, light.R2, light.G2, light.B2);
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

        // BeatPulse: all 3 LEDs pulse together
        // SpectrumBands: 3 LEDs show progressive fill (VU-meter style)
        if (light.ReactiveMode == ReactiveMode.SpectrumBands)
        {
            // VU-meter fill: LED 0 lights first, LED 1 at 33%, LED 2 at 66%
            float[] ledBright = {
                Math.Clamp(level * 3f, 0f, 1f),
                Math.Clamp((level - 0.33f) * 3f, 0f, 1f),
                Math.Clamp((level - 0.66f) * 3f, 0f, 1f),
            };
            for (int led = 0; led < 3; led++)
            {
                float lb = ledBright[led];
                var (cr, cg, cb) = GetKnobPaletteColor(light, lb);
                SetColor(k, led, (int)(cr * lb), (int)(cg * lb), (int)(cb * lb));
            }
        }
        else
        {
            var (r, g, b2) = GetKnobPaletteColor(light, level);
            SetColor(k, r, g, b2);
        }
    }

    /// <summary>
    /// Hybrid effect: AudioReactive (SpectrumBands) when music is playing,
    /// smooth crossfade to PositionBlend when audio stops.
    /// Uses color1→color2 gradient for both modes.
    /// </summary>
    private void EffectAudioPositionBlend(int k, LightConfig light, float pos)
    {
        var bands = _getAudioBands?.Invoke();
        bool hasAudio = bands != null && bands.Length >= 5;

        // Detect if audio is actually active (not just noise floor)
        float energy = 0;
        if (hasAudio)
        {
            for (int b = 0; b < 5; b++) energy += bands![b];
            energy /= 5f;
        }
        bool audioActive = hasAudio && energy > 0.02f;

        // PositionBlend that pulses with music:
        // The fill stays based on knob position, but brightness modulates with audio energy.
        // When silent, shows steady position fill. When music plays, brightness beats.

        // Compute audio energy for this knob's frequency band
        float sensitivity = light.EffectSpeed / 50f;
        float bandLevel = audioActive ? Math.Clamp(bands![Math.Clamp(k, 0, 4)] * sensitivity, 0f, 1f) : 0f;

        // Smooth the beat: fast attack, slower decay
        float prev = _audioBlendFade[k];
        float smoothed = bandLevel > prev
            ? Math.Min(prev + 0.3f, 1f)   // fast attack
            : Math.Max(prev - 0.06f, 0f); // medium decay
        _audioBlendFade[k] = smoothed;

        // Brightness multiplier: idle = 1.0, with audio = 0.15 base + 0.85 * beat
        // This makes the fill pulse between dim and full brightness with the music
        float brightMul = audioActive ? 0.15f + 0.85f * smoothed : 1f;

        // Compute PositionBlend fill (3 LEDs)
        float pct = pos * 100f;
        float led0Pos = pct < 33f ? pct / 33f : 1f;
        float led1Pos = pct < 33f ? 0f : pct < 66f ? (pct - 33f) / 33f : 1f;
        float led2Pos = pct > 66f ? (pct - 66f) / 34f : 0f;
        float[] posBright = { led0Pos, led1Pos, led2Pos };

        // Render position fill with beat-modulated brightness
        for (int led = 0; led < 3; led++)
        {
            float t = led / 2f; // gradient position 0, 0.5, 1.0
            var (cr, cg, cb) = GetKnobPaletteColor(light, t);
            float bright = posBright[led] * brightMul;

            int r = (int)Math.Clamp(cr * bright, 0, 255);
            int g = (int)Math.Clamp(cg * bright, 0, 255);
            int b = (int)Math.Clamp(cb * bright, 0, 255);
            SetColor(k, led, r, g, b);
        }
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
            // Sample palette at LED position for gradient across the 3 LEDs
            float t = led / 2f;
            var (cr, cg, cb) = GetKnobPaletteColor(light, t);
            int r = Math.Clamp((int)(cr * brightness), 0, 255);
            int g = Math.Clamp((int)(cg * brightness), 0, 255);
            int b = Math.Clamp((int)(cb * brightness), 0, 255);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Randomized warm flickering across the 3 LEDs. Each LED gets independent brightness.
    /// </summary>
    private void EffectFire(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float smoothing = 0.05f + (speed / 100f) * 0.2f; // smooth like Candle

        for (int led = 0; led < 3; led++)
        {
            // Smooth interpolation toward random target (no strobing)
            _candleCurrent[k, led] += (_candleTarget[k, led] - _candleCurrent[k, led]) * smoothing;
            if (Math.Abs(_candleCurrent[k, led] - _candleTarget[k, led]) < 0.05f)
                _candleTarget[k, led] = 0.15f + (float)_rng.NextDouble() * 0.85f;

            float bright = _candleCurrent[k, led];
            // LED 0 = deep ember (palette start), LED 2 = bright tip (palette end)
            // Fire maps heat intensity to palette position: low=ember, high=flame
            float heatT = led / 2f; // 0.0=bottom ember, 1.0=top flame
            var (cr, cg, cb) = GetKnobPaletteColor(light, heatT);
            int r = Math.Clamp((int)(cr * bright), 0, 255);
            int g = Math.Clamp((int)(cg * bright), 0, 255);
            int b = Math.Clamp((int)(cb * bright), 0, 255);
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
            // Head glows white-hot, tail shows palette gradient
            float ledT = led / 2f;
            float whiteBlend = dist == 0 ? 0.3f : 0f;
            var (cr, cg, cb) = GetKnobPaletteColor(light, ledT);
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

        // Set palette-gradient base dim color on each LED
        for (int led = 0; led < 3; led++)
        {
            float t = led / 2f;
            var (cr, cg, cb) = GetKnobPaletteColor(light, t);
            SetColor(k, led, (int)(cr * 0.15f), (int)(cg * 0.15f), (int)(cb * 0.15f));
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
            // Sparkle flashes palette end color blended with white at peak
            var (pr, pg, pb) = GetKnobPaletteColor(light, 1f);
            float whiteBlend = sparkBright * 0.4f;
            int sr = Math.Clamp((int)((pr * (1f - whiteBlend) + 255 * whiteBlend) * sparkBright), 0, 255);
            int sg = Math.Clamp((int)((pg * (1f - whiteBlend) + 255 * whiteBlend) * sparkBright), 0, 255);
            int sb = Math.Clamp((int)((pb * (1f - whiteBlend) + 255 * whiteBlend) * sparkBright), 0, 255);
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
            var (r, g, b) = GetKnobPaletteColor(light, t);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Smooth progressive fill with color gradient — each LED fades in across its third.
    /// LED colors blend from color1 (LED 0) to color2 (LED 2).
    /// </summary>
    private void EffectPositionBlend(int k, LightConfig light, float pos)
    {
        float pct = pos * 100f;

        float led0 = pct < 33f ? pct / 33f : 1f;
        float led1 = pct < 33f ? 0f : pct < 66f ? (pct - 33f) / 33f : 1f;
        float led2 = pct > 66f ? (pct - 66f) / 34f : 0f;

        float[] brightness = { led0, led1, led2 };
        for (int led = 0; led < 3; led++)
        {
            float t = led / 2f; // 0, 0.5, 1.0 — gradient position
            var (cr, cg, cb) = GetKnobPaletteColor(light, t);
            int r = Math.Clamp((int)(cr * brightness[led]), 0, 255);
            int g = Math.Clamp((int)(cg * brightness[led]), 0, 255);
            int b = Math.Clamp((int)(cb * brightness[led]), 0, 255);
            SetColor(k, led, r, g, b);
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

            // Bright dot uses palette end, dim background uses palette start
            var (cr, cg, cb) = GetKnobPaletteColor(light, brightness);
            var (dr, dg, db) = GetKnobPaletteColor(light, 0f);
            int r = Math.Clamp((int)(cr * brightness + dr * 0.08f * (1f - brightness)), 0, 255);
            int g = Math.Clamp((int)(cg * brightness + dg * 0.08f * (1f - brightness)), 0, 255);
            int b = Math.Clamp((int)(cb * brightness + db * 0.08f * (1f - brightness)), 0, 255);
            SetColor(k, led, r, g, b);
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
                // Palette gradient across lit LEDs
                float t = led / 2f;
                var (r, g, b) = GetKnobPaletteColor(light, t);
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

            // Sample palette across the 3 LEDs
            float t = led / 2f;
            var (cr, cg, cb) = GetKnobPaletteColor(light, t);
            int r = Math.Clamp((int)(cr * brightness), 0, 255);
            int g = Math.Clamp((int)(cg * brightness), 0, 255);
            int b = Math.Clamp((int)(cb * brightness), 0, 255);
            SetColor(k, led, r, g, b);
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
            // Map brightness to palette: dim=ember(0.0), bright=flame(1.0)
            var (cr, cg, cb) = GetKnobPaletteColor(light, bright);
            int r = Math.Clamp((int)(cr * bright), 0, 255);
            int g = Math.Clamp((int)(cg * bright), 0, 255);
            int b = Math.Clamp((int)(cb * bright), 0, 255);
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

            // Head samples palette end, tail samples palette start
            float blendT = dist < 0.5f ? 1f : dist < 1.5f ? 0.5f : 0f;
            var (cr, cg, cb) = GetKnobPaletteColor(light, blendT);
            int r = Math.Clamp((int)(cr * brightness), 0, 255);
            int g = Math.Clamp((int)(cg * brightness), 0, 255);
            int b = Math.Clamp((int)(cb * brightness), 0, 255);
            SetColor(k, led, r, g, b);
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
    /// Realistic heartbeat: two quick pulses (lub-dub) then pause.
    /// Color1 = pulse color. Speed controls BPM.
    /// </summary>
    private void EffectHeartbeat(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        // BPM: speed 1 = 40 BPM, speed 50 = 70 BPM, speed 100 = 120 BPM
        float bpm = 40f + (speed / 100f) * 80f;
        float periodTicks = 60f / bpm / 0.05f; // ticks per beat
        float t = (_animTick % (int)Math.Max(periodTicks, 1)) / Math.Max(periodTicks, 1);

        // Heartbeat shape: two peaks at t=0.0 and t=0.2, rest is silence
        float pulse = 0f;
        if (t < 0.08f) pulse = MathF.Sin(t / 0.08f * MathF.PI); // first beat (lub)
        else if (t > 0.15f && t < 0.25f) pulse = MathF.Sin((t - 0.15f) / 0.10f * MathF.PI) * 0.7f; // second beat (dub, softer)

        pulse *= pulse; // sharpen the peaks

        for (int led = 0; led < 3; led++)
        {
            // Slight outward ripple: center LED leads, outer LEDs follow
            float ledDelay = Math.Abs(led - 1) * 0.03f;
            float ledPulse = Math.Max(0f, pulse - ledDelay * 5f);
            int r = Math.Clamp((int)(light.R * ledPulse + light.R * 0.05f), 0, 255);
            int g = Math.Clamp((int)(light.G * ledPulse + light.G * 0.05f), 0, 255);
            int b = Math.Clamp((int)(light.B * ledPulse + light.B * 0.05f), 0, 255);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Psychedelic color morphing via overlapping sine waves at different frequencies.
    /// Each LED gets a different phase for organic flowing color.
    /// </summary>
    private void EffectPlasma(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        float rate = 0.02f + (speed / 100f) * 0.1f;
        float t = _animTick * rate;

        for (int led = 0; led < 3; led++)
        {
            float offset = k * 1.7f + led * 2.3f; // unique offset per knob+LED
            float v1 = MathF.Sin(t * 1.1f + offset);
            float v2 = MathF.Sin(t * 1.7f + offset * 0.7f);
            float v3 = MathF.Sin(t * 0.6f + offset * 1.3f);
            float blend = (v1 + v2 + v3 + 3f) / 6f; // normalize 0-1

            // Cycle through hue space for psychedelic effect
            float hue = (t * 30f + blend * 120f + k * 72f) % 360f;
            HsvToRgb(hue, 0.9f, 0.3f + blend * 0.7f, out int r, out int g, out int b);
            SetColor(k, led, r, g, b);
        }
    }

    /// <summary>
    /// Liquid drip: droplet forms at LED 0, falls to LED 2, splashes.
    /// Color1 = drop color. Speed controls drip rate.
    /// </summary>
    private void EffectDrip(int k, LightConfig light)
    {
        int speed = Math.Clamp(light.EffectSpeed, 1, 100);
        _dripTick[k]++;

        switch (_dripPhase[k])
        {
            case 0: // Forming: LED 0 brightens
                float formProgress = Math.Min(_dripTick[k] / 15f, 1f);
                SetColor(k, 0, (int)(light.R * formProgress), (int)(light.G * formProgress), (int)(light.B * formProgress));
                SetColor(k, 1, 0, 0, 0);
                SetColor(k, 2, 0, 0, 0);
                if (_dripTick[k] >= 15) { _dripPhase[k] = 1; _dripTick[k] = 0; _dripPos[k] = 0f; }
                break;

            case 1: // Falling: bright pixel drops from LED 0 to LED 2
                float fallSpeed = 0.1f + (speed / 100f) * 0.2f;
                _dripPos[k] += fallSpeed;
                for (int led = 0; led < 3; led++)
                {
                    float dist = Math.Abs(led - _dripPos[k]);
                    float bright = Math.Max(0f, 1f - dist * 1.5f);
                    SetColor(k, led, (int)(light.R * bright), (int)(light.G * bright), (int)(light.B * bright));
                }
                if (_dripPos[k] >= 2f) { _dripPhase[k] = 2; _dripTick[k] = 0; }
                break;

            case 2: // Splash: LED 2 flashes bright, spreads to LED 1
                float splashT = _dripTick[k] / 8f;
                float led2 = Math.Max(0f, 1f - splashT);
                float led1 = splashT < 0.5f ? splashT * 1.5f : Math.Max(0f, 1f - (splashT - 0.5f) * 2f);
                // White-blended splash
                SetColor(k, 0, 0, 0, 0);
                SetColor(k, 1, (int)(light.R * led1 * 0.5f), (int)(light.G * led1 * 0.5f), (int)(light.B * led1 * 0.5f));
                int wr = Math.Clamp((int)(light.R * 0.6f + 255 * 0.4f), 0, 255);
                int wg = Math.Clamp((int)(light.G * 0.6f + 255 * 0.4f), 0, 255);
                int wb = Math.Clamp((int)(light.B * 0.6f + 255 * 0.4f), 0, 255);
                SetColor(k, 2, (int)(wr * led2), (int)(wg * led2), (int)(wb * led2));
                if (_dripTick[k] >= 8) { _dripPhase[k] = 3; _dripTick[k] = 0; }
                break;

            case 3: // Pause
                SetColor(k, 0, 0, 0, 0);
                SetColor(k, 1, 0, 0, 0);
                SetColor(k, 2, 0, 0, 0);
                int pauseLen = Math.Max(5, 30 - speed / 4);
                if (_dripTick[k] >= pauseLen) { _dripPhase[k] = 0; _dripTick[k] = 0; }
                break;
        }
    }

    /// <summary>
    /// Show color1 at full brightness when unmuted, dim color1 at MuteBrightness% when muted. (Issue #9)
    /// </summary>
    private void EffectProgramMute(int k, LightConfig light)
    {
        bool muted;
        lock (_stateLock) muted = _programMuteStates.GetValueOrDefault(k, true); // default to muted if unknown
        if (muted)
        {
            float scale = _muteBrightnessPct / 100f;
            SetColor(k, (int)(light.R * scale), (int)(light.G * scale), (int)(light.B * scale));
        }
        else
            SetColor(k, light.R, light.G, light.B);
    }

    /// <summary>
    /// Show color1 at full brightness when any app unmuted, dim color1 at MuteBrightness% when all muted. (Issue #9)
    /// </summary>
    private void EffectAppGroupMute(int k, LightConfig light)
    {
        bool allMuted;
        lock (_stateLock) allMuted = _appGroupMuteStates.GetValueOrDefault(k, false); // default to unmuted
        if (allMuted)
        {
            float scale = _muteBrightnessPct / 100f;
            SetColor(k, (int)(light.R * scale), (int)(light.G * scale), (int)(light.B * scale));
        }
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

    /// <summary>
    /// Global AudioPositionBlend: renders the configured IdleEffect normally,
    /// then crossfades to audio-reactive SpectrumBands when music is detected.
    /// Works with both spanning effects (Ocean, Aurora, etc.) and per-knob effects.
    /// </summary>
    private void RenderGlobalAudioBlend(GlobalLightConfig gl)
    {
        // ── 1. Detect audio ──
        var bands = _getAudioBands?.Invoke();
        bool hasAudio = bands != null && bands.Length >= 5;
        float energy = 0;
        if (hasAudio)
        {
            for (int b = 0; b < 5; b++) energy += bands![b];
            energy /= 5f;
        }
        bool audioActive = hasAudio && energy > 0.02f;

        // Smooth crossfade: fast attack, slow decay
        float target = audioActive ? 1f : 0f;
        float fade = _audioBlendFadeGlobal;
        if (target > fade)
            fade = Math.Min(fade + 0.15f, 1f);
        else
            fade = Math.Max(fade - 0.03f, 0f);
        _audioBlendFadeGlobal = fade;

        // ── 2. Render idle effect ──
        var idleGl = new GlobalLightConfig
        {
            Enabled = true,
            Effect = gl.IdleEffect,
            R = gl.R, G = gl.G, B = gl.B,
            R2 = gl.R2, G2 = gl.G2, B2 = gl.B2,
            EffectSpeed = gl.EffectSpeed,
            ReactiveMode = gl.ReactiveMode,
            PaletteName = gl.PaletteName,
        };

        if (SpanningEffects.Contains(gl.IdleEffect))
            ApplyGlobalSpanEffect(idleGl);
        else
        {
            for (int k = 0; k < 5; k++)
            {
                var (kr, kg, kb) = GetGradientColor(gl, k / 4f);
                var light = new LightConfig
                {
                    Idx = k, Effect = gl.IdleEffect,
                    R = kr, G = kg, B = kb,
                    R2 = gl.R2, G2 = gl.G2, B2 = gl.B2,
                    EffectSpeed = gl.EffectSpeed,
                    PaletteName = gl.PaletteName,
                };
                ApplyEffect(k, light);
            }
        }

        // If no audio, idle effect is the final output — done
        if (fade < 0.01f)
            return;

        // Capture idle frame (linear pre-gamma colors used by OnFrameReady)
        byte[] idleLinear = new byte[45];
        Array.Copy(_linearColors, idleLinear, 45);

        // ── 3. Render audio reactive (SpectrumBands per knob) ──
        float sensitivity = gl.EffectSpeed / 50f;
        for (int k = 0; k < 5; k++)
        {
            float level = hasAudio ? Math.Clamp(bands![Math.Clamp(k, 0, 4)] * sensitivity, 0f, 1f) : 0f;
            float[] vuBright = {
                Math.Clamp(level * 3f, 0f, 1f),
                Math.Clamp((level - 0.33f) * 3f, 0f, 1f),
                Math.Clamp((level - 0.66f) * 3f, 0f, 1f),
            };
            for (int led = 0; led < 3; led++)
            {
                var (cr, cg, cb) = GetKnobPaletteColor(
                    new LightConfig { R = gl.R, G = gl.G, B = gl.B, R2 = gl.R2, G2 = gl.G2, B2 = gl.B2, PaletteName = gl.PaletteName },
                    vuBright[led]);
                SetColor(k, led, (int)(cr * vuBright[led]), (int)(cg * vuBright[led]), (int)(cb * vuBright[led]));
            }
        }

        // ── 4. Crossfade linear colors (used by OnFrameReady → Govee/Corsair) ──
        for (int i = 0; i < 45; i++)
        {
            _linearColors[i] = (byte)Math.Clamp(
                idleLinear[i] * (1f - fade) + _linearColors[i] * fade, 0, 255);
        }

        // Re-apply gamma from the blended linear colors to the hardware output buffer
        for (int k = 0; k < 5; k++)
            for (int led = 0; led < 3; led++)
            {
                int offset = k * 9 + led * 3 + 2; // +2 for FE 05 header
                int linOff = k * 9 + led * 3;
                _colorMsg[offset]     = _gammaR[_linearColors[linOff]];
                _colorMsg[offset + 1] = _gammaG[_linearColors[linOff + 1]];
                _colorMsg[offset + 2] = _gammaB[_linearColors[linOff + 2]];
            }
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
            case LightEffect.Aurora:        GlobalAurora(gl); break;
            case LightEffect.Matrix:        GlobalMatrix(gl); break;
            case LightEffect.Starfield:     GlobalStarfield(gl); break;
            case LightEffect.Equalizer:     GlobalEqualizer(gl); break;
            case LightEffect.Waterfall:     GlobalWaterfall(gl); break;
            case LightEffect.Lava:          GlobalLava(gl); break;
            case LightEffect.VuWave:        GlobalVuWave(gl); break;
            case LightEffect.NebulaDrift:   GlobalNebulaDrift(gl); break;
            case LightEffect.Vortex:       GlobalVortex(gl); break;
            case LightEffect.Shockwave:    GlobalShockwave(gl); break;
            case LightEffect.Tidal:        GlobalTidal(gl); break;
            case LightEffect.Prism:        GlobalPrism(gl); break;
            case LightEffect.EmberDrift:   GlobalEmberDrift(gl); break;
            case LightEffect.Glitch:       GlobalGlitch(gl); break;
            case LightEffect.OpalWave:     GlobalOpalWave(gl); break;
            case LightEffect.Bloom:        GlobalBloom(gl); break;
            case LightEffect.ColorTwinkle: GlobalColorTwinkle(gl); break;
            case LightEffect.AuroraVeil:       GlobalAuroraVeil(gl); break;
            case LightEffect.SolarStorm:       GlobalSolarStorm(gl); break;
            case LightEffect.StarlightCanopy:  GlobalStarlightCanopy(gl); break;
            case LightEffect.PlasmaBloom:      GlobalPlasmaBloom(gl); break;
            case LightEffect.RippleRoom:       GlobalRippleRoom(gl); break;
            case LightEffect.PrismDrift:       GlobalPrismDrift(gl); break;
            case LightEffect.NebulaRain:       GlobalNebulaRain(gl); break;
            case LightEffect.ReactiveAurora:   GlobalReactiveAurora(gl); break;
            case LightEffect.LiquidGlass:      GlobalLiquidGlass(gl); break;
            case LightEffect.ChromaLayerStack: GlobalChromaLayerStack(gl); break;
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
        float t = _animTick * (0.01f + speed / 100f * 0.06f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            // Three sine layers at different frequencies for organic, non-repeating motion
            float wave1 = MathF.Sin(t * 0.9f + pos * 5f) * 0.5f + 0.5f;
            float wave2 = MathF.Sin(t * 1.4f + pos * 7f + 1.8f) * 0.5f + 0.5f;
            float wave3 = MathF.Sin(t * 0.5f + pos * 3f + 3.5f) * 0.5f + 0.5f;
            float combined = (wave1 + wave2 * 0.6f + wave3 * 0.3f) / 1.9f;

            // Use combined to pick gradient position — still respects user palette
            var (cr, cg, cb) = GetGradientColor(gl, combined);

            // Brightness squaring for dramatic contrast — bright peaks, dark troughs
            float brightness = 0.12f + combined * 0.88f;
            brightness *= brightness;

            int r = Math.Clamp((int)(cr * brightness), 0, 255);
            int g = Math.Clamp((int)(cg * brightness), 0, 255);
            int b = Math.Clamp((int)(cb * brightness), 0, 255);
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
        float t = _animTick * (0.02f + speed / 100f * 0.06f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;

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

                float random = 0.2f + (float)_rng.NextDouble() * 0.8f;
                _fireWallTarget[i] = random * 0.7f + neighborAvg * 0.3f;
            }

            // Multi-layer sine waves for organic flickering on top of smooth random
            float flicker1 = MathF.Sin(t * 1.2f + pos * 5f) * 0.5f + 0.5f;
            float flicker2 = MathF.Sin(t * 2.1f + pos * 8f + 2f) * 0.5f + 0.5f;
            float flicker3 = MathF.Sin(t * 0.6f + pos * 3f + 4f) * 0.5f + 0.5f;
            float sineLayer = (flicker1 + flicker2 * 0.5f + flicker3 * 0.3f) / 1.8f;

            // Blend smooth random with sine layers (60/40) for organic but structured fire
            float combined = _fireWallCurrent[i] * 0.6f + sineLayer * 0.4f;

            // HSV in warm range: red (0) → orange (30) → yellow (60)
            float hue = combined * 55f + MathF.Sin(t * 0.4f + pos * 4f) * 8f;
            hue = Math.Clamp(hue, 0f, 60f);

            // Brightness squaring for sharp flame tips
            float brightness = 0.08f + combined * 0.92f;
            brightness *= brightness;

            HsvToRgb(hue, 0.9f, brightness, out int r, out int g, out int b);
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
            float pos = i / 14f;
            // Three overlapping sine layers for organic wave motion
            float wave1 = MathF.Sin(t * 0.8f + pos * 4f) * 0.5f + 0.5f;
            float wave2 = MathF.Sin(t * 1.3f + pos * 6f + 1.5f) * 0.5f + 0.5f;
            float wave3 = MathF.Sin(t * 0.5f + pos * 2.5f + 3f) * 0.5f + 0.5f;
            float combined = (wave1 + wave2 * 0.6f + wave3 * 0.3f) / 1.9f;

            // HSV ocean hues: deep blue → teal → cyan, with slow drift
            float hue = 195f + combined * 50f + MathF.Sin(t * 0.25f + pos * 3f) * 20f;
            hue = ((hue % 360f) + 360f) % 360f;

            // Brightness squaring for dramatic wave peaks with dark troughs
            float brightness = 0.1f + combined * 0.9f;
            brightness *= brightness;

            // Whitecap effect: high wave peaks get desaturated (foamy white)
            float capBlend = Math.Max(0f, (combined - 0.75f) / 0.25f);
            float sat = 0.85f * (1f - capBlend * 0.7f); // desaturate at peaks
            float capBright = brightness + capBlend * (1f - brightness) * 0.6f; // brighten at peaks

            HsvToRgb(hue, sat, capBright, out int r, out int g, out int b);
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
        int cycleTicks = Math.Max(30, 80 - (speed * 60 / 100));
        int collisionTicks = 8;
        int fadeTicks = Math.Max(4, cycleTicks / 5);
        int approachTicks = cycleTicks - collisionTicks - fadeTicks;

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

    /// <summary>
    /// Northern lights: slow-drifting bands of green, blue, purple across 15 LEDs.
    /// Uses overlapping sine waves for organic movement.
    /// </summary>
    private void GlobalAurora(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.01f + speed / 100f * 0.05f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            // Multiple sine layers for organic aurora movement
            float wave1 = MathF.Sin(t * 0.7f + pos * 4f) * 0.5f + 0.5f;
            float wave2 = MathF.Sin(t * 1.1f + pos * 6f + 1.5f) * 0.5f + 0.5f;
            float wave3 = MathF.Sin(t * 0.4f + pos * 2f + 3f) * 0.5f + 0.5f;
            float combined = (wave1 + wave2 * 0.6f + wave3 * 0.3f) / 1.9f;

            // Aurora colors: shift through green, teal, blue, purple
            float hue = 120f + combined * 180f + MathF.Sin(t * 0.3f + pos * 3f) * 40f;
            hue = ((hue % 360f) + 360f) % 360f;
            float brightness = 0.15f + combined * 0.85f;
            brightness *= brightness; // sharpen the bands

            HsvToRgb(hue, 0.8f, brightness, out int r, out int g, out int b);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Digital rain: bright green droplets cascade across 15 LEDs with fading trails.
    /// </summary>
    private void GlobalMatrix(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float fallSpeed = 0.15f + (speed / 100f) * 0.4f;

        // Decay all LEDs
        for (int i = 0; i < 15; i++)
        {
            _starBright[i] *= 0.85f; // trail fade
            if (_starBright[i] < 0.02f) _starBright[i] = 0f;
        }

        // Move existing drops down (higher LED index = lower on device)
        // Spawn new drops at random positions
        if (_rng.NextDouble() < 0.1 + speed / 200.0)
        {
            int spawnLed = _rng.Next(3); // spawn at top of a random knob (LED 0 of knob 0-4)
            int spawnKnob = _rng.Next(5);
            int idx = spawnKnob * 3 + spawnLed;
            _starBright[idx] = 1f;
        }

        // Drip: each tick, bright pixels flow downward within each knob
        for (int knob = 0; knob < 5; knob++)
        {
            for (int led = 2; led >= 1; led--)
            {
                int idx = knob * 3 + led;
                int above = knob * 3 + led - 1;
                if (_starBright[above] > 0.7f)
                {
                    _starBright[idx] = Math.Max(_starBright[idx], _starBright[above] * 0.9f);
                    _starBright[above] *= 0.6f;
                }
            }
        }

        for (int i = 0; i < 15; i++)
        {
            float b = _starBright[i];
            // Matrix green with white-hot leading edge
            bool isHead = b > 0.8f;
            int r = isHead ? (int)(180 * b) : (int)(gl.R * b * 0.3f);
            int g = (int)(255 * b);
            int blue = isHead ? (int)(180 * b) : (int)(gl.B * b * 0.3f);
            SetGlobalLed(i, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(blue, 0, 255));
        }
    }

    /// <summary>
    /// Gentle twinkling stars against dark background.
    /// Random LEDs slowly fade in and out at different rates.
    /// </summary>
    private void GlobalStarfield(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float smoothing = 0.02f + (speed / 100f) * 0.08f;

        for (int i = 0; i < 15; i++)
        {
            // Smooth toward target brightness
            _starBright[i] += (_starTarget[i] - _starBright[i]) * smoothing;

            // Pick new target when close
            if (Math.Abs(_starBright[i] - _starTarget[i]) < 0.03f)
            {
                // Most stars are dim, occasional bright ones
                _starTarget[i] = _rng.NextDouble() < 0.3 ? 0.5f + (float)_rng.NextDouble() * 0.5f : (float)_rng.NextDouble() * 0.15f;
            }

            float b = _starBright[i];
            // Slight color variation per star — warm to cool
            float hueShift = (i * 37f) % 60f - 30f; // ±30° variation
            HsvFromRgb(gl.R, gl.G, gl.B, out float h, out float s, out _);
            float hue = ((h + hueShift) % 360f + 360f) % 360f;
            // Bright stars get a white-hot tint
            float sat = b > 0.7f ? s * 0.5f : s;
            HsvToRgb(hue, sat, b, out int r, out int g, out int blue);
            SetGlobalLed(i, r, g, blue);
        }
    }

    /// <summary>
    /// Full-spectrum nebula: like Aurora but shifts through ALL rainbow hues slowly
    /// with multiple sine layers and brightness squaring for dramatic, colorful drift.
    /// </summary>
    private void GlobalNebulaDrift(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.008f + speed / 100f * 0.04f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            // Three overlapping sine layers for organic motion
            float wave1 = MathF.Sin(t * 0.6f + pos * 4f) * 0.5f + 0.5f;
            float wave2 = MathF.Sin(t * 1.0f + pos * 7f + 2f) * 0.5f + 0.5f;
            float wave3 = MathF.Sin(t * 0.35f + pos * 2.5f + 4.5f) * 0.5f + 0.5f;
            float combined = (wave1 + wave2 * 0.6f + wave3 * 0.3f) / 1.9f;

            // Full rainbow hue sweep — slow drift + position-based spread
            float hue = t * 15f + pos * 120f + combined * 180f + MathF.Sin(t * 0.2f + pos * 5f) * 60f;
            hue = ((hue % 360f) + 360f) % 360f;

            // Brightness squaring for sharp vivid bands with dark gaps
            float brightness = 0.1f + combined * 0.9f;
            brightness *= brightness;

            HsvToRgb(hue, 0.85f, brightness, out int r, out int g, out int b);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Classic 5-band equalizer visualization. Each knob = one frequency band.
    /// Bottom LED (2) lights first, then middle (1), then top (0) as level rises.
    /// Looks amazing on vertical wall-mounted segment lights.
    /// </summary>
    private void GlobalEqualizer(GlobalLightConfig gl)
    {
        var bands = _getAudioBands?.Invoke();
        if (bands == null || bands.Length < 5)
            bands = new float[5]; // silent — show dark

        for (int knob = 0; knob < 5; knob++)
        {
            float level = Math.Clamp(bands[knob], 0f, 1f);
            // Color from gradient position (each band gets its own hue)
            var (cr, cg, cb) = GetGradientColor(gl, knob / 4f);

            for (int led = 0; led < 3; led++)
            {
                // LED 2 = bottom, LED 0 = top. Map so bottom lights first.
                int physLed = 2 - led; // led 0 = bottom (threshold 0.0), led 2 = top (threshold 0.66)
                float threshold = led / 3f;        // 0.0, 0.33, 0.66
                float nextThreshold = (led + 1) / 3f;

                float brightness;
                if (level >= nextThreshold)
                    brightness = 1f; // fully lit
                else if (level > threshold)
                    brightness = (level - threshold) / (nextThreshold - threshold); // partial fill
                else
                    brightness = 0.03f; // dim background so bars are visible

                // Top segment (led=2, physLed=0) gets a warm peak tint when level is high
                if (led == 2 && level > 0.75f)
                {
                    float peak = (level - 0.75f) / 0.25f;
                    int r = Math.Clamp((int)(cr * brightness * (1f - peak * 0.3f) + 255 * peak * 0.5f), 0, 255);
                    int g = Math.Clamp((int)(cg * brightness * (1f - peak * 0.5f)), 0, 255);
                    int b = Math.Clamp((int)(cb * brightness * (1f - peak * 0.5f)), 0, 255);
                    SetColor(knob, physLed, r, g, b);
                }
                else
                {
                    SetColor(knob, physLed,
                        Math.Clamp((int)(cr * brightness), 0, 255),
                        Math.Clamp((int)(cg * brightness), 0, 255),
                        Math.Clamp((int)(cb * brightness), 0, 255));
                }
            }
        }
    }

    /// <summary>
    /// Colors cascade downward like a waterfall. New colors appear at the top
    /// and flow down across all 15 LEDs. Speed adjustable. Uses palette gradient.
    /// Gorgeous on vertical wall panels.
    /// </summary>
    private void GlobalWaterfall(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float flowRate = 0.02f + (speed / 100f) * 0.08f;
        float t = _animTick * flowRate;

        for (int i = 0; i < 15; i++)
        {
            // Each LED samples from a scrolling gradient — higher index = further downstream
            float gradPos = (t + i * 0.12f) % 1f;

            var (cr, cg, cb) = GetGradientColor(gl, gradPos);

            // Add a gentle shimmer — slight brightness variation per LED over time
            float shimmer = 0.75f + 0.25f * MathF.Sin(_animTick * 0.15f + i * 0.9f);

            // Water sparkle effect: occasional bright flashes
            float sparkle = 1f;
            float sparklePhase = MathF.Sin(_animTick * 0.3f + i * 2.7f);
            if (sparklePhase > 0.92f)
                sparkle = 1.3f + (sparklePhase - 0.92f) * 5f;

            float brightness = Math.Clamp(shimmer * sparkle, 0f, 1.5f);

            SetGlobalLed(i,
                Math.Clamp((int)(cr * brightness), 0, 255),
                Math.Clamp((int)(cg * brightness), 0, 255),
                Math.Clamp((int)(cb * brightness), 0, 255));
        }
    }

    /// <summary>
    /// Organic rising/falling blobs of warm color, like a lava lamp.
    /// Multiple independent blobs with smooth sine-based motion.
    /// Mesmerizing on vertical wall-mounted LED bars.
    /// </summary>
    private void GlobalLava(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.015f + speed / 100f * 0.06f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f; // 0..1 across the strip

            // 4 independent blobs at different speeds and sizes
            float blob1 = MathF.Exp(-8f * MathF.Pow(pos - (0.5f + 0.45f * MathF.Sin(t * 0.7f)), 2));
            float blob2 = MathF.Exp(-6f * MathF.Pow(pos - (0.5f + 0.4f * MathF.Sin(t * 1.1f + 2f)), 2));
            float blob3 = MathF.Exp(-10f * MathF.Pow(pos - (0.5f + 0.35f * MathF.Sin(t * 0.5f + 4f)), 2));
            float blob4 = MathF.Exp(-7f * MathF.Pow(pos - (0.3f + 0.3f * MathF.Sin(t * 0.9f + 1f)), 2));

            float combined = Math.Clamp(blob1 + blob2 * 0.8f + blob3 * 0.6f + blob4 * 0.5f, 0f, 1f);

            // Sample palette color based on blob intensity — hotter blobs toward end of gradient
            var (cr, cg, cb) = GetGradientColor(gl, combined);

            // Brightness follows blob intensity with a warm ambient floor
            float brightness = 0.05f + combined * 0.95f;
            brightness *= brightness; // square for sharper blob edges

            SetGlobalLed(i,
                Math.Clamp((int)(cr * brightness), 0, 255),
                Math.Clamp((int)(cg * brightness), 0, 255),
                Math.Clamp((int)(cb * brightness), 0, 255));
        }
    }

    /// <summary>
    /// Audio-reactive wave that ripples across all 15 LEDs.
    /// Bass creates big rolling waves from center, treble adds small ripples on top.
    /// Uses palette colors. Stunning on wall-mounted segment devices.
    /// </summary>
    private void GlobalVuWave(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.05f + speed / 100f * 0.15f);

        var bands = _getAudioBands?.Invoke();
        float bass = bands != null && bands.Length > 1 ? bands[1] : 0f;
        float mid = bands != null && bands.Length > 2 ? bands[2] : 0f;
        float treble = bands != null && bands.Length > 4 ? bands[4] : 0f;

        // Smooth the audio values for more organic motion
        bass = Math.Clamp(bass, 0f, 1f);
        mid = Math.Clamp(mid, 0f, 1f);
        treble = Math.Clamp(treble, 0f, 1f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            float center = Math.Abs(pos - 0.5f) * 2f; // 0 at center, 1 at edges

            // Bass: big slow wave from center outward
            float bassWave = bass * MathF.Sin(center * MathF.PI * 1.5f - t * 2f) * 0.5f + 0.5f;
            bassWave *= bass;

            // Mid: medium ripples
            float midWave = mid * MathF.Sin(pos * MathF.PI * 4f - t * 3f) * 0.3f;

            // Treble: fast small ripples layered on top
            float trebleWave = treble * MathF.Sin(pos * MathF.PI * 8f - t * 6f) * 0.15f;

            float combined = Math.Clamp(0.05f + bassWave + midWave + trebleWave, 0f, 1f);

            // Color from palette — shifts with audio energy
            float colorPos = Math.Clamp(pos + bass * 0.2f * MathF.Sin(t), 0f, 1f);
            var (cr, cg, cb) = GetGradientColor(gl, colorPos);

            SetGlobalLed(i,
                Math.Clamp((int)(cr * combined), 0, 255),
                Math.Clamp((int)(cg * combined), 0, 255),
                Math.Clamp((int)(cb * combined), 0, 255));
        }
    }

    // ── New room sweep effects (v0.9.9) ──

    /// <summary>
    /// Spiral that accelerates toward center — deep purple/magenta/cyan whirlpool.
    /// Colors shift through jewel tones with organic sine-based spiral motion.
    /// </summary>
    private void GlobalVortex(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.03f + speed / 100f * 0.12f);
        float center = 7f;

        for (int i = 0; i < 15; i++)
        {
            float dist = Math.Abs(i - center) / 7f;
            float spiralPhase = t * (1f + (1f - dist) * 3f) + i * 0.4f;
            float brightness = 0.15f + 0.85f * (0.5f + 0.5f * MathF.Sin(spiralPhase));
            brightness *= 0.4f + 0.6f * (1f - dist * 0.5f);

            // Rich jewel tones: purple→magenta→cyan→violet, spiraling with position
            float hue = 260f + MathF.Sin(spiralPhase * 0.7f) * 80f + dist * 60f;
            hue += MathF.Sin(t * 0.4f + i * 0.3f) * 30f;
            hue = ((hue % 360f) + 360f) % 360f;
            float sat = 0.75f + 0.2f * MathF.Sin(spiralPhase * 1.3f);
            brightness *= brightness; // squared for dramatic contrast

            HsvToRgb(hue, sat, brightness, out int r, out int g, out int b);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Sharp bright pulse expands outward from center — warm gold→orange→red ring with cool blue trail.
    /// Each pulse shifts hue slightly for variety. Bright white-hot leading edge.
    /// </summary>
    private float _shockwavePhase;
    private int _shockwavePulseCount;
    private void GlobalShockwave(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float step = 0.06f + speed / 100f * 0.2f;
        _shockwavePhase += step;

        float cycleLen = 1.3f;
        float phase = _shockwavePhase % cycleLen;
        if (phase < step) _shockwavePulseCount++; // count each new pulse
        float waveFront = phase / 1.0f;

        for (int i = 0; i < 15; i++)
        {
            float dist = Math.Abs(i - 7f) / 7f;
            float ringDist = Math.Abs(dist - waveFront);
            float ringWidth = 0.12f;
            float ring = Math.Max(0f, 1f - ringDist / ringWidth);
            ring *= ring;
            float trail = dist < waveFront ? Math.Max(0f, 1f - (waveFront - dist) * 3f) * 0.3f : 0f;
            float brightness = Math.Clamp(ring + trail, 0f, 1f);
            if (phase > 1.0f) brightness *= Math.Max(0f, 1f - (phase - 1.0f) / 0.3f);

            // Leading edge: hot white→gold. Trail: orange→deep red→cool blue
            float hueBase = 30f + (_shockwavePulseCount % 5) * 20f; // shift each pulse
            float hue;
            float sat;
            if (ring > 0.3f)
            {
                // Leading edge: warm gold, desaturated (white-hot)
                hue = hueBase;
                sat = 0.4f + 0.3f * (1f - ring);
            }
            else
            {
                // Trail: shift from orange→red→blue as it fades
                float trailAge = Math.Max(0f, waveFront - dist);
                hue = hueBase + trailAge * 200f; // 30→230 (gold→blue)
                sat = 0.7f + trailAge * 0.2f;
            }
            hue = ((hue % 360f) + 360f) % 360f;

            HsvToRgb(hue, Math.Clamp(sat, 0f, 1f), brightness, out int r, out int g, out int b);
            SetGlobalLed(i, r, g, b);
        }
    }

    /// <summary>
    /// Rising tide with rich ocean colors — deep navy depths, teal mid-water,
    /// cyan surface with bright white foam crest. Recedes to reveal deep purple.
    /// </summary>
    private void GlobalTidal(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.008f + speed / 100f * 0.03f);

        float tideSin = MathF.Sin(t * MathF.PI * 2f);
        float tideLevel = 0.5f + 0.5f * tideSin;
        float foam = 0.03f * MathF.Sin(t * MathF.PI * 17f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            float waterLine = tideLevel + foam;

            if (pos <= waterLine)
            {
                // Submerged: deep navy at bottom → teal → bright cyan near surface
                float depthRatio = (waterLine - pos) / Math.Max(waterLine, 0.01f);
                // Depth hue: 180 (cyan) at surface → 220 (blue) → 260 (indigo) at deep
                float hue = 180f + depthRatio * 80f;
                hue += MathF.Sin(pos * 8f - t * 3f) * 10f; // subtle shimmer
                float sat = 0.7f + depthRatio * 0.2f;
                float bright = 0.9f - depthRatio * 0.5f;
                // Subtle wave motion
                bright *= 0.9f + 0.1f * MathF.Sin(pos * 12f - t * 5f);

                HsvToRgb(((hue % 360f) + 360f) % 360f, sat, bright, out int r, out int g, out int b);
                SetGlobalLed(i, r, g, b);
            }
            else
            {
                // Above water: bright foam crest, dark sky above
                float distFromWater = pos - waterLine;
                float crest = Math.Max(0f, 1f - distFromWater * 15f);
                crest *= crest;
                // Foam: white-cyan at crest, fading to dark purple-blue sky
                float skyHue = 260f + distFromWater * 40f; // deep purple sky
                if (crest > 0.1f)
                {
                    // Foam: near-white cyan
                    HsvToRgb(185f, 0.15f * (1f - crest), crest, out int r, out int g, out int b);
                    SetGlobalLed(i, r, g, b);
                }
                else
                {
                    // Dark sky with faint purple
                    float skyBright = 0.03f + 0.05f * MathF.Sin(t * 0.5f + pos * 4f);
                    HsvToRgb(((skyHue % 360f) + 360f) % 360f, 0.6f, skyBright, out int r, out int g, out int b);
                    SetGlobalLed(i, r, g, b);
                }
            }
        }
    }

    /// <summary>
    /// Light through a prism — full visible spectrum bands spread apart then merge to white.
    /// Uses all 360 degrees of hue with high saturation for vivid spectral colors.
    /// </summary>
    private void GlobalPrism(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.015f + speed / 100f * 0.06f);

        float spread = 0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2f);
        float offset = t * 0.5f;

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            // Full spectral spread: red→orange→yellow→green→cyan→blue→violet
            float hue = ((pos * spread * 300f + offset * 360f) % 360f + 360f) % 360f;

            // High saturation for vivid spectral colors, slight variation per position
            float sat = 0.85f + 0.15f * MathF.Sin(pos * MathF.PI);
            float bright = 0.8f + 0.2f * spread;

            HsvToRgb(hue, sat, bright, out int sr, out int sg, out int sb);

            // Blend toward warm white when merged (spread=0)
            float whiteBlend = (1f - spread) * 0.8f;
            int r = Math.Clamp((int)(sr + (255 - sr) * whiteBlend), 0, 255);
            int g = Math.Clamp((int)(sg + (245 - sg) * whiteBlend), 0, 255); // warm white
            int b = Math.Clamp((int)(sb + (220 - sb) * whiteBlend), 0, 255);

            float mergePulse = 0.85f + 0.15f * (1f - spread);
            SetGlobalLed(i,
                Math.Clamp((int)(r * mergePulse), 0, 255),
                Math.Clamp((int)(g * mergePulse), 0, 255),
                Math.Clamp((int)(b * mergePulse), 0, 255));
        }
    }

    /// <summary>
    /// Floating embers — warm particles drifting through amber, copper, rose, and gold hues.
    /// Each ember has its own unique warm hue that slowly shifts. Dark base with rich glow.
    /// </summary>
    private readonly float[] _emberSpeeds = new float[] { 0.013f, 0.021f, 0.017f, 0.009f, 0.025f, 0.011f };
    private readonly float[] _emberPhases = new float[] { 0f, 2.1f, 4.3f, 1.2f, 3.7f, 5.5f };
    // Each ember gets a unique warm hue offset: amber, copper, gold, rose, peach, crimson
    private readonly float[] _emberHues = new float[] { 25f, 15f, 42f, 345f, 30f, 5f };
    private void GlobalEmberDrift(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float speedMul = 0.5f + speed / 100f * 2f;

        // Dark warm base: very dim deep red
        for (int i = 0; i < 15; i++)
        {
            float baseHue = 10f + MathF.Sin(i * 0.5f + _animTick * 0.005f) * 15f;
            HsvToRgb(baseHue, 0.9f, 0.04f, out int br, out int bg, out int bb);
            SetGlobalLed(i, br, bg, bb);
        }

        // Float 6 independent embers with unique warm hues
        for (int e = 0; e < 6; e++)
        {
            float emberPos = 7f + 6.5f * MathF.Sin(_animTick * _emberSpeeds[e] * speedMul + _emberPhases[e]);
            float glow = 0.4f + 0.6f * (0.5f + 0.5f * MathF.Sin(_animTick * _emberSpeeds[e] * speedMul * 1.7f + _emberPhases[e] * 2f));

            // Each ember shifts hue slowly over time
            float hue = _emberHues[e] + MathF.Sin(_animTick * 0.01f + e * 1.5f) * 15f;
            hue = ((hue % 360f) + 360f) % 360f;
            float sat = 0.85f + 0.1f * MathF.Sin(_animTick * 0.02f + e);

            HsvToRgb(hue, sat, glow, out int er, out int eg, out int eb);

            for (int i = 0; i < 15; i++)
            {
                float dist = Math.Abs(i - emberPos);
                if (dist > 2.5f) continue;
                float falloff = MathF.Exp(-dist * dist * 0.6f);
                int offset = i * 3;
                int curR = _linearColors[offset], curG = _linearColors[offset + 1], curB = _linearColors[offset + 2];
                SetGlobalLed(i,
                    Math.Clamp(curR + (int)(er * falloff), 0, 255),
                    Math.Clamp(curG + (int)(eg * falloff), 0, 255),
                    Math.Clamp(curB + (int)(eb * falloff), 0, 255));
            }
        }
    }

    /// <summary>
    /// Cyberpunk digital corruption — neon cyan/magenta/green base with harsh white glitch bursts.
    /// Each glitch has randomized neon hue corruption. Scan lines flash across.
    /// </summary>
    private readonly float[] _glitchLevels = new float[15];
    private readonly float[] _glitchHueShift = new float[15];
    private int _glitchCooldown;
    private void GlobalGlitch(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);

        // Base: dim neon gradient — cyan→magenta→green shifting slowly
        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            float baseHue = 170f + pos * 120f + MathF.Sin(_animTick * 0.02f) * 30f; // cyan→magenta range
            baseHue = ((baseHue % 360f) + 360f) % 360f;
            float baseBright = 0.12f + 0.06f * MathF.Sin(pos * 6f + _animTick * 0.05f);
            HsvToRgb(baseHue, 0.9f, baseBright + _glitchLevels[i] * 0.3f, out int r, out int g, out int b);
            SetGlobalLed(i, r, g, b);
        }

        // Decay existing glitches
        for (int i = 0; i < 15; i++)
        {
            _glitchLevels[i] *= 0.75f;
            if (_glitchLevels[i] < 0.02f) _glitchLevels[i] = 0f;
        }

        // Spawn new glitch burst
        _glitchCooldown--;
        if (_glitchCooldown <= 0)
        {
            int burstLen = 1 + _rng.Next(4);
            int burstStart = _rng.Next(15 - burstLen + 1);
            for (int i = burstStart; i < burstStart + burstLen; i++)
            {
                _glitchLevels[i] = 0.6f + (float)_rng.NextDouble() * 0.4f;
                // Random neon hue: cyan(180), magenta(300), green(120), yellow(60)
                float[] neonHues = { 180f, 300f, 120f, 60f, 200f, 330f };
                _glitchHueShift[i] = neonHues[_rng.Next(neonHues.Length)];
            }
            int minWait = Math.Max(1, 12 - speed / 10);
            int maxWait = Math.Max(minWait + 1, 30 - speed / 4);
            _glitchCooldown = minWait + _rng.Next(maxWait - minWait);

            if (_rng.Next(5) == 0)
            {
                for (int i = 0; i < 15; i++)
                    _glitchLevels[i] = Math.Max(_glitchLevels[i], 0.3f + (float)_rng.NextDouble() * 0.2f);
            }
        }

        // Render glitch bursts with neon hue corruption
        for (int i = 0; i < 15; i++)
        {
            if (_glitchLevels[i] <= 0.02f) continue;
            float level = _glitchLevels[i];
            // Glitch color: neon hue with high saturation, going white-hot at peak
            float sat = Math.Max(0f, 0.9f - level * 0.6f); // desaturates toward white at high levels
            HsvToRgb(_glitchHueShift[i], sat, level, out int gr, out int gg, out int gb);
            // Additive blend on top of base
            int offset = i * 3;
            SetGlobalLed(i,
                Math.Clamp(_linearColors[offset] + gr, 0, 255),
                Math.Clamp(_linearColors[offset + 1] + gg, 0, 255),
                Math.Clamp(_linearColors[offset + 2] + gb, 0, 255));
        }
    }

    /// <summary>
    /// Soft pearlescent waves inspired by popular palette/noise effects in WLED/FastLED.
    /// Uses the current palette and continuously shifts it through overlapping slow waves.
    /// </summary>
    private void GlobalOpalWave(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.01f + speed / 100f * 0.045f);

        for (int i = 0; i < 15; i++)
        {
            float pos = i / 14f;
            float flow = pos
                + 0.18f * MathF.Sin(t * 1.3f + pos * MathF.PI * 2.3f)
                + 0.08f * MathF.Sin(t * 2.1f - pos * MathF.PI * 4.7f);

            float wrapped = flow - MathF.Floor(flow);
            var (pr, pg, pb) = GetGradientColor(gl, wrapped);

            float shimmer = 0.72f
                + 0.18f * MathF.Sin(t * 2.2f + pos * 7f)
                + 0.10f * MathF.Sin(t * 4.5f - pos * 11f);
            shimmer = Math.Clamp(shimmer, 0.45f, 1f);

            // Pearlescent lift toward white keeps multi-color palettes dreamy instead of muddy.
            int r = Math.Clamp((int)(pr + (255 - pr) * 0.18f), 0, 255);
            int g = Math.Clamp((int)(pg + (248 - pg) * 0.14f), 0, 255);
            int b = Math.Clamp((int)(pb + (255 - pb) * 0.20f), 0, 255);

            SetGlobalLed(i,
                Math.Clamp((int)(r * shimmer), 0, 255),
                Math.Clamp((int)(g * shimmer), 0, 255),
                Math.Clamp((int)(b * shimmer), 0, 255));
        }
    }

    /// <summary>
    /// Expanding floral color blooms. Each pulse opens from a drifting center and fades
    /// through the active palette, giving a softer palette-driven alternative to Shockwave.
    /// </summary>
    private void GlobalBloom(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.012f + speed / 100f * 0.055f);
        float center = 7f + MathF.Sin(t * 0.7f) * 2.6f;
        float bloomRadius = 0.6f + 6.2f * (0.5f + 0.5f * MathF.Sin(t * 1.4f));

        for (int i = 0; i < 15; i++)
        {
            float dist = Math.Abs(i - center);
            float shell = MathF.Max(0f, 1f - MathF.Abs(dist - bloomRadius) / 1.9f);
            float core = MathF.Max(0f, 1f - dist / (1.4f + 0.35f * bloomRadius));
            float glow = Math.Clamp(shell * 0.85f + core * 0.55f, 0f, 1f);
            glow *= glow;

            float colorPos = (i / 14f + t * 0.08f + core * 0.22f) % 1f;
            var (r, g, b) = GetGradientColor(gl, colorPos);

            // Add a soft highlight at the bloom front.
            float bloomLift = 0.10f + shell * 0.22f;
            r = Math.Clamp((int)(r + (255 - r) * bloomLift), 0, 255);
            g = Math.Clamp((int)(g + (245 - g) * bloomLift), 0, 255);
            b = Math.Clamp((int)(b + (255 - b) * bloomLift), 0, 255);

            SetGlobalLed(i,
                Math.Clamp((int)(r * glow), 0, 255),
                Math.Clamp((int)(g * glow), 0, 255),
                Math.Clamp((int)(b * glow), 0, 255));
        }
    }

    /// <summary>
    /// Palette-driven twinkles inspired by WLED/FastLED favorites like Twinkle/Colortwinkle.
    /// Smooth attack/decay keeps the effect elegant instead of looking like random blinking.
    /// </summary>
    private readonly float[] _twinkleLevels = new float[15];
    private readonly float[] _twinkleTargets = new float[15];
    private readonly float[] _twinklePalettePos = new float[15];
    private readonly int[] _twinkleCooldowns = new int[15];
    private void GlobalColorTwinkle(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);

        for (int i = 0; i < 15; i++)
        {
            if (_twinkleCooldowns[i] > 0)
                _twinkleCooldowns[i]--;

            if (_twinkleCooldowns[i] <= 0 && _rng.NextDouble() < (0.05 + speed / 100.0 * 0.12))
            {
                _twinkleTargets[i] = 0.55f + (float)_rng.NextDouble() * 0.45f;
                _twinklePalettePos[i] = ((float)_rng.NextDouble() + i / 14f * 0.18f) % 1f;
                _twinkleCooldowns[i] = 4 + _rng.Next(Math.Max(3, 18 - speed / 6));
            }

            // Fast attack, slower decay.
            float target = _twinkleTargets[i];
            _twinkleLevels[i] += (_twinkleLevels[i] < target ? 0.24f : -0.08f);
            _twinkleLevels[i] = Math.Clamp(_twinkleLevels[i], 0f, 1f);
            _twinkleTargets[i] = Math.Max(0f, _twinkleTargets[i] - 0.06f);

            float baseGlow = 0.06f + 0.05f * MathF.Sin(_animTick * 0.01f + i * 0.7f);
            var (r, g, b) = GetGradientColor(gl, (_twinklePalettePos[i] + _animTick * 0.0025f) % 1f);

            float alpha = Math.Clamp(baseGlow + _twinkleLevels[i], 0f, 1f);
            int rr = Math.Clamp((int)(r * alpha), 0, 255);
            int gg = Math.Clamp((int)(g * alpha), 0, 255);
            int bb = Math.Clamp((int)(b * alpha), 0, 255);

            if (_twinkleLevels[i] > 0.55f)
            {
                rr = Math.Clamp((int)(rr + (255 - rr) * 0.25f), 0, 255);
                gg = Math.Clamp((int)(gg + (250 - gg) * 0.18f), 0, 255);
                bb = Math.Clamp((int)(bb + (255 - bb) * 0.25f), 0, 255);
            }

            SetGlobalLed(i, rr, gg, bb);
        }
    }

    private void GlobalAuroraVeil(GlobalLightConfig gl)
    {
        GlobalAurora(gl);
        AddPaletteVeil(gl, 0.24f, 0.018f);
    }

    private void GlobalSolarStorm(GlobalLightConfig gl)
    {
        GlobalAurora(gl);
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.018f + speed / 100f * 0.09f);
        float arc = (t * 1.7f) % 15f;
        for (int i = 0; i < 15; i++)
        {
            float d = MathF.Abs(i - arc);
            d = MathF.Min(d, 15f - d);
            float pulse = MathF.Exp(-d * d * 0.75f);
            var (sr, sg, sb) = GetGradientColor(gl, (i / 14f + 0.18f) % 1f);
            AddToGlobalLed(i, (int)(sr * pulse * 0.75f), (int)(sg * pulse * 0.50f), (int)(sb * pulse * 0.25f));
        }
    }

    private void GlobalStarlightCanopy(GlobalLightConfig gl)
    {
        GlobalNebulaDrift(gl);
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float chance = 0.03f + speed / 100f * 0.07f;
        for (int i = 0; i < 15; i++)
        {
            float seed = Frac(MathF.Sin((i * 0.73f + _animTick * 0.006f) * 151.7f) * 43758.545f);
            float star = seed > 1f - chance ? MathF.Pow((seed - (1f - chance)) / chance, 2f) : 0f;
            if (star > 0f)
                AddToGlobalLed(i, (int)(255 * star), (int)(245 * star), (int)(255 * star));
        }
    }

    private void GlobalPlasmaBloom(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.012f + speed / 100f * 0.055f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float plasma = Smooth((Wave(x * 2.8f + t) + Wave(x * 7.5f - t * 0.63f) + Wave(x * 11.0f + t * 0.37f)) / 3f);
            var (r, g, b) = GetGradientColor(gl, (plasma + t * 0.08f) % 1f);
            float bloom = 0.28f + MathF.Pow(plasma, 1.7f) * 0.92f;
            SetGlobalLed(i, (int)(r * bloom), (int)(g * bloom), (int)(b * bloom));
        }
    }

    private void GlobalRippleRoom(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.018f + speed / 100f * 0.08f);
        float center = 7f + MathF.Sin(t * 0.39f) * 2.5f;
        float radius = (t * 5.2f) % 11f;
        for (int i = 0; i < 15; i++)
        {
            float d = MathF.Abs(i - center);
            float ring = MathF.Exp(-MathF.Pow(d - radius, 2f) * 0.7f);
            float baseWave = 0.12f + 0.18f * Wave(i / 14f * 2f - t * 0.3f);
            var (r, g, b) = GetGradientColor(gl, (i / 14f + ring * 0.25f) % 1f);
            float a = Math.Clamp(baseWave + ring, 0f, 1f);
            SetGlobalLed(i, (int)(r * a), (int)(g * a), (int)(b * a));
        }
    }

    private void GlobalPrismDrift(GlobalLightConfig gl)
    {
        GlobalPrism(gl);
        AddPaletteVeil(gl, 0.18f, 0.011f);
    }

    private void GlobalNebulaRain(GlobalLightConfig gl)
    {
        GlobalNebulaDrift(gl);
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.015f + speed / 100f * 0.06f);
        for (int i = 0; i < 15; i++)
        {
            float streak = MathF.Pow(Smooth(Wave(i * 0.61f + t * 2.4f)), 7f);
            var (r, g, b) = GetGradientColor(gl, (i / 14f + 0.4f) % 1f);
            AddToGlobalLed(i, (int)(r * streak * 0.8f), (int)(g * streak * 0.8f), (int)(b * streak));
        }
    }

    private void GlobalReactiveAurora(GlobalLightConfig gl)
    {
        GlobalAurora(gl);
        var bands = _getAudioBands?.Invoke();
        float energy = bands != null && bands.Length >= 5
            ? Math.Clamp(bands[0] * 0.45f + bands[1] * 0.25f + bands[2] * 0.18f + bands[3] * 0.08f + bands[4] * 0.04f, 0f, 1f)
            : 0.22f + 0.18f * Wave(_animTick * 0.015f);

        for (int i = 0; i < 15; i++)
        {
            float shimmer = energy * MathF.Pow(Wave(i * 0.23f + _animTick * 0.07f), 2f);
            var (r, g, b) = GetGradientColor(gl, (i / 14f + 0.22f) % 1f);
            AddToGlobalLed(i, (int)(r * shimmer), (int)(g * shimmer), (int)(b * shimmer));
        }
    }

    private void GlobalLiquidGlass(GlobalLightConfig gl)
    {
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.008f + speed / 100f * 0.035f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float caustic = MathF.Pow(Smooth(Wave(x * 4.5f - t) * 0.65f + Wave(x * 13f + t * 0.8f) * 0.35f), 2.2f);
            var (r, g, b) = GetGradientColor(gl, (x + caustic * 0.18f + t * 0.04f) % 1f);
            float glass = 0.30f + caustic * 0.85f;
            SetGlobalLed(i,
                Math.Clamp((int)((r + (255 - r) * caustic * 0.18f) * glass), 0, 255),
                Math.Clamp((int)((g + (255 - g) * caustic * 0.18f) * glass), 0, 255),
                Math.Clamp((int)((b + (255 - b) * caustic * 0.22f) * glass), 0, 255));
        }
    }

    private void GlobalChromaLayerStack(GlobalLightConfig gl)
    {
        GlobalColorWave(gl);
        int speed = Math.Clamp(gl.EffectSpeed, 1, 100);
        float t = _animTick * (0.02f + speed / 100f * 0.07f);
        float scanner = PingPong(t) * 14f;
        for (int i = 0; i < 15; i++)
        {
            float d = MathF.Abs(i - scanner);
            float ripple = MathF.Exp(-d * d * 0.9f);
            var (r, g, b) = HsvToRgb((i / 15f * 360f + _animTick * 2f) % 360f, 0.85f, ripple * 0.85f);
            AddToGlobalLed(i, r, g, b);
        }
    }

    private void AddPaletteVeil(GlobalLightConfig gl, float amount, float speed)
    {
        for (int i = 0; i < 15; i++)
        {
            var (r, g, b) = GetGradientColor(gl, (i / 14f + _animTick * speed) % 1f);
            AddToGlobalLed(i, (int)(r * amount), (int)(g * amount), (int)(b * amount));
        }
    }

    private void AddToGlobalLed(int globalIdx, int r, int g, int b)
    {
        int offset = globalIdx * 3;
        SetGlobalLed(globalIdx,
            Math.Clamp(_linearColors[offset] + r, 0, 255),
            Math.Clamp(_linearColors[offset + 1] + g, 0, 255),
            Math.Clamp(_linearColors[offset + 2] + b, 0, 255));
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

    private static float Frac(float value)
    {
        return value - MathF.Floor(value);
    }

    private static float Wave(float phase)
    {
        return MathF.Sin(phase * MathF.Tau) * 0.5f + 0.5f;
    }

    private static float Smooth(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private static float PingPong(float value)
    {
        float t = Frac(value);
        return 1f - MathF.Abs(t * 2f - 1f);
    }

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
    /// Get interpolated color at position 0.0-1.0 from the active global palette.
    /// Uses the resolved _globalPalette (set each tick in UpdateEffects).
    /// Falls back to legacy color1/color2 blend if no palette resolved.
    /// </summary>
    private (int r, int g, int b) GetGradientColor(GlobalLightConfig cfg, float position)
    {
        if (_globalPalette != null)
            return _globalPalette.Sample(position);

        // Legacy fallback: simple color1→color2 lerp
        float t = Math.Clamp(position, 0f, 1f);
        return (
            Math.Clamp((int)(cfg.R + (cfg.R2 - cfg.R) * t), 0, 255),
            Math.Clamp((int)(cfg.G + (cfg.G2 - cfg.G) * t), 0, 255),
            Math.Clamp((int)(cfg.B + (cfg.B2 - cfg.B) * t), 0, 255)
        );
    }

    /// <summary>
    /// Get interpolated color from a per-knob palette at position 0.0-1.0.
    /// Falls back to color1/color2 blend if no palette set.
    /// </summary>
    private (int r, int g, int b) GetKnobPaletteColor(LightConfig light, float position)
    {
        if (!string.IsNullOrEmpty(light.PaletteName))
        {
            var palette = ResolvePalette(light.PaletteName, light.R, light.G, light.B, light.R2, light.G2, light.B2);
            return palette.Sample(position);
        }
        // Legacy fallback
        float t = Math.Clamp(position, 0f, 1f);
        return (
            Math.Clamp((int)(light.R + (light.R2 - light.R) * t), 0, 255),
            Math.Clamp((int)(light.G + (light.G2 - light.G) * t), 0, 255),
            Math.Clamp((int)(light.B + (light.B2 - light.B) * t), 0, 255)
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
