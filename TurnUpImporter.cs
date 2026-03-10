using System.IO;
using Newtonsoft.Json;

namespace WolfMixer;

// ── Turn Up config DTOs ────────────────────────────────────────────

public class TurnUpConfig
{
    [JsonProperty("profiles")]
    public List<TurnUpProfile> Profiles { get; set; } = new();

    [JsonProperty("settings")]
    public TurnUpSettings Settings { get; set; } = new();
}

public class TurnUpProfile
{
    [JsonProperty("profileName")]
    public string ProfileName { get; set; } = "";

    [JsonProperty("knobs")]
    public List<TurnUpKnob> Knobs { get; set; } = new();

    [JsonProperty("buttons")]
    public List<TurnUpButton> Buttons { get; set; } = new();

    [JsonProperty("lights")]
    public List<TurnUpLight> Lights { get; set; } = new();
}

public class TurnUpKnob
{
    [JsonProperty("effectType")]
    public string EffectType { get; set; } = "None";

    [JsonProperty("displayName")]
    public string? DisplayName { get; set; }

    [JsonProperty("programs")]
    public List<TurnUpProgram>? Programs { get; set; }

    [JsonProperty("audioDevice")]
    public TurnUpAudioDevice? AudioDevice { get; set; }
}

public class TurnUpProgram
{
    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("installLocation")]
    public string InstallLocation { get; set; } = "";
}

public class TurnUpAudioDevice
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("deviceId")]
    public string DeviceId { get; set; } = "";
}

public class TurnUpButton
{
    [JsonProperty("effectType")]
    public string EffectType { get; set; } = "None";

    [JsonProperty("powerOption")]
    public string? PowerOption { get; set; }

    [JsonProperty("filePath")]
    public string? FilePath { get; set; }

    [JsonProperty("profile")]
    public string? Profile { get; set; }

    [JsonProperty("macro")]
    public List<TurnUpMacroKey>? Macro { get; set; }
}

public class TurnUpMacroKey
{
    [JsonProperty("keyCode")]
    public int KeyCode { get; set; }

    [JsonProperty("printableName")]
    public string PrintableName { get; set; } = "";

    [JsonProperty("down")]
    public bool Down { get; set; }

    [JsonProperty("virtualKeyCode")]
    public int? VirtualKeyCode { get; set; }
}

public class TurnUpLight
{
    [JsonProperty("effectType")]
    public string EffectType { get; set; } = "None";

    [JsonProperty("colors")]
    public List<TurnUpColor>? Colors { get; set; }

    [JsonProperty("invertDirection")]
    public bool InvertDirection { get; set; }
}

public class TurnUpColor
{
    [JsonProperty("r")]
    public int R { get; set; }

    [JsonProperty("g")]
    public int G { get; set; }

    [JsonProperty("b")]
    public int B { get; set; }
}

public class TurnUpSettings
{
    [JsonProperty("responseCurve")]
    public int ResponseCurve { get; set; }

    [JsonProperty("brightness")]
    public double Brightness { get; set; } = 100;
}

// ── Importer Logic ─────────────────────────────────────────────────

public static class TurnUpImporter
{
    /// <summary>
    /// Searches for the Turn Up config file in %APPDATA%\Turn Up\.
    /// </summary>
    public static string? FindConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, "Turn Up", "TurnUpConfig.json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Parses a Turn Up TurnUpConfig.json file.
    /// </summary>
    public static TurnUpConfig? LoadConfig(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<TurnUpConfig>(json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to parse Turn Up config: {ex.Message}");
            return null;
        }
    }

    // ── Knob Mapping ───────────────────────────────────────────────

    public static KnobConfig MapKnob(TurnUpKnob knob, int idx, ResponseCurve globalCurve)
    {
        var config = new KnobConfig { Idx = idx, Curve = globalCurve };

        switch (knob.EffectType)
        {
            case "MasterVolume":
                config.Target = "master";
                config.Label = knob.DisplayName ?? "Master";
                break;
            case "MicrophoneVolume":
                config.Target = "mic";
                config.Label = knob.DisplayName ?? "Mic";
                break;
            case "ActiveWindow":
                config.Target = "active_window";
                config.Label = knob.DisplayName ?? "Active";
                break;
            case "MonitorBrightness":
                config.Target = "monitor";
                config.Label = knob.DisplayName ?? "Monitor";
                break;
            case "Program":
                if (knob.Programs != null && knob.Programs.Count > 1)
                {
                    config.Target = "apps";
                    config.Apps = knob.Programs.Select(ExtractProcessName).ToList();
                    config.Label = knob.DisplayName ?? string.Join(", ", config.Apps);
                }
                else if (knob.Programs != null && knob.Programs.Count == 1)
                {
                    var proc = ExtractProcessName(knob.Programs[0]);
                    config.Target = proc;
                    config.Label = knob.DisplayName ?? knob.Programs[0].DisplayName;
                }
                else
                {
                    config.Target = "none";
                }
                break;
            case "OutputVolume":
                config.Target = "output_device";
                config.DeviceId = knob.AudioDevice?.DeviceId ?? "";
                config.Label = knob.DisplayName ?? knob.AudioDevice?.Name ?? "Output";
                break;
            default: // None, LightBrightness, unknown
                config.Target = "none";
                config.Label = "";
                break;
        }

        return config;
    }

    private static string ExtractProcessName(TurnUpProgram program)
    {
        if (!string.IsNullOrEmpty(program.InstallLocation))
        {
            var fileName = Path.GetFileNameWithoutExtension(program.InstallLocation);
            if (!string.IsNullOrEmpty(fileName))
                return fileName.ToLowerInvariant();
        }
        return program.DisplayName.ToLowerInvariant();
    }

    // ── Button Mapping ─────────────────────────────────────────────

    public static ButtonConfig MapButton(TurnUpButton button, int idx)
    {
        var config = new ButtonConfig { Idx = idx };

        switch (button.EffectType)
        {
            case "PlayPause":
                config.Action = "media_play_pause";
                break;
            case "SkipForward":
                config.Action = "media_next";
                break;
            case "SkipBackward":
                config.Action = "media_prev";
                break;
            case "SystemPower":
                config.Action = "system_power";
                config.PowerAction = (button.PowerOption ?? "sleep").ToLowerInvariant();
                break;
            case "SwitchProfile":
                config.Action = "switch_profile";
                config.ProfileName = button.Profile ?? "";
                break;
            case "LaunchProgram":
                config.Action = "launch_exe";
                config.Path = button.FilePath ?? "";
                break;
            case "Macro":
                config.Action = "macro";
                config.MacroKeys = ConvertMacro(button.Macro);
                break;
            default: // None, CycleBrightness, unknown
                config.Action = "none";
                break;
        }

        return config;
    }

    public static string ConvertMacro(List<TurnUpMacroKey>? macro)
    {
        if (macro == null || macro.Count == 0) return "";

        var modifiers = new HashSet<string>();
        string mainKey = "";

        foreach (var key in macro.Where(k => k.Down))
        {
            var vk = key.VirtualKeyCode ?? key.KeyCode;
            switch (vk)
            {
                case 16: modifiers.Add("shift"); break;
                case 17: modifiers.Add("ctrl"); break;
                case 18: modifiers.Add("alt"); break;
                case 91: case 92: modifiers.Add("win"); break;
                default:
                    if (string.IsNullOrEmpty(mainKey))
                        mainKey = key.PrintableName.ToLowerInvariant();
                    break;
            }
        }

        var parts = new List<string>();
        if (modifiers.Contains("ctrl")) parts.Add("ctrl");
        if (modifiers.Contains("alt")) parts.Add("alt");
        if (modifiers.Contains("shift")) parts.Add("shift");
        if (modifiers.Contains("win")) parts.Add("win");
        if (!string.IsNullOrEmpty(mainKey)) parts.Add(mainKey);

        return string.Join("+", parts);
    }

    // ── Light Mapping ──────────────────────────────────────────────

    public static LightConfig MapLight(TurnUpLight light, int idx)
    {
        var config = new LightConfig { Idx = idx };

        if (light.Colors != null && light.Colors.Count > 0)
        {
            config.R = light.Colors[0].R;
            config.G = light.Colors[0].G;
            config.B = light.Colors[0].B;

            if (light.Colors.Count > 1)
            {
                config.R2 = light.Colors[1].R;
                config.G2 = light.Colors[1].G;
                config.B2 = light.Colors[1].B;
            }
        }

        config.Effect = light.EffectType switch
        {
            "SingleColor" => LightEffect.SingleColor,
            "PositionFill" => LightEffect.PositionFill,
            "RainbowCycle" => LightEffect.RainbowCycle,
            "RainbowWave" => LightEffect.RainbowWave,
            "Blink" => LightEffect.Blink,
            "Pulse" => LightEffect.Pulse,
            _ => LightEffect.SingleColor
        };

        return config;
    }

    // ── Settings Mapping ───────────────────────────────────────────

    public static ResponseCurve MapResponseCurve(int turnUpCurve)
    {
        return turnUpCurve switch
        {
            0 => ResponseCurve.Linear,
            1 => ResponseCurve.Logarithmic,
            2 => ResponseCurve.Exponential,
            _ => ResponseCurve.Linear
        };
    }

    // ── Description Helpers (for wizard display) ───────────────────

    public static string DescribeKnob(TurnUpKnob knob)
    {
        return knob.EffectType switch
        {
            "MasterVolume" => "Master Volume",
            "MicrophoneVolume" => "Microphone",
            "ActiveWindow" => "Active Window",
            "MonitorBrightness" => "Monitor Brightness",
            "Program" when knob.Programs?.Count > 0 =>
                "App: " + string.Join(", ", knob.Programs.Select(p => p.DisplayName)),
            "OutputVolume" => "Output: " + (knob.AudioDevice?.Name ?? "Unknown"),
            "LightBrightness" => "LED Brightness",
            "None" => "None",
            _ => knob.EffectType
        };
    }

    public static string DescribeButton(TurnUpButton button)
    {
        return button.EffectType switch
        {
            "PlayPause" => "Play / Pause",
            "SkipForward" => "Next Track",
            "SkipBackward" => "Previous Track",
            "SystemPower" => $"System: {button.PowerOption ?? "Sleep"}",
            "SwitchProfile" => $"Profile: {button.Profile}",
            "LaunchProgram" => $"Launch: {Path.GetFileName(button.FilePath ?? "")}",
            "Macro" => $"Macro: {ConvertMacro(button.Macro)}",
            "CycleBrightness" => "Cycle Brightness",
            "None" => "None",
            _ => button.EffectType
        };
    }

    /// <summary>
    /// Returns true if the Turn Up effectType has no Amp Up equivalent.
    /// </summary>
    public static bool IsUnsupportedKnob(string effectType)
        => effectType is "LightBrightness" or "CycleBrightness";

    public static bool IsUnsupportedButton(string effectType)
        => effectType is "CycleBrightness";
}
