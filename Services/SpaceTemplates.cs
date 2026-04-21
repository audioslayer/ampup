using System;
using System.Collections.Generic;
using AmpUp.Core.Models;

namespace AmpUp.Services;

/// <summary>
/// Catalogue of pre-built N3 Space layouts. Each template is a factory —
/// click "Add to Spaces" in Buttons → Space Templates and the UI calls
/// Build() to materialize a fresh ButtonFolderConfig into the user's
/// config. Templates are data-only, don't ship their own icons beyond
/// what's already in Icons/.
/// </summary>
public static class SpaceTemplates
{
    // Matches StreamControllerDisplayKeyBase + keys-per-page in the UI.
    private const int KeyBase = 100;
    private const int KeysPerPage = 6;

    public record Template(
        string Name,
        string Description,
        string AccentHex,
        string CardIconKind,
        Func<ButtonFolderConfig> Build);

    public static readonly IReadOnlyList<Template> All = new[]
    {
        new Template("Room Effects",  "18 room lighting patterns across 3 pages (Aurora, Ocean, Fire, Lightning, Matrix …).",
            "#69F0AE", "neon_lava_lamp", BuildRoomEffects),
        new Template("Media",         "Prev / Play-Pause / Next, mute master + mic, screenshot.",
            "#448AFF", "material_playpause", BuildMedia),
        new Template("Discord Quick", "System mic mute + Discord shortcuts (Mute, Deafen, Quick Switcher).",
            "#7289DA", "material_discord", BuildDiscord),
        new Template("System",        "Lock, Sleep, Restart, Screenshot, Task Manager, Explorer.",
            "#FF5252", "neon_lock", BuildSystem),
        new Template("Apps",          "6 common app launchers — Chrome, Spotify, Discord, OBS, Terminal, Explorer.",
            "#FFD740", "neon_folder", BuildApps),
        new Template("Audio Profiles", "Cycle output / input devices, mute master / mic, switch AmpUp profile.",
            "#E040FB", "neon_mixer", BuildAudioProfiles),
        new Template("Spotify",       "Spotify transport + launch / mute / shuffle.",
            "#1DB954", "material_spotify", BuildSpotify),
    };

    // ── Builders ────────────────────────────────────────────────────────

    private static ButtonFolderConfig BuildRoomEffects()
    {
        (string Title, string Accent, string Effect, string Icon)[] page1 =
        {
            ("Aurora",    "#69F0AE", "Aurora",        "fx_aurora"),
            ("Ocean",     "#29B6F6", "Ocean",         "fx_ocean"),
            ("Starfield", "#B0BEC5", "Starfield",     "fx_starfield"),
            ("Plasma",    "#E040FB", "Plasma",        "fx_plasma"),
            ("Nebula",    "#7C4DFF", "NebulaDrift",   "fx_nebuladrift"),
            ("Breathing", "#90A4AE", "BreathingSync", "fx_breathingsync"),
        };
        (string, string, string, string)[] page2 =
        {
            ("Fire",      "#FF5722", "Fire",          "fx_fire"),
            ("Lava",      "#FF6B35", "Lava",          "fx_lava"),
            ("Lightning", "#FFEB3B", "Lightning",     "fx_lightning"),
            ("Police",    "#2196F3", "PoliceLights",  "fx_police"),
            ("Scanner",   "#F44336", "Scanner",       "fx_scanner"),
            ("Matrix",    "#00E676", "Matrix",        "fx_matrix"),
        };
        (string, string, string, string)[] page3 =
        {
            ("ColorWave", "#00ACC1", "ColorWave",     "fx_colorwave"),
            ("Rainfall",  "#4FC3F7", "Rainfall",      "fx_rainfall"),
            ("Waterfall", "#4DD0E1", "Waterfall",     "fx_waterfall"),
            ("Rainbow",   "#FF4081", "RainbowWave",   "fx_rainbow"),
            ("Meteor",    "#FF9800", "MeteorRain",    "fx_meteor"),
            ("Heartbeat", "#E91E63", "Heartbeat",     "fx_heartbeat"),
        };

        var folder = new ButtonFolderConfig { Name = "Room Effects", PageCount = 3, BackKeyEnabled = false };
        var pages = new[] { page1, page2, page3 };
        for (int p = 0; p < pages.Length; p++)
        {
            for (int s = 0; s < KeysPerPage; s++)
            {
                int local = p * KeysPerPage + s;
                var (title, accent, effect, icon) = pages[p][s];
                folder.DisplayKeys.Add(DisplayKey(local, title, accent, icon));
                folder.Buttons.Add(Btn(KeyBase + local, "room_effect", effect));
            }
        }
        return folder;
    }

    private static ButtonFolderConfig BuildMedia()
    {
        return SinglePage("Media", new[]
        {
            ("Prev",        "#448AFF", "material_prev",        "media_prev",       ""),
            ("Play/Pause",  "#448AFF", "material_playpause",   "media_play_pause", ""),
            ("Next",        "#448AFF", "material_next",        "media_next",       ""),
            ("Mute",        "#FF5252", "neon_volume_mute",     "mute_master",      ""),
            ("Mic Mute",    "#FFD740", "material_mic_mute",    "mute_mic",         ""),
            ("Screenshot",  "#69F0AE", "neon_screenshot",      "screenshot",       ""),
        });
    }

    private static ButtonFolderConfig BuildDiscord()
    {
        return SinglePage("Discord", new[]
        {
            ("Open",        "#7289DA", "material_discord",     "launch_exe",   ""), // user fills path
            ("Discord Mute","#FF5252", "neon_discord_mute",    "macro",        ""), // macroKeys set below
            ("Deafen",      "#FFD740", "material_discord_mute","macro",        ""),
            ("Mic Mute",    "#FFD740", "material_mic_mute",    "mute_mic",     ""),
            ("Quick Search","#00ACC1", "neon_search",          "macro",        ""),
            ("Close",       "#FF5252", "material_discord",     "close_program","discord"),
        }, configure: btns =>
        {
            // Discord in-app shortcuts — users can change Discord's keybind
            // to match. These are Discord's defaults.
            btns[1].MacroKeys = "ctrl+shift+m";  // Toggle mute
            btns[2].MacroKeys = "ctrl+shift+d";  // Toggle deafen
            btns[4].MacroKeys = "ctrl+k";        // Quick switcher
        });
    }

    private static ButtonFolderConfig BuildSystem()
    {
        return SinglePage("System", new[]
        {
            ("Lock",        "#448AFF", "neon_lock",        "power_lock",    ""),
            ("Sleep",       "#7C4DFF", "neon_sleep",       "power_sleep",   ""),
            ("Restart",     "#FF5252", "neon_restart",     "power_restart", ""),
            ("Screenshot",  "#69F0AE", "neon_screenshot",  "screenshot",    ""),
            ("Task Mgr",    "#FFD740", "neon_taskmgr",     "launch_exe",    "taskmgr.exe"),
            ("Explorer",    "#00ACC1", "neon_explorer",    "launch_exe",    "explorer.exe"),
        });
    }

    private static ButtonFolderConfig BuildApps()
    {
        return SinglePage("Apps", new[]
        {
            ("Chrome",   "#448AFF", "neon_chrome",     "launch_exe", "chrome.exe"),
            ("Spotify",  "#1DB954", "material_spotify","launch_exe", "spotify.exe"),
            ("Discord",  "#7289DA", "material_discord","launch_exe", "discord.exe"),
            ("OBS",      "#E040FB", "neon_obs",        "launch_exe", "obs64.exe"),
            ("Terminal", "#69F0AE", "neon_terminal",   "launch_exe", "wt.exe"),
            ("Explorer", "#FFD740", "neon_explorer",   "launch_exe", "explorer.exe"),
        });
    }

    private static ButtonFolderConfig BuildAudioProfiles()
    {
        return SinglePage("Audio Profiles", new[]
        {
            ("Output",       "#448AFF", "neon_headphones",    "cycle_output",   ""),
            ("Input",        "#00ACC1", "material_mic",       "cycle_input",    ""),
            ("Mute",         "#FF5252", "neon_volume_mute",   "mute_master",    ""),
            ("Mic Mute",     "#FFD740", "material_mic_mute",  "mute_mic",       ""),
            ("Profile Def",  "#69F0AE", "neon_mixer",         "switch_profile", "Default"),
            ("Profile Alt",  "#E040FB", "neon_mixer",         "switch_profile", "Lights"),
        }, configure: btns =>
        {
            // switch_profile reads ProfileName, not Path — populate both.
            btns[4].ProfileName = "Default";
            btns[5].ProfileName = "Lights";
        });
    }

    private static ButtonFolderConfig BuildSpotify()
    {
        return SinglePage("Spotify", new[]
        {
            ("Prev",        "#1DB954", "material_prev",      "media_prev",       ""),
            ("Play/Pause",  "#1DB954", "material_playpause", "media_play_pause", ""),
            ("Next",        "#1DB954", "material_next",      "media_next",       ""),
            ("Open",        "#1DB954", "material_spotify",   "launch_exe",       "spotify.exe"),
            ("Mute Spotify","#FF5252", "neon_volume_mute",   "mute_program",     "spotify"),
            ("Shuffle",     "#E040FB", "neon_shuffle",       "macro",            ""),
        }, configure: btns =>
        {
            btns[5].MacroKeys = "ctrl+s"; // Spotify default shuffle toggle
        });
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static ButtonFolderConfig SinglePage(
        string name,
        (string Title, string Accent, string Icon, string Action, string Path)[] keys,
        Action<List<ButtonConfig>>? configure = null)
    {
        var folder = new ButtonFolderConfig { Name = name, PageCount = 1, BackKeyEnabled = false };
        for (int i = 0; i < keys.Length && i < KeysPerPage; i++)
        {
            var (title, accent, icon, action, path) = keys[i];
            folder.DisplayKeys.Add(DisplayKey(i, title, accent, icon));
            folder.Buttons.Add(Btn(KeyBase + i, action, path));
        }
        // Fill any unused slots with inert entries so the folder is always complete.
        for (int i = keys.Length; i < KeysPerPage; i++)
        {
            folder.DisplayKeys.Add(DisplayKey(i, "", "#1C1C1C", ""));
            folder.Buttons.Add(Btn(KeyBase + i, "none", ""));
        }
        configure?.Invoke(folder.Buttons);
        return folder;
    }

    private static StreamControllerDisplayKeyConfig DisplayKey(int idx, string title, string accent, string iconKind) => new()
    {
        Idx = idx,
        ImagePath = "",
        PresetIconKind = iconKind,
        Title = title,
        Subtitle = "",
        BackgroundColor = "#1C1C1C",
        AccentColor = accent,
        TextPosition = DisplayTextPosition.Bottom,
        TextSize = 12,
        TextColor = "#FFFFFF",
        IconColor = accent,
        FontFamily = "Segoe UI",
        Brightness = 100,
        DisplayType = DisplayKeyType.Normal,
        ClockFormat = "HH:mm",
    };

    private static ButtonConfig Btn(int idx, string action, string path) => new()
    {
        Idx = idx,
        Action = action,
        Path = path,
        HoldAction = "none",
        DoublePressAction = "none",
        LinkedKnobIdx = -1,
    };
}
