using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AmpUp;

public class AppConfig
{
    public SerialConfig Serial { get; set; } = new();
    public List<KnobConfig> Knobs { get; set; } = new();
    public List<ButtonConfig> Buttons { get; set; } = new();
    public List<LightConfig> Lights { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public int LedBrightness { get; set; } = 100; // 0-100 global brightness
    public string AccentColor { get; set; } = "#00E676"; // UI accent color hex
    public string ActiveProfile { get; set; } = "Default";
    public List<string> Profiles { get; set; } = new() { "Default" };
    public Dictionary<string, string> ProfileEmojis { get; set; } = new(); // deprecated, kept for migration
    public Dictionary<string, ProfileIconConfig> ProfileIcons { get; set; } = new()
    {
        { "Default", new ProfileIconConfig() }
    };

    // OSD Overlay
    public OsdConfig Osd { get; set; } = new();

    // Global lighting override
    public GlobalLightConfig GlobalLight { get; set; } = new();

    // Profile switch transition animation
    [JsonConverter(typeof(StringEnumConverter))]
    public ProfileTransition ProfileTransition { get; set; } = ProfileTransition.Cascade;

    // Integrations
    public HomeAssistantConfig HomeAssistant { get; set; } = new();

    // Ambience — Govee LAN room lighting sync
    public AmbienceConfig Ambience { get; set; } = new();

    // Auto-Ducking
    public DuckingConfig Ducking { get; set; } = new();

    // Auto-Profile Switching
    public AutoSwitchConfig AutoSwitch { get; set; } = new();

    // First-run setup flag
    public bool HasCompletedSetup { get; set; } = false;
    public bool AutoSuggestLayout { get; set; } = false; // opt-in: show "apply suggested layout?" banner
    public string LastWelcomeVersion { get; set; } = "";  // tracks which version last showed the welcome
}

public class SerialConfig
{
    public string Port { get; set; } = "COM3";
    public int Baud { get; set; } = 115200;
}

public class KnobConfig
{
    public int Idx { get; set; }
    public string Label { get; set; } = "";
    public string Target { get; set; } = "none";
    public string DeviceId { get; set; } = "";       // for output_device / input_device targets
    public int MinVolume { get; set; } = 0;           // 0-100 volume range clamp
    public int MaxVolume { get; set; } = 100;         // 0-100 volume range clamp
    [JsonConverter(typeof(StringEnumConverter))]
    public ResponseCurve Curve { get; set; } = ResponseCurve.Linear;
    public List<string> Apps { get; set; } = new();   // for "apps" target — multiple process names
}

public enum ResponseCurve
{
    Linear,
    Logarithmic,
    Exponential
}

public class ButtonConfig
{
    public int Idx { get; set; }
    public string Label { get; set; } = "";
    public string Action { get; set; } = "none";
    public string Path { get; set; } = "";
    public string HoldAction { get; set; } = "none";
    public string HoldPath { get; set; } = "";
    public string DoublePressAction { get; set; } = "none";
    public string DoublePressPath { get; set; } = "";
    // TAP gesture context
    public string DeviceId { get; set; } = "";        // for select_output / select_input / cycle
    public List<string> DeviceIds { get; set; } = new(); // for cycle_output / cycle_input (subset)
    public string MacroKeys { get; set; } = "";        // for macro action e.g. "ctrl+shift+m"
    public string ProfileName { get; set; } = "";      // for switch_profile action
    public string PowerAction { get; set; } = "";      // for system_power: sleep/lock/shutdown/restart/logoff
    public int LinkedKnobIdx { get; set; } = -1;       // for mute_app_group: which knob's app group to mute

    // HOLD gesture context
    public string HoldDeviceId { get; set; } = "";
    public List<string> HoldDeviceIds { get; set; } = new();
    public string HoldMacroKeys { get; set; } = "";
    public string HoldProfileName { get; set; } = "";
    public string HoldPowerAction { get; set; } = "";
    public int HoldLinkedKnobIdx { get; set; } = -1;

    // DOUBLE PRESS gesture context
    public string DoublePressDeviceId { get; set; } = "";
    public List<string> DoublePressDeviceIds { get; set; } = new();
    public string DoublePressMacroKeys { get; set; } = "";
    public string DoublePressProfileName { get; set; } = "";
    public string DoublePressPowerAction { get; set; } = "";
    public int DoublePressLinkedKnobIdx { get; set; } = -1;
}

public class DeviceColorEntry
{
    public string DeviceId { get; set; } = "";
    public int R { get; set; } = 0;
    public int G { get; set; } = 150;
    public int B { get; set; } = 255;
}

public class LightConfig
{
    public int Idx { get; set; }
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public LightEffect Effect { get; set; } = LightEffect.SingleColor;
    public int R2 { get; set; }  // second color for blend/pulse/blink effects
    public int G2 { get; set; }
    public int B2 { get; set; }
    public int EffectSpeed { get; set; } = 50; // 1-100, used by animated effects; doubles as sensitivity for AudioReactive
    [JsonConverter(typeof(StringEnumConverter))]
    public ReactiveMode ReactiveMode { get; set; } = ReactiveMode.SpectrumBands;
    public string ProgramName { get; set; } = ""; // for ProgramMute effect
    public List<DeviceColorEntry> DeviceColors { get; set; } = new(); // for DeviceSelect effect (up to 3)

}

public class GlobalLightConfig
{
    public bool Enabled { get; set; } = false;
    [JsonConverter(typeof(StringEnumConverter))]
    public LightEffect Effect { get; set; } = LightEffect.RainbowWave;
    public int R { get; set; } = 0;
    public int G { get; set; } = 230;
    public int B { get; set; } = 118;
    public int R2 { get; set; } = 255;
    public int G2 { get; set; } = 255;
    public int B2 { get; set; } = 255;
    public int EffectSpeed { get; set; } = 50;
    [JsonConverter(typeof(StringEnumConverter))]
    public ReactiveMode ReactiveMode { get; set; } = ReactiveMode.SpectrumBands;
    public List<string> GradientColors { get; set; } = new(); // hex colors for multi-color palettes, mapped across 15 LEDs
}

public enum LightEffect
{
    SingleColor,
    ColorBlend,       // blend between color1 and color2 based on knob position
    PositionFill,     // LEDs fill left-to-right as knob increases
    Blink,            // alternate between color1 and color2
    Pulse,            // smooth pulse between color1 and color2
    RainbowWave,      // HSV rainbow across all knobs
    RainbowCycle,     // HSV rainbow rotation per knob
    MicStatus,        // color1 = unmuted, color2 = muted
    DeviceMute,       // color1 = unmuted, color2 = muted (master)
    AudioReactive,    // audio-reactive RGB via FFT frequency bands
    Breathing,        // smooth sine-wave brightness fade in/out (Apple sleep indicator style)
    Fire,             // randomized warm flickering across 3 LEDs
    Comet,            // bright pixel chases across 3 LEDs with fading tail
    Sparkle,          // random LED flashes white briefly, fades back
    GradientFill,     // static gradient from color1 to color2 across 3 LEDs
    PositionBlend,    // like PositionFill but color blends from color1 to color2 across lit LEDs
    PositionBlendMute, // PositionBlend while unmuted, solid color2 when device is muted

    // New per-knob 3-LED effects
    PingPong,         // bright dot bounces back and forth across 3 LEDs
    Stack,            // LEDs build up one by one, then reset
    Wave,             // sine wave of brightness travels across 3 LEDs
    Candle,           // smooth organic flickering (slower/calmer than Fire)
    Wheel,            // single bright dot rotates around 3 LEDs with dim trail
    RainbowWheel,     // tightly-spaced rainbow hues rotate across 3 LEDs per knob

    // Reactive/Status effects
    ProgramMute,      // color1 = not muted, color2 = muted (specific program audio session)
    AppGroupMute,     // color1 = any app in group unmuted, color2 = all apps muted (uses knob's own apps[] list)
    DeviceSelect,     // shows per-device color based on which output device is currently default

    // Global-spanning effects (use all 15 LEDs as one strip)
    Scanner,          // Cylon/KITT scanner sweeps back and forth across all 15 LEDs
    MeteorRain,       // bright comet with long tail across all 15 LEDs
    ColorWave,        // scrolling color1→color2 gradient across all 15 LEDs
    Segments,         // rotating barber-pole bands of color1/color2 across all 15 LEDs
    TheaterChase,     // every 3rd LED lit, pattern shifts — classic chase
    RainbowScanner,   // scanner sweep but head color cycles through rainbow hues
    SparkleRain,      // random LEDs flash and fade across all 15
    BreathingSync,    // sine wave of brightness travels across all 15 LEDs
    FireWall,         // continuous fire effect across all 15 LEDs as one flame

    // New global-spanning effects (all 15 LEDs as one dramatic strip)
    DualRacer,        // two dots racing opposite directions with fading tails, colors blend on overlap
    Lightning,        // random dramatic lightning strikes cascade outward from a random LED
    Fillup,           // LEDs fill left-to-right one by one, pause, drain right-to-left, repeat
    Ocean,            // overlapping sine waves simulate ocean with whitecap peaks
    Collision,        // two pulses race from opposite ends, collide at center with white flash
    DNA,              // two interleaving sine waves travel opposite directions (double helix)
    Rainfall,         // drops streak from right to left with splash on landing
    PoliceLights,     // emergency double-flash pattern alternating color1/color2 halves
}

public enum ReactiveMode
{
    BeatPulse,     // bass drives ALL knob brightness simultaneously
    SpectrumBands, // each knob = its own frequency band
    ColorShift,    // hue shifts across spectrum based on audio energy
}

public enum ProfileTransition
{
    None,
    Flash,          // 3 quick flashes across all knobs
    Cascade,        // knobs light up left-to-right then fade out
    RainbowSweep,   // fast rainbow wave across all knobs, accelerates then fades
    Ripple,         // center-out ripple with profile color + complementary accents
    ColorBurst,     // explosion from center, profile color with triadic sparks
    Wipe,           // left-to-right color wipe with trailing analogous gradient
}

public static class ConfigManager
{
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmpUp");

    private static readonly string ConfigDir = InitConfigDir();
    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");
    private static readonly object _saveLock = new();

    private static string InitConfigDir()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmpUp");
        Directory.CreateDirectory(appDataDir);
        return appDataDir;
    }

    public static string GetProfilePath(string profileName) => ProfilePath(profileName);

    private static string ProfilePath(string profileName)
    {
        // Sanitize: allow only letters, digits, hyphens — everything else becomes underscore
        var safe = string.Concat(profileName
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        if (string.IsNullOrEmpty(safe)) safe = "unnamed";
        return Path.Combine(ConfigDir, $"profile_{safe}.json");
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                EnsureDefaults(config);
                return config;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load config: {ex.Message}");
        }
        var defaults = new AppConfig();
        EnsureDefaults(defaults);
        return defaults;
    }

    private static readonly string[] DefaultKnobLabels = { "", "", "", "", "" };
    private static readonly string[] DefaultKnobTargets = { "none", "none", "none", "none", "none" };

    private static void EnsureDefaults(AppConfig config)
    {
        // Ensure 5 knobs exist
        for (int i = 0; i < 5; i++)
        {
            if (!config.Knobs.Any(k => k.Idx == i))
                config.Knobs.Add(new KnobConfig
                {
                    Idx = i,
                    Label = DefaultKnobLabels[i],
                    Target = DefaultKnobTargets[i]
                });
        }

        // Ensure 5 buttons exist
        for (int i = 0; i < 5; i++)
        {
            if (!config.Buttons.Any(b => b.Idx == i))
                config.Buttons.Add(new ButtonConfig { Idx = i });
        }

        // Ensure 5 lights exist
        for (int i = 0; i < 5; i++)
        {
            if (!config.Lights.Any(l => l.Idx == i))
                config.Lights.Add(new LightConfig { Idx = i, R = 0, G = 150, B = 255 });
        }
        // Deduplicate profiles
        config.Profiles = config.Profiles.Distinct().ToList();
        if (config.Profiles.Count == 0)
            config.Profiles.Add("Default");
        if (string.IsNullOrEmpty(config.ActiveProfile))
            config.ActiveProfile = "Default";

        // Ensure every profile has an icon entry
        foreach (var p in config.Profiles)
        {
            if (!config.ProfileIcons.ContainsKey(p))
                config.ProfileIcons[p] = new ProfileIconConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            lock (_saveLock)
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save config: {ex.Message}");
        }
    }

    public static void SaveProfile(AppConfig config, string profileName)
    {
        try
        {
            lock (_saveLock)
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ProfilePath(profileName), json);
            }
            Logger.Log($"Profile saved: {profileName}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save profile {profileName}: {ex.Message}");
        }
    }

    public static AppConfig? LoadProfile(string profileName)
    {
        try
        {
            var path = ProfilePath(profileName);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                if (config != null) EnsureDefaults(config);
                return config;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load profile {profileName}: {ex.Message}");
        }
        return null;
    }
}

public class OsdConfig
{
    public bool ShowVolume { get; set; } = true;
    public bool ShowProfileSwitch { get; set; } = true;
    public bool ShowDeviceSwitch { get; set; } = true;
    public double VolumeDuration { get; set; } = 2.0;       // seconds
    public double ProfileDuration { get; set; } = 3.5;      // seconds
    public double DeviceDuration { get; set; } = 2.5;       // seconds
    [JsonConverter(typeof(StringEnumConverter))]
    public OsdPosition Position { get; set; } = OsdPosition.BottomRight;
}

public enum OsdPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public class ProfileIconConfig
{
    public string Symbol { get; set; } = "AccountCircleOutline";
    public string Color { get; set; } = "#00E676";
}

public class HomeAssistantConfig
{
    public bool Enabled { get; set; } = false;
    public string Url { get; set; } = "http://homeassistant.local:8123";
    public string Token { get; set; } = "";
}

public class AmbienceConfig
{
    public bool GoveeEnabled { get; set; } = false;
    public bool GoveeCloudEnabled { get; set; } = false;
    public List<GoveeDeviceConfig> GoveeDevices { get; set; } = new();
    public bool LinkToLights { get; set; } = false;  // Mirror Turn Up LEDs to Govee devices
    public int BrightnessScale { get; set; } = 75;
    public bool WarmToneShift { get; set; } = false;
    public string GoveeApiKey { get; set; } = "";
    public ScreenSyncConfig ScreenSync { get; set; } = new();
}

public class GoveeDeviceConfig
{
    public string Ip { get; set; } = "";
    public string Name { get; set; } = "";         // friendly name from Cloud API, or SKU fallback
    public string Sku { get; set; } = "";           // model number e.g. "H6056"
    public string DeviceId { get; set; } = "";      // MAC address from LAN scan or Cloud API device ID
    public string SyncMode { get; set; } = "off";   // "off" | "global" | "knob0"-"knob4"
}

// ── DreamView / Screen Sync ─────────────────────────────────────────────────

public enum ZoneSide
{
    Full,       // entire screen averaged into one color
    Left,       // left half of screen
    Right,      // right half of screen
    Top,        // top half of screen
    Bottom,     // bottom half of screen
}

public class ZoneDeviceMapping
{
    public string DeviceIp { get; set; } = "";      // Govee LAN device IP
    public ZoneSide Side { get; set; } = ZoneSide.Full; // which zone to sample for this device
}

public class ScreenSyncConfig
{
    public bool Enabled { get; set; } = false;
    public int MonitorIndex { get; set; } = 0;      // 0 = primary monitor
    public int TargetFps { get; set; } = 30;        // 15 / 30 / 60
    public int ZoneCount { get; set; } = 8;         // 4, 8, or 16 zones across the screen
    public float Saturation { get; set; } = 1.2f;   // 0.5 - 2.0 saturation boost
    public int Sensitivity { get; set; } = 5;       // 1-20: minimum color delta to trigger send
    public List<ZoneDeviceMapping> DeviceMappings { get; set; } = new();
}
