using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Material.Icons;
using Material.Icons.WPF;
using AmpUp.Controls;

namespace AmpUp.Views;

public partial class ButtonsView : UserControl
{
    private AppConfig? _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Action definitions: (DisplayName, ConfigValue)
    private static readonly (string Display, string Value)[] Actions =
    {
        ("None", "none"),
        ("Play / Pause", "media_play_pause"), ("Next Track", "media_next"), ("Prev Track", "media_prev"),
        ("Mute Volume", "mute_master"), ("Mute Mic", "mute_mic"), ("Mute App", "mute_program"),
        ("Mute Active Window", "mute_active_window"), ("Mute App Group", "mute_app_group"),
        ("Mute Device", "mute_device"),
        ("Launch App", "launch_exe"), ("Close App", "close_program"),
        ("Cycle Output", "cycle_output"), ("Cycle Input", "cycle_input"),
        ("Set Output", "select_output"), ("Set Input", "select_input"),
        ("Keyboard Macro", "macro"), ("Switch Profile", "switch_profile"),
        ("Cycle Profiles", "cycle_profile"),
        ("Cycle Brightness", "cycle_brightness"), ("Quick Wheel", "quick_wheel"),
        ("Sleep", "power_sleep"), ("Lock", "power_lock"), ("Off", "power_off"),
        ("Restart", "power_restart"), ("Logoff", "power_logoff"), ("Hibernate", "power_hibernate"),
        ("Home Assistant: Toggle", "ha_toggle"), ("Home Assistant: Scene", "ha_scene"), ("Home Assistant: Service", "ha_service"),
        ("Govee: Toggle", "govee_toggle"), ("Govee: Color", "govee_color"),
        ("OBS: Record", "obs_record"), ("OBS: Stream", "obs_stream"),
        ("OBS: Scene", "obs_scene"), ("OBS: Mute", "obs_mute"),
        ("VM: Mute Strip", "vm_mute_strip"), ("VM: Mute Bus", "vm_mute_bus"),
    };

    private static readonly string[] PathActions = { "mute_program", "launch_exe", "close_program" };

    private static readonly (string Display, string Value)[] PowerOptions =
    {
        ("Sleep", "sleep"), ("Lock", "lock"), ("Off", "shutdown"),
        ("Restart", "restart"), ("Logoff", "logoff"), ("Hibernate", "hibernate"),
    };

    private static readonly Dictionary<string, string> ActionIcons = new()
    {
        { "none", "—" }, { "media_play_pause", "⏯" }, { "media_next", "⏭" },
        { "media_prev", "⏮" }, { "mute_master", "🔇" }, { "mute_mic", "🎤" },
        { "mute_program", "🔇" }, { "mute_active_window", "🔇" }, { "launch_exe", "🚀" },
        { "close_program", "✕" }, { "cycle_output", "🔊" }, { "cycle_input", "🎙" },
        { "select_output", "🔊" }, { "select_input", "🎙" }, { "macro", "⌨" },
        { "switch_profile", "📋" }, { "cycle_profile", "🔄" }, { "cycle_brightness", "💡" }, { "quick_wheel", "🎡" }, { "mute_app_group", "🔇" },
        { "mute_device", "🔇" },
        { "power_sleep", "😴" }, { "power_lock", "🔒" }, { "power_off", "⏻" },
        { "power_restart", "🔄" }, { "power_logoff", "🚪" }, { "power_hibernate", "❄" },
        { "ha_toggle", "⚡" }, { "ha_scene", "🎬" }, { "ha_service", "⚙" },
        { "govee_toggle", "◈" }, { "govee_color", "◉" },
        { "obs_record", "●" }, { "obs_stream", "◉" },
        { "obs_scene", "🎬" }, { "obs_mute", "🔇" },
        { "vm_mute_strip", "🔇" }, { "vm_mute_bus", "🔇" },
    };

    private static readonly Dictionary<string, Color> ActionColors = new()
    {
        { "none",               Color.FromRgb(0x44, 0x44, 0x44) },
        { "media_play_pause",   Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "media_next",         Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "media_prev",         Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "mute_master",        Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_mic",           Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_program",       Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_active_window", Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_app_group",     Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_device",        Color.FromRgb(0xEF, 0x53, 0x50) },
        { "launch_exe",         Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "close_program",      Color.FromRgb(0xFF, 0x7C, 0x43) },
        { "cycle_output",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "cycle_input",        Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_output",      Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_input",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "macro",              Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "switch_profile",     Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "cycle_profile",     Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "cycle_brightness",   Color.FromRgb(0xFF, 0xF1, 0x76) },
        { "quick_wheel",        Color.FromRgb(0x00, 0xE6, 0x76) },
        { "power_sleep",        Color.FromRgb(0x7C, 0x8C, 0xF8) },
        { "power_lock",         Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "power_off",          Color.FromRgb(0xFF, 0x44, 0x44) },
        { "power_restart",      Color.FromRgb(0xFF, 0x8A, 0x3D) },
        { "power_logoff",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "power_hibernate",    Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "ha_toggle",          Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "ha_scene",           Color.FromRgb(0xFF, 0xA7, 0x26) },
        { "ha_service",         Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "govee_toggle",       Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "govee_color",        Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "obs_record",         Color.FromRgb(0xFF, 0x44, 0x44) },
        { "obs_stream",         Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "obs_scene",          Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "obs_mute",           Color.FromRgb(0xEF, 0x53, 0x50) },
        { "vm_mute_strip",      Color.FromRgb(0xFF, 0x8F, 0x00) },
        { "vm_mute_bus",        Color.FromRgb(0xFF, 0x8F, 0x00) },
    };

    // Clipboard for button copy/paste
    private static ButtonConfig? _buttonClipboard;

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    // Per-column header elements
    private readonly TextBox[] _headers = new TextBox[5];
    private readonly TextBlock[] _headerIcons = new TextBlock[5];
    private readonly TextBlock[] _headerActions = new TextBlock[5];
    private readonly Border[] _columnCards = new Border[5];

    // TAP controls
    private readonly ActionPicker[] _tapCombos = new ActionPicker[5];
    private readonly TextBox[] _tapPathBoxes = new TextBox[5];
    private readonly StackPanel[] _tapPathPanels = new StackPanel[5];
    private readonly TextBlock[] _tapPathLabels = new TextBlock[5];
    private readonly Button[] _tapBrowseButtons = new Button[5];
    private readonly Button[] _tapPickButtons = new Button[5];
    private readonly TextBox[] _tapMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _tapMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _tapDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _tapDevicePanels = new StackPanel[5];
    private readonly ListPicker[] _tapProfilePickers = new ListPicker[5];
    private readonly StackPanel[] _tapProfilePanels = new StackPanel[5];
    private readonly SegmentedControl[] _tapPowerSegments = new SegmentedControl[5];
    private readonly StackPanel[] _tapPowerPanels = new StackPanel[5];
    private readonly ListPicker[] _tapKnobPickers = new ListPicker[5];
    private readonly StackPanel[] _tapKnobPanels = new StackPanel[5];

    private readonly CheckListPicker[] _tapCycleDevicePickers = new CheckListPicker[5];
    private readonly StackPanel[] _tapCycleDevicePanels = new StackPanel[5];
    private readonly SegmentedControl[] _tapCycleTypePickers = new SegmentedControl[5];
    private readonly StackPanel[] _tapCycleTypePanels = new StackPanel[5];

    // DOUBLE controls
    private readonly ActionPicker[] _dblCombos = new ActionPicker[5];
    private readonly TextBox[] _dblPathBoxes = new TextBox[5];
    private readonly StackPanel[] _dblPathPanels = new StackPanel[5];
    private readonly TextBlock[] _dblPathLabels = new TextBlock[5];
    private readonly Button[] _dblBrowseButtons = new Button[5];
    private readonly Button[] _dblPickButtons = new Button[5];
    private readonly TextBox[] _dblMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _dblMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _dblDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _dblDevicePanels = new StackPanel[5];
    private readonly CheckListPicker[] _dblCycleDevicePickers = new CheckListPicker[5];
    private readonly StackPanel[] _dblCycleDevicePanels = new StackPanel[5];
    private readonly SegmentedControl[] _dblCycleTypePickers = new SegmentedControl[5];
    private readonly StackPanel[] _dblCycleTypePanels = new StackPanel[5];
    private readonly ListPicker[] _dblProfilePickers = new ListPicker[5];
    private readonly StackPanel[] _dblProfilePanels = new StackPanel[5];
    private readonly SegmentedControl[] _dblPowerSegments = new SegmentedControl[5];
    private readonly StackPanel[] _dblPowerPanels = new StackPanel[5];
    private readonly ListPicker[] _dblKnobPickers = new ListPicker[5];
    private readonly StackPanel[] _dblKnobPanels = new StackPanel[5];

    // HOLD controls
    private readonly ActionPicker[] _holdCombos = new ActionPicker[5];
    private readonly TextBox[] _holdPathBoxes = new TextBox[5];
    private readonly StackPanel[] _holdPathPanels = new StackPanel[5];
    private readonly TextBlock[] _holdPathLabels = new TextBlock[5];
    private readonly Button[] _holdBrowseButtons = new Button[5];
    private readonly Button[] _holdPickButtons = new Button[5];
    private readonly TextBox[] _holdMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _holdMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _holdDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _holdDevicePanels = new StackPanel[5];
    private readonly CheckListPicker[] _holdCycleDevicePickers = new CheckListPicker[5];
    private readonly StackPanel[] _holdCycleDevicePanels = new StackPanel[5];
    private readonly SegmentedControl[] _holdCycleTypePickers = new SegmentedControl[5];
    private readonly StackPanel[] _holdCycleTypePanels = new StackPanel[5];
    private readonly ListPicker[] _holdProfilePickers = new ListPicker[5];
    private readonly StackPanel[] _holdProfilePanels = new StackPanel[5];
    private readonly SegmentedControl[] _holdPowerSegments = new SegmentedControl[5];
    private readonly StackPanel[] _holdPowerPanels = new StackPanel[5];
    private readonly ListPicker[] _holdKnobPickers = new ListPicker[5];
    private readonly StackPanel[] _holdKnobPanels = new StackPanel[5];


    // Govee device pickers (tap/double/hold)
    private readonly ListPicker[] _tapGoveeDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _tapGoveeDevicePanels = new StackPanel[5];
    private readonly ListPicker[] _dblGoveeDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _dblGoveeDevicePanels = new StackPanel[5];
    private readonly ListPicker[] _holdGoveeDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _holdGoveeDevicePanels = new StackPanel[5];

    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();
    private HAIntegration? _ha;
    private AmbienceSync? _ambienceSync;

    public void SetAmbienceSync(AmbienceSync? sync) => _ambienceSync = sync;

    public ButtonsView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            CollectAndSave();
        };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildColumns();
        SetupColumnContextMenus();
    }

    private void SetupColumnContextMenus()
    {
        var menuBg = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C));
        var menuFg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var menuBorder = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var border = _columnCards[i];
            if (border == null) continue;

            var copyItem = new MenuItem
            {
                Header = "Copy Button",
                Foreground = menuFg,
                Background = menuBg,
            };
            var pasteItem = new MenuItem
            {
                Header = "Paste Button",
                Foreground = menuFg,
                Background = menuBg,
            };
            var resetItem = new MenuItem
            {
                Header = "Reset to Default",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
                Background = menuBg,
            };

            copyItem.Click += (_, _) =>
            {
                if (_config == null) return;
                var btn = _config.Buttons.FirstOrDefault(b => b.Idx == idx);
                if (btn == null) return;
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(btn);
                _buttonClipboard = Newtonsoft.Json.JsonConvert.DeserializeObject<ButtonConfig>(json);
            };

            pasteItem.Click += (_, _) =>
            {
                if (_buttonClipboard == null || _config == null || _onSave == null) return;
                var btn = _config.Buttons.FirstOrDefault(b => b.Idx == idx);
                if (btn == null) return;

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_buttonClipboard);
                var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<ButtonConfig>(json)!;
                copy.Idx = idx;

                // Apply all fields
                btn.Action = copy.Action; btn.Path = copy.Path; btn.MacroKeys = copy.MacroKeys;
                btn.DeviceId = copy.DeviceId; btn.DeviceIds = copy.DeviceIds ?? new List<string>();
                btn.CycleDeviceType = copy.CycleDeviceType;
                btn.ProfileName = copy.ProfileName; btn.PowerAction = copy.PowerAction;
                btn.LinkedKnobIdx = copy.LinkedKnobIdx;

                btn.DoublePressAction = copy.DoublePressAction; btn.DoublePressPath = copy.DoublePressPath;
                btn.DoublePressMacroKeys = copy.DoublePressMacroKeys; btn.DoublePressDeviceId = copy.DoublePressDeviceId;
                btn.DoublePressDeviceIds = copy.DoublePressDeviceIds ?? new List<string>();
                btn.DoublePressCycleDeviceType = copy.DoublePressCycleDeviceType;
                btn.DoublePressProfileName = copy.DoublePressProfileName; btn.DoublePressPowerAction = copy.DoublePressPowerAction;
                btn.DoublePressLinkedKnobIdx = copy.DoublePressLinkedKnobIdx;

                btn.HoldAction = copy.HoldAction; btn.HoldPath = copy.HoldPath; btn.HoldMacroKeys = copy.HoldMacroKeys;
                btn.HoldDeviceId = copy.HoldDeviceId; btn.HoldDeviceIds = copy.HoldDeviceIds ?? new List<string>();
                btn.HoldCycleDeviceType = copy.HoldCycleDeviceType;
                btn.HoldProfileName = copy.HoldProfileName; btn.HoldPowerAction = copy.HoldPowerAction;
                btn.HoldLinkedKnobIdx = copy.HoldLinkedKnobIdx;

                ReloadButtonColumn(idx, btn);
                QueueSave();
            };

            resetItem.Click += (_, _) =>
            {
                if (_config == null || _onSave == null) return;
                var btn = _config.Buttons.FirstOrDefault(b => b.Idx == idx);
                if (btn == null) return;

                btn.Action = "none"; btn.Path = ""; btn.MacroKeys = "";
                btn.DeviceId = ""; btn.DeviceIds = new List<string>();
                btn.CycleDeviceType = CycleDeviceType.Both;
                btn.ProfileName = ""; btn.PowerAction = ""; btn.LinkedKnobIdx = -1;

                btn.DoublePressAction = "none"; btn.DoublePressPath = ""; btn.DoublePressMacroKeys = "";
                btn.DoublePressDeviceId = ""; btn.DoublePressDeviceIds = new List<string>();
                btn.DoublePressCycleDeviceType = CycleDeviceType.Both;
                btn.DoublePressProfileName = ""; btn.DoublePressPowerAction = ""; btn.DoublePressLinkedKnobIdx = -1;

                btn.HoldAction = "none"; btn.HoldPath = ""; btn.HoldMacroKeys = "";
                btn.HoldDeviceId = ""; btn.HoldDeviceIds = new List<string>();
                btn.HoldCycleDeviceType = CycleDeviceType.Both;
                btn.HoldProfileName = ""; btn.HoldPowerAction = ""; btn.HoldLinkedKnobIdx = -1;

                ReloadButtonColumn(idx, btn);
                QueueSave();
            };

            var separator = new Separator
            {
                Background = menuBorder,
                Foreground = menuBorder,
                Margin = new Thickness(4, 2, 4, 2),
            };

            var contextMenu = new ContextMenu
            {
                Background = menuBg,
                BorderBrush = menuBorder,
                BorderThickness = new Thickness(1),
            };

            contextMenu.ContextMenuOpening += (_, _) =>
            {
                pasteItem.IsEnabled = _buttonClipboard != null;
                pasteItem.Opacity = _buttonClipboard != null ? 1.0 : 0.4;
            };

            contextMenu.Items.Add(copyItem);
            contextMenu.Items.Add(pasteItem);
            contextMenu.Items.Add(separator);
            contextMenu.Items.Add(resetItem);

            border.ContextMenu = contextMenu;
        }
    }

    private static string ExtractPathBoxValue(string action, string path)
    {
        // For govee_color, path is "ip|hexcolor" — only show hex in path box
        if (action == "govee_color" && path.Contains('|'))
            return path.Split('|', 2)[1];
        // For govee_toggle, path is just ip — no path box needed
        if (action is "govee_toggle")
            return "";
        return path;
    }

    private void ReloadButtonColumn(int idx, ButtonConfig btn)
    {
        _loading = true;

        // TAP
        SelectCombo(_tapCombos[idx], btn.Action);
        SetTextBoxValue(_tapPathBoxes[idx], ExtractPathBoxValue(btn.Action, btn.Path));
        SetTextBoxValue(_tapMacroBoxes[idx], btn.MacroKeys);
        SelectDevicePicker(_tapDevicePickers[idx], btn.DeviceId);
        _tapCycleDevicePickers[idx].SetCheckedIds(btn.DeviceIds);
        SelectCycleType(_tapCycleTypePickers[idx], btn.CycleDeviceType);
        SelectProfilePicker(_tapProfilePickers[idx], btn.ProfileName);
        SelectPowerSegment(_tapPowerSegments[idx], btn.PowerAction);
        SelectKnobPicker(_tapKnobPickers[idx], btn.LinkedKnobIdx);
        SelectHaSubTag(_tapCombos[idx], btn.Action, btn.Path);
        SelectDeviceSubTag(_tapCombos[idx], btn.Action, btn.DeviceId);
        SelectGoveeDevicePicker(_tapGoveeDevicePickers[idx], btn.Action, btn.Path);
        UpdateTapVisibility(idx, btn.Action);
        UpdateHeaderDisplay(idx);

        // DOUBLE
        SelectCombo(_dblCombos[idx], btn.DoublePressAction);
        SetTextBoxValue(_dblPathBoxes[idx], ExtractPathBoxValue(btn.DoublePressAction, btn.DoublePressPath));
        SetTextBoxValue(_dblMacroBoxes[idx], btn.DoublePressMacroKeys);
        SelectDevicePicker(_dblDevicePickers[idx], btn.DoublePressDeviceId);
        _dblCycleDevicePickers[idx].SetCheckedIds(btn.DoublePressDeviceIds);
        SelectCycleType(_dblCycleTypePickers[idx], btn.DoublePressCycleDeviceType);
        SelectProfilePicker(_dblProfilePickers[idx], btn.DoublePressProfileName);
        SelectPowerSegment(_dblPowerSegments[idx], btn.DoublePressPowerAction);
        SelectKnobPicker(_dblKnobPickers[idx], btn.DoublePressLinkedKnobIdx);
        SelectHaSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressPath);
        SelectDeviceSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressDeviceId);
        SelectGoveeDevicePicker(_dblGoveeDevicePickers[idx], btn.DoublePressAction, btn.DoublePressPath);
        UpdateGestureVisibility(_dblPathPanels[idx], _dblPathLabels[idx], _dblBrowseButtons[idx], _dblPickButtons[idx], _dblMacroPanels[idx],
            _dblDevicePanels[idx], _dblCycleDevicePanels[idx], _dblCycleDevicePickers[idx], _dblCycleTypePanels[idx], _dblProfilePanels[idx],
            _dblPowerPanels[idx], _dblKnobPanels[idx], _dblGoveeDevicePanels[idx], btn.DoublePressAction);

        // HOLD
        SelectCombo(_holdCombos[idx], btn.HoldAction);
        SetTextBoxValue(_holdPathBoxes[idx], ExtractPathBoxValue(btn.HoldAction, btn.HoldPath));
        SetTextBoxValue(_holdMacroBoxes[idx], btn.HoldMacroKeys);
        SelectDevicePicker(_holdDevicePickers[idx], btn.HoldDeviceId);
        _holdCycleDevicePickers[idx].SetCheckedIds(btn.HoldDeviceIds);
        SelectCycleType(_holdCycleTypePickers[idx], btn.HoldCycleDeviceType);
        SelectProfilePicker(_holdProfilePickers[idx], btn.HoldProfileName);
        SelectPowerSegment(_holdPowerSegments[idx], btn.HoldPowerAction);
        SelectKnobPicker(_holdKnobPickers[idx], btn.HoldLinkedKnobIdx);
        SelectHaSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldPath);
        SelectDeviceSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldDeviceId);
        SelectGoveeDevicePicker(_holdGoveeDevicePickers[idx], btn.HoldAction, btn.HoldPath);
        UpdateGestureVisibility(_holdPathPanels[idx], _holdPathLabels[idx], _holdBrowseButtons[idx], _holdPickButtons[idx], _holdMacroPanels[idx],
            _holdDevicePanels[idx], _holdCycleDevicePanels[idx], _holdCycleDevicePickers[idx], _holdCycleTypePanels[idx], _holdProfilePanels[idx],
            _holdPowerPanels[idx], _holdKnobPanels[idx], _holdGoveeDevicePanels[idx], btn.HoldAction);

        _loading = false;
    }

    public void LoadConfig(AppConfig config, AudioMixer mixer, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _mixer = mixer;
        _onSave = onSave;

        _audioDevices = mixer.GetAudioDevices();

        // Create/update HA client if enabled
        if (config.HomeAssistant.Enabled && !string.IsNullOrWhiteSpace(config.HomeAssistant.Token))
        {
            if (_ha == null)
                _ha = new HAIntegration(config.HomeAssistant);
            else
                _ha.UpdateConfig(config.HomeAssistant);
        }

        RebuildActionPickers(config);

        for (int i = 0; i < 5; i++)
        {
            PopulateDevicePicker(_tapDevicePickers[i]);
            PopulateCycleDevicePicker(_tapCycleDevicePickers[i]);
            PopulateProfilePicker(_tapProfilePickers[i], config);
            PopulateKnobPicker(_tapKnobPickers[i], config);
            PopulateGoveeDevicePicker(_tapGoveeDevicePickers[i], config);

            PopulateDevicePicker(_dblDevicePickers[i]);
            PopulateCycleDevicePicker(_dblCycleDevicePickers[i]);
            PopulateProfilePicker(_dblProfilePickers[i], config);
            PopulateKnobPicker(_dblKnobPickers[i], config);
            PopulateGoveeDevicePicker(_dblGoveeDevicePickers[i], config);

            PopulateDevicePicker(_holdDevicePickers[i]);
            PopulateCycleDevicePicker(_holdCycleDevicePickers[i]);
            PopulateProfilePicker(_holdProfilePickers[i], config);
            PopulateKnobPicker(_holdKnobPickers[i], config);
            PopulateGoveeDevicePicker(_holdGoveeDevicePickers[i], config);
        }

        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // Header label — use button label, fall back to knob label, then default
            if (!string.IsNullOrWhiteSpace(btn.Label))
            {
                _headers[i].Text = btn.Label;
                _headers[i].Foreground = FindBrush("TextSecBrush");
            }
            else
            {
                var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
                if (knob != null && !string.IsNullOrWhiteSpace(knob.Label))
                {
                    _headers[i].Text = knob.Label;
                    _headers[i].Foreground = FindBrush("TextSecBrush");
                }
                else
                {
                    _headers[i].Text = $"Button {i + 1}";
                    _headers[i].Foreground = FindBrush("TextDimBrush");
                }
            }

            // TAP
            SelectCombo(_tapCombos[i], btn.Action);
            SetTextBoxValue(_tapPathBoxes[i], ExtractPathBoxValue(btn.Action, btn.Path));
            SetTextBoxValue(_tapMacroBoxes[i], btn.MacroKeys);
            SelectDevicePicker(_tapDevicePickers[i], btn.DeviceId);
            _tapCycleDevicePickers[i].SetCheckedIds(btn.DeviceIds);
            SelectCycleType(_tapCycleTypePickers[i], btn.CycleDeviceType);
            SelectProfilePicker(_tapProfilePickers[i], btn.ProfileName);
            SelectPowerSegment(_tapPowerSegments[i], btn.PowerAction);
            SelectKnobPicker(_tapKnobPickers[i], btn.LinkedKnobIdx);
            SelectHaSubTag(_tapCombos[i], btn.Action, btn.Path);
            SelectDeviceSubTag(_tapCombos[i], btn.Action, btn.DeviceId);
            SelectGoveeDevicePicker(_tapGoveeDevicePickers[i], btn.Action, btn.Path);
            UpdateTapVisibility(i, btn.Action);
            UpdateHeaderDisplay(i);

            // DOUBLE
            SelectCombo(_dblCombos[i], btn.DoublePressAction);
            SetTextBoxValue(_dblPathBoxes[i], ExtractPathBoxValue(btn.DoublePressAction, btn.DoublePressPath));
            SetTextBoxValue(_dblMacroBoxes[i], btn.DoublePressMacroKeys);
            SelectDevicePicker(_dblDevicePickers[i], btn.DoublePressDeviceId);
            _dblCycleDevicePickers[i].SetCheckedIds(btn.DoublePressDeviceIds);
            SelectCycleType(_dblCycleTypePickers[i], btn.DoublePressCycleDeviceType);
            SelectProfilePicker(_dblProfilePickers[i], btn.DoublePressProfileName);
            SelectPowerSegment(_dblPowerSegments[i], btn.DoublePressPowerAction);
            SelectKnobPicker(_dblKnobPickers[i], btn.DoublePressLinkedKnobIdx);
            SelectHaSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressPath);
            SelectDeviceSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressDeviceId);
            SelectGoveeDevicePicker(_dblGoveeDevicePickers[i], btn.DoublePressAction, btn.DoublePressPath);
            UpdateGestureVisibility(_dblPathPanels[i], _dblPathLabels[i], _dblBrowseButtons[i], _dblPickButtons[i], _dblMacroPanels[i],
                _dblDevicePanels[i], _dblCycleDevicePanels[i], _dblCycleDevicePickers[i], _dblCycleTypePanels[i], _dblProfilePanels[i],
                _dblPowerPanels[i], _dblKnobPanels[i], _dblGoveeDevicePanels[i], btn.DoublePressAction);

            // HOLD
            SelectCombo(_holdCombos[i], btn.HoldAction);
            SetTextBoxValue(_holdPathBoxes[i], ExtractPathBoxValue(btn.HoldAction, btn.HoldPath));
            SetTextBoxValue(_holdMacroBoxes[i], btn.HoldMacroKeys);
            SelectDevicePicker(_holdDevicePickers[i], btn.HoldDeviceId);
            _holdCycleDevicePickers[i].SetCheckedIds(btn.HoldDeviceIds);
            SelectCycleType(_holdCycleTypePickers[i], btn.HoldCycleDeviceType);
            SelectProfilePicker(_holdProfilePickers[i], btn.HoldProfileName);
            SelectPowerSegment(_holdPowerSegments[i], btn.HoldPowerAction);
            SelectKnobPicker(_holdKnobPickers[i], btn.HoldLinkedKnobIdx);
            SelectHaSubTag(_holdCombos[i], btn.HoldAction, btn.HoldPath);
            SelectDeviceSubTag(_holdCombos[i], btn.HoldAction, btn.HoldDeviceId);
            SelectGoveeDevicePicker(_holdGoveeDevicePickers[i], btn.HoldAction, btn.HoldPath);
            UpdateGestureVisibility(_holdPathPanels[i], _holdPathLabels[i], _holdBrowseButtons[i], _holdPickButtons[i], _holdMacroPanels[i],
                _holdDevicePanels[i], _holdCycleDevicePanels[i], _holdCycleDevicePickers[i], _holdCycleTypePanels[i], _holdProfilePanels[i],
                _holdPowerPanels[i], _holdKnobPanels[i], _holdGoveeDevicePanels[i], btn.HoldAction);
        }

        _loading = false;

        // Fetch HA entities in background for sub-flyout menus
        if (_ha != null)
            _ = _ha.RefreshEntitiesAsync();
    }

    // ── Build 5 columns ─────────────────────────────────────────────
    // Uses Grid rows with SharedSizeGroup so DOUBLE/HOLD align across columns

    private void BuildColumns()
    {
        var grids = new[] { Btn0Grid, Btn1Grid, Btn2Grid, Btn3Grid, Btn4Grid };

        // Store card border references
        foreach (var child in ColumnsGrid.Children)
        {
            if (child is Border border && border.Child is Grid g)
            {
                int col = Grid.GetColumn(border);
                if (col >= 0 && col < 5)
                    _columnCards[col] = border;
            }
        }

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var grid = grids[i];

            // Define rows: Header | Sep | TAP | Sep | DOUBLE | Sep | HOLD
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Header" });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // sep
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Tap" });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // sep
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Double" });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // sep
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "Hold" });

            // ── Row 0: Header ──
            var headerStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };

            var headerBox = new TextBox
            {
                Text = $"Button {i + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("TextDimBrush"),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                CaretBrush = FindBrush("TextPrimaryBrush"),
                MaxLength = 20,
            };
            headerBox.GotFocus += (_, _) =>
            {
                headerBox.Foreground = FindBrush("TextPrimaryBrush");
                headerBox.Cursor = System.Windows.Input.Cursors.IBeam;
                if (headerBox.Text == $"Button {idx + 1}")
                    headerBox.SelectAll();
            };
            headerBox.LostFocus += (_, _) =>
            {
                headerBox.Cursor = System.Windows.Input.Cursors.Hand;
                var text = headerBox.Text.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    headerBox.Text = $"Button {idx + 1}";
                    headerBox.Foreground = FindBrush("TextDimBrush");
                }
                else
                {
                    headerBox.Foreground = FindBrush("TextSecBrush");
                }
                QueueSave();
            };
            headerBox.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    System.Windows.Input.Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            _headers[i] = headerBox;
            headerStack.Children.Add(headerBox);

            var headerIcon = new TextBlock
            {
                Text = "—",
                FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextDimBrush"),
            };
            _headerIcons[i] = headerIcon;
            headerStack.Children.Add(headerIcon);

            var headerAction = new TextBlock
            {
                Text = "None",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            _headerActions[i] = headerAction;
            headerStack.Children.Add(headerAction);

            Grid.SetRow(headerStack, 0);
            grid.Children.Add(headerStack);

            // ── Row 1: Sep ──
            var sep1 = MakeSeparator(12);
            Grid.SetRow(sep1, 1);
            grid.Children.Add(sep1);

            // ── Row 2: TAP section ──
            var tapSection = new StackPanel();
            tapSection.Children.Add(MakeGestureHeader("TAP"));

            var tapCombo = MakeActionCombo();
            tapCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(tapCombo);
                UpdateTapVisibility(idx, val);
                UpdateHeaderDisplay(idx);
                QueueSave();
            };
            _tapCombos[i] = tapCombo;
            tapSection.Children.Add(tapCombo);

            var (pathPanel, pathLabel, pathBox, browseBtn, pickBtn) = MakePathRow("PROCESS NAME", "discord");
            pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPathPanels[i] = pathPanel;
            _tapPathLabels[i] = pathLabel;
            _tapPathBoxes[i] = pathBox;
            _tapBrowseButtons[i] = browseBtn;
            _tapPickButtons[i] = pickBtn;
            browseBtn.Click += (_, _) => BrowseForFile(pathBox);
            pickBtn.Click += (_, _) => ShowProcessPicker(pickBtn, pathBox, GetComboActionValue(_tapCombos[idx]));
            tapSection.Children.Add(pathPanel);

            var (macroPanel, macroBox) = MakeTextBoxRow("MACRO KEYS", "ctrl+shift+m");
            macroBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapMacroPanels[i] = macroPanel;
            _tapMacroBoxes[i] = macroBox;
            tapSection.Children.Add(macroPanel);

            var (devicePanel, devicePicker) = MakeListPickerRow("DEVICE");
            devicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapDevicePanels[i] = devicePanel;
            _tapDevicePickers[i] = devicePicker;
            tapSection.Children.Add(devicePanel);

            var (tapCycleDevicePanel, tapCycleDevicePicker) = MakeCheckListPickerRow("CYCLE DEVICES");
            tapCycleDevicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapCycleDevicePanels[i] = tapCycleDevicePanel;
            _tapCycleDevicePickers[i] = tapCycleDevicePicker;
            tapSection.Children.Add(tapCycleDevicePanel);

            var (tapCycleTypePanel, tapCycleTypeSegment) = MakeCycleTypeRow();
            tapCycleTypeSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapCycleTypePanels[i] = tapCycleTypePanel;
            _tapCycleTypePickers[i] = tapCycleTypeSegment;
            tapSection.Children.Add(tapCycleTypePanel);

            var (profilePanel, profilePicker) = MakeListPickerRow("PROFILE");
            profilePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapProfilePanels[i] = profilePanel;
            _tapProfilePickers[i] = profilePicker;
            tapSection.Children.Add(profilePanel);

            var (powerPanel, powerSegment) = MakePowerSegmentRow("POWER");
            powerSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPowerPanels[i] = powerPanel;
            _tapPowerSegments[i] = powerSegment;
            tapSection.Children.Add(powerPanel);

            var (knobPanel, knobPicker) = MakeListPickerRow("LINKED KNOB");
            knobPicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapKnobPanels[i] = knobPanel;
            _tapKnobPickers[i] = knobPicker;
            tapSection.Children.Add(knobPanel);

            var (tapGoveePanel, tapGoveePicker) = MakeListPickerRow("GOVEE DEVICE");
            tapGoveePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapGoveeDevicePanels[i] = tapGoveePanel;
            _tapGoveeDevicePickers[i] = tapGoveePicker;
            tapSection.Children.Add(tapGoveePanel);

            Grid.SetRow(tapSection, 2);
            grid.Children.Add(tapSection);

            // ── Row 3: Sep ──
            var sep2 = MakeSeparator(14);
            Grid.SetRow(sep2, 3);
            grid.Children.Add(sep2);

            // ── Row 4: DOUBLE section ──
            var dblSection = new StackPanel();
            dblSection.Children.Add(MakeGestureHeader("DOUBLE"));

            var dblCombo = MakeActionCombo();
            _dblCombos[i] = dblCombo;
            dblSection.Children.Add(dblCombo);

            var (dblPathPanel, dblPathLabel, dblPathBox, dblBrowseBtn, dblPickBtn) = MakePathRow("PROCESS NAME", "discord");
            dblPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblPathPanels[i] = dblPathPanel;
            _dblPathLabels[i] = dblPathLabel;
            _dblPathBoxes[i] = dblPathBox;
            _dblBrowseButtons[i] = dblBrowseBtn;
            _dblPickButtons[i] = dblPickBtn;
            dblBrowseBtn.Click += (_, _) => BrowseForFile(dblPathBox);
            dblPickBtn.Click += (_, _) => ShowProcessPicker(dblPickBtn, dblPathBox, GetComboActionValue(_dblCombos[idx]));
            dblSection.Children.Add(dblPathPanel);

            var (dblMacroPanel, dblMacroBox) = MakeTextBoxRow("MACRO KEYS", "ctrl+shift+m");
            dblMacroBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblMacroPanels[i] = dblMacroPanel;
            _dblMacroBoxes[i] = dblMacroBox;
            dblSection.Children.Add(dblMacroPanel);

            var (dblDevicePanel, dblDevicePicker) = MakeListPickerRow("DEVICE");
            dblDevicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblDevicePanels[i] = dblDevicePanel;
            _dblDevicePickers[i] = dblDevicePicker;
            dblSection.Children.Add(dblDevicePanel);

            var (dblCycleDevicePanel, dblCycleDevicePicker) = MakeCheckListPickerRow("CYCLE DEVICES");
            dblCycleDevicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblCycleDevicePanels[i] = dblCycleDevicePanel;
            _dblCycleDevicePickers[i] = dblCycleDevicePicker;
            dblSection.Children.Add(dblCycleDevicePanel);

            var (dblCycleTypePanel, dblCycleTypeSegment) = MakeCycleTypeRow();
            dblCycleTypeSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblCycleTypePanels[i] = dblCycleTypePanel;
            _dblCycleTypePickers[i] = dblCycleTypeSegment;
            dblSection.Children.Add(dblCycleTypePanel);

            var (dblProfilePanel, dblProfilePicker) = MakeListPickerRow("PROFILE");
            dblProfilePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblProfilePanels[i] = dblProfilePanel;
            _dblProfilePickers[i] = dblProfilePicker;
            dblSection.Children.Add(dblProfilePanel);

            var (dblPowerPanel, dblPowerSegment) = MakePowerSegmentRow("POWER");
            dblPowerSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblPowerPanels[i] = dblPowerPanel;
            _dblPowerSegments[i] = dblPowerSegment;
            dblSection.Children.Add(dblPowerPanel);

            var (dblKnobPanel, dblKnobPicker) = MakeListPickerRow("LINKED KNOB");
            dblKnobPicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblKnobPanels[i] = dblKnobPanel;
            _dblKnobPickers[i] = dblKnobPicker;
            dblSection.Children.Add(dblKnobPanel);

            var (dblGoveePanel, dblGoveePicker) = MakeListPickerRow("GOVEE DEVICE");
            dblGoveePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblGoveeDevicePanels[i] = dblGoveePanel;
            _dblGoveeDevicePickers[i] = dblGoveePicker;
            dblSection.Children.Add(dblGoveePanel);

            dblCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(dblCombo);
                UpdateGestureVisibility(_dblPathPanels[idx], _dblPathLabels[idx], _dblBrowseButtons[idx], _dblPickButtons[idx], _dblMacroPanels[idx],
                    _dblDevicePanels[idx], _dblCycleDevicePanels[idx], _dblCycleDevicePickers[idx], _dblCycleTypePanels[idx], _dblProfilePanels[idx],
                    _dblPowerPanels[idx], _dblKnobPanels[idx], _dblGoveeDevicePanels[idx], val);
                QueueSave();
            };

            Grid.SetRow(dblSection, 4);
            grid.Children.Add(dblSection);

            // ── Row 5: Sep ──
            var sep3 = MakeSeparator(14);
            Grid.SetRow(sep3, 5);
            grid.Children.Add(sep3);

            // ── Row 6: HOLD section ──
            var holdSection = new StackPanel();
            holdSection.Children.Add(MakeGestureHeader("HOLD"));

            var holdCombo = MakeActionCombo();
            _holdCombos[i] = holdCombo;
            holdSection.Children.Add(holdCombo);

            var (holdPathPanel, holdPathLabel, holdPathBox, holdBrowseBtn, holdPickBtn) = MakePathRow("PROCESS NAME", "discord");
            holdPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdPathPanels[i] = holdPathPanel;
            _holdPathLabels[i] = holdPathLabel;
            _holdPathBoxes[i] = holdPathBox;
            _holdBrowseButtons[i] = holdBrowseBtn;
            _holdPickButtons[i] = holdPickBtn;
            holdBrowseBtn.Click += (_, _) => BrowseForFile(holdPathBox);
            holdPickBtn.Click += (_, _) => ShowProcessPicker(holdPickBtn, holdPathBox, GetComboActionValue(_holdCombos[idx]));
            holdSection.Children.Add(holdPathPanel);

            var (holdMacroPanel, holdMacroBox) = MakeTextBoxRow("MACRO KEYS", "ctrl+shift+m");
            holdMacroBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdMacroPanels[i] = holdMacroPanel;
            _holdMacroBoxes[i] = holdMacroBox;
            holdSection.Children.Add(holdMacroPanel);

            var (holdDevicePanel, holdDevicePicker) = MakeListPickerRow("DEVICE");
            holdDevicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdDevicePanels[i] = holdDevicePanel;
            _holdDevicePickers[i] = holdDevicePicker;
            holdSection.Children.Add(holdDevicePanel);

            var (holdCycleDevicePanel, holdCycleDevicePicker) = MakeCheckListPickerRow("CYCLE DEVICES");
            holdCycleDevicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdCycleDevicePanels[i] = holdCycleDevicePanel;
            _holdCycleDevicePickers[i] = holdCycleDevicePicker;
            holdSection.Children.Add(holdCycleDevicePanel);

            var (holdCycleTypePanel, holdCycleTypeSegment) = MakeCycleTypeRow();
            holdCycleTypeSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdCycleTypePanels[i] = holdCycleTypePanel;
            _holdCycleTypePickers[i] = holdCycleTypeSegment;
            holdSection.Children.Add(holdCycleTypePanel);

            var (holdProfilePanel, holdProfilePicker) = MakeListPickerRow("PROFILE");
            holdProfilePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdProfilePanels[i] = holdProfilePanel;
            _holdProfilePickers[i] = holdProfilePicker;
            holdSection.Children.Add(holdProfilePanel);

            var (holdPowerPanel, holdPowerSegment) = MakePowerSegmentRow("POWER");
            holdPowerSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdPowerPanels[i] = holdPowerPanel;
            _holdPowerSegments[i] = holdPowerSegment;
            holdSection.Children.Add(holdPowerPanel);

            var (holdKnobPanel, holdKnobPicker) = MakeListPickerRow("LINKED KNOB");
            holdKnobPicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdKnobPanels[i] = holdKnobPanel;
            _holdKnobPickers[i] = holdKnobPicker;
            holdSection.Children.Add(holdKnobPanel);

            var (holdGoveePanel, holdGoveePicker) = MakeListPickerRow("GOVEE DEVICE");
            holdGoveePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdGoveeDevicePanels[i] = holdGoveePanel;
            _holdGoveeDevicePickers[i] = holdGoveePicker;
            holdSection.Children.Add(holdGoveePanel);

            holdCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(holdCombo);
                UpdateGestureVisibility(_holdPathPanels[idx], _holdPathLabels[idx], _holdBrowseButtons[idx], _holdPickButtons[idx], _holdMacroPanels[idx],
                    _holdDevicePanels[idx], _holdCycleDevicePanels[idx], _holdCycleDevicePickers[idx], _holdCycleTypePanels[idx], _holdProfilePanels[idx],
                    _holdPowerPanels[idx], _holdKnobPanels[idx], _holdGoveeDevicePanels[idx], val);
                QueueSave();
            };

            Grid.SetRow(holdSection, 6);
            grid.Children.Add(holdSection);
        }
    }

    // ── Header display update ──────────────────────────────────────

    private void UpdateHeaderDisplay(int idx)
    {
        var action = GetComboActionValue(_tapCombos[idx]);
        var c = ActionColors.GetValueOrDefault(action, Color.FromRgb(0x44, 0x44, 0x44));
        var icon = ActionIcons.GetValueOrDefault(action, "—");
        var display = GetActionDisplay(action);

        _headerIcons[idx].Text = icon;
        _headerActions[idx].Text = display;

        if (action == "none")
        {
            _headerIcons[idx].Foreground = FindBrush("TextDimBrush");
            _headerActions[idx].Foreground = FindBrush("TextDimBrush");
        }
        else
        {
            _headerIcons[idx].Foreground = new SolidColorBrush(c);
            _headerActions[idx].Foreground = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B));
        }
    }

    private static string GetActionDisplay(string actionValue)
    {
        foreach (var (display, value) in Actions)
            if (value == actionValue) return display;
        return "None";
    }

    // ── Visibility ──────────────────────────────────────────────────

    private void UpdateTapVisibility(int idx, string action)
    {
        bool isHaServiceAction = action == "ha_service";
        bool isGoveeAction = action is "govee_toggle" or "govee_color";
        bool isObsPathAction = action is "obs_scene" or "obs_mute";
        bool isVmPathAction = action is "vm_mute_strip" or "vm_mute_bus";
        _tapPathPanels[idx].Visibility = PathActions.Contains(action) || isHaServiceAction || action == "govee_color" || isObsPathAction || isVmPathAction ? Visibility.Visible : Visibility.Collapsed;
        ApplyPathLabelAndButtons(_tapPathLabels[idx], _tapPathBoxes[idx], _tapBrowseButtons[idx], _tapPickButtons[idx], action);
        _tapMacroPanels[idx].Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        _tapDevicePanels[idx].Visibility = Visibility.Collapsed; // select/mute now use sub-flyout
        _tapCycleDevicePanels[idx].Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        _tapCycleTypePanels[idx].Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        if (action is "cycle_output" or "cycle_input")
            PopulateCycleDevicePicker(_tapCycleDevicePickers[idx], action == "cycle_output");
        _tapProfilePanels[idx].Visibility = action is "switch_profile" or "cycle_profile" ? Visibility.Visible : Visibility.Collapsed;
        _tapPowerPanels[idx].Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        _tapKnobPanels[idx].Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
        _tapGoveeDevicePanels[idx].Visibility = isGoveeAction ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateGestureVisibility(
        StackPanel pathPanel, TextBlock pathLabel, Button browseBtn, Button pickBtn, StackPanel macroPanel,
        StackPanel devicePanel, StackPanel cycleDevicePanel, CheckListPicker cycleDevicePicker, StackPanel cycleTypePanel,
        StackPanel profilePanel, StackPanel powerPanel, StackPanel knobPanel, StackPanel goveeDevicePanel, string action)
    {
        bool isHaServiceAction = action == "ha_service";
        bool isGoveeAction = action is "govee_toggle" or "govee_color";
        bool isObsPathAction = action is "obs_scene" or "obs_mute";
        bool isVmPathAction = action is "vm_mute_strip" or "vm_mute_bus";
        pathPanel.Visibility = PathActions.Contains(action) || isHaServiceAction || action == "govee_color" || isObsPathAction || isVmPathAction ? Visibility.Visible : Visibility.Collapsed;
        ApplyPathLabelAndButtons(pathLabel, null, browseBtn, pickBtn, action);
        macroPanel.Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        devicePanel.Visibility = Visibility.Collapsed; // select/mute now use sub-flyout
        cycleDevicePanel.Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        cycleTypePanel.Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        if (action is "cycle_output" or "cycle_input")
            PopulateCycleDevicePicker(cycleDevicePicker, action == "cycle_output");
        profilePanel.Visibility = action is "switch_profile" or "cycle_profile" ? Visibility.Visible : Visibility.Collapsed;
        powerPanel.Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        knobPanel.Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
        goveeDevicePanel.Visibility = isGoveeAction ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ApplyPathLabelAndButtons(TextBlock label, TextBox? box, Button browseBtn, Button pickBtn, string action)
    {
        switch (action)
        {
            case "mute_program":
                label.Text = "PROCESS NAME";
                if (box != null) box.ToolTip = "Enter part of the process name (e.g. discord, spotify). No full path needed.";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Visible;
                break;
            case "close_program":
                label.Text = "PROCESS NAME";
                if (box != null) box.ToolTip = "Enter part of the process name to close (e.g. notepad, chrome)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Visible;
                break;
            case "launch_exe":
                label.Text = "PROGRAM PATH";
                if (box != null) box.ToolTip = "Full path to the executable to launch, or use the app picker";
                browseBtn.Visibility = Visibility.Visible;
                pickBtn.Visibility = Visibility.Visible;
                break;
            case "ha_service":
                label.Text = "SERVICE CALL";
                if (box != null) box.ToolTip = "Format: domain.service:entity_id (e.g. light.turn_on:light.office)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            case "govee_color":
                label.Text = "HEX COLOR";
                if (box != null) box.ToolTip = "Hex color to set (e.g. FF0080 for pink, 00FF00 for green)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            case "obs_scene":
                label.Text = "SCENE NAME";
                if (box != null) box.ToolTip = "OBS scene name to switch to (e.g. Gaming, Webcam)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            case "obs_mute":
                label.Text = "SOURCE NAME";
                if (box != null) box.ToolTip = "OBS audio source name to toggle mute (e.g. Mic/Aux, Desktop Audio)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            case "vm_mute_strip":
                label.Text = "STRIP INDEX";
                if (box != null) box.ToolTip = "VoiceMeeter strip index (0-4)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            case "vm_mute_bus":
                label.Text = "BUS INDEX";
                if (box != null) box.ToolTip = "VoiceMeeter bus index (0-2)";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            default:
                label.Text = "PROCESS NAME";
                if (box != null) box.ToolTip = "Enter part of the process name";
                browseBtn.Visibility = Visibility.Collapsed;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
        }
    }

    // ── Collect and save ────────────────────────────────────────────

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null) return;

        for (int i = 0; i < 5; i++)
        {
            var btn = _config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // Save button label (empty string = default "Button N")
            var labelText = _headers[i].Text.Trim();
            btn.Label = labelText == $"Button {i + 1}" ? "" : labelText;

            btn.Action = GetComboActionValue(_tapCombos[i]);
            btn.Path = GetActionPath(_tapCombos[i], _tapGoveeDevicePickers[i], _tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetDeviceIdForAction(btn.Action, _tapCombos[i], _tapDevicePickers[i]);
            btn.DeviceIds = _tapCycleDevicePickers[i].GetCheckedIds();
            btn.CycleDeviceType = _tapCycleTypePickers[i].SelectedTag is CycleDeviceType t ? t : CycleDeviceType.Both;
            btn.ProfileName = _tapProfilePickers[i].SelectedTag as string ?? "";
            btn.PowerAction = GetSelectedPowerValue(_tapPowerSegments[i]);
            btn.LinkedKnobIdx = int.TryParse(_tapKnobPickers[i].SelectedTag as string, out int ki) ? ki : -1;

            btn.DoublePressAction = GetComboActionValue(_dblCombos[i]);
            btn.DoublePressPath = GetActionPath(_dblCombos[i], _dblGoveeDevicePickers[i], _dblPathBoxes[i]);
            btn.DoublePressMacroKeys = GetTextBoxValue(_dblMacroBoxes[i]);
            btn.DoublePressDeviceId = GetDeviceIdForAction(btn.DoublePressAction, _dblCombos[i], _dblDevicePickers[i]);
            btn.DoublePressDeviceIds = _dblCycleDevicePickers[i].GetCheckedIds();
            btn.DoublePressCycleDeviceType = _dblCycleTypePickers[i].SelectedTag is CycleDeviceType dt ? dt : CycleDeviceType.Both;
            btn.DoublePressProfileName = _dblProfilePickers[i].SelectedTag as string ?? "";
            btn.DoublePressPowerAction = GetSelectedPowerValue(_dblPowerSegments[i]);
            btn.DoublePressLinkedKnobIdx = int.TryParse(_dblKnobPickers[i].SelectedTag as string, out int dki) ? dki : -1;

            btn.HoldAction = GetComboActionValue(_holdCombos[i]);
            btn.HoldPath = GetActionPath(_holdCombos[i], _holdGoveeDevicePickers[i], _holdPathBoxes[i]);
            btn.HoldMacroKeys = GetTextBoxValue(_holdMacroBoxes[i]);
            btn.HoldDeviceId = GetDeviceIdForAction(btn.HoldAction, _holdCombos[i], _holdDevicePickers[i]);
            btn.HoldDeviceIds = _holdCycleDevicePickers[i].GetCheckedIds();
            btn.HoldCycleDeviceType = _holdCycleTypePickers[i].SelectedTag is CycleDeviceType ht ? ht : CycleDeviceType.Both;
            btn.HoldProfileName = _holdProfilePickers[i].SelectedTag as string ?? "";
            btn.HoldPowerAction = GetSelectedPowerValue(_holdPowerSegments[i]);
            btn.HoldLinkedKnobIdx = int.TryParse(_holdKnobPickers[i].SelectedTag as string, out int hki) ? hki : -1;
        }

        _onSave(_config);
    }

    // ── Control factories ───────────────────────────────────────────

    private static readonly Dictionary<string, string> GestureTooltips = new()
    {
        { "TAP", "Single press — released within 500ms" },
        { "DOUBLE", "Press twice quickly within 300ms" },
        { "HOLD", "Hold for 500ms+ (fires while held)" },
    };

    private Grid MakeGestureHeader(string title)
    {
        var accent = ThemeManager.Accent;
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bar = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 8, 1),
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        var label = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            ToolTip = GestureTooltips.GetValueOrDefault(title, title),
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        _sectionHeaders.Add((bar, label));
        return grid;
    }

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            bar.Background = new SolidColorBrush(accent);
            label.Foreground = new SolidColorBrush(accent);
        }

        for (int i = 0; i < 5; i++)
        {
            _tapDevicePickers[i].RefreshAccent();
            _tapProfilePickers[i].RefreshAccent();
            _tapKnobPickers[i].RefreshAccent();
            _tapGoveeDevicePickers[i].RefreshAccent();
            _tapPowerSegments[i].AccentColor = accent;
            _tapCycleDevicePickers[i].AccentColor = accent;

            _dblDevicePickers[i].RefreshAccent();
            _dblProfilePickers[i].RefreshAccent();
            _dblKnobPickers[i].RefreshAccent();
            _dblGoveeDevicePickers[i].RefreshAccent();
            _dblPowerSegments[i].AccentColor = accent;
            _dblCycleDevicePickers[i].AccentColor = accent;

            _holdDevicePickers[i].RefreshAccent();
            _holdProfilePickers[i].RefreshAccent();
            _holdKnobPickers[i].RefreshAccent();
            _holdGoveeDevicePickers[i].RefreshAccent();
            _holdPowerSegments[i].AccentColor = accent;
            _holdCycleDevicePickers[i].AccentColor = accent;
        }
    }

    private Border MakeSeparator(int spacing = 10)
    {
        return new Border
        {
            Height = 1,
            Background = FindBrush("CardBorderBrush"),
            Margin = new Thickness(0, spacing, 0, spacing),
        };
    }

    private static readonly Dictionary<string, string> ActionTooltips = new()
    {
        { "none",               "Do nothing when pressed" },
        { "media_play_pause",   "Play or pause media" },
        { "media_next",         "Skip to next track" },
        { "media_prev",         "Go back to previous track" },
        { "mute_master",        "Toggle system-wide mute" },
        { "mute_mic",           "Toggle microphone mute" },
        { "mute_program",       "Toggle mute for a specific app" },
        { "mute_active_window", "Toggle mute for focused window" },
        { "mute_app_group",     "Toggle mute for all apps in linked knob group" },
        { "mute_device",        "Toggle mute for a specific audio device" },
        { "launch_exe",         "Launch an application or script" },
        { "close_program",      "Force-close a running process" },
        { "cycle_output",       "Switch to next output device" },
        { "cycle_input",        "Switch to next input device" },
        { "select_output",      "Switch to a specific output device" },
        { "select_input",       "Switch to a specific input device" },
        { "macro",              "Send a keyboard shortcut (e.g. ctrl+shift+m)" },
        { "switch_profile",     "Load a different AmpUp profile" },
        { "cycle_profile",      "Cycle through selected profiles" },
        { "cycle_brightness",   "Step through LED brightness levels" },
        { "quick_wheel",        "Hold to open radial profile picker" },
        { "power_sleep",        "Put the PC to sleep" },
        { "power_lock",         "Lock the workstation" },
        { "power_off",          "Shut down the PC" },
        { "power_restart",      "Restart the PC" },
        { "power_logoff",       "Log off the current user" },
        { "power_hibernate",    "Hibernate the PC" },
        { "ha_toggle",          "Toggle a Home Assistant entity on/off" },
        { "ha_scene",           "Activate a Home Assistant scene" },
        { "ha_service",         "Call any Home Assistant service (format: domain.service:entity_id)" },
        { "govee_toggle",       "Toggle a Govee device on/off via LAN" },
        { "govee_color",        "Set a Govee device to a specific color (enter hex in path, e.g. FF0080)" },
        { "obs_record",         "Toggle OBS Studio recording on/off" },
        { "obs_stream",         "Toggle OBS Studio streaming on/off" },
        { "obs_scene",          "Switch to an OBS scene (enter scene name in path)" },
        { "obs_mute",           "Toggle mute on an OBS audio source (enter source name in path)" },
        { "vm_mute_strip",      "Toggle mute on a VoiceMeeter strip (enter strip index 0-4 in path)" },
        { "vm_mute_bus",        "Toggle mute on a VoiceMeeter bus (enter bus index 0-2 in path)" },
    };

    private void RebuildActionPickers(AppConfig config)
    {
        bool haEnabled = config.HomeAssistant.Enabled;
        bool goveeEnabled = config.Ambience.GoveeEnabled && config.Ambience.GoveeDevices.Count > 0;

        // Check if any button currently has an HA action configured (so we can still show it even when disabled)
        bool anyHaConfigured = config.Buttons.Any(b =>
            IsHaAction(b.Action) || IsHaAction(b.DoublePressAction) || IsHaAction(b.HoldAction));

        // Check if any button currently has a Govee action configured
        bool anyGoveeConfigured = config.Buttons.Any(b =>
            IsGoveeAction(b.Action) || IsGoveeAction(b.DoublePressAction) || IsGoveeAction(b.HoldAction));

        bool obsEnabled = config.Obs.Enabled;
        bool anyObsConfigured = config.Buttons.Any(b =>
            IsObsAction(b.Action) || IsObsAction(b.DoublePressAction) || IsObsAction(b.HoldAction));

        bool vmEnabled = config.VoiceMeeter.Enabled;
        bool anyVmConfigured = config.Buttons.Any(b =>
            IsVmAction(b.Action) || IsVmAction(b.DoublePressAction) || IsVmAction(b.HoldAction));

        for (int i = 0; i < 5; i++)
        {
            PopulateActionPicker(_tapCombos[i], haEnabled, anyHaConfigured, goveeEnabled, anyGoveeConfigured, obsEnabled, anyObsConfigured, vmEnabled, anyVmConfigured);
            PopulateActionPicker(_dblCombos[i], haEnabled, anyHaConfigured, goveeEnabled, anyGoveeConfigured, obsEnabled, anyObsConfigured, vmEnabled, anyVmConfigured);
            PopulateActionPicker(_holdCombos[i], haEnabled, anyHaConfigured, goveeEnabled, anyGoveeConfigured, obsEnabled, anyObsConfigured, vmEnabled, anyVmConfigured);
        }

        // Register sub-flyout providers
        for (int i = 0; i < 5; i++)
        {
            foreach (var picker in new[] { _tapCombos[i], _dblCombos[i], _holdCombos[i] })
            {
                // HA entity sub-menus
                if (haEnabled || anyHaConfigured)
                {
                    picker.RegisterSubMenu("ha_toggle", () => GetHASubItems("ha_toggle"));
                    picker.RegisterSubMenu("ha_scene", () => GetHASubItems("ha_scene"));
                }

                // Device sub-menus (filtered by direction)
                picker.RegisterSubMenu("select_output", () => GetDeviceSubItems(isOutput: true));
                picker.RegisterSubMenu("select_input", () => GetDeviceSubItems(isOutput: false));
                picker.RegisterSubMenu("mute_device", () => GetDeviceSubItems(isOutput: true));
            }
        }
    }

    private static bool IsHaAction(string? action)
        => action is "ha_toggle" or "ha_scene" or "ha_service";

    private static bool IsGoveeAction(string? action)
        => action is "govee_toggle" or "govee_color";

    private static bool IsObsAction(string? action)
        => action is "obs_record" or "obs_stream" or "obs_scene" or "obs_mute";

    private static bool IsVmAction(string? action)
        => action is "vm_mute_strip" or "vm_mute_bus";

    // Category groupings for the action picker
    private static readonly (string Category, string[] Values)[] ActionCategories =
    {
        ("Media",           new[] { "none", "media_play_pause", "media_next", "media_prev" }),
        ("Mute",            new[] { "mute_master", "mute_mic", "mute_program", "mute_active_window", "mute_app_group", "mute_device" }),
        ("App Control",     new[] { "launch_exe", "close_program" }),
        ("Device",          new[] { "cycle_output", "cycle_input", "select_output", "select_input" }),
        ("System",          new[] { "macro", "switch_profile", "cycle_profile", "cycle_brightness", "quick_wheel" }),
        ("Power",           new[] { "power_sleep", "power_lock", "power_off", "power_restart", "power_logoff", "power_hibernate" }),
        ("Integrations",    new[] { "ha_toggle", "ha_scene", "ha_service", "govee_toggle", "govee_color", "obs_record", "obs_stream", "obs_scene", "obs_mute", "vm_mute_strip", "vm_mute_bus" }),
    };

    private static readonly Dictionary<string, (string Display, string Value)> ActionLookup =
        Actions.ToDictionary(a => a.Value, a => a);

    private void PopulateActionPicker(ActionPicker picker, bool haEnabled, bool anyHaConfigured, bool goveeEnabled, bool anyGoveeConfigured, bool obsEnabled = false, bool anyObsConfigured = false, bool vmEnabled = false, bool anyVmConfigured = false)
    {
        picker.ClearItems();

        foreach (var (category, values) in ActionCategories)
        {
            bool anyAdded = false;

            foreach (var value in values)
            {
                if (!ActionLookup.TryGetValue(value, out var action))
                    continue;

                bool isHa = IsHaAction(value);
                bool isGovee = IsGoveeAction(value);
                bool isObs = IsObsAction(value);
                bool isVm = IsVmAction(value);

                if (isHa && !haEnabled && !anyHaConfigured) continue;
                if (isGovee && !goveeEnabled && !anyGoveeConfigured) continue;
                if (isObs && !obsEnabled && !anyObsConfigured) continue;
                if (isVm && !vmEnabled && !anyVmConfigured) continue;

                if (!anyAdded) { picker.AddCategory(category); anyAdded = true; }

                string displayName = action.Display;
                if (isHa && !haEnabled) displayName = $"{action.Display} (HA disabled)";
                if (isGovee && !goveeEnabled) displayName = $"{action.Display} (Govee disabled)";
                if (isObs && !obsEnabled) displayName = $"{action.Display} (OBS disabled)";
                if (isVm && !vmEnabled) displayName = $"{action.Display} (VM disabled)";

                var icon = ActionIcons.GetValueOrDefault(value, "—");
                var color = (isHa && !haEnabled) || (isGovee && !goveeEnabled) || (isObs && !obsEnabled) || (isVm && !vmEnabled)
                    ? Color.FromRgb(0x55, 0x55, 0x55)
                    : ActionColors.GetValueOrDefault(value, Color.FromRgb(0x88, 0x88, 0x88));
                var tooltip = ActionTooltips.GetValueOrDefault(value, action.Display);
                picker.AddItem(displayName, value, icon, color, tooltip);
            }
        }

        picker.BuildPopup();
    }

    private ActionPicker MakeActionCombo()
    {
        var picker = new ActionPicker
        {
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        foreach (var (category, values) in ActionCategories)
        {
            picker.AddCategory(category);
            foreach (var value in values)
            {
                if (!ActionLookup.TryGetValue(value, out var action)) continue;
                var icon = ActionIcons.GetValueOrDefault(value, "—");
                var color = ActionColors.GetValueOrDefault(value, Color.FromRgb(0x88, 0x88, 0x88));
                var tooltip = ActionTooltips.GetValueOrDefault(value, action.Display);
                picker.AddItem(action.Display, value, icon, color, tooltip);
            }
        }

        picker.BuildPopup();
        picker.Select("none");
        return picker;
    }

    private static void SelectCombo(ActionPicker picker, string actionValue) => picker.Select(actionValue);

    private static string GetComboActionValue(ActionPicker picker) => picker.SelectedValue;

    private (StackPanel panel, TextBox box) MakeTextBoxRow(string label, string placeholder)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));
        var box = new TextBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 11,
            ToolTip = "Enter keyboard shortcut (e.g. ctrl+shift+m)",
        };
        box.Tag = placeholder;
        box.Text = "";
        SetPlaceholder(box);
        container.Children.Add(box);
        return (container, box);
    }

    private (StackPanel panel, TextBlock labelBlock, TextBox box, Button browseBtn, Button pickBtn) MakePathRow(string label, string placeholder)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        var labelBlock = MakeLabel(label);
        container.Children.Add(labelBlock);

        var row = new DockPanel { LastChildFill = true };

        // Browse button with folder icon
        var browseBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        var browseHoverBg = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
        browseBg.Freeze(); browseHoverBg.Freeze();
        var browseBtn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.FolderOpen, Width = 16, Height = 16, Foreground = FindBrush("TextPrimaryBrush") },
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = browseBg,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Browse for file",
        };
        browseBtn.Resources[SystemParameters.FocusVisualStyleKey] = null;
        var browseBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(6),
            Background = browseBg,
            Margin = new Thickness(4, 0, 0, 0),
            Child = browseBtn,
        };
        browseBorder.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = browseBtn });
        browseBtn.MouseEnter += (_, _) => browseBorder.Background = browseHoverBg;
        browseBtn.MouseLeave += (_, _) => browseBorder.Background = browseBg;
        DockPanel.SetDock(browseBorder, Dock.Right);
        row.Children.Add(browseBorder);

        // Pick (process list) button with list icon
        var pickBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        var pickHoverBg = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
        pickBg.Freeze(); pickHoverBg.Freeze();
        var pickBtn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.FormatListBulleted, Width = 16, Height = 16, Foreground = FindBrush("TextSecBrush") },
            Width = 34,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            Background = pickBg,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Pick from running processes",
        };
        pickBtn.Resources[SystemParameters.FocusVisualStyleKey] = null;
        var pickBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(6),
            Background = pickBg,
            Margin = new Thickness(4, 0, 0, 0),
            Child = pickBtn,
        };
        pickBorder.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = pickBtn });
        pickBtn.MouseEnter += (_, _) => pickBorder.Background = pickHoverBg;
        pickBtn.MouseLeave += (_, _) => pickBorder.Background = pickBg;
        DockPanel.SetDock(pickBorder, Dock.Right);
        row.Children.Add(pickBorder);

        // Text input with rounded border wrapper
        var inputBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
        };
        var box = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11,
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Enter part of the process name (e.g. discord, spotify). No full path needed.",
        };
        box.Tag = placeholder;
        box.Text = "";
        SetPlaceholder(box);
        inputBorder.Child = box;
        row.Children.Add(inputBorder);

        container.Children.Add(row);
        return (container, labelBlock, box, browseBtn, pickBtn);
    }

    private void BrowseForFile(TextBox targetBox)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executables (*.exe)|*.exe|Batch Files (*.bat)|*.bat|All Files (*.*)|*.*",
            FilterIndex = 1,
        };

        // Try to start in Program Files if no existing path
        var current = GetTextBoxValue(targetBox);
        if (!string.IsNullOrEmpty(current) && System.IO.Path.GetDirectoryName(current) is string dir && System.IO.Directory.Exists(dir))
            dlg.InitialDirectory = dir;
        else
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (dlg.ShowDialog() == true)
        {
            targetBox.Text = dlg.FileName;
            targetBox.Foreground = FindBrush("TextPrimaryBrush");
            QueueSave();
        }
    }

    private void ShowProcessPicker(Button anchor, TextBox targetBox, string action)
    {
        // launch_exe: show the app picker dialog instead
        if (action == "launch_exe")
        {
            var picker = new AppPickerDialog { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedPath))
            {
                targetBox.Text = picker.SelectedPath;
                targetBox.Foreground = FindBrush("TextPrimaryBrush");
                QueueSave();
            }
            return;
        }

        if (_mixer == null) return;

        List<string> processes;
        if (action == "mute_program")
        {
            // Only show processes with active audio sessions
            processes = _mixer.GetRunningAudioApps();
        }
        else
        {
            // close_program: show all running processes (deduplicated, sorted)
            processes = System.Diagnostics.Process.GetProcesses()
                .Select(p => { try { return p.ProcessName; } catch { return null!; } })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (processes.Count == 0)
        {
            var tip = new System.Windows.Controls.ToolTip { Content = "No processes found", IsOpen = true };
            anchor.ToolTip = tip;
            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1500);
                tip.IsOpen = false;
            });
            return;
        }

        var scroll = new ScrollViewer
        {
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var panel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            MinWidth = 160,
        };

        var border = new System.Windows.Controls.Border
        {
            Child = scroll,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
        };
        scroll.Content = panel;

        Window? procFlyout = null;
        Action closeProcFlyout = () => { procFlyout?.Close(); procFlyout = null; };

        foreach (var proc in processes)
        {
            var procCapture = proc;
            var item = new Button
            {
                Content = proc,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 5, 10, 5),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Cursor = Cursors.Hand,
            };
            item.MouseEnter += (_, _) => item.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0xE6, 0x76));
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            item.Click += (_, _) =>
            {
                targetBox.Text = procCapture;
                targetBox.Foreground = FindBrush("TextPrimaryBrush");
                closeProcFlyout();
                QueueSave();
            };
            panel.Children.Add(item);
        }

        var screenPos = anchor.PointToScreen(new Point(0, anchor.ActualHeight + 2));
        var dpiSource = PresentationSource.FromVisual(anchor);
        if (dpiSource?.CompositionTarget != null)
        {
            var dpiX = dpiSource.CompositionTarget.TransformToDevice.M11;
            var dpiY = dpiSource.CompositionTarget.TransformToDevice.M22;
            screenPos = new Point(screenPos.X / dpiX, screenPos.Y / dpiY);
        }
        procFlyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
            Content = border,
            Left = screenPos.X,
            Top = screenPos.Y
        };
        procFlyout.Deactivated += (_, _) => closeProcFlyout();
        procFlyout.KeyDown += (_, e2) => { if (e2.Key == Key.Escape) closeProcFlyout(); };
        procFlyout.Show();
    }

    private static readonly Dictionary<string, string> PickerTooltips = new()
    {
        { "DEVICE",       "Audio device to target" },
        { "CYCLE DEVICES","Devices to cycle through (check to include)" },
        { "PROFILE",      "Profile to switch to when pressed" },
        { "LINKED KNOB",  "Knob whose app group is muted" },
        { "HOME ASSISTANT ENTITY",    "Home Assistant entity to control (click to load entities)" },
        { "GOVEE DEVICE", "Govee LAN device to control" },
    };

    private (StackPanel panel, ListPicker picker) MakeListPickerRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));
        var picker = new ListPicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = PickerTooltips.GetValueOrDefault(label, ""),
        };
        container.Children.Add(picker);
        return (container, picker);
    }

    private (StackPanel panel, CheckListPicker picker) MakeCheckListPickerRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));
        var picker = new CheckListPicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = PickerTooltips.GetValueOrDefault(label, ""),
        };
        container.Children.Add(picker);
        return (container, picker);
    }

    private void PopulateCycleDevicePicker(CheckListPicker picker, bool? outputOnly = null)
    {
        picker.ClearItems();
        foreach (var (id, name, isOutput) in _audioDevices)
        {
            if (outputOnly.HasValue && isOutput != outputOnly.Value) continue;
            picker.AddItem(name, id);
        }
    }

    private (StackPanel panel, SegmentedControl segment) MakePowerSegmentRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));
        var segment = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Sleep: pause PC | Lock: lock screen | Off: shutdown | Restart | Logoff | Hibernate",
        };
        foreach (var (display, value) in PowerOptions)
            segment.AddSegment(display, value);
        container.Children.Add(segment);
        return (container, segment);
    }

    private (StackPanel panel, SegmentedControl segment) MakeCycleTypeRow()
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel("DEVICE TYPE"));
        var segment = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Media: cycle media devices only | Comms: cycle communications devices only | Both: cycle all roles",
        };
        segment.AddSegment("Media", CycleDeviceType.Media);
        segment.AddSegment("Comms", CycleDeviceType.Communications);
        segment.AddSegment("Both", CycleDeviceType.Both);
        segment.SelectedIndex = 2; // Default to Both
        container.Children.Add(segment);
        return (container, segment);
    }

    private TextBlock MakeLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 4, 0, 3),
        };
    }

    // ── Placeholder simulation ──────────────────────────────────────

    private void SetPlaceholder(TextBox box)
    {
        var placeholder = box.Tag as string ?? "";
        var dimBrush = FindBrush("TextDimBrush");
        var primaryBrush = FindBrush("TextPrimaryBrush");

        box.GotFocus += (_, _) =>
        {
            if (box.Text == placeholder && Equals(box.Foreground, dimBrush))
            {
                box.Text = "";
                box.Foreground = primaryBrush;
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(box.Text))
            {
                box.Text = placeholder;
                box.Foreground = dimBrush;
            }
        };

        if (string.IsNullOrEmpty(box.Text))
        {
            box.Text = placeholder;
            box.Foreground = dimBrush;
        }
    }

    private void SetTextBoxValue(TextBox box, string value)
    {
        var placeholder = box.Tag as string ?? "";
        if (!string.IsNullOrEmpty(value))
        {
            box.Text = value;
            box.Foreground = FindBrush("TextPrimaryBrush");
        }
        else
        {
            box.Text = placeholder;
            box.Foreground = FindBrush("TextDimBrush");
        }
    }

    private string GetTextBoxValue(TextBox box)
    {
        var placeholder = box.Tag as string ?? "";
        var dimBrush = FindBrush("TextDimBrush");
        if (box.Text == placeholder && Equals(box.Foreground, dimBrush))
            return "";
        return box.Text.Trim();
    }

    // ── Picker helpers ──────────────────────────────────────────────

    private void PopulateDevicePicker(ListPicker picker, bool? outputOnly = null)
    {
        picker.ClearItems();
        foreach (var (id, name, isOutput) in _audioDevices)
        {
            if (outputOnly.HasValue && isOutput != outputOnly.Value) continue;
            picker.AddItem(name, id);
        }
    }

    private void SelectDevicePicker(ListPicker picker, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) { picker.SelectedIndex = -1; return; }
        for (int i = 0; i < picker.ItemCount; i++)
            if (picker.GetTagAt(i) as string == deviceId) { picker.SelectedIndex = i; return; }
        picker.SelectedIndex = -1;
    }

    private string GetSelectedDeviceId(ListPicker picker) => picker.SelectedTag as string ?? "";

    private string GetDeviceIdForAction(string action, ActionPicker combo, ListPicker legacyPicker)
    {
        // For select/mute actions, prefer sub-tag from ActionPicker flyout
        if (action is "select_output" or "select_input" or "mute_device")
            return combo.SelectedSubTag ?? GetSelectedDeviceId(legacyPicker);
        return GetSelectedDeviceId(legacyPicker);
    }

    private void PopulateProfilePicker(ListPicker picker, AppConfig config)
    {
        picker.ClearItems();
        foreach (var profile in config.Profiles)
            picker.AddItem(profile, profile);
    }

    private void PopulateKnobPicker(ListPicker picker, AppConfig config)
    {
        picker.ClearItems();
        for (int k = 0; k < 5; k++)
        {
            var knob = config.Knobs.FirstOrDefault(kn => kn.Idx == k);
            string label = knob != null && !string.IsNullOrWhiteSpace(knob.Label)
                ? $"Knob {k + 1}: {knob.Label}"
                : knob != null && knob.Target != "none"
                    ? $"Knob {k + 1}: {knob.Target}"
                    : $"Knob {k + 1}";
            if (knob?.Target == "apps" && knob.Apps.Count > 0)
                label += $" ({string.Join(", ", knob.Apps)})";
            picker.AddItem(label, k.ToString());
        }
    }

    private void SelectKnobPicker(ListPicker picker, int knobIdx)
    {
        if (knobIdx < 0) { picker.SelectedIndex = -1; return; }
        var tag = knobIdx.ToString();
        for (int i = 0; i < picker.ItemCount; i++)
            if (picker.GetTagAt(i) as string == tag) { picker.SelectedIndex = i; return; }
        picker.SelectedIndex = -1;
    }

    private void SelectProfilePicker(ListPicker picker, string profileName)
    {
        if (string.IsNullOrEmpty(profileName)) { picker.SelectedIndex = -1; return; }
        for (int i = 0; i < picker.ItemCount; i++)
            if (picker.GetTagAt(i) as string == profileName) { picker.SelectedIndex = i; return; }
        picker.SelectedIndex = -1;
    }

    private void PopulateGoveeDevicePicker(ListPicker picker, AppConfig config)
    {
        picker.ClearItems();
        if (!config.Ambience.GoveeEnabled) return;
        foreach (var device in config.Ambience.GoveeDevices)
        {
            if (!string.IsNullOrWhiteSpace(device.Ip))
            {
                var nameIsIp = device.Name == device.Ip || System.Net.IPAddress.TryParse(device.Name, out _);
                var displayName = !string.IsNullOrWhiteSpace(device.Name) && !nameIsIp ? device.Name
                    : !string.IsNullOrEmpty(device.Sku) ? AmbienceSync.GetProductName(device.Sku)
                    : device.Ip;
                picker.AddItem($"{displayName} ({device.Ip})", device.Ip);
            }
        }
    }

    private static void SelectGoveeDevicePicker(ListPicker picker, string action, string path)
    {
        if (action is not ("govee_toggle" or "govee_color") || string.IsNullOrEmpty(path))
        {
            picker.SelectedIndex = -1;
            return;
        }
        // path may be "ip" or "ip|hexcolor" — extract IP
        var ip = path.Contains('|') ? path.Split('|')[0] : path;
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == ip)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
        picker.SelectedIndex = -1;
    }

    private void SelectHaSubTag(ActionPicker picker, string action, string entityId)
    {
        if (action is not ("ha_toggle" or "ha_scene") || string.IsNullOrEmpty(entityId))
            return;
        picker.SelectWithSub(action, entityId);
    }

    private void SelectDeviceSubTag(ActionPicker picker, string action, string deviceId)
    {
        if (action is not ("select_output" or "select_input" or "mute_device") || string.IsNullOrEmpty(deviceId))
            return;
        picker.SelectWithSub(action, deviceId);
    }

    private string GetActionPath(ActionPicker combo, ListPicker goveePicker, TextBox pathBox)
    {
        var action = GetComboActionValue(combo);
        if (action is "ha_toggle" or "ha_scene" or "select_output" or "select_input" or "mute_device")
            return combo.SelectedSubTag ?? "";
        if (action is "govee_toggle")
            return goveePicker.SelectedTag as string ?? "";
        if (action is "govee_color")
        {
            // Combine device IP + hex color: "ip|hexcolor"
            var ip = goveePicker.SelectedTag as string ?? "";
            var hex = GetTextBoxValue(pathBox);
            if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(hex))
                return $"{ip}|{hex}";
            return ip; // fall back to just IP
        }
        return GetTextBoxValue(pathBox);
    }

    private List<ActionPicker.SubItem> GetHASubItems(string action)
    {
        if (_ha == null) return new();

        // Determine domain filter based on action
        string? domain = action == "ha_scene" ? "scene" : null;
        var entities = domain != null
            ? _ha.CachedEntities.Where(e => e.Domain == domain)
            : _ha.CachedEntities;

        return entities
            .OrderBy(e => e.FriendlyName)
            .Select(e =>
            {
                var (icon, color) = HADomainStyles.GetStyle(e.Domain);
                return new ActionPicker.SubItem(e.FriendlyName, e.EntityId, icon, color);
            })
            .ToList();
    }

    private List<ActionPicker.SubItem> GetDeviceSubItems(bool isOutput)
    {
        return _audioDevices
            .Where(d => d.IsOutput == isOutput)
            .OrderBy(d => d.Name)
            .Select(d => new ActionPicker.SubItem(d.Name, d.Id, isOutput ? "🔊" : "🎙",
                Color.FromRgb(0xAB, 0x47, 0xBC)))
            .ToList();
    }

    private void SelectPowerSegment(SegmentedControl segment, string powerAction)
    {
        if (string.IsNullOrEmpty(powerAction)) { segment.SelectedIndex = -1; return; }
        for (int i = 0; i < segment.SegmentCount; i++)
            if (segment.GetTagAt(i) as string == powerAction) { segment.SelectedIndex = i; return; }
        segment.SelectedIndex = -1;
    }

    private string GetSelectedPowerValue(SegmentedControl segment) => segment.SelectedTag as string ?? "";

    private static void SelectCycleType(SegmentedControl segment, CycleDeviceType type)
    {
        for (int i = 0; i < segment.SegmentCount; i++)
            if (segment.GetTagAt(i) is CycleDeviceType t && t == type) { segment.SelectedIndex = i; return; }
        segment.SelectedIndex = 2; // Default to Both
    }

    // ── Resource helpers ────────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
}
