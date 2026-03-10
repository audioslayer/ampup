using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WolfMixer;

public class AppConfig
{
    public SerialConfig Serial { get; set; } = new();
    public List<KnobConfig> Knobs { get; set; } = new();
    public List<ButtonConfig> Buttons { get; set; } = new();
    public List<LightConfig> Lights { get; set; } = new();
    public bool StartWithWindows { get; set; } = true;
    public int LedBrightness { get; set; } = 100; // 0-100 global brightness
    public string ActiveProfile { get; set; } = "Default";
    public List<string> Profiles { get; set; } = new() { "Default" };

    // Integrations
    public HomeAssistantConfig HomeAssistant { get; set; } = new();
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
    public string Action { get; set; } = "none";
    public string Path { get; set; } = "";
    public string HoldAction { get; set; } = "none";
    public string HoldPath { get; set; } = "";
    public string DoublePressAction { get; set; } = "none";
    public string DoublePressPath { get; set; } = "";
    public string DeviceId { get; set; } = "";        // for select_output / select_input / cycle
    public List<string> DeviceIds { get; set; } = new(); // for cycle_output / cycle_input (subset)
    public string MacroKeys { get; set; } = "";        // for macro action e.g. "ctrl+shift+m"
    public string ProfileName { get; set; } = "";      // for switch_profile action
    public string PowerAction { get; set; } = "";      // for system_power: sleep/lock/shutdown/restart/logoff
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
    public int EffectSpeed { get; set; } = 50; // 1-100, used by animated effects
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
}

public static class ConfigManager
{
    private static readonly string ConfigDir = InitConfigDir();
    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    private static string InitConfigDir()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AmpUp");
        Directory.CreateDirectory(appDataDir);

        // Migrate config from old exe-relative location if it exists
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var oldConfig = Path.Combine(exeDir, "config.json");
        var newConfig = Path.Combine(appDataDir, "config.json");
        if (File.Exists(oldConfig) && !File.Exists(newConfig))
        {
            try
            {
                File.Copy(oldConfig, newConfig);
                // Also migrate any profile files
                foreach (var profileFile in Directory.GetFiles(exeDir, "profile_*.json"))
                    File.Copy(profileFile, Path.Combine(appDataDir, Path.GetFileName(profileFile)), false);
            }
            catch { /* best effort */ }
        }

        return appDataDir;
    }

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

    private static void EnsureDefaults(AppConfig config)
    {
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
    }

    public static void Save(AppConfig config)
    {
        try
        {
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
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
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ProfilePath(profileName), json);
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

public class HomeAssistantConfig
{
    public bool Enabled { get; set; } = false;
    public string Url { get; set; } = "http://homeassistant.local:8123";
    public string Token { get; set; } = "";
}
