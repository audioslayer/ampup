using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
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
        ("Cycle Brightness", "cycle_brightness"),
        ("Sleep", "power_sleep"), ("Lock", "power_lock"), ("Off", "power_off"),
        ("Restart", "power_restart"), ("Logoff", "power_logoff"), ("Hibernate", "power_hibernate"),
        ("HA: Toggle", "ha_toggle"), ("HA: Scene", "ha_scene"), ("HA: Service", "ha_service"),
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
        { "switch_profile", "📋" }, { "cycle_brightness", "💡" }, { "mute_app_group", "🔇" },
        { "mute_device", "🔇" },
        { "power_sleep", "😴" }, { "power_lock", "🔒" }, { "power_off", "⏻" },
        { "power_restart", "🔄" }, { "power_logoff", "🚪" }, { "power_hibernate", "❄" },
        { "ha_toggle", "⚡" }, { "ha_scene", "🎬" }, { "ha_service", "⚙" },
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
        { "cycle_brightness",   Color.FromRgb(0xFF, 0xF1, 0x76) },
        { "power_sleep",        Color.FromRgb(0x7C, 0x8C, 0xF8) },
        { "power_lock",         Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "power_off",          Color.FromRgb(0xFF, 0x44, 0x44) },
        { "power_restart",      Color.FromRgb(0xFF, 0x8A, 0x3D) },
        { "power_logoff",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "power_hibernate",    Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "ha_toggle",          Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "ha_scene",           Color.FromRgb(0xFF, 0xA7, 0x26) },
        { "ha_service",         Color.FromRgb(0xAB, 0x47, 0xBC) },
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
    private readonly ListPicker[] _holdProfilePickers = new ListPicker[5];
    private readonly StackPanel[] _holdProfilePanels = new StackPanel[5];
    private readonly SegmentedControl[] _holdPowerSegments = new SegmentedControl[5];
    private readonly StackPanel[] _holdPowerPanels = new StackPanel[5];
    private readonly ListPicker[] _holdKnobPickers = new ListPicker[5];
    private readonly StackPanel[] _holdKnobPanels = new StackPanel[5];

    // HA entity pickers (tap/double/hold)
    private readonly ListPicker[] _tapHaEntityPickers = new ListPicker[5];
    private readonly StackPanel[] _tapHaEntityPanels = new StackPanel[5];
    private readonly ListPicker[] _dblHaEntityPickers = new ListPicker[5];
    private readonly StackPanel[] _dblHaEntityPanels = new StackPanel[5];
    private readonly ListPicker[] _holdHaEntityPickers = new ListPicker[5];
    private readonly StackPanel[] _holdHaEntityPanels = new StackPanel[5];

    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();
    private HAIntegration? _ha;

    public void SetHA(HAIntegration? ha) => _ha = ha;

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
                btn.ProfileName = copy.ProfileName; btn.PowerAction = copy.PowerAction;
                btn.LinkedKnobIdx = copy.LinkedKnobIdx;

                btn.DoublePressAction = copy.DoublePressAction; btn.DoublePressPath = copy.DoublePressPath;
                btn.DoublePressMacroKeys = copy.DoublePressMacroKeys; btn.DoublePressDeviceId = copy.DoublePressDeviceId;
                btn.DoublePressDeviceIds = copy.DoublePressDeviceIds ?? new List<string>();
                btn.DoublePressProfileName = copy.DoublePressProfileName; btn.DoublePressPowerAction = copy.DoublePressPowerAction;
                btn.DoublePressLinkedKnobIdx = copy.DoublePressLinkedKnobIdx;

                btn.HoldAction = copy.HoldAction; btn.HoldPath = copy.HoldPath; btn.HoldMacroKeys = copy.HoldMacroKeys;
                btn.HoldDeviceId = copy.HoldDeviceId; btn.HoldDeviceIds = copy.HoldDeviceIds ?? new List<string>();
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
                btn.ProfileName = ""; btn.PowerAction = ""; btn.LinkedKnobIdx = -1;

                btn.DoublePressAction = "none"; btn.DoublePressPath = ""; btn.DoublePressMacroKeys = "";
                btn.DoublePressDeviceId = ""; btn.DoublePressDeviceIds = new List<string>();
                btn.DoublePressProfileName = ""; btn.DoublePressPowerAction = ""; btn.DoublePressLinkedKnobIdx = -1;

                btn.HoldAction = "none"; btn.HoldPath = ""; btn.HoldMacroKeys = "";
                btn.HoldDeviceId = ""; btn.HoldDeviceIds = new List<string>();
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

    private void ReloadButtonColumn(int idx, ButtonConfig btn)
    {
        _loading = true;

        // TAP
        SelectCombo(_tapCombos[idx], btn.Action);
        SetTextBoxValue(_tapPathBoxes[idx], btn.Path);
        SetTextBoxValue(_tapMacroBoxes[idx], btn.MacroKeys);
        SelectDevicePicker(_tapDevicePickers[idx], btn.DeviceId);
        _tapCycleDevicePickers[idx].SetCheckedIds(btn.DeviceIds);
        SelectProfilePicker(_tapProfilePickers[idx], btn.ProfileName);
        SelectPowerSegment(_tapPowerSegments[idx], btn.PowerAction);
        SelectKnobPicker(_tapKnobPickers[idx], btn.LinkedKnobIdx);
        SelectHaEntityPicker(_tapHaEntityPickers[idx], btn.Action, btn.Path);
        UpdateTapVisibility(idx, btn.Action);
        UpdateHeaderDisplay(idx);

        // DOUBLE
        SelectCombo(_dblCombos[idx], btn.DoublePressAction);
        SetTextBoxValue(_dblPathBoxes[idx], btn.DoublePressPath);
        SetTextBoxValue(_dblMacroBoxes[idx], btn.DoublePressMacroKeys);
        SelectDevicePicker(_dblDevicePickers[idx], btn.DoublePressDeviceId);
        _dblCycleDevicePickers[idx].SetCheckedIds(btn.DoublePressDeviceIds);
        SelectProfilePicker(_dblProfilePickers[idx], btn.DoublePressProfileName);
        SelectPowerSegment(_dblPowerSegments[idx], btn.DoublePressPowerAction);
        SelectKnobPicker(_dblKnobPickers[idx], btn.DoublePressLinkedKnobIdx);
        SelectHaEntityPicker(_dblHaEntityPickers[idx], btn.DoublePressAction, btn.DoublePressPath);
        UpdateGestureVisibility(_dblPathPanels[idx], _dblPathLabels[idx], _dblBrowseButtons[idx], _dblPickButtons[idx], _dblMacroPanels[idx],
            _dblDevicePanels[idx], _dblCycleDevicePanels[idx], _dblProfilePanels[idx],
            _dblPowerPanels[idx], _dblKnobPanels[idx], _dblHaEntityPanels[idx], btn.DoublePressAction);

        // HOLD
        SelectCombo(_holdCombos[idx], btn.HoldAction);
        SetTextBoxValue(_holdPathBoxes[idx], btn.HoldPath);
        SetTextBoxValue(_holdMacroBoxes[idx], btn.HoldMacroKeys);
        SelectDevicePicker(_holdDevicePickers[idx], btn.HoldDeviceId);
        _holdCycleDevicePickers[idx].SetCheckedIds(btn.HoldDeviceIds);
        SelectProfilePicker(_holdProfilePickers[idx], btn.HoldProfileName);
        SelectPowerSegment(_holdPowerSegments[idx], btn.HoldPowerAction);
        SelectKnobPicker(_holdKnobPickers[idx], btn.HoldLinkedKnobIdx);
        SelectHaEntityPicker(_holdHaEntityPickers[idx], btn.HoldAction, btn.HoldPath);
        UpdateGestureVisibility(_holdPathPanels[idx], _holdPathLabels[idx], _holdBrowseButtons[idx], _holdPickButtons[idx], _holdMacroPanels[idx],
            _holdDevicePanels[idx], _holdCycleDevicePanels[idx], _holdProfilePanels[idx],
            _holdPowerPanels[idx], _holdKnobPanels[idx], _holdHaEntityPanels[idx], btn.HoldAction);

        _loading = false;
    }

    public void LoadConfig(AppConfig config, AudioMixer mixer, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _mixer = mixer;
        _onSave = onSave;

        _audioDevices = mixer.GetAudioDevices();

        for (int i = 0; i < 5; i++)
        {
            PopulateDevicePicker(_tapDevicePickers[i]);
            PopulateCycleDevicePicker(_tapCycleDevicePickers[i]);
            PopulateProfilePicker(_tapProfilePickers[i], config);
            PopulateKnobPicker(_tapKnobPickers[i], config);

            PopulateDevicePicker(_dblDevicePickers[i]);
            PopulateCycleDevicePicker(_dblCycleDevicePickers[i]);
            PopulateProfilePicker(_dblProfilePickers[i], config);
            PopulateKnobPicker(_dblKnobPickers[i], config);

            PopulateDevicePicker(_holdDevicePickers[i]);
            PopulateCycleDevicePicker(_holdCycleDevicePickers[i]);
            PopulateProfilePicker(_holdProfilePickers[i], config);
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
            SetTextBoxValue(_tapPathBoxes[i], btn.Path);
            SetTextBoxValue(_tapMacroBoxes[i], btn.MacroKeys);
            SelectDevicePicker(_tapDevicePickers[i], btn.DeviceId);
            _tapCycleDevicePickers[i].SetCheckedIds(btn.DeviceIds);
            SelectProfilePicker(_tapProfilePickers[i], btn.ProfileName);
            SelectPowerSegment(_tapPowerSegments[i], btn.PowerAction);
            SelectKnobPicker(_tapKnobPickers[i], btn.LinkedKnobIdx);
            SelectHaEntityPicker(_tapHaEntityPickers[i], btn.Action, btn.Path);
            UpdateTapVisibility(i, btn.Action);
            UpdateHeaderDisplay(i);

            // DOUBLE
            SelectCombo(_dblCombos[i], btn.DoublePressAction);
            SetTextBoxValue(_dblPathBoxes[i], btn.DoublePressPath);
            SetTextBoxValue(_dblMacroBoxes[i], btn.DoublePressMacroKeys);
            SelectDevicePicker(_dblDevicePickers[i], btn.DoublePressDeviceId);
            _dblCycleDevicePickers[i].SetCheckedIds(btn.DoublePressDeviceIds);
            SelectProfilePicker(_dblProfilePickers[i], btn.DoublePressProfileName);
            SelectPowerSegment(_dblPowerSegments[i], btn.DoublePressPowerAction);
            SelectKnobPicker(_dblKnobPickers[i], btn.DoublePressLinkedKnobIdx);
            SelectHaEntityPicker(_dblHaEntityPickers[i], btn.DoublePressAction, btn.DoublePressPath);
            UpdateGestureVisibility(_dblPathPanels[i], _dblPathLabels[i], _dblBrowseButtons[i], _dblPickButtons[i], _dblMacroPanels[i],
                _dblDevicePanels[i], _dblCycleDevicePanels[i], _dblProfilePanels[i],
                _dblPowerPanels[i], _dblKnobPanels[i], _dblHaEntityPanels[i], btn.DoublePressAction);

            // HOLD
            SelectCombo(_holdCombos[i], btn.HoldAction);
            SetTextBoxValue(_holdPathBoxes[i], btn.HoldPath);
            SetTextBoxValue(_holdMacroBoxes[i], btn.HoldMacroKeys);
            SelectDevicePicker(_holdDevicePickers[i], btn.HoldDeviceId);
            _holdCycleDevicePickers[i].SetCheckedIds(btn.HoldDeviceIds);
            SelectProfilePicker(_holdProfilePickers[i], btn.HoldProfileName);
            SelectPowerSegment(_holdPowerSegments[i], btn.HoldPowerAction);
            SelectKnobPicker(_holdKnobPickers[i], btn.HoldLinkedKnobIdx);
            SelectHaEntityPicker(_holdHaEntityPickers[i], btn.HoldAction, btn.HoldPath);
            UpdateGestureVisibility(_holdPathPanels[i], _holdPathLabels[i], _holdBrowseButtons[i], _holdPickButtons[i], _holdMacroPanels[i],
                _holdDevicePanels[i], _holdCycleDevicePanels[i], _holdProfilePanels[i],
                _holdPowerPanels[i], _holdKnobPanels[i], _holdHaEntityPanels[i], btn.HoldAction);
        }

        _loading = false;
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

            var (tapHaPanel, tapHaPicker) = MakeListPickerRow("HA ENTITY");
            tapHaPicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            tapHaPicker.DropdownOpening += (_, _) => LoadHaEntitiesIntoPicker(tapHaPicker, GetComboActionValue(_tapCombos[idx]));
            _tapHaEntityPanels[i] = tapHaPanel;
            _tapHaEntityPickers[i] = tapHaPicker;
            tapSection.Children.Add(tapHaPanel);

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

            var (dblHaPanel, dblHaPicker) = MakeListPickerRow("HA ENTITY");
            dblHaPicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            dblHaPicker.DropdownOpening += (_, _) => LoadHaEntitiesIntoPicker(dblHaPicker, GetComboActionValue(_dblCombos[idx]));
            _dblHaEntityPanels[i] = dblHaPanel;
            _dblHaEntityPickers[i] = dblHaPicker;
            dblSection.Children.Add(dblHaPanel);

            dblCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(dblCombo);
                UpdateGestureVisibility(_dblPathPanels[idx], _dblPathLabels[idx], _dblBrowseButtons[idx], _dblPickButtons[idx], _dblMacroPanels[idx],
                    _dblDevicePanels[idx], _dblCycleDevicePanels[idx], _dblProfilePanels[idx],
                    _dblPowerPanels[idx], _dblKnobPanels[idx], _dblHaEntityPanels[idx], val);
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

            var (holdHaPanel, holdHaPicker) = MakeListPickerRow("HA ENTITY");
            holdHaPicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            holdHaPicker.DropdownOpening += (_, _) => LoadHaEntitiesIntoPicker(holdHaPicker, GetComboActionValue(_holdCombos[idx]));
            _holdHaEntityPanels[i] = holdHaPanel;
            _holdHaEntityPickers[i] = holdHaPicker;
            holdSection.Children.Add(holdHaPanel);

            holdCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetComboActionValue(holdCombo);
                UpdateGestureVisibility(_holdPathPanels[idx], _holdPathLabels[idx], _holdBrowseButtons[idx], _holdPickButtons[idx], _holdMacroPanels[idx],
                    _holdDevicePanels[idx], _holdCycleDevicePanels[idx], _holdProfilePanels[idx],
                    _holdPowerPanels[idx], _holdKnobPanels[idx], _holdHaEntityPanels[idx], val);
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
        bool isHaEntityAction = action is "ha_toggle" or "ha_scene";
        bool isHaServiceAction = action == "ha_service";
        _tapPathPanels[idx].Visibility = PathActions.Contains(action) || isHaServiceAction ? Visibility.Visible : Visibility.Collapsed;
        ApplyPathLabelAndButtons(_tapPathLabels[idx], _tapPathBoxes[idx], _tapBrowseButtons[idx], _tapPickButtons[idx], action);
        _tapMacroPanels[idx].Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        _tapDevicePanels[idx].Visibility = action is "select_output" or "select_input" or "mute_device" ? Visibility.Visible : Visibility.Collapsed;
        _tapCycleDevicePanels[idx].Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        _tapProfilePanels[idx].Visibility = action == "switch_profile" ? Visibility.Visible : Visibility.Collapsed;
        _tapPowerPanels[idx].Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        _tapKnobPanels[idx].Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
        _tapHaEntityPanels[idx].Visibility = isHaEntityAction ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void UpdateGestureVisibility(
        StackPanel pathPanel, TextBlock pathLabel, Button browseBtn, Button pickBtn, StackPanel macroPanel,
        StackPanel devicePanel, StackPanel cycleDevicePanel, StackPanel profilePanel,
        StackPanel powerPanel, StackPanel knobPanel, StackPanel haEntityPanel, string action)
    {
        bool isHaEntityAction = action is "ha_toggle" or "ha_scene";
        bool isHaServiceAction = action == "ha_service";
        pathPanel.Visibility = PathActions.Contains(action) || isHaServiceAction ? Visibility.Visible : Visibility.Collapsed;
        ApplyPathLabelAndButtons(pathLabel, null, browseBtn, pickBtn, action);
        macroPanel.Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        devicePanel.Visibility = action is "select_output" or "select_input" or "mute_device" ? Visibility.Visible : Visibility.Collapsed;
        cycleDevicePanel.Visibility = action is "cycle_output" or "cycle_input" ? Visibility.Visible : Visibility.Collapsed;
        profilePanel.Visibility = action == "switch_profile" ? Visibility.Visible : Visibility.Collapsed;
        powerPanel.Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        knobPanel.Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
        haEntityPanel.Visibility = isHaEntityAction ? Visibility.Visible : Visibility.Collapsed;
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
                if (box != null) box.ToolTip = "Full path to the executable to launch";
                browseBtn.Visibility = Visibility.Visible;
                pickBtn.Visibility = Visibility.Collapsed;
                break;
            case "ha_service":
                label.Text = "SERVICE CALL";
                if (box != null) box.ToolTip = "Format: domain.service:entity_id (e.g. light.turn_on:light.office)";
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
            btn.Path = GetHaOrPathValue(_tapCombos[i], _tapHaEntityPickers[i], _tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetSelectedDeviceId(_tapDevicePickers[i]);
            btn.DeviceIds = _tapCycleDevicePickers[i].GetCheckedIds();
            btn.ProfileName = _tapProfilePickers[i].SelectedTag as string ?? "";
            btn.PowerAction = GetSelectedPowerValue(_tapPowerSegments[i]);
            btn.LinkedKnobIdx = int.TryParse(_tapKnobPickers[i].SelectedTag as string, out int ki) ? ki : -1;

            btn.DoublePressAction = GetComboActionValue(_dblCombos[i]);
            btn.DoublePressPath = GetHaOrPathValue(_dblCombos[i], _dblHaEntityPickers[i], _dblPathBoxes[i]);
            btn.DoublePressMacroKeys = GetTextBoxValue(_dblMacroBoxes[i]);
            btn.DoublePressDeviceId = GetSelectedDeviceId(_dblDevicePickers[i]);
            btn.DoublePressDeviceIds = _dblCycleDevicePickers[i].GetCheckedIds();
            btn.DoublePressProfileName = _dblProfilePickers[i].SelectedTag as string ?? "";
            btn.DoublePressPowerAction = GetSelectedPowerValue(_dblPowerSegments[i]);
            btn.DoublePressLinkedKnobIdx = int.TryParse(_dblKnobPickers[i].SelectedTag as string, out int dki) ? dki : -1;

            btn.HoldAction = GetComboActionValue(_holdCombos[i]);
            btn.HoldPath = GetHaOrPathValue(_holdCombos[i], _holdHaEntityPickers[i], _holdPathBoxes[i]);
            btn.HoldMacroKeys = GetTextBoxValue(_holdMacroBoxes[i]);
            btn.HoldDeviceId = GetSelectedDeviceId(_holdDevicePickers[i]);
            btn.HoldDeviceIds = _holdCycleDevicePickers[i].GetCheckedIds();
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
            _tapHaEntityPickers[i].RefreshAccent();
            _tapPowerSegments[i].AccentColor = accent;
            _tapCycleDevicePickers[i].AccentColor = accent;

            _dblDevicePickers[i].RefreshAccent();
            _dblProfilePickers[i].RefreshAccent();
            _dblKnobPickers[i].RefreshAccent();
            _dblHaEntityPickers[i].RefreshAccent();
            _dblPowerSegments[i].AccentColor = accent;
            _dblCycleDevicePickers[i].AccentColor = accent;

            _holdDevicePickers[i].RefreshAccent();
            _holdProfilePickers[i].RefreshAccent();
            _holdKnobPickers[i].RefreshAccent();
            _holdHaEntityPickers[i].RefreshAccent();
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
        { "cycle_brightness",   "Step through LED brightness levels" },
        { "power_sleep",        "Put the PC to sleep" },
        { "power_lock",         "Lock the workstation" },
        { "power_off",          "Shut down the PC" },
        { "power_restart",      "Restart the PC" },
        { "power_logoff",       "Log off the current user" },
        { "power_hibernate",    "Hibernate the PC" },
        { "ha_toggle",          "Toggle a Home Assistant entity on/off" },
        { "ha_scene",           "Activate a Home Assistant scene" },
        { "ha_service",         "Call any Home Assistant service (format: domain.service:entity_id)" },
    };

    private ActionPicker MakeActionCombo()
    {
        var picker = new ActionPicker
        {
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        foreach (var (display, value) in Actions)
        {
            var icon = ActionIcons.GetValueOrDefault(value, "—");
            var color = ActionColors.GetValueOrDefault(value, Color.FromRgb(0x88, 0x88, 0x88));
            var tooltip = ActionTooltips.GetValueOrDefault(value, display);
            picker.AddItem(display, value, icon, color, tooltip);
        }

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

        var browseBtn = new Button
        {
            Content = "...",
            Width = 28,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(4, 0, 0, 0),
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Browse for file",
        };
        DockPanel.SetDock(browseBtn, Dock.Right);
        row.Children.Add(browseBtn);

        var pickBtn = new Button
        {
            Content = "▾",
            Width = 28,
            FontSize = 12,
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(4, 0, 0, 0),
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextSecBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            ToolTip = "Pick from running processes",
        };
        DockPanel.SetDock(pickBtn, Dock.Right);
        row.Children.Add(pickBtn);

        var box = new TextBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 11,
            ToolTip = "Enter part of the process name (e.g. discord, spotify). No full path needed.",
        };
        box.Tag = placeholder;
        box.Text = "";
        SetPlaceholder(box);
        row.Children.Add(box);

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

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };

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
                popup.IsOpen = false;
                QueueSave();
            };
            panel.Children.Add(item);
        }

        popup.Child = border;
        popup.IsOpen = true;
    }

    private static readonly Dictionary<string, string> PickerTooltips = new()
    {
        { "DEVICE",       "Audio device to target" },
        { "CYCLE DEVICES","Devices to cycle through (check to include)" },
        { "PROFILE",      "Profile to switch to when pressed" },
        { "LINKED KNOB",  "Knob whose app group is muted" },
        { "HA ENTITY",    "Home Assistant entity to control (click to load entities)" },
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

    private void PopulateCycleDevicePicker(CheckListPicker picker)
    {
        picker.ClearItems();
        foreach (var (id, name, isOutput) in _audioDevices)
            picker.AddItem($"[{(isOutput ? "OUT" : "IN")}] {name}", id);
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

    private void PopulateDevicePicker(ListPicker picker)
    {
        picker.ClearItems();
        foreach (var (id, name, isOutput) in _audioDevices)
            picker.AddItem($"[{(isOutput ? "OUT" : "IN")}] {name}", id);
    }

    private void SelectDevicePicker(ListPicker picker, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) { picker.SelectedIndex = -1; return; }
        for (int i = 0; i < picker.ItemCount; i++)
            if (picker.GetTagAt(i) as string == deviceId) { picker.SelectedIndex = i; return; }
        picker.SelectedIndex = -1;
    }

    private string GetSelectedDeviceId(ListPicker picker) => picker.SelectedTag as string ?? "";

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

    private void SelectHaEntityPicker(ListPicker picker, string action, string entityId)
    {
        if (action is not ("ha_toggle" or "ha_scene") || string.IsNullOrEmpty(entityId))
        {
            picker.SelectedIndex = -1;
            return;
        }
        // Try to find an existing item by tag (entity ID)
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == entityId)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
        // Not yet loaded — pre-populate with a placeholder so the saved value shows up
        picker.ClearItems();
        picker.AddItem(entityId, entityId);
        picker.SelectedIndex = 0;
    }

    private string GetHaOrPathValue(ActionPicker combo, ListPicker haPicker, TextBox pathBox)
    {
        var action = GetComboActionValue(combo);
        if (action is "ha_toggle" or "ha_scene")
            return haPicker.SelectedTag as string ?? "";
        return GetTextBoxValue(pathBox);
    }

    private void LoadHaEntitiesIntoPicker(ListPicker picker, string action)
    {
        if (_ha == null || !_ha.IsAvailable) return;

        // Keep current selection tag before reload
        var currentTag = picker.SelectedTag as string;

        // Only reload if still showing a placeholder (1 item matching its own display)
        // or empty — avoids redundant network calls when already loaded
        if (picker.ItemCount > 1) return;

        var domain = action == "ha_scene" ? "scene" : null;

        _ = Task.Run(async () =>
        {
            var entities = await _ha.GetEntitiesAsync(domain);
            await Dispatcher.BeginInvoke(() =>
            {
                picker.ClearItems();
                foreach (var e in entities.OrderBy(e => e.FriendlyName))
                {
                    var (icon, color) = HADomainStyles.GetStyle(e.Domain);
                    picker.AddItem(e.FriendlyName, e.EntityId, icon, color);
                }

                // Re-select previously selected entity
                if (!string.IsNullOrEmpty(currentTag))
                {
                    for (int i = 0; i < picker.ItemCount; i++)
                    {
                        if (picker.GetTagAt(i) as string == currentTag)
                        {
                            picker.SelectedIndex = i;
                            break;
                        }
                    }
                }
            });
        });
    }

    private void SelectPowerSegment(SegmentedControl segment, string powerAction)
    {
        if (string.IsNullOrEmpty(powerAction)) { segment.SelectedIndex = -1; return; }
        for (int i = 0; i < segment.SegmentCount; i++)
            if (segment.GetTagAt(i) as string == powerAction) { segment.SelectedIndex = i; return; }
        segment.SelectedIndex = -1;
    }

    private string GetSelectedPowerValue(SegmentedControl segment) => segment.SelectedTag as string ?? "";

    // ── Resource helpers ────────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
}
