using System.Windows.Media;
using NAudio.CoreAudioApi;

namespace AmpUp.Services;

/// <summary>One Quick Wheel segment: what it shows and what picking it does.</summary>
public sealed record QuickWheelItem(string Id, string Label, string Symbol, Color Color, Action Run);

/// <summary>
/// Builds what a Quick Wheel shows for its mode — shared by the hardware-triggered wheel
/// (App) and the settings-page preview (OsdView) so both always show the same items.
/// </summary>
public sealed class QuickWheelContent
{
    public List<QuickWheelItem> Items { get; } = new();
    /// <summary>Item the highlight starts on.</summary>
    public int CurrentIndex { get; set; }
    /// <summary>Item that's in effect right now (active profile / default device / last effect), or -1.</summary>
    public int ActiveIndex { get; set; } = -1;
    /// <summary>Small line under the name in the center card, e.g. "Output device".</summary>
    public string Kind { get; set; } = "";
    /// <summary>Fewest items for the hardware trigger to open the wheel (a 1-item profile wheel is pointless).</summary>
    public int MinItems { get; set; } = 1;

    public const int MaxItems = 12;

    private static readonly Color DeviceColor = Color.FromRgb(0xAB, 0x47, 0xBC);
    private static readonly Color InputColor = Color.FromRgb(0xFF, 0xB8, 0x00);
    private static readonly Color EffectColor = Color.FromRgb(0xAB, 0x47, 0xBC);

    private static readonly (string id, string label, string symbol, Color color)[] MediaControls =
    {
        ("media_play_pause", "Play / Pause", "PlayPause", Color.FromRgb(0x00, 0xE6, 0x76)),
        ("media_prev", "Previous", "SkipPrevious", Color.FromRgb(0x00, 0xBC, 0xD4)),
        ("media_next", "Next", "SkipNext", Color.FromRgb(0x00, 0xBC, 0xD4)),
        ("mute_master", "Mute Master", "VolumeOff", Color.FromRgb(0xFF, 0x44, 0x44)),
        ("mute_mic", "Mute Mic", "MicrophoneOff", Color.FromRgb(0xFF, 0xB8, 0x00)),
        ("volume_up", "Volume Up", "VolumeHigh", Color.FromRgb(0x42, 0xA5, 0xF5)),
        ("volume_down", "Volume Down", "VolumeLow", Color.FromRgb(0x42, 0xA5, 0xF5)),
        ("media_stop", "Stop", "Stop", Color.FromRgb(0x9E, 0x9E, 0x9E)),
    };

    /// <summary>
    /// Build the wheel's items. <paramref name="switchProfile"/> and <paramref name="runAction"/>
    /// are what picking an item calls; leave them null for a preview that does nothing.
    /// </summary>
    public static QuickWheelContent Build(AppConfig config, QuickWheelConfig wheel,
        Action<string>? switchProfile = null, Action<string, ButtonConfig?>? runAction = null)
    {
        switchProfile ??= _ => { };
        runAction ??= (_, _) => { };
        var c = new QuickWheelContent();

        switch (wheel.Mode)
        {
            case QuickWheelMode.OutputDevice:
                AddDevices(c, DataFlow.Render, wheel.OutputDeviceIds, runAction);
                break;
            case QuickWheelMode.InputDevice:
                AddDevices(c, DataFlow.Capture, wheel.InputDeviceIds, runAction);
                break;
            case QuickWheelMode.SignalRgbEffect:
                AddSignalRgbEffects(c, wheel.SignalRgbEffects, runAction);
                break;
            case QuickWheelMode.MediaControls:
                c.Kind = "Media control";
                foreach (var m in MediaControls)
                {
                    var id = m.id;
                    c.Items.Add(new QuickWheelItem(id, m.label, m.symbol, m.color, () => runAction(id, null)));
                }
                break;
            case QuickWheelMode.Custom:
                c.Kind = "Action";
                foreach (var slot in wheel.CustomSlots)
                {
                    if (string.IsNullOrEmpty(slot.ActionId)) continue;
                    if (c.Items.Count >= MaxItems) break;
                    var (symbol, color) = GetActionVisuals(slot.ActionId);
                    var s = slot;
                    c.Items.Add(new QuickWheelItem(s.ActionId,
                        string.IsNullOrEmpty(s.Label) ? s.ActionId : s.Label, symbol, color,
                        () => runAction(s.ActionId, s.ToButtonConfig())));
                }
                break;
            default:
                AddProfiles(c, config, switchProfile);
                break;
        }
        return c;
    }

    private static void AddProfiles(QuickWheelContent c, AppConfig config, Action<string> switchProfile)
    {
        c.Kind = "Profile";
        c.MinItems = 2;
        foreach (var name in config.Profiles.Take(MaxItems))
        {
            Color color = ThemeManager.Accent;
            string symbol = "AccountCircleOutline";
            if (config.ProfileIcons != null && config.ProfileIcons.TryGetValue(name, out var icon))
            {
                try { color = (Color)ColorConverter.ConvertFromString(icon.Color); }
                catch { color = ThemeManager.Accent; }
                if (!string.IsNullOrEmpty(icon.Symbol)) symbol = icon.Symbol;
            }
            var n = name;
            c.Items.Add(new QuickWheelItem(n, n, symbol, color, () => switchProfile(n)));
        }
        c.ActiveIndex = c.Items.FindIndex(i => i.Id == config.ActiveProfile);
        c.CurrentIndex = Math.Max(0, c.ActiveIndex);
    }

    /// <summary>
    /// Output or input devices. When the user picked devices, show those (in their order,
    /// skipping unplugged ones); otherwise the first active ones.
    /// </summary>
    private static void AddDevices(QuickWheelContent c, DataFlow flow, List<string> pickedIds,
        Action<string, ButtonConfig?> runAction)
    {
        bool isInput = flow == DataFlow.Capture;
        c.Kind = isInput ? "Input device" : "Output device";
        c.MinItems = pickedIds.Count > 0 ? 1 : 2;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            string currentId = "";
            try
            {
                using var current = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                currentId = current.ID;
            }
            catch { /* no default device */ }

            var active = new List<(string id, string name)>();
            for (int i = 0; i < devices.Count; i++)
            {
                using var d = devices[i];
                active.Add((d.ID, d.FriendlyName));
            }

            var list = pickedIds.Count > 0
                ? pickedIds.Select(id => active.FirstOrDefault(a => a.id == id)).Where(a => a.id != null).ToList()
                : active;

            string action = isInput ? "select_input" : "select_output";
            foreach (var (id, name) in list.Take(MaxItems))
            {
                var devId = id;
                c.Items.Add(new QuickWheelItem(devId, name, isInput ? "Microphone" : "VolumeHigh",
                    isInput ? InputColor : DeviceColor,
                    () => runAction(action, new ButtonConfig { DeviceId = devId })));
            }
            c.ActiveIndex = c.Items.FindIndex(i => i.Id == currentId);
            c.CurrentIndex = Math.Max(0, c.ActiveIndex);
        }
        catch (Exception ex)
        {
            Logger.Log($"Quick Wheel device enum error: {ex.Message}");
            c.Items.Clear();
        }
    }

    private static void AddSignalRgbEffects(QuickWheelContent c, List<string> picked,
        Action<string, ButtonConfig?> runAction)
    {
        c.Kind = "SignalRGB effect";
        List<string> names;
        try
        {
            names = picked.Count > 0
                ? picked.Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                : SignalRgbEffectCatalog.GetInstalledEffects().Select(e => e.Name).ToList();
        }
        catch (Exception ex)
        {
            Logger.Log($"Quick Wheel SignalRGB effect list error: {ex.Message}");
            names = new List<string>();
        }

        foreach (var name in names.Take(MaxItems))
        {
            var n = name;
            c.Items.Add(new QuickWheelItem(n, n, "Palette", EffectColor,
                () => runAction("signalrgb_effect", new ButtonConfig { Path = n })));
        }
        c.ActiveIndex = c.Items.FindIndex(i =>
            string.Equals(i.Id, SignalRgbEffectCatalog.LastAppliedEffectName, StringComparison.OrdinalIgnoreCase));
        c.CurrentIndex = Math.Max(0, c.ActiveIndex);
    }

    public static (string symbol, Color color) GetActionVisuals(string actionId) => actionId switch
    {
        "media_play_pause" => ("PlayPause", Color.FromRgb(0x00, 0xE6, 0x76)),
        "media_next" => ("SkipNext", Color.FromRgb(0x00, 0xBC, 0xD4)),
        "media_prev" => ("SkipPrevious", Color.FromRgb(0x00, 0xBC, 0xD4)),
        "media_stop" => ("Stop", Color.FromRgb(0x9E, 0x9E, 0x9E)),
        "mute_master" => ("VolumeOff", Color.FromRgb(0xFF, 0x44, 0x44)),
        "mute_mic" => ("MicrophoneOff", Color.FromRgb(0xFF, 0xB8, 0x00)),
        "volume_up" => ("VolumeHigh", Color.FromRgb(0x42, 0xA5, 0xF5)),
        "volume_down" => ("VolumeLow", Color.FromRgb(0x42, 0xA5, 0xF5)),
        "mute_program" => ("VolumeOff", Color.FromRgb(0xFF, 0x44, 0x44)),
        "mute_active_window" => ("VolumeOff", Color.FromRgb(0xFF, 0x44, 0x44)),
        "add_active_app_to_group" => ("PlusCircleOutline", Color.FromRgb(0x26, 0xC6, 0xDA)),
        "switch_profile" => ("AccountCircleOutline", Color.FromRgb(0xAB, 0x47, 0xBC)),
        "cycle_brightness" => ("Brightness6", Color.FromRgb(0xFF, 0xB8, 0x00)),
        "launch_exe" => ("Launch", Color.FromRgb(0x42, 0xA5, 0xF5)),
        "open_url" => ("Web", Color.FromRgb(0x42, 0xA5, 0xF5)),
        "macro" => ("Keyboard", Color.FromRgb(0xFF, 0xB8, 0x00)),
        "signalrgb_effect" => ("Palette", Color.FromRgb(0xAB, 0x47, 0xBC)),
        "power_sleep" => ("Sleep", Color.FromRgb(0x9E, 0x9E, 0x9E)),
        "power_lock" => ("Lock", Color.FromRgb(0x9E, 0x9E, 0x9E)),
        "power_off" => ("Power", Color.FromRgb(0xFF, 0x44, 0x44)),
        "power_restart" => ("Restart", Color.FromRgb(0xFF, 0xB8, 0x00)),
        _ => ("CircleOutline", Color.FromRgb(0x9E, 0x9E, 0x9E)),
    };
}
