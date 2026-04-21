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
    private bool _configLoaded;
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
        ("Room: Toggle All", "room_toggle"),
        ("Room: Set Effect", "room_effect"),
        ("Group: Toggle", "group_toggle"),
        ("iCUE: Toggle Lights", "corsair_toggle"),
        ("Govee: Toggle", "govee_toggle"), ("Govee: Color", "govee_color"), ("Govee: White Toggle", "govee_white_toggle"),
        ("OBS: Record", "obs_record"), ("OBS: Stream", "obs_stream"),
        ("OBS: Scene", "obs_scene"), ("OBS: Mute", "obs_mute"),
        ("VM: Mute Strip", "vm_mute_strip"), ("VM: Mute Bus", "vm_mute_bus"),
        ("SC: Next Page", "sc_page_next"), ("SC: Prev Page", "sc_page_prev"),
        ("SC: Home Page", "sc_page_home"), ("SC: Go To Page", "sc_go_to_page"),
        ("Multi-Action",  "multi_action"),
        ("Open Space",    "open_folder"),
        ("Toggle (A/B)",  "toggle_action"),
        ("Open URL",      "open_url"),
        ("Type Text",     "type_text"),
        ("Screenshot",    "screenshot"),
    };

    private static readonly string[] PathActions = { "mute_program", "launch_exe", "close_program", "sc_go_to_page", "open_url" };

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
        { "room_toggle", "💡" }, { "room_effect", "🎨" },
        { "group_toggle", "▣" },
        { "corsair_toggle", "✦" },
        { "govee_toggle", "◈" }, { "govee_color", "◉" }, { "govee_white_toggle", "◇" },
        { "obs_record", "●" }, { "obs_stream", "◉" },
        { "obs_scene", "🎬" }, { "obs_mute", "🔇" },
        { "vm_mute_strip", "🔇" }, { "vm_mute_bus", "🔇" },
        { "sc_page_next", "▶" }, { "sc_page_prev", "◀" },
        { "sc_page_home", "⌂" }, { "sc_go_to_page", "▦" },
        { "multi_action", "≡" }, { "open_folder", "▤" },
        { "toggle_action", "⇄" }, { "open_url", "🌐" },
        { "type_text", "✎" }, { "screenshot", "📷" },
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
        { "room_toggle",        Color.FromRgb(0x69, 0xF0, 0xAE) },
        { "room_effect",        Color.FromRgb(0xE8, 0x6F, 0xFF) },
        { "group_toggle",      Color.FromRgb(0x69, 0xF0, 0xAE) },
        { "corsair_toggle",     Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "govee_toggle",       Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "govee_color",        Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "govee_white_toggle", Color.FromRgb(0xEE, 0xEE, 0xEE) },
        { "obs_record",         Color.FromRgb(0xFF, 0x44, 0x44) },
        { "obs_stream",         Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "obs_scene",          Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "obs_mute",           Color.FromRgb(0xEF, 0x53, 0x50) },
        { "vm_mute_strip",      Color.FromRgb(0xFF, 0x8F, 0x00) },
        { "vm_mute_bus",        Color.FromRgb(0xFF, 0x8F, 0x00) },
        { "sc_page_next",       Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "sc_page_prev",       Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "sc_page_home",       Color.FromRgb(0x00, 0xE6, 0x76) },
        { "sc_go_to_page",      Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "multi_action",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "open_folder",        Color.FromRgb(0xFF, 0xC1, 0x07) },
        { "toggle_action",      Color.FromRgb(0x7E, 0x57, 0xC2) },
        { "open_url",           Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "type_text",          Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "screenshot",         Color.FromRgb(0xFF, 0x8A, 0x65) },
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
    private readonly System.Windows.Controls.Border[] _tapAppChips = new System.Windows.Controls.Border[5];
    private readonly TextBox[] _tapMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _tapMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _tapDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _tapDevicePanels = new StackPanel[5];
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
    private readonly System.Windows.Controls.Border[] _dblAppChips = new System.Windows.Controls.Border[5];
    private readonly TextBox[] _dblMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _dblMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _dblDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _dblDevicePanels = new StackPanel[5];
    private readonly CheckListPicker[] _dblCycleDevicePickers = new CheckListPicker[5];
    private readonly StackPanel[] _dblCycleDevicePanels = new StackPanel[5];
    private readonly SegmentedControl[] _dblCycleTypePickers = new SegmentedControl[5];
    private readonly StackPanel[] _dblCycleTypePanels = new StackPanel[5];
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
    private readonly System.Windows.Controls.Border[] _holdAppChips = new System.Windows.Controls.Border[5];
    private readonly TextBox[] _holdMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _holdMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _holdDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _holdDevicePanels = new StackPanel[5];
    private readonly CheckListPicker[] _holdCycleDevicePickers = new CheckListPicker[5];
    private readonly StackPanel[] _holdCycleDevicePanels = new StackPanel[5];
    private readonly SegmentedControl[] _holdCycleTypePickers = new SegmentedControl[5];
    private readonly StackPanel[] _holdCycleTypePanels = new StackPanel[5];
    private readonly SegmentedControl[] _holdPowerSegments = new SegmentedControl[5];
    private readonly StackPanel[] _holdPowerPanels = new StackPanel[5];
    private readonly ListPicker[] _holdKnobPickers = new ListPicker[5];
    private readonly StackPanel[] _holdKnobPanels = new StackPanel[5];

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
        InitializeDeviceSelector();
        BuildStreamControllerDesigner();
        SetupColumnContextMenus();
    }

    private void SetupColumnContextMenus()
    {
        var menuFg = FindBrush("TextPrimaryBrush");

        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var border = _columnCards[i];
            if (border == null) continue;

            var copyItem = new MenuItem
            {
                Header = "Copy Button",
                Foreground = menuFg,
            };
            copyItem.SetResourceReference(MenuItem.BackgroundProperty, "CardBgBrush");
            var pasteItem = new MenuItem
            {
                Header = "Paste Button",
                Foreground = menuFg,
            };
            pasteItem.SetResourceReference(MenuItem.BackgroundProperty, "CardBgBrush");
            var resetItem = new MenuItem
            {
                Header = "Reset to Default",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x88)),
            };
            resetItem.SetResourceReference(MenuItem.BackgroundProperty, "CardBgBrush");

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
                Background = FindBrush("CardBorderBrush"),
                Foreground = FindBrush("CardBorderBrush"),
                Margin = new Thickness(4, 2, 4, 2),
            };

            var contextMenu = new ContextMenu
            {
                BorderThickness = new Thickness(1),
            };
            contextMenu.SetResourceReference(ContextMenu.BackgroundProperty, "CardBgBrush");
            contextMenu.SetResourceReference(ContextMenu.BorderBrushProperty, "CardBorderBrush");

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
        // For govee_toggle / govee_white_toggle, path is just ip — no path box needed
        if (action is "govee_toggle" or "govee_white_toggle")
            return "";
        // For group_toggle, path is group name — no path box needed
        if (action == "group_toggle")
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
        SelectCycleType(_tapCycleTypePickers[idx], btn.CycleDeviceType);
        SelectPowerSegment(_tapPowerSegments[idx], btn.PowerAction);
        SelectKnobPicker(_tapKnobPickers[idx], btn.LinkedKnobIdx);
        SelectHaSubTag(_tapCombos[idx], btn.Action, btn.Path);
        SelectDeviceSubTag(_tapCombos[idx], btn.Action, btn.DeviceId);
        SelectProfileSubTag(_tapCombos[idx], btn.Action, btn.ProfileName);
        SelectGroupSubTag(_tapCombos[idx], btn.Action, btn.Path);
        SelectGoveeSubTag(_tapCombos[idx], btn.Action, btn.Path);
        UpdateTapVisibility(idx, btn.Action);
        // Restore checked IDs after UpdateTapVisibility (which may re-populate and clear)
        _tapCycleDevicePickers[idx].SetCheckedIds(btn.DeviceIds);
        UpdateHeaderDisplay(idx);

        // DOUBLE
        SelectCombo(_dblCombos[idx], btn.DoublePressAction);
        SetTextBoxValue(_dblPathBoxes[idx], ExtractPathBoxValue(btn.DoublePressAction, btn.DoublePressPath));
        SetTextBoxValue(_dblMacroBoxes[idx], btn.DoublePressMacroKeys);
        SelectDevicePicker(_dblDevicePickers[idx], btn.DoublePressDeviceId);
        SelectCycleType(_dblCycleTypePickers[idx], btn.DoublePressCycleDeviceType);
        SelectPowerSegment(_dblPowerSegments[idx], btn.DoublePressPowerAction);
        SelectKnobPicker(_dblKnobPickers[idx], btn.DoublePressLinkedKnobIdx);
        SelectHaSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressPath);
        SelectDeviceSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressDeviceId);
        SelectProfileSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressProfileName);
        SelectGroupSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressPath);
        SelectGoveeSubTag(_dblCombos[idx], btn.DoublePressAction, btn.DoublePressPath);
        UpdateGestureVisibility(_dblPathPanels[idx], _dblPathLabels[idx], _dblBrowseButtons[idx], _dblPickButtons[idx], _dblMacroPanels[idx],
            _dblDevicePanels[idx], _dblCycleDevicePanels[idx], _dblCycleDevicePickers[idx], _dblCycleTypePanels[idx],
            _dblPowerPanels[idx], _dblKnobPanels[idx], btn.DoublePressAction, _dblAppChips[idx], _dblPathBoxes[idx]);
        // Restore checked IDs after UpdateGestureVisibility (which may re-populate and clear)
        _dblCycleDevicePickers[idx].SetCheckedIds(btn.DoublePressDeviceIds);

        // HOLD
        SelectCombo(_holdCombos[idx], btn.HoldAction);
        SetTextBoxValue(_holdPathBoxes[idx], ExtractPathBoxValue(btn.HoldAction, btn.HoldPath));
        SetTextBoxValue(_holdMacroBoxes[idx], btn.HoldMacroKeys);
        SelectDevicePicker(_holdDevicePickers[idx], btn.HoldDeviceId);
        SelectCycleType(_holdCycleTypePickers[idx], btn.HoldCycleDeviceType);
        SelectPowerSegment(_holdPowerSegments[idx], btn.HoldPowerAction);
        SelectKnobPicker(_holdKnobPickers[idx], btn.HoldLinkedKnobIdx);
        SelectHaSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldPath);
        SelectDeviceSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldDeviceId);
        SelectProfileSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldProfileName);
        SelectGroupSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldPath);
        SelectGoveeSubTag(_holdCombos[idx], btn.HoldAction, btn.HoldPath);
        UpdateGestureVisibility(_holdPathPanels[idx], _holdPathLabels[idx], _holdBrowseButtons[idx], _holdPickButtons[idx], _holdMacroPanels[idx],
            _holdDevicePanels[idx], _holdCycleDevicePanels[idx], _holdCycleDevicePickers[idx], _holdCycleTypePanels[idx],
            _holdPowerPanels[idx], _holdKnobPanels[idx], btn.HoldAction, _holdAppChips[idx], _holdPathBoxes[idx]);
        // Restore checked IDs after UpdateGestureVisibility (which may re-populate and clear)
        _holdCycleDevicePickers[idx].SetCheckedIds(btn.HoldDeviceIds);

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
            PopulateKnobPicker(_tapKnobPickers[i], config);

            PopulateDevicePicker(_dblDevicePickers[i]);
            PopulateCycleDevicePicker(_dblCycleDevicePickers[i]);
            PopulateKnobPicker(_dblKnobPickers[i], config);

            PopulateDevicePicker(_holdDevicePickers[i]);
            PopulateCycleDevicePicker(_holdCycleDevicePickers[i]);
            PopulateKnobPicker(_holdKnobPickers[i], config);
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
            SelectCycleType(_tapCycleTypePickers[i], btn.CycleDeviceType);
            SelectPowerSegment(_tapPowerSegments[i], btn.PowerAction);
            SelectKnobPicker(_tapKnobPickers[i], btn.LinkedKnobIdx);
            SelectHaSubTag(_tapCombos[i], btn.Action, btn.Path);
            SelectDeviceSubTag(_tapCombos[i], btn.Action, btn.DeviceId);
            SelectProfileSubTag(_tapCombos[i], btn.Action, btn.ProfileName);
            SelectGroupSubTag(_tapCombos[i], btn.Action, btn.Path);
            SelectGoveeSubTag(_tapCombos[i], btn.Action, btn.Path);
            UpdateTapVisibility(i, btn.Action);
            // Restore checked IDs after UpdateTapVisibility (which may re-populate and clear)
            _tapCycleDevicePickers[i].SetCheckedIds(btn.DeviceIds);
            UpdateHeaderDisplay(i);

            // DOUBLE
            SelectCombo(_dblCombos[i], btn.DoublePressAction);
            SetTextBoxValue(_dblPathBoxes[i], ExtractPathBoxValue(btn.DoublePressAction, btn.DoublePressPath));
            SetTextBoxValue(_dblMacroBoxes[i], btn.DoublePressMacroKeys);
            SelectDevicePicker(_dblDevicePickers[i], btn.DoublePressDeviceId);
            SelectCycleType(_dblCycleTypePickers[i], btn.DoublePressCycleDeviceType);
            SelectPowerSegment(_dblPowerSegments[i], btn.DoublePressPowerAction);
            SelectKnobPicker(_dblKnobPickers[i], btn.DoublePressLinkedKnobIdx);
            SelectHaSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressPath);
            SelectDeviceSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressDeviceId);
            SelectProfileSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressProfileName);
            SelectGroupSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressPath);
            SelectGoveeSubTag(_dblCombos[i], btn.DoublePressAction, btn.DoublePressPath);
            UpdateGestureVisibility(_dblPathPanels[i], _dblPathLabels[i], _dblBrowseButtons[i], _dblPickButtons[i], _dblMacroPanels[i],
                _dblDevicePanels[i], _dblCycleDevicePanels[i], _dblCycleDevicePickers[i], _dblCycleTypePanels[i],
                _dblPowerPanels[i], _dblKnobPanels[i], btn.DoublePressAction, _dblAppChips[i], _dblPathBoxes[i]);
            // Restore checked IDs after UpdateGestureVisibility (which may re-populate and clear)
            _dblCycleDevicePickers[i].SetCheckedIds(btn.DoublePressDeviceIds);

            // HOLD
            SelectCombo(_holdCombos[i], btn.HoldAction);
            SetTextBoxValue(_holdPathBoxes[i], ExtractPathBoxValue(btn.HoldAction, btn.HoldPath));
            SetTextBoxValue(_holdMacroBoxes[i], btn.HoldMacroKeys);
            SelectDevicePicker(_holdDevicePickers[i], btn.HoldDeviceId);
            SelectCycleType(_holdCycleTypePickers[i], btn.HoldCycleDeviceType);
            SelectPowerSegment(_holdPowerSegments[i], btn.HoldPowerAction);
            SelectKnobPicker(_holdKnobPickers[i], btn.HoldLinkedKnobIdx);
            SelectHaSubTag(_holdCombos[i], btn.HoldAction, btn.HoldPath);
            SelectDeviceSubTag(_holdCombos[i], btn.HoldAction, btn.HoldDeviceId);
            SelectProfileSubTag(_holdCombos[i], btn.HoldAction, btn.HoldProfileName);
            SelectGroupSubTag(_holdCombos[i], btn.HoldAction, btn.HoldPath);
            SelectGoveeSubTag(_holdCombos[i], btn.HoldAction, btn.HoldPath);
            UpdateGestureVisibility(_holdPathPanels[i], _holdPathLabels[i], _holdBrowseButtons[i], _holdPickButtons[i], _holdMacroPanels[i],
                _holdDevicePanels[i], _holdCycleDevicePanels[i], _holdCycleDevicePickers[i], _holdCycleTypePanels[i],
                _holdPowerPanels[i], _holdKnobPanels[i], btn.HoldAction, _holdAppChips[i], _holdPathBoxes[i]);
            // Restore checked IDs after UpdateGestureVisibility (which may re-populate and clear)
            _holdCycleDevicePickers[i].SetCheckedIds(btn.HoldDeviceIds);
        }

        _loading = true;
        DeviceSelector.SelectedIndex = config.TabSelection.Buttons switch
        {
            DeviceSurface.StreamController => 1,
            DeviceSurface.Both => 2,
            _ => 0,
        };
        UpdateDeviceSurfaceVisibility(config.TabSelection.Buttons);
        _loading = false;

        _loading = true;
        LoadStreamControllerConfig();
        _loading = false;

        _loading = false;
        _configLoaded = true;

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
        foreach (var child in TurnUpButtonsPanel.Children)
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

            var (pathPanel, pathLabel, pathBox, browseBtn, pickBtn, appChip) = MakePathRow("PROCESS NAME", "discord");
            pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPathPanels[i] = pathPanel;
            _tapPathLabels[i] = pathLabel;
            _tapPathBoxes[i] = pathBox;
            _tapBrowseButtons[i] = browseBtn;
            _tapPickButtons[i] = pickBtn;
            _tapAppChips[i] = appChip;
            browseBtn.Click += (_, _) => BrowseForFile(pathBox, appChip);
            pickBtn.Click += (_, _) => ShowProcessPicker(pickBtn, pathBox, GetComboActionValue(_tapCombos[idx]));
            appChip.MouseLeftButtonDown += (_, _) => OnAppChipClick(pathBox, appChip);
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

            var (dblPathPanel, dblPathLabel, dblPathBox, dblBrowseBtn, dblPickBtn, dblAppChip) = MakePathRow("PROCESS NAME", "discord");
            dblPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblPathPanels[i] = dblPathPanel;
            _dblPathLabels[i] = dblPathLabel;
            _dblPathBoxes[i] = dblPathBox;
            _dblBrowseButtons[i] = dblBrowseBtn;
            _dblPickButtons[i] = dblPickBtn;
            _dblAppChips[i] = dblAppChip;
            dblBrowseBtn.Click += (_, _) => BrowseForFile(dblPathBox, dblAppChip);
            dblPickBtn.Click += (_, _) => ShowProcessPicker(dblPickBtn, dblPathBox, GetComboActionValue(_dblCombos[idx]));
            dblAppChip.MouseLeftButtonDown += (_, _) => OnAppChipClick(dblPathBox, dblAppChip);
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

            dblCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(dblCombo);
                UpdateGestureVisibility(_dblPathPanels[idx], _dblPathLabels[idx], _dblBrowseButtons[idx], _dblPickButtons[idx], _dblMacroPanels[idx],
                    _dblDevicePanels[idx], _dblCycleDevicePanels[idx], _dblCycleDevicePickers[idx], _dblCycleTypePanels[idx],
                    _dblPowerPanels[idx], _dblKnobPanels[idx], val, _dblAppChips[idx], _dblPathBoxes[idx]);
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

            var (holdPathPanel, holdPathLabel, holdPathBox, holdBrowseBtn, holdPickBtn, holdAppChip) = MakePathRow("PROCESS NAME", "discord");
            holdPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdPathPanels[i] = holdPathPanel;
            _holdPathLabels[i] = holdPathLabel;
            _holdPathBoxes[i] = holdPathBox;
            _holdBrowseButtons[i] = holdBrowseBtn;
            _holdPickButtons[i] = holdPickBtn;
            _holdAppChips[i] = holdAppChip;
            holdBrowseBtn.Click += (_, _) => BrowseForFile(holdPathBox, holdAppChip);
            holdPickBtn.Click += (_, _) => ShowProcessPicker(holdPickBtn, holdPathBox, GetComboActionValue(_holdCombos[idx]));
            holdAppChip.MouseLeftButtonDown += (_, _) => OnAppChipClick(holdPathBox, holdAppChip);
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

            holdCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(holdCombo);
                UpdateGestureVisibility(_holdPathPanels[idx], _holdPathLabels[idx], _holdBrowseButtons[idx], _holdPickButtons[idx], _holdMacroPanels[idx],
                    _holdDevicePanels[idx], _holdCycleDevicePanels[idx], _holdCycleDevicePickers[idx], _holdCycleTypePanels[idx],
                    _holdPowerPanels[idx], _holdKnobPanels[idx], val, _holdAppChips[idx], _holdPathBoxes[idx]);
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
        bool isObsPathAction = action is "obs_scene" or "obs_mute";
        bool isVmPathAction = action is "vm_mute_strip" or "vm_mute_bus";
        _tapPathPanels[idx].Visibility = PathActions.Contains(action) || isHaServiceAction || action == "govee_color" || isObsPathAction || isVmPathAction ? Visibility.Visible : Visibility.Collapsed;
        ApplyPathLabelAndButtons(_tapPathLabels[idx], _tapPathBoxes[idx], _tapBrowseButtons[idx], _tapPickButtons[idx], action, _tapAppChips[idx]);
        _tapMacroPanels[idx].Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        _tapDevicePanels[idx].Visibility = Visibility.Collapsed; // select/mute now use sub-flyout
        _tapCycleDevicePanels[idx].Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        _tapCycleTypePanels[idx].Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        if (action is "cycle_output" or "cycle_input")
            PopulateCycleDevicePicker(_tapCycleDevicePickers[idx], action == "cycle_output");
        _tapPowerPanels[idx].Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        _tapKnobPanels[idx].Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateGestureVisibility(
        StackPanel pathPanel, TextBlock pathLabel, Button browseBtn, Button pickBtn, StackPanel macroPanel,
        StackPanel devicePanel, StackPanel cycleDevicePanel, CheckListPicker cycleDevicePicker, StackPanel cycleTypePanel,
        StackPanel powerPanel, StackPanel knobPanel, string action,
        System.Windows.Controls.Border? chip = null, TextBox? box = null)
    {
        bool isHaServiceAction = action == "ha_service";
        bool isObsPathAction = action is "obs_scene" or "obs_mute";
        bool isVmPathAction = action is "vm_mute_strip" or "vm_mute_bus";
        pathPanel.Visibility = PathActions.Contains(action) || isHaServiceAction || action == "govee_color" || isObsPathAction || isVmPathAction ? Visibility.Visible : Visibility.Collapsed;
        ApplyPathLabelAndButtons(pathLabel, box, browseBtn, pickBtn, action, chip);
        macroPanel.Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        devicePanel.Visibility = Visibility.Collapsed; // select/mute now use sub-flyout
        cycleDevicePanel.Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        cycleTypePanel.Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        if (action is "cycle_output" or "cycle_input")
            PopulateCycleDevicePicker(cycleDevicePicker, action == "cycle_output");
        powerPanel.Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        knobPanel.Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPathLabelAndButtons(TextBlock label, TextBox? box, Button browseBtn, Button pickBtn, string action, System.Windows.Controls.Border? chip = null)
    {
        // launch_exe shows the compact app chip; everything else shows the textbox input.
        bool showChip = action == "launch_exe" && chip != null;
        var inputBorder = box?.Parent as System.Windows.Controls.Border;
        if (chip != null)
            chip.Visibility = showChip ? Visibility.Visible : Visibility.Collapsed;
        if (inputBorder != null)
            inputBorder.Visibility = showChip ? Visibility.Collapsed : Visibility.Visible;
        if (showChip && box != null)
            UpdateAppChipDisplay(chip!, GetTextBoxValue(box));

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
                label.Text = "APP";
                if (box != null) box.ToolTip = "Full path to the executable to launch, or use the app picker";
                browseBtn.Visibility = Visibility.Visible;
                pickBtn.Visibility = Visibility.Collapsed;
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
        if (_config == null || _onSave == null || !_configLoaded) return;

        for (int i = 0; i < 5; i++)
        {
            var btn = _config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // Save button label (empty string = default "Button N")
            var labelText = _headers[i].Text.Trim();
            btn.Label = labelText == $"Button {i + 1}" ? "" : labelText;

            btn.Action = GetComboActionValue(_tapCombos[i]);
            btn.Path = GetActionPath(_tapCombos[i], _tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetDeviceIdForAction(btn.Action, _tapCombos[i], _tapDevicePickers[i]);
            btn.DeviceIds = _tapCycleDevicePickers[i].GetCheckedIds();
            btn.CycleDeviceType = _tapCycleTypePickers[i].SelectedTag is CycleDeviceType t ? t : CycleDeviceType.Both;
            btn.ProfileName = btn.Action == "switch_profile" ? (_tapCombos[i].SelectedSubTag ?? "") : "";
            btn.PowerAction = GetSelectedPowerValue(_tapPowerSegments[i]);
            btn.LinkedKnobIdx = int.TryParse(_tapKnobPickers[i].SelectedTag as string, out int ki) ? ki : -1;

            btn.DoublePressAction = GetComboActionValue(_dblCombos[i]);
            btn.DoublePressPath = GetActionPath(_dblCombos[i], _dblPathBoxes[i]);
            btn.DoublePressMacroKeys = GetTextBoxValue(_dblMacroBoxes[i]);
            btn.DoublePressDeviceId = GetDeviceIdForAction(btn.DoublePressAction, _dblCombos[i], _dblDevicePickers[i]);
            btn.DoublePressDeviceIds = _dblCycleDevicePickers[i].GetCheckedIds();
            btn.DoublePressCycleDeviceType = _dblCycleTypePickers[i].SelectedTag is CycleDeviceType dt ? dt : CycleDeviceType.Both;
            btn.DoublePressProfileName = btn.DoublePressAction == "switch_profile" ? (_dblCombos[i].SelectedSubTag ?? "") : "";
            btn.DoublePressPowerAction = GetSelectedPowerValue(_dblPowerSegments[i]);
            btn.DoublePressLinkedKnobIdx = int.TryParse(_dblKnobPickers[i].SelectedTag as string, out int dki) ? dki : -1;

            btn.HoldAction = GetComboActionValue(_holdCombos[i]);
            btn.HoldPath = GetActionPath(_holdCombos[i], _holdPathBoxes[i]);
            btn.HoldMacroKeys = GetTextBoxValue(_holdMacroBoxes[i]);
            btn.HoldDeviceId = GetDeviceIdForAction(btn.HoldAction, _holdCombos[i], _holdDevicePickers[i]);
            btn.HoldDeviceIds = _holdCycleDevicePickers[i].GetCheckedIds();
            btn.HoldCycleDeviceType = _holdCycleTypePickers[i].SelectedTag is CycleDeviceType ht ? ht : CycleDeviceType.Both;
            btn.HoldProfileName = btn.HoldAction == "switch_profile" ? (_holdCombos[i].SelectedSubTag ?? "") : "";
            btn.HoldPowerAction = GetSelectedPowerValue(_holdPowerSegments[i]);
            btn.HoldLinkedKnobIdx = int.TryParse(_holdKnobPickers[i].SelectedTag as string, out int hki) ? hki : -1;
        }

        if (DeviceSelector.SelectedTag is DeviceSurface surface)
            _config.TabSelection.Buttons = surface;

        UpdateStreamControllerSelection();

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
        DeviceSelector.AccentColor = accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            bar.Background = new SolidColorBrush(accent);
            label.Foreground = new SolidColorBrush(accent);
        }

        for (int i = 0; i < 5; i++)
        {
            _tapDevicePickers[i].RefreshAccent();
            _tapKnobPickers[i].RefreshAccent();
            _tapPowerSegments[i].AccentColor = accent;
            _tapCycleDevicePickers[i].AccentColor = accent;

            _dblDevicePickers[i].RefreshAccent();
            _dblKnobPickers[i].RefreshAccent();
            _dblPowerSegments[i].AccentColor = accent;
            _dblCycleDevicePickers[i].AccentColor = accent;

            _holdDevicePickers[i].RefreshAccent();
            _holdKnobPickers[i].RefreshAccent();
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
        { "room_toggle",        "Toggle all room lights on/off (Govee + Corsair)" },
        { "room_effect",        "Switch the active room effect (Fire, Ocean, Aurora, etc.)" },
        { "group_toggle",      "Toggle a device group on/off" },
        { "corsair_toggle",     "Toggle Corsair iCUE lights on/off (drives LEDs black when off)" },
        { "govee_toggle",       "Toggle a Govee device on/off via LAN" },
        { "govee_color",        "Set a Govee device to a specific color (enter hex in path, e.g. FF0080)" },
        { "govee_white_toggle", "Toggle white on/off — saves current color first, restores it on second press" },
        { "obs_record",         "Toggle OBS Studio recording on/off" },
        { "obs_stream",         "Toggle OBS Studio streaming on/off" },
        { "obs_scene",          "Switch to an OBS scene (enter scene name in path)" },
        { "obs_mute",           "Toggle mute on an OBS audio source (enter source name in path)" },
        { "vm_mute_strip",      "Toggle mute on a VoiceMeeter strip (enter strip index 0-4 in path)" },
        { "vm_mute_bus",        "Toggle mute on a VoiceMeeter bus (enter bus index 0-2 in path)" },
        { "sc_page_next",       "Navigate to the next Stream Controller page" },
        { "sc_page_prev",       "Navigate to the previous Stream Controller page" },
        { "sc_page_home",       "Jump back to page 1 (home page)" },
        { "sc_go_to_page",      "Jump to a specific page number (enter page number in path)" },
        { "multi_action",       "Run a sequence of actions with optional delays" },
        { "open_folder",        "Open a named Space (nested page group)" },
        { "toggle_action",      "Alternate between two actions on each press (A / B / A / B)" },
        { "open_url",           "Open a web URL in the default browser" },
        { "type_text",          "Type a pre-set text snippet as keystrokes" },
        { "screenshot",         "Capture the screen to clipboard" },
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

        bool groupsExist = config.Groups.Count > 0;
        bool anyGroupConfigured = config.Buttons.Any(b =>
            b.Action == "group_toggle" || b.DoublePressAction == "group_toggle" || b.HoldAction == "group_toggle");

        for (int i = 0; i < 5; i++)
        {
            PopulateActionPicker(_tapCombos[i], haEnabled, anyHaConfigured, goveeEnabled, anyGoveeConfigured, obsEnabled, anyObsConfigured, vmEnabled, anyVmConfigured, groupsExist, anyGroupConfigured);
            PopulateActionPicker(_dblCombos[i], haEnabled, anyHaConfigured, goveeEnabled, anyGoveeConfigured, obsEnabled, anyObsConfigured, vmEnabled, anyVmConfigured, groupsExist, anyGroupConfigured);
            PopulateActionPicker(_holdCombos[i], haEnabled, anyHaConfigured, goveeEnabled, anyGoveeConfigured, obsEnabled, anyObsConfigured, vmEnabled, anyVmConfigured, groupsExist, anyGroupConfigured);
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

                // Profile sub-menu
                picker.RegisterSubMenu("switch_profile", () => GetProfileSubItems());

                // Device group sub-menu
                if (groupsExist || anyGroupConfigured)
                    picker.RegisterSubMenu("group_toggle", () => GetGroupSubItems());

                // Govee device sub-menus
                if (goveeEnabled || anyGoveeConfigured)
                {
                    picker.RegisterSubMenu("govee_toggle", () => GetGoveeSubItems());
                    picker.RegisterSubMenu("govee_color", () => GetGoveeSubItems());
                    picker.RegisterSubMenu("govee_white_toggle", () => GetGoveeSubItems());
                }
            }
        }
    }

    private static bool IsHaAction(string? action)
        => action is "ha_toggle" or "ha_scene" or "ha_service";

    private static bool IsGoveeAction(string? action)
        => action is "govee_toggle" or "govee_color" or "govee_white_toggle";

    private static bool IsObsAction(string? action)
        => action is "obs_record" or "obs_stream" or "obs_scene" or "obs_mute";

    private static bool IsVmAction(string? action)
        => action is "vm_mute_strip" or "vm_mute_bus";

    private static bool IsCorsairAction(string? action)
        => action is "corsair_toggle";

    // Category groupings for the action picker
    private static readonly (string Category, string[] Values)[] ActionCategories =
    {
        ("Media",           new[] { "none", "media_play_pause", "media_next", "media_prev" }),
        ("Mute",            new[] { "mute_master", "mute_mic", "mute_program", "mute_active_window", "mute_app_group", "mute_device" }),
        ("App Control",     new[] { "launch_exe", "close_program", "open_url", "type_text", "screenshot" }),
        ("Device",          new[] { "cycle_output", "cycle_input", "select_output", "select_input" }),
        ("System",          new[] { "macro", "switch_profile", "cycle_profile", "cycle_brightness", "quick_wheel" }),
        ("Advanced",        new[] { "multi_action", "toggle_action", "open_folder" }),
        ("Power",           new[] { "power_sleep", "power_lock", "power_off", "power_restart", "power_logoff", "power_hibernate" }),
        ("Room",            new[] { "room_toggle", "room_effect" }),
        ("Integrations",    new[] { "group_toggle", "ha_toggle", "ha_scene", "ha_service", "corsair_toggle", "govee_toggle", "govee_color", "govee_white_toggle", "obs_record", "obs_stream", "obs_scene", "obs_mute", "vm_mute_strip", "vm_mute_bus" }),
        ("Stream Controller", new[] { "sc_page_next", "sc_page_prev", "sc_page_home", "sc_go_to_page" }),
    };

    private static readonly Dictionary<string, (string Display, string Value)> ActionLookup =
        Actions.ToDictionary(a => a.Value, a => a);

    private static bool IsScPageAction(string? action)
        => action is "sc_page_next" or "sc_page_prev" or "sc_page_home" or "sc_go_to_page";

    private void PopulateActionPicker(ActionPicker picker, bool haEnabled, bool anyHaConfigured, bool goveeEnabled, bool anyGoveeConfigured, bool obsEnabled = false, bool anyObsConfigured = false, bool vmEnabled = false, bool anyVmConfigured = false, bool groupsExist = false, bool anyGroupConfigured = false, bool showScPageActions = false)
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
                bool isGroup = value == "group_toggle";
                bool isScPage = IsScPageAction(value);

                if (isHa && !haEnabled && !anyHaConfigured) continue;
                if (isGovee && !goveeEnabled && !anyGoveeConfigured) continue;
                if (isObs && !obsEnabled && !anyObsConfigured) continue;
                if (isVm && !vmEnabled && !anyVmConfigured) continue;
                if (isGroup && !groupsExist && !anyGroupConfigured) continue;
                if (isScPage && !showScPageActions) continue;

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

    private (StackPanel panel, TextBlock labelBlock, TextBox box, Button browseBtn, Button pickBtn, System.Windows.Controls.Border chip) MakePathRow(string label, string placeholder)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        var labelBlock = MakeLabel(label);
        container.Children.Add(labelBlock);

        // Grid so chip and text input can share column 0, toggled by Visibility
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Browse button with folder icon
        var browseBg = (Brush)Application.Current.FindResource("CardBorderBrush");
        var browseHoverBg = (Brush)Application.Current.FindResource("InputBorderBrush");
        var browseBtn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.FolderOpen, Width = 16, Height = 16, Foreground = FindBrush("TextSecBrush") },
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
        Grid.SetColumn(browseBorder, 2);
        row.Children.Add(browseBorder);

        // Pick (process list) button with list icon
        var pickBg = (Brush)Application.Current.FindResource("CardBorderBrush");
        var pickHoverBg = (Brush)Application.Current.FindResource("InputBorderBrush");
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
        Grid.SetColumn(pickBorder, 1);
        row.Children.Add(pickBorder);

        // Text input with rounded border wrapper (shown for process-name actions)
        var inputBorder = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
        };
        inputBorder.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        inputBorder.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
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
        Grid.SetColumn(inputBorder, 0);
        row.Children.Add(inputBorder);

        // App chip (shown for launch_exe) — collapsed by default
        var chip = MakeAppChip();
        chip.Visibility = Visibility.Collapsed;
        Grid.SetColumn(chip, 0);
        row.Children.Add(chip);

        container.Children.Add(row);
        return (container, labelBlock, box, browseBtn, pickBtn, chip);
    }

    /// <summary>
    /// Build a compact "app chip" — rounded border with app icon, display name,
    /// and chevron. Click opens AppPickerDialog. Used for launch_exe action
    /// instead of a raw program-path textbox.
    /// </summary>
    private System.Windows.Controls.Border MakeAppChip()
    {
        var iconImg = new System.Windows.Controls.Image
        {
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var placeholderIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.RocketLaunchOutline,
            Width = 18,
            Height = 18,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
        };

        var nameText = new TextBlock
        {
            Text = "Choose Program...",
            FontSize = 12,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var chevron = new MaterialIcon
        {
            Kind = MaterialIconKind.ChevronDown,
            Width = 14,
            Height = 14,
            Foreground = FindBrush("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(iconImg, Dock.Left);
        DockPanel.SetDock(placeholderIcon, Dock.Left);
        DockPanel.SetDock(chevron, Dock.Right);
        content.Children.Add(iconImg);
        content.Children.Add(placeholderIcon);
        content.Children.Add(chevron);
        content.Children.Add(nameText);

        var bg = (Brush)Application.Current.FindResource("InputBgBrush");
        var hoverBg = (Brush)Application.Current.FindResource("CardBorderBrush");
        var idleBorder = (Brush)Application.Current.FindResource("InputBorderBrush");

        var border = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(6),
            Background = bg,
            BorderBrush = idleBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            Height = 34,
            Cursor = Cursors.Hand,
            Child = content,
            ToolTip = "Click to pick an app",
        };

        // Store inner refs on the Tag for retrieval in UpdateAppChipDisplay
        border.Tag = new AppChipRefs(iconImg, placeholderIcon, nameText);

        border.MouseEnter += (_, _) =>
        {
            border.Background = hoverBg;
            border.BorderBrush = new SolidColorBrush(ThemeManager.Accent);
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = bg;
            border.BorderBrush = idleBorder;
        };

        return border;
    }

    private sealed record AppChipRefs(System.Windows.Controls.Image Icon, MaterialIcon Placeholder, TextBlock Name);

    /// <summary>
    /// Refresh an app chip's icon, display name, and tooltip from the given path.
    /// Empty path → "Choose Program..." placeholder state.
    /// </summary>
    private void UpdateAppChipDisplay(System.Windows.Controls.Border chip, string? path)
    {
        if (chip.Tag is not AppChipRefs refs) return;

        if (string.IsNullOrWhiteSpace(path))
        {
            refs.Icon.Source = null;
            refs.Icon.Visibility = Visibility.Collapsed;
            refs.Placeholder.Visibility = Visibility.Visible;
            refs.Name.Text = "Choose Program...";
            refs.Name.Foreground = FindBrush("TextDimBrush");
            chip.ToolTip = "Click to pick an app";
            return;
        }

        refs.Name.Text = GetAppDisplayName(path);
        refs.Name.Foreground = FindBrush("TextPrimaryBrush");

        var icon = TryExtractIcon(path);
        if (icon != null)
        {
            refs.Icon.Source = icon;
            refs.Icon.Visibility = Visibility.Visible;
            refs.Placeholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            refs.Icon.Source = null;
            refs.Icon.Visibility = Visibility.Collapsed;
            refs.Placeholder.Visibility = Visibility.Visible;
        }

        chip.ToolTip = path;
    }

    private static string GetAppDisplayName(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            // Strip trailing args (first token before any space, respecting quoted paths)
            var exe = expanded.StartsWith("\"")
                ? expanded.Substring(1, Math.Max(0, expanded.IndexOf('"', 1) - 1))
                : expanded.Split(' ')[0];

            if (!string.IsNullOrWhiteSpace(exe) && System.IO.File.Exists(exe))
            {
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrWhiteSpace(fvi.ProductName))
                    return fvi.ProductName!;
                return System.IO.Path.GetFileNameWithoutExtension(exe);
            }

            return System.IO.Path.GetFileNameWithoutExtension(exe);
        }
        catch
        {
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }

    private static ImageSource? TryExtractIcon(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            var exe = expanded.StartsWith("\"")
                ? expanded.Substring(1, Math.Max(0, expanded.IndexOf('"', 1) - 1))
                : expanded.Split(' ')[0];

            if (string.IsNullOrWhiteSpace(exe) || !System.IO.File.Exists(exe))
                return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (icon == null) return null;

            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }

    private void BrowseForFile(TextBox targetBox, System.Windows.Controls.Border? chip = null)
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
            if (chip != null) UpdateAppChipDisplay(chip, dlg.FileName);
            QueueSave();
        }
    }

    private void OnAppChipClick(TextBox targetBox, System.Windows.Controls.Border chip)
    {
        var picker = new AppPickerDialog { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedPath))
        {
            targetBox.Text = picker.SelectedPath;
            targetBox.Foreground = FindBrush("TextPrimaryBrush");
            UpdateAppChipDisplay(chip, picker.SelectedPath);
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
            Background = FindBrush("CardBgBrush"),
            MinWidth = 160,
        };

        var border = new System.Windows.Controls.Border
        {
            Child = scroll,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
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
            Background = FindBrush("CardBgBrush"),
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

    private List<ActionPicker.SubItem> GetProfileSubItems()
    {
        if (_config == null) return new();
        return _config.Profiles
            .Select(p => new ActionPicker.SubItem(p, p, "\uD83D\uDCCB", Color.FromRgb(0x29, 0xB6, 0xF6)))
            .ToList();
    }

    private List<ActionPicker.SubItem> GetGroupSubItems()
    {
        if (_config == null) return new();
        return _config.Groups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new ActionPicker.SubItem(g.Name, g.Name, "\u25A3", Color.FromRgb(0x69, 0xF0, 0xAE)))
            .ToList();
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

    private void SelectProfileSubTag(ActionPicker picker, string action, string profileName)
    {
        if (action != "switch_profile" || string.IsNullOrEmpty(profileName))
            return;
        picker.SelectWithSub(action, profileName);
    }

    private List<ActionPicker.SubItem> GetGoveeSubItems()
    {
        if (_config == null || !_config.Ambience.GoveeEnabled) return new();

        var items = new List<ActionPicker.SubItem>();

        foreach (var d in _config.Ambience.GoveeDevices)
        {
            bool hasLan = !string.IsNullOrWhiteSpace(d.Ip);
            bool hasCloud = !hasLan && !string.IsNullOrWhiteSpace(d.DeviceId) && !string.IsNullOrWhiteSpace(d.Sku);
            if (!hasLan && !hasCloud) continue;

            var nameIsIp = d.Name == d.Ip || System.Net.IPAddress.TryParse(d.Name, out _);
            var displayName = !string.IsNullOrWhiteSpace(d.Name) && !nameIsIp ? d.Name
                : !string.IsNullOrEmpty(d.Sku) ? AmbienceSync.GetProductName(d.Sku)
                : (hasLan ? d.Ip : d.DeviceId);

            if (hasLan)
            {
                items.Add(new ActionPicker.SubItem($"{displayName} ({d.Ip})", d.Ip, "\u25C8", Color.FromRgb(0x66, 0xBB, 0x6A)));
            }
            else
            {
                // Cloud-only devices (e.g. H604C G1S Pro) — tag with cloud: prefix
                // so ButtonHandler routes through the Cloud API instead of LAN UDP.
                items.Add(new ActionPicker.SubItem($"{displayName} (API)", $"cloud:{d.DeviceId}", "\u25C8", Color.FromRgb(0x42, 0xA5, 0xF5)));
            }
        }

        return items;
    }

    private void SelectGoveeSubTag(ActionPicker picker, string action, string path)
    {
        if (action is not ("govee_toggle" or "govee_color" or "govee_white_toggle") || string.IsNullOrEmpty(path))
            return;
        // path variants:
        //   "ip"           LAN
        //   "ip|hexcolor"  LAN (govee_color)
        //   "cloud:<id>"   Cloud API
        string tag = path.Contains('|') ? path.Split('|')[0] : path;
        picker.SelectWithSub(action, tag);
    }

    private void SelectGroupSubTag(ActionPicker picker, string action, string path)
    {
        if (action != "group_toggle" || string.IsNullOrEmpty(path))
            return;
        picker.SelectWithSub(action, path);
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

    private string GetActionPath(ActionPicker combo, TextBox pathBox)
    {
        var action = GetComboActionValue(combo);
        if (action is "ha_toggle" or "ha_scene" or "select_output" or "select_input" or "mute_device" or "group_toggle"
            or "govee_toggle" or "govee_white_toggle")
            return combo.SelectedSubTag ?? "";
        if (action is "govee_color")
        {
            // Combine device IP + hex color: "ip|hexcolor"
            var ip = combo.SelectedSubTag ?? "";
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
