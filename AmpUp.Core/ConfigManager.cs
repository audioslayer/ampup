using System.IO;
using AmpUp.Core.Models;
using Newtonsoft.Json;

namespace AmpUp.Core;

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
        var safe = string.Concat(profileName
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        if (string.IsNullOrEmpty(safe)) safe = "unnamed";
        return Path.Combine(ConfigDir, $"profile_{safe}.json");
    }

    public static AppConfig Load()
    {
        var config = LoadJsonFile<AppConfig>(ConfigPath, "config", cfg => { if (cfg != null) EnsureDefaults(cfg); });
        if (config != null) return config;
        var defaults = new AppConfig();
        EnsureDefaults(defaults);
        return defaults;
    }

    /// <summary>
    /// Deserialize a JSON file, falling back to the .bak file if the primary is missing or corrupt.
    /// Returns null if both fail.
    /// </summary>
    private static T? LoadJsonFile<T>(string path, string label, Action<T?> postLoad) where T : class
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var json = File.ReadAllText(candidate);
                var result = JsonConvert.DeserializeObject<T>(json);
                postLoad(result);
                if (candidate != path)
                    Logger.Log($"Loaded {label} from backup: {candidate}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load {label} from {candidate}: {ex.Message}");
            }
        }
        return null;
    }

    private static readonly string[] DefaultKnobLabels = { "", "", "", "", "" };
    private static readonly string[] DefaultKnobTargets = { "none", "none", "none", "none", "none" };
    private static readonly (int idx, string label, string action)[] DefaultN3Buttons =
    {
        (100, "N3 Key 1", "none"),
        (101, "N3 Key 2", "none"),
        (102, "N3 Key 3", "none"),
        (103, "N3 Key 4", "none"),
        (104, "N3 Key 5", "none"),
        (105, "N3 Key 6", "none"),
        (106, "N3 Side 1", "media_prev"),
        (107, "N3 Side 2", "media_play_pause"),
        (108, "N3 Side 3", "media_next"),
        (109, "N3 Press 1", "mute_master"),
        (110, "N3 Press 2", "mute_active_window"),
        (111, "N3 Press 3", "mute_mic"),
    };
    private static readonly (int idx, string title, string subtitle, string background, string accent)[] DefaultN3DisplayKeys =
    {
        (0, "Key 1", "", "#1C1C1C", "#00E676"),
        (1, "Key 2", "", "#1C1C1C", "#00B4D8"),
        (2, "Key 3", "", "#1C1C1C", "#448AFF"),
        (3, "Key 4", "", "#1C1C1C", "#FF6E40"),
        (4, "Key 5", "", "#1C1C1C", "#FFD740"),
        (5, "Key 6", "", "#1C1C1C", "#FF4081"),
    };
    private static readonly string[] DefaultStreamControllerKnobLabels = { "Encoder 1", "Encoder 2", "Encoder 3" };
    private static readonly string[] DefaultStreamControllerKnobTargets = { "none", "none", "none" };

    private static void EnsureDefaults(AppConfig config)
    {
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
        for (int i = 0; i < 5; i++)
        {
            if (!config.Buttons.Any(b => b.Idx == i))
                config.Buttons.Add(new ButtonConfig { Idx = i });
        }
        for (int i = 0; i < 3; i++)
        {
            if (!config.N3.Knobs.Any(k => k.Idx == i))
            {
                var migrated = config.Knobs.FirstOrDefault(k => k.Idx == i);
                if (migrated != null && config.N3.Knobs.Count == 0)
                {
                    var json = JsonConvert.SerializeObject(migrated);
                    var copy = JsonConvert.DeserializeObject<KnobConfig>(json) ?? new KnobConfig();
                    copy.Idx = i;
                    config.N3.Knobs.Add(copy);
                }
                else
                {
                    config.N3.Knobs.Add(new KnobConfig
                    {
                        Idx = i,
                        Label = DefaultStreamControllerKnobLabels[i],
                        Target = DefaultStreamControllerKnobTargets[i]
                    });
                }
            }
        }
        foreach (var (idx, label, action) in DefaultN3Buttons)
        {
            if (!config.N3.Buttons.Any(b => b.Idx == idx))
            {
                config.N3.Buttons.Add(new ButtonConfig
                {
                    Idx = idx,
                    Label = label,
                    Action = action
                });
            }
        }
        foreach (var (idx, title, subtitle, background, accent) in DefaultN3DisplayKeys)
        {
            if (!config.N3.DisplayKeys.Any(k => k.Idx == idx))
            {
                config.N3.DisplayKeys.Add(new StreamControllerDisplayKeyConfig
                {
                    Idx = idx,
                    Title = title,
                    Subtitle = subtitle,
                    BackgroundColor = background,
                    AccentColor = accent
                });
            }
        }
        for (int i = 0; i < 5; i++)
        {
            if (!config.Lights.Any(l => l.Idx == i))
                config.Lights.Add(new LightConfig { Idx = i, R = 0, G = 150, B = 255 });
        }
        config.Profiles = config.Profiles.Distinct().ToList();
        if (config.Profiles.Count == 0)
            config.Profiles.Add("Default");
        if (string.IsNullOrEmpty(config.ActiveProfile))
            config.ActiveProfile = "Default";
        foreach (var p in config.Profiles)
        {
            if (!config.ProfileIcons.ContainsKey(p))
                config.ProfileIcons[p] = new ProfileIconConfig();
        }

        // Migrate legacy single QuickWheel → QuickWheels list
        if (config.Osd.QuickWheel != null && config.Osd.QuickWheels.Count == 0)
        {
            if (config.Osd.QuickWheel.Enabled)
                config.Osd.QuickWheels.Add(config.Osd.QuickWheel);
            config.Osd.QuickWheel = null;
        }

        NormalizeDeviceSurfaceSelections(config);
    }

    private static void NormalizeDeviceSurfaceSelections(AppConfig config)
    {
        switch (config.HardwareMode)
        {
            case HardwareMode.TurnUpOnly:
                config.TabSelection.Mixer = DeviceSurface.TurnUp;
                config.TabSelection.Buttons = DeviceSurface.TurnUp;
                config.TabSelection.Lights = DeviceSurface.TurnUp;
                break;
            case HardwareMode.StreamControllerOnly:
                config.TabSelection.Mixer = DeviceSurface.StreamController;
                config.TabSelection.Buttons = DeviceSurface.StreamController;
                config.TabSelection.Lights = DeviceSurface.StreamController;
                break;
            case HardwareMode.DualMode:
            default:
                break;
        }
    }

    /// <summary>
    /// Atomic write: write to .tmp, then File.Replace to swap in the new file while keeping a .bak.
    /// Falls back to File.Move if File.Replace fails (e.g. cross-volume).
    /// </summary>
    private static void AtomicWrite(string destPath, string json)
    {
        var tmpPath = destPath + ".tmp";
        var bakPath = destPath + ".bak";
        File.WriteAllText(tmpPath, json);
        try
        {
            File.Replace(tmpPath, destPath, bakPath);
        }
        catch
        {
            // Cross-volume or other edge case — fall back to overwrite move
            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tmpPath, destPath);
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            lock (_saveLock)
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                AtomicWrite(ConfigPath, json);
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
                AtomicWrite(ProfilePath(profileName), json);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save profile {profileName}: {ex.Message}");
        }
    }

    public static AppConfig? LoadProfile(string profileName)
    {
        var path = ProfilePath(profileName);
        return LoadJsonFile<AppConfig>(path, $"profile {profileName}", cfg => { if (cfg != null) EnsureDefaults(cfg); });
    }
}
