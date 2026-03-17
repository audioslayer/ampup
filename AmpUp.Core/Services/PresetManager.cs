using System.IO;
using AmpUp.Core.Models;
using Newtonsoft.Json;

namespace AmpUp.Core.Services;

public static class PresetManager
{
    private static readonly string PresetsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AmpUp", "presets");

    public static List<LedPreset> GetBuiltInPresets()
    {
        return new List<LedPreset>
        {
            Cyberpunk(),
            Stealth(),
            Party(),
            DeepBass(),
            Spectrum(),
            OceanPreset(),
            Lava(),
            Arctic(),
            Sunset(),
            Matrix(),
            Campfire(),
            Vaporwave(),
        };
    }

    public static List<LedPreset> LoadCustomPresets()
    {
        var presets = new List<LedPreset>();
        try
        {
            if (!Directory.Exists(PresetsDir)) return presets;
            foreach (var file in Directory.GetFiles(PresetsDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var preset = JsonConvert.DeserializeObject<LedPreset>(json);
                    if (preset != null)
                    {
                        preset.IsBuiltIn = false;
                        presets.Add(preset);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to load preset {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to scan presets dir: {ex.Message}");
        }
        return presets;
    }

    public static void SaveCustomPreset(LedPreset preset)
    {
        try
        {
            Directory.CreateDirectory(PresetsDir);
            var safeName = string.Concat(preset.Name
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
            if (string.IsNullOrEmpty(safeName)) safeName = "unnamed";
            var path = Path.Combine(PresetsDir, $"{safeName}.json");
            preset.IsBuiltIn = false;
            var json = JsonConvert.SerializeObject(preset, Formatting.Indented);
            File.WriteAllText(path, json);
            Logger.Log($"Preset saved: {preset.Name} → {path}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save preset {preset.Name}: {ex.Message}");
        }
    }

    public static void DeleteCustomPreset(string presetName)
    {
        try
        {
            var safeName = string.Concat(presetName
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
            var path = Path.Combine(PresetsDir, $"{safeName}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                Logger.Log($"Preset deleted: {presetName}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to delete preset {presetName}: {ex.Message}");
        }
    }

    public static void ApplyPreset(LedPreset preset, AppConfig config)
    {
        // Apply per-knob lights
        for (int i = 0; i < Math.Min(preset.Lights.Count, 5); i++)
        {
            var src = preset.Lights[i];
            if (i < config.Lights.Count)
            {
                var dst = config.Lights[i];
                dst.R = src.R;
                dst.G = src.G;
                dst.B = src.B;
                dst.R2 = src.R2;
                dst.G2 = src.G2;
                dst.B2 = src.B2;
                dst.Effect = src.Effect;
                dst.EffectSpeed = src.EffectSpeed;
                dst.ReactiveMode = src.ReactiveMode;
            }
        }

        // Apply global light if preset has one
        if (preset.GlobalLight != null)
        {
            config.GlobalLight.Enabled = true;
            config.GlobalLight.Effect = preset.GlobalLight.Effect;
            config.GlobalLight.R = preset.GlobalLight.R;
            config.GlobalLight.G = preset.GlobalLight.G;
            config.GlobalLight.B = preset.GlobalLight.B;
            config.GlobalLight.R2 = preset.GlobalLight.R2;
            config.GlobalLight.G2 = preset.GlobalLight.G2;
            config.GlobalLight.B2 = preset.GlobalLight.B2;
            config.GlobalLight.EffectSpeed = preset.GlobalLight.EffectSpeed;
            config.GlobalLight.ReactiveMode = preset.GlobalLight.ReactiveMode;
        }
        else
        {
            config.GlobalLight.Enabled = false;
        }

        Logger.Log($"Preset applied: {preset.Name}");
    }

    public static LedPreset CreateFromConfig(AppConfig config, string name, string category)
    {
        var preset = new LedPreset
        {
            Name = name,
            Description = "Custom preset",
            Category = category,
            IsBuiltIn = false,
            Lights = new List<LightConfig>(),
        };

        foreach (var light in config.Lights)
        {
            preset.Lights.Add(new LightConfig
            {
                Idx = light.Idx,
                R = light.R, G = light.G, B = light.B,
                R2 = light.R2, G2 = light.G2, B2 = light.B2,
                Effect = light.Effect,
                EffectSpeed = light.EffectSpeed,
                ReactiveMode = light.ReactiveMode,
            });
        }

        if (config.GlobalLight.Enabled)
        {
            preset.GlobalLight = new LightConfig
            {
                R = config.GlobalLight.R, G = config.GlobalLight.G, B = config.GlobalLight.B,
                R2 = config.GlobalLight.R2, G2 = config.GlobalLight.G2, B2 = config.GlobalLight.B2,
                Effect = config.GlobalLight.Effect,
                EffectSpeed = config.GlobalLight.EffectSpeed,
                ReactiveMode = config.GlobalLight.ReactiveMode,
            };
        }

        return preset;
    }

    // ──────── Built-in presets ────────

    private static LedPreset Cyberpunk() => new()
    {
        Name = "Cyberpunk",
        Description = "Neon pink/cyan pulse on alternating knobs",
        Category = "Gaming",
        IsBuiltIn = true,
        GlobalLight = null,
        Lights = new List<LightConfig>
        {
            Light(0, 0xFF, 0x00, 0x80, 0x00, 0xFF, 0xFF, LightEffect.Pulse, 60),
            Light(1, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x80, LightEffect.Pulse, 60),
            Light(2, 0xFF, 0x00, 0x80, 0x00, 0xFF, 0xFF, LightEffect.Pulse, 60),
            Light(3, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x80, LightEffect.Pulse, 60),
            Light(4, 0xFF, 0x00, 0x80, 0x00, 0xFF, 0xFF, LightEffect.Pulse, 60),
        },
    };

    private static LedPreset Stealth() => new()
    {
        Name = "Stealth",
        Description = "Dim white single color, minimal distraction",
        Category = "Work",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 40, G = 40, B = 40,
            Effect = LightEffect.SingleColor,
            EffectSpeed = 50,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Party() => new()
    {
        Name = "Party",
        Description = "Rainbow wave at high speed",
        Category = "Party",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            Effect = LightEffect.RainbowWave,
            EffectSpeed = 90,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset DeepBass() => new()
    {
        Name = "Deep Bass",
        Description = "Audio-reactive red/purple BeatPulse",
        Category = "Music",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 0xFF, G = 0x00, B = 0x20,
            R2 = 0x80, G2 = 0x00, B2 = 0xFF,
            Effect = LightEffect.AudioReactive,
            EffectSpeed = 70,
            ReactiveMode = ReactiveMode.BeatPulse,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Spectrum() => new()
    {
        Name = "Spectrum",
        Description = "Audio-reactive SpectrumBands, rainbow per knob",
        Category = "Music",
        IsBuiltIn = true,
        Lights = new List<LightConfig>
        {
            Light(0, 0xFF, 0x00, 0x00, 0xFF, 0x80, 0x00, LightEffect.AudioReactive, 50, ReactiveMode.SpectrumBands),
            Light(1, 0xFF, 0x80, 0x00, 0xFF, 0xFF, 0x00, LightEffect.AudioReactive, 50, ReactiveMode.SpectrumBands),
            Light(2, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, LightEffect.AudioReactive, 50, ReactiveMode.SpectrumBands),
            Light(3, 0x00, 0x80, 0xFF, 0x00, 0x00, 0xFF, LightEffect.AudioReactive, 50, ReactiveMode.SpectrumBands),
            Light(4, 0x80, 0x00, 0xFF, 0xFF, 0x00, 0xFF, LightEffect.AudioReactive, 50, ReactiveMode.SpectrumBands),
        },
    };

    private static LedPreset OceanPreset() => new()
    {
        Name = "Ocean",
        Description = "Blue/teal breathing, slow and calm",
        Category = "Ambient",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 0x00, G = 0x77, B = 0xB6,
            Effect = LightEffect.Breathing,
            EffectSpeed = 25,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Lava() => new()
    {
        Name = "Lava",
        Description = "Fire effect, red/orange flickering",
        Category = "Ambient",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 0xFF, G = 0x30, B = 0x00,
            R2 = 0xFF, G2 = 0x8C, B2 = 0x00,
            Effect = LightEffect.Fire,
            EffectSpeed = 60,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Arctic() => new()
    {
        Name = "Arctic",
        Description = "Ice blue/white sparkle",
        Category = "Ambient",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 0x80, G = 0xDE, B = 0xEA,
            Effect = LightEffect.Sparkle,
            EffectSpeed = 40,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Sunset() => new()
    {
        Name = "Sunset",
        Description = "Warm orange/pink gradient fill",
        Category = "Ambient",
        IsBuiltIn = true,
        Lights = new List<LightConfig>
        {
            Light(0, 0xFF, 0x17, 0x44, 0xFF, 0x6B, 0x35, LightEffect.GradientFill, 50),
            Light(1, 0xFF, 0x6B, 0x35, 0xFF, 0x8C, 0x00, LightEffect.GradientFill, 50),
            Light(2, 0xFF, 0x8C, 0x00, 0xFF, 0xD7, 0x00, LightEffect.GradientFill, 50),
            Light(3, 0xFF, 0xD7, 0x00, 0xFF, 0x8C, 0x00, LightEffect.GradientFill, 50),
            Light(4, 0xFF, 0x8C, 0x00, 0xFF, 0x17, 0x44, LightEffect.GradientFill, 50),
        },
    };

    private static LedPreset Matrix() => new()
    {
        Name = "Matrix",
        Description = "Green comet trails",
        Category = "Gaming",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 0x00, G = 0xFF, B = 0x00,
            Effect = LightEffect.Comet,
            EffectSpeed = 65,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Campfire() => new()
    {
        Name = "Campfire",
        Description = "Warm candle flicker with embers",
        Category = "Ambient",
        IsBuiltIn = true,
        GlobalLight = new LightConfig
        {
            R = 0xFF, G = 0x80, B = 0x00,
            R2 = 0xFF, G2 = 0x20, B2 = 0x00,
            Effect = LightEffect.Candle,
            EffectSpeed = 45,
        },
        Lights = DefaultLights(),
    };

    private static LedPreset Vaporwave() => new()
    {
        Name = "Vaporwave",
        Description = "Pink/cyan retro aesthetic wave",
        Category = "Party",
        IsBuiltIn = true,
        Lights = new List<LightConfig>
        {
            Light(0, 0xFF, 0x71, 0xCE, 0x01, 0xCD, 0xFE, LightEffect.Pulse, 35),
            Light(1, 0x01, 0xCD, 0xFE, 0xB9, 0x67, 0xFF, LightEffect.Pulse, 40),
            Light(2, 0xB9, 0x67, 0xFF, 0x05, 0xFC, 0xC1, LightEffect.Pulse, 35),
            Light(3, 0x05, 0xFC, 0xC1, 0xFF, 0x00, 0xA0, LightEffect.Pulse, 40),
            Light(4, 0xFF, 0x00, 0xA0, 0xFF, 0x71, 0xCE, LightEffect.Pulse, 35),
        },
    };

    // ──────── Helpers ────────

    private static LightConfig Light(int idx, int r, int g, int b, int r2, int g2, int b2,
        LightEffect effect, int speed, ReactiveMode reactive = ReactiveMode.SpectrumBands) => new()
    {
        Idx = idx,
        R = r, G = g, B = b,
        R2 = r2, G2 = g2, B2 = b2,
        Effect = effect,
        EffectSpeed = speed,
        ReactiveMode = reactive,
    };

    private static List<LightConfig> DefaultLights() => new()
    {
        new LightConfig { Idx = 0 },
        new LightConfig { Idx = 1 },
        new LightConfig { Idx = 2 },
        new LightConfig { Idx = 3 },
        new LightConfig { Idx = 4 },
    };
}
