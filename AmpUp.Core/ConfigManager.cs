using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AmpUp.Core.Models;
using Newtonsoft.Json;

namespace AmpUp.Core;

public static class ConfigManager
{
    private const int LegacyN3SideButtonBase = 106;
    private const int LegacyN3EncoderPressBase = 109;
    private const int N3SideButtonBase = 10000;
    private const int N3EncoderPressBase = 10003;
    private static readonly string[] LegacyN3SideDefaultActions = { "media_prev", "media_play_pause", "media_next" };
    private static readonly string[] LegacyN3EncoderDefaultActions = { "mute_master", "mute_active_window", "mute_mic" };

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
        return Path.Combine(ConfigDir, $"profile_v2_{ProfileStorageKey(profileName)}.json");
    }

    private static string LegacyProfilePath(string profileName)
    {
        var safe = string.Concat(profileName
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        if (string.IsNullOrEmpty(safe)) safe = "unnamed";
        return Path.Combine(ConfigDir, $"profile_{safe}.json");
    }

    private static string ProfileStorageKey(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        var canonicalName = profileName.Trim().ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalName))).ToLowerInvariant();
    }

    public static bool IsProfileNameAvailable(IEnumerable<string> profileNames, string candidate, string? exceptName = null)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        var trimmed = candidate.Trim();
        var candidateKey = ProfileStorageKey(trimmed);
        foreach (var existing in profileNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            if (exceptName != null && string.Equals(existing, exceptName, StringComparison.Ordinal))
                continue;
            if (string.Equals(existing.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ProfileStorageKey(existing), candidateKey, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public static string GetUniqueProfileName(IEnumerable<string> profileNames, string preferredName)
    {
        var baseName = string.IsNullOrWhiteSpace(preferredName) ? "Imported" : preferredName.Trim();
        var candidate = baseName;
        int suffix = 2;
        while (!IsProfileNameAvailable(profileNames, candidate))
            candidate = $"{baseName} {suffix++}";
        return candidate;
    }

    /// <summary>
    /// Returns true when the five Turn Up knobs, buttons, and light slots still
    /// match a newly-created profile. The last raw knob positions are ignored
    /// because the hardware can populate them before the user assigns anything.
    /// </summary>
    public static bool IsTurnUpProfileEmpty(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var knobs = config.Knobs
            .Where(knob => knob.Idx is >= 0 and < 5)
            .OrderBy(knob => knob.Idx)
            .Select(knob => new
            {
                knob.Idx,
                Label = knob.Label ?? "",
                Target = knob.Target ?? "none",
                DeviceId = knob.DeviceId ?? "",
                knob.MinVolume,
                knob.MaxVolume,
                knob.Curve,
                Apps = knob.Apps ?? new List<string>(),
                knob.EncoderStep,
            })
            .ToList();
        var defaultKnobs = Enumerable.Range(0, 5)
            .Select(idx => new KnobConfig { Idx = idx })
            .Select(knob => new
            {
                knob.Idx,
                Label = knob.Label ?? "",
                Target = knob.Target ?? "none",
                DeviceId = knob.DeviceId ?? "",
                knob.MinVolume,
                knob.MaxVolume,
                knob.Curve,
                Apps = knob.Apps ?? new List<string>(),
                knob.EncoderStep,
            })
            .ToList();

        var buttons = config.Buttons
            .Where(button => button.Idx is >= 0 and < 5)
            .OrderBy(button => button.Idx)
            .ToList();
        var defaultButtons = Enumerable.Range(0, 5)
            .Select(idx => new ButtonConfig { Idx = idx })
            .ToList();

        var lights = config.Lights
            .Where(light => light.Idx is >= 0 and < 5)
            .OrderBy(light => light.Idx)
            .ToList();
        var defaultLights = Enumerable.Range(0, 5)
            .Select(idx => new LightConfig { Idx = idx, R = 0, G = 150, B = 255 })
            .ToList();

        return config.LedBrightness == 100
            && knobs.Count == 5
            && buttons.Count == 5
            && lights.Count == 5
            && JsonConvert.SerializeObject(knobs) == JsonConvert.SerializeObject(defaultKnobs)
            && JsonConvert.SerializeObject(buttons) == JsonConvert.SerializeObject(defaultButtons)
            && JsonConvert.SerializeObject(lights) == JsonConvert.SerializeObject(defaultLights);
    }

    public static AppConfig Load()
    {
        var config = LoadJsonFile<AppConfig>(ConfigPath, "config", cfg => { if (cfg != null) NormalizeAndValidate(cfg); });
        if (config != null) return config;
        var defaults = new AppConfig();
        NormalizeAndValidate(defaults);
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
        (N3SideButtonBase + 0, "N3 Side 1", "media_prev"),
        (N3SideButtonBase + 1, "N3 Side 2", "media_play_pause"),
        (N3SideButtonBase + 2, "N3 Side 3", "media_next"),
        (N3EncoderPressBase + 0, "N3 Press 1", "mute_master"),
        (N3EncoderPressBase + 1, "N3 Press 2", "mute_active_window"),
        (N3EncoderPressBase + 2, "N3 Press 3", "mute_mic"),
    };
    private static readonly (int idx, string title, string subtitle, string background, string accent)[] DefaultN3DisplayKeys =
    {
        (0, "", "", "#1C1C1C", "#00E676"),
        (1, "", "", "#1C1C1C", "#00B4D8"),
        (2, "", "", "#1C1C1C", "#448AFF"),
        (3, "", "", "#1C1C1C", "#FF6E40"),
        (4, "", "", "#1C1C1C", "#FFD740"),
        (5, "", "", "#1C1C1C", "#FF4081"),
    };
    private static readonly string[] DefaultStreamControllerKnobLabels = { "Encoder 1", "Encoder 2", "Encoder 3" };
    private static readonly string[] DefaultStreamControllerKnobTargets = { "none", "none", "none" };

    public static AppConfig DeserializeAndNormalize(string json)
    {
        var config = JsonConvert.DeserializeObject<AppConfig>(json)
            ?? throw new InvalidDataException("JSON does not contain a valid AmpUp configuration.");
        return NormalizeAndValidate(config);
    }

    public static AppConfig NormalizeAndValidate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        NormalizeNullMembers(config, new AppConfig(), new HashSet<object>(ReferenceEqualityComparer.Instance));
        // The custom room-layout UI has been retired. Room effects now always
        // use spatial distribution, falling back to automatic device order
        // when no legacy placement data exists.
        config.Ambience.SpatialSync = true;
        MigrateLegacyN3ControlButtonIds(config);
        MigrateN3RoomEffectSpaces(config.N3.Folders);

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
        foreach (var key in config.N3.DisplayKeys)
        {
            bool legacyPlaceholderTitle =
                key.Title.Equals($"Key {key.Idx + 1}", StringComparison.OrdinalIgnoreCase)
                || key.Title.Equals($"K{key.Idx + 1}", StringComparison.OrdinalIgnoreCase);
            bool looksUntouched =
                string.IsNullOrWhiteSpace(key.ImagePath)
                && string.IsNullOrWhiteSpace(key.Subtitle)
                && legacyPlaceholderTitle;
            if (looksUntouched)
                key.Title = "";
        }
        NormalizeN3EncoderContexts(config.N3.EncoderContexts);
        foreach (var folder in config.N3.Folders)
            NormalizeN3EncoderContexts(folder.EncoderContexts);
        for (int i = 0; i < 5; i++)
        {
            if (!config.Lights.Any(l => l.Idx == i))
                config.Lights.Add(new LightConfig { Idx = i, R = 0, G = 150, B = 255 });
        }
        NormalizeProfileMetadata(config);
        if (config.Profiles.Count == 0)
            config.Profiles.Add("Default");
        if (string.IsNullOrEmpty(config.ActiveProfile)
            || !config.Profiles.Any(name => string.Equals(name, config.ActiveProfile, StringComparison.OrdinalIgnoreCase)))
            config.ActiveProfile = config.Profiles.FirstOrDefault(name =>
                string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase)) ?? config.Profiles[0];
        if (string.IsNullOrWhiteSpace(config.DiscordRpc.ClientId))
            config.DiscordRpc.ClientId = new DiscordRpcConfig().ClientId;
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
        MigrateLegacyProfileFiles(config.Profiles);
        return config;
    }

    private static readonly (string Title, string Accent, string Effect, string Icon)[] NewN3RoomEffects =
    {
        ("Black Hole", "#7C4DFF", "BlackHole",       "fx_blackhole"),
        ("Lava Lamp",  "#FF7043", "LavaLamp",        "fx_lavalamp"),
        ("Bubbles",    "#40C4FF", "Bubbles",         "fx_bubbles"),
        ("Fractal",    "#E040FB", "FractalMotion",   "fx_fractalmotion"),
        ("Noise Map",   "#26A69A", "NoiseMap",        "fx_noisemap"),
        ("Panes",       "#FFCA28", "MovingPanes",     "fx_movingpanes"),
        ("Sunrise",     "#FF8A65", "Sunrise",         "fx_sunrise"),
        ("Shimmer",     "#FFF59D", "Shimmer",         "fx_shimmer"),
        ("Spots",       "#69F0AE", "SpotsFade",       "fx_spotsfade"),
        ("Dual Stream", "#448AFF", "StreamDual",      "fx_streamdual"),
        ("Clouds",     "#7ED6FF", "ColorClouds",      "fx_colorclouds"),
        ("Fireflies",  "#D9FF6A", "FireflyGarden",    "fx_fireflygarden"),
        ("Sparkler",   "#FFD166", "Sparkler",         "fx_sparkler"),
        ("Shadows",    "#7C4DFF", "DancingShadows",  "fx_dancingshadows"),
        ("Nova Burst", "#FF5CD6", "NovaBurst",        "fx_novaburst"),
        ("Chroma",     "#39FFD0", "ChromaticSpring", "fx_chromaticspring"),
        ("Overdrive",  "#FF1774", "RgbOverdrive",    "fx_rgboverdrive"),
        ("Lasers",     "#00FFE5", "LaserGrid",       "fx_lasergrid"),
        ("Hyperdrive", "#7C4DFF", "Hyperdrive",      "fx_hyperdrive"),
        ("Prism Pulse","#FFEA00", "PrismPulse",      "fx_prismpulse"),
        ("Juggle",     "#FF40C8", "ColorJuggle",     "fx_colorjuggle"),
        ("Surge",      "#00E5FF", "SpectrumSurge",   "fx_spectrumsurge"),
    };

    /// <summary>
    /// Extends an existing N3 Effects/Room Effects Space without replacing any
    /// user-designed keys. Aurora and Ocean remain slots 0/1; the new scenes are
    /// inserted directly after them and the old tail shifts forward.
    /// </summary>
    private static void MigrateN3RoomEffectSpaces(List<ButtonFolderConfig> folders)
    {
        foreach (var folder in folders)
        {
            if (!folder.Name.Contains("effect", StringComparison.OrdinalIgnoreCase))
                continue;

            var displayKeys = folder.DisplayKeys
                .Where(key => key.Idx >= 0)
                .OrderBy(key => key.Idx)
                .ToList();
            if (displayKeys.Count < 2) continue;

            var pairs = displayKeys.Select(key =>
            {
                var button = folder.Buttons.FirstOrDefault(candidate => candidate.Idx == 100 + key.Idx)
                    ?? new ButtonConfig { Idx = 100 + key.Idx };
                return (Key: key, Button: button);
            }).ToList();

            if (!IsRoomEffect(pairs[0].Button, "Aurora")
                || !IsRoomEffect(pairs[1].Button, "Ocean"))
                continue;

            var expectedPrefix = new[] { "Aurora", "Ocean" }
                .Concat(NewN3RoomEffects.Select(effect => effect.Effect))
                .ToArray();
            bool alreadyMigrated = pairs.Count >= expectedPrefix.Length
                && expectedPrefix.Select((effect, idx) => IsRoomEffect(pairs[idx].Button, effect)).All(matches => matches);
            if (alreadyMigrated) continue;

            var newEffectNames = NewN3RoomEffects
                .Select(effect => effect.Effect)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var reordered = new List<(StreamControllerDisplayKeyConfig Key, ButtonConfig Button)>
            {
                pairs[0],
                pairs[1],
            };

            foreach (var effect in NewN3RoomEffects)
                reordered.Add(CreateN3RoomEffectPair(effect));

            reordered.AddRange(pairs.Skip(2).Where(pair =>
                !string.Equals(pair.Button.Action, "room_effect", StringComparison.OrdinalIgnoreCase)
                || !newEffectNames.Contains(pair.Button.Path)));

            var pairedButtons = pairs.Select(pair => pair.Button).ToHashSet();
            var extraButtons = folder.Buttons
                .Where(button => !pairedButtons.Contains(button))
                .ToList();
            for (int i = 0; i < reordered.Count; i++)
            {
                reordered[i].Key.Idx = i;
                reordered[i].Button.Idx = 100 + i;
            }

            folder.DisplayKeys = reordered.Select(pair => pair.Key).ToList();
            folder.Buttons = reordered.Select(pair => pair.Button).Concat(extraButtons).ToList();
            folder.PageCount = Math.Max(folder.PageCount,
                (int)Math.Ceiling(reordered.Count / 6.0));
        }
    }

    private static bool IsRoomEffect(ButtonConfig button, string effect)
        => string.Equals(button.Action, "room_effect", StringComparison.OrdinalIgnoreCase)
            && string.Equals(button.Path, effect, StringComparison.OrdinalIgnoreCase);

    private static (StreamControllerDisplayKeyConfig Key, ButtonConfig Button) CreateN3RoomEffectPair(
        (string Title, string Accent, string Effect, string Icon) effect)
    {
        var key = new StreamControllerDisplayKeyConfig
        {
            ImagePath = "",
            PresetIconKind = effect.Icon,
            Title = effect.Title,
            Subtitle = "",
            BackgroundColor = "#0B0B16",
            AccentColor = effect.Accent,
            TextPosition = DisplayTextPosition.Bottom,
            TextSize = effect.Title.Length > 10 ? 10 : 12,
            TextColor = "#FFFFFF",
            IconColor = effect.Accent,
            FontFamily = "Segoe UI",
            Brightness = 100,
            DisplayType = DisplayKeyType.Normal,
            ClockFormat = "h:mm",
            DynamicStateGlowColor = effect.Accent,
        };
        var button = new ButtonConfig
        {
            Label = effect.Title,
            Action = "room_effect",
            Path = effect.Effect,
            HoldAction = "none",
            DoublePressAction = "none",
            LinkedKnobIdx = -1,
        };
        return (key, button);
    }

    private static void NormalizeN3EncoderContexts(List<N3EncoderContextConfig> contexts)
    {
        contexts.RemoveAll(context => context.Page < -1);
        foreach (var context in contexts)
        {
            context.Knobs.RemoveAll(knob => knob.Idx is < 0 or > 2);

            var seen = new HashSet<int>();
            for (int i = context.Knobs.Count - 1; i >= 0; i--)
            {
                if (!seen.Add(context.Knobs[i].Idx))
                    context.Knobs.RemoveAt(i);
            }
        }

        var seenPages = new HashSet<int>();
        for (int i = contexts.Count - 1; i >= 0; i--)
        {
            if (!seenPages.Add(contexts[i].Page))
                contexts.RemoveAt(i);
        }
    }

    private static void NormalizeProfileMetadata(AppConfig config)
    {
        var normalized = new List<string>();
        foreach (var rawName in config.Profiles)
        {
            var name = rawName?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (IsProfileNameAvailable(normalized, name))
                normalized.Add(name);
            else
                Logger.Log($"Ignoring duplicate profile name/storage key: {name}");
        }

        var active = normalized.FirstOrDefault(name =>
            string.Equals(name, config.ActiveProfile?.Trim(), StringComparison.OrdinalIgnoreCase));
        config.ActiveProfile = active ?? config.ActiveProfile?.Trim() ?? "Default";
        config.Profiles = normalized;

        var icons = new Dictionary<string, ProfileIconConfig>(StringComparer.OrdinalIgnoreCase);
        var emojis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in normalized)
        {
            var icon = config.ProfileIcons.FirstOrDefault(pair =>
                string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            icons[name] = icon ?? new ProfileIconConfig();

            var emoji = config.ProfileEmojis.FirstOrDefault(pair =>
                string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            if (emoji != null) emojis[name] = emoji;
        }
        config.ProfileIcons = icons;
        config.ProfileEmojis = emojis;
    }

    private static void NormalizeNullMembers(object value, object? defaults, HashSet<object> visited)
    {
        if (!visited.Add(value)) return;

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;

            var current = property.GetValue(value);
            var defaultValue = defaults == null ? null : property.GetValue(defaults);
            if (current == null)
            {
                if (defaultValue != null)
                    property.SetValue(value, defaultValue);
                continue;
            }

            if (current is string || property.PropertyType.IsValueType) continue;
            if (current is IDictionary dictionary)
            {
                NormalizeDictionary(dictionary, property.PropertyType, visited);
                continue;
            }
            if (current is IList list)
            {
                NormalizeList(list, visited);
                continue;
            }

            object? childDefaults = defaultValue;
            if (childDefaults == null && current.GetType().GetConstructor(Type.EmptyTypes) != null)
                childDefaults = Activator.CreateInstance(current.GetType());
            NormalizeNullMembers(current, childDefaults, visited);
        }
    }

    private static void NormalizeList(IList list, HashSet<object> visited)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var item = list[i];
            if (item == null)
            {
                list.RemoveAt(i);
                continue;
            }
            if (item is IDictionary nestedDictionary)
            {
                NormalizeDictionary(nestedDictionary, item.GetType(), visited);
                continue;
            }
            if (item is IList nestedList)
            {
                NormalizeList(nestedList, visited);
                continue;
            }
            if (item is not string && !item.GetType().IsValueType)
            {
                var defaults = item.GetType().GetConstructor(Type.EmptyTypes) == null
                    ? null
                    : Activator.CreateInstance(item.GetType());
                NormalizeNullMembers(item, defaults, visited);
            }
        }
    }

    private static void NormalizeDictionary(IDictionary dictionary, Type dictionaryType, HashSet<object> visited)
    {
        var valueType = dictionaryType.IsGenericType ? dictionaryType.GetGenericArguments()[1] : typeof(object);
        foreach (var key in dictionary.Keys.Cast<object>().ToArray())
        {
            var item = dictionary[key];
            if (item == null)
            {
                if (valueType == typeof(string)) dictionary[key] = string.Empty;
                else if (valueType.GetConstructor(Type.EmptyTypes) != null) dictionary[key] = Activator.CreateInstance(valueType);
                else dictionary.Remove(key);
                continue;
            }
            if (item is IDictionary nestedDictionary)
            {
                NormalizeDictionary(nestedDictionary, item.GetType(), visited);
                continue;
            }
            if (item is IList nestedList)
            {
                NormalizeList(nestedList, visited);
                continue;
            }
            if (item is not string && !item.GetType().IsValueType)
            {
                var defaults = item.GetType().GetConstructor(Type.EmptyTypes) == null
                    ? null
                    : Activator.CreateInstance(item.GetType());
                NormalizeNullMembers(item, defaults, visited);
            }
        }
    }

    private static void MigrateLegacyProfileFiles(IEnumerable<string> profileNames)
    {
        lock (_saveLock)
        {
            foreach (var profileName in profileNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                var destination = ProfilePath(profileName);
                var legacy = LegacyProfilePath(profileName);
                bool migrated = CopyIfMissing(legacy, destination);
                migrated |= CopyIfMissing(legacy + ".bak", destination + ".bak");
                if (migrated)
                    Logger.Log($"Migrated profile storage for {profileName}");
            }
        }
    }

    private static bool CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return false;
        try
        {
            File.Copy(source, destination, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(destination))
        {
            return false;
        }
    }

    private static void MigrateLegacyN3ControlButtonIds(AppConfig config)
    {
        if (config.N3?.Buttons == null) return;

        for (int i = 0; i < 3; i++)
        {
            MigrateLegacyN3ControlButtonId(
                config.N3.Buttons,
                LegacyN3SideButtonBase + i,
                N3SideButtonBase + i,
                $"N3 Side {i + 1}",
                LegacyN3SideDefaultActions[i]);
            MigrateLegacyN3ControlButtonId(
                config.N3.Buttons,
                LegacyN3EncoderPressBase + i,
                N3EncoderPressBase + i,
                $"N3 Press {i + 1}",
                LegacyN3EncoderDefaultActions[i]);
        }
    }

    private static void MigrateLegacyN3ControlButtonId(List<ButtonConfig> buttons, int oldIdx, int newIdx, string defaultLabel, string defaultAction)
    {
        var oldButton = buttons.FirstOrDefault(b => b.Idx == oldIdx);
        if (oldButton == null) return;

        bool legacyDefault = IsLegacyN3DefaultButton(oldButton, defaultLabel, defaultAction);
        if (buttons.Any(b => b.Idx == newIdx))
        {
            if (legacyDefault)
                buttons.Remove(oldButton);
            return;
        }

        if (legacyDefault)
        {
            oldButton.Idx = newIdx;
            return;
        }

        var controlCopy = CloneButton(oldButton);
        controlCopy.Idx = newIdx;
        buttons.Add(controlCopy);
    }

    private static ButtonConfig CloneButton(ButtonConfig button)
        => JsonConvert.DeserializeObject<ButtonConfig>(JsonConvert.SerializeObject(button))
           ?? new ButtonConfig { Idx = button.Idx };

    private static bool IsLegacyN3DefaultButton(ButtonConfig button, string defaultLabel, string defaultAction)
        => string.Equals(button.Label ?? "", defaultLabel, StringComparison.OrdinalIgnoreCase)
           && string.Equals(button.Action ?? "none", defaultAction, StringComparison.OrdinalIgnoreCase)
           && string.Equals(button.Path ?? "", "", StringComparison.Ordinal)
           && string.Equals(button.HoldAction ?? "none", "none", StringComparison.OrdinalIgnoreCase)
           && string.Equals(button.DoublePressAction ?? "none", "none", StringComparison.OrdinalIgnoreCase)
           && string.IsNullOrEmpty(button.MacroKeys)
           && string.IsNullOrEmpty(button.DeviceId)
           && string.IsNullOrEmpty(button.ProfileName)
           && string.IsNullOrEmpty(button.FolderName)
           && button.ActionSequence.Count == 0
           && string.Equals(button.ToggleActionA ?? "none", "none", StringComparison.OrdinalIgnoreCase)
           && string.Equals(button.ToggleActionB ?? "none", "none", StringComparison.OrdinalIgnoreCase);

    private static void NormalizeDeviceSurfaceSelections(AppConfig config)
    {
        // One-time migration: configs saved before PreferredSurface existed
        // will have its default (TurnUp) while Buttons may already reflect
        // the user's chosen surface. Copy it over so the Active Surface
        // picker shows the right state after upgrade.
        if (config.TabSelection.PreferredSurface == DeviceSurface.TurnUp
            && config.TabSelection.Buttons != DeviceSurface.TurnUp)
        {
            config.TabSelection.PreferredSurface = config.TabSelection.Buttons;
        }

        // Legacy N3.IdleSleepMinutes -> IdleSleepSeconds. Older builds saved
        // the timeout in minutes. Migrate once and clear the legacy field so
        // the runtime only looks at seconds going forward.
        if (config.N3.IdleSleepSeconds == 600 && config.N3.IdleSleepMinutes > 0)
        {
            config.N3.IdleSleepSeconds = config.N3.IdleSleepMinutes * 60;
            config.N3.IdleSleepMinutes = 0;
        }

        switch (config.HardwareMode)
        {
            case HardwareMode.Auto:
                config.TabSelection.Mixer = DeviceSurface.TurnUp;
                config.TabSelection.Buttons = DeviceSurface.TurnUp;
                config.TabSelection.Lights = DeviceSurface.TurnUp;
                break;
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
    /// Falls back to an overwrite move if File.Replace cannot be used (for
    /// example, when the destination does not exist yet).
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
            // The temp file is next to the destination. Do not delete the
            // last known-good file before its replacement is ready.
            File.Move(tmpPath, destPath, overwrite: true);
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
        MigrateLegacyProfileFiles(new[] { profileName });
        var path = ProfilePath(profileName);
        return LoadJsonFile<AppConfig>(path, $"profile {profileName}", cfg => { if (cfg != null) NormalizeAndValidate(cfg); });
    }

    public static void RenameProfileFile(string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        lock (_saveLock)
        {
            MigrateLegacyProfileFiles(new[] { oldName });
            var source = ProfilePath(oldName);
            var destination = ProfilePath(newName);
            var sourceBackup = source + ".bak";
            var destinationBackup = destination + ".bak";
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(source) && !File.Exists(sourceBackup)) return;
            if (File.Exists(destination) || File.Exists(destinationBackup))
                throw new IOException($"A profile file for '{newName}' already exists.");

            if (File.Exists(source))
                File.Move(source, destination);
            if (File.Exists(sourceBackup))
                File.Move(sourceBackup, destinationBackup);
        }
    }

    public static void DeleteProfileFiles(string profileName, IEnumerable<string> remainingProfileNames)
    {
        lock (_saveLock)
        {
            DeleteFileIfExists(ProfilePath(profileName));
            DeleteFileIfExists(ProfilePath(profileName) + ".bak");

            var legacy = LegacyProfilePath(profileName);
            bool legacyIsShared = remainingProfileNames.Any(other =>
                string.Equals(LegacyProfilePath(other), legacy, StringComparison.OrdinalIgnoreCase));
            if (!legacyIsShared)
            {
                DeleteFileIfExists(legacy);
                DeleteFileIfExists(legacy + ".bak");
            }
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static void CreateBackup(string destinationPath, AppConfig config)
    {
        lock (_saveLock)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            AtomicWrite(ConfigPath, json);
            if (!string.IsNullOrWhiteSpace(config.ActiveProfile))
                AtomicWrite(ProfilePath(config.ActiveProfile), json);

            var backupDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(backupDir))
                Directory.CreateDirectory(backupDir);

            var tempPath = destinationPath + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                AddJsonFileIfExists(archive, ConfigPath, "config.json");

                foreach (var profilePath in Directory.EnumerateFiles(ConfigDir, "profile_*.json"))
                    AddJsonFileIfExists(archive, profilePath, Path.GetFileName(profilePath));

                var manifest = new
                {
                    app = "AmpUp",
                    backupVersion = 1,
                    createdUtc = DateTime.UtcNow,
                    activeProfile = config.ActiveProfile,
                    profiles = config.Profiles,
                };
                var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
    }

    public static AppConfig RestoreBackup(string sourcePath)
    {
        lock (_saveLock)
        {
            var restoredJson = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var archive = ZipFile.OpenRead(sourcePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var fileName = Path.GetFileName(entry.FullName);
                    if (!IsRestorableConfigFile(fileName))
                        continue;

                    using var reader = new StreamReader(entry.Open());
                    restoredJson[fileName] = reader.ReadToEnd();
                }
            }

            if (!restoredJson.TryGetValue("config.json", out var configJson))
                throw new InvalidDataException("Backup does not contain config.json.");

            var restoredConfig = DeserializeAndNormalize(configJson);

            foreach (var path in Directory.EnumerateFiles(ConfigDir, "profile_*.json"))
                File.Delete(path);

            foreach (var pair in restoredJson)
            {
                var destination = pair.Key.Equals("config.json", StringComparison.OrdinalIgnoreCase)
                    ? ConfigPath
                    : Path.Combine(ConfigDir, pair.Key);
                AtomicWrite(destination, pair.Value);
            }

            return restoredConfig;
        }
    }

    private static void AddJsonFileIfExists(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
            return;

        archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
    }

    private static bool IsRestorableConfigFile(string fileName)
    {
        if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase))
            return true;

        return fileName.StartsWith("profile_", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}
