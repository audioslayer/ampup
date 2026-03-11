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
        ("Launch App", "launch_exe"), ("Close App", "close_program"),
        ("Cycle Output", "cycle_output"), ("Cycle Input", "cycle_input"),
        ("Set Output", "select_output"), ("Set Input", "select_input"),
        ("Keyboard Macro", "macro"), ("Switch Profile", "switch_profile"),
        ("Cycle Brightness", "cycle_brightness"),
        ("Sleep", "power_sleep"), ("Lock", "power_lock"), ("Off", "power_off"),
        ("Restart", "power_restart"), ("Logoff", "power_logoff"), ("Hibernate", "power_hibernate"),
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
        { "power_sleep", "😴" }, { "power_lock", "🔒" }, { "power_off", "⏻" },
        { "power_restart", "🔄" }, { "power_logoff", "🚪" }, { "power_hibernate", "❄" },
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
    };

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    // Per-column header elements
    private readonly TextBlock[] _headers = new TextBlock[5];
    private readonly TextBlock[] _headerIcons = new TextBlock[5];
    private readonly TextBlock[] _headerActions = new TextBlock[5];
    private readonly Border[] _columnCards = new Border[5];

    // TAP controls
    private readonly ComboBox[] _tapCombos = new ComboBox[5];
    private readonly TextBox[] _tapPathBoxes = new TextBox[5];
    private readonly StackPanel[] _tapPathPanels = new StackPanel[5];
    private readonly Button[] _tapBrowseButtons = new Button[5];
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

    // DOUBLE controls
    private readonly ComboBox[] _dblCombos = new ComboBox[5];
    private readonly TextBox[] _dblPathBoxes = new TextBox[5];
    private readonly StackPanel[] _dblPathPanels = new StackPanel[5];
    private readonly Button[] _dblBrowseButtons = new Button[5];

    // HOLD controls
    private readonly ComboBox[] _holdCombos = new ComboBox[5];
    private readonly TextBox[] _holdPathBoxes = new TextBox[5];
    private readonly StackPanel[] _holdPathPanels = new StackPanel[5];
    private readonly Button[] _holdBrowseButtons = new Button[5];

    private List<(string Id, string Name, bool IsOutput)> _audioDevices = new();

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
            PopulateProfilePicker(_tapProfilePickers[i], config);
            PopulateKnobPicker(_tapKnobPickers[i], config);
        }

        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // Header label from knob config
            var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
            if (knob != null && !string.IsNullOrWhiteSpace(knob.Label))
                _headers[i].Text = knob.Label;

            // TAP
            SelectCombo(_tapCombos[i], btn.Action);
            SetTextBoxValue(_tapPathBoxes[i], btn.Path);
            SetTextBoxValue(_tapMacroBoxes[i], btn.MacroKeys);
            SelectDevicePicker(_tapDevicePickers[i], btn.DeviceId);
            SelectProfilePicker(_tapProfilePickers[i], btn.ProfileName);
            SelectPowerSegment(_tapPowerSegments[i], btn.PowerAction);
            SelectKnobPicker(_tapKnobPickers[i], btn.LinkedKnobIdx);
            UpdateTapVisibility(i, btn.Action);
            UpdateHeaderDisplay(i);

            // DOUBLE
            SelectCombo(_dblCombos[i], btn.DoublePressAction);
            SetTextBoxValue(_dblPathBoxes[i], btn.DoublePressPath);
            UpdateSimpleVisibility(_dblPathPanels[i], _dblBrowseButtons[i], btn.DoublePressAction);

            // HOLD
            SelectCombo(_holdCombos[i], btn.HoldAction);
            SetTextBoxValue(_holdPathBoxes[i], btn.HoldPath);
            UpdateSimpleVisibility(_holdPathPanels[i], _holdBrowseButtons[i], btn.HoldAction);
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

            var header = new TextBlock
            {
                Text = $"BTN {i + 1}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };
            _headers[i] = header;
            headerStack.Children.Add(header);

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

            var (pathPanel, pathBox, browseBtn) = MakePathRow("PATH", "process name or exe path");
            pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPathPanels[i] = pathPanel;
            _tapPathBoxes[i] = pathBox;
            _tapBrowseButtons[i] = browseBtn;
            browseBtn.Click += (_, _) => BrowseForFile(pathBox);
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
            dblCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                UpdateSimpleVisibility(_dblPathPanels[idx], _dblBrowseButtons[idx], GetComboActionValue(dblCombo));
                QueueSave();
            };
            _dblCombos[i] = dblCombo;
            dblSection.Children.Add(dblCombo);

            var (dblPathPanel, dblPathBox, dblBrowseBtn) = MakePathRow("PATH", "process name or exe path");
            dblPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblPathPanels[i] = dblPathPanel;
            _dblPathBoxes[i] = dblPathBox;
            _dblBrowseButtons[i] = dblBrowseBtn;
            dblBrowseBtn.Click += (_, _) => BrowseForFile(dblPathBox);
            dblSection.Children.Add(dblPathPanel);

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
            holdCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                UpdateSimpleVisibility(_holdPathPanels[idx], _holdBrowseButtons[idx], GetComboActionValue(holdCombo));
                QueueSave();
            };
            _holdCombos[i] = holdCombo;
            holdSection.Children.Add(holdCombo);

            var (holdPathPanel, holdPathBox, holdBrowseBtn) = MakePathRow("PATH", "process name or exe path");
            holdPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdPathPanels[i] = holdPathPanel;
            _holdPathBoxes[i] = holdPathBox;
            _holdBrowseButtons[i] = holdBrowseBtn;
            holdBrowseBtn.Click += (_, _) => BrowseForFile(holdPathBox);
            holdSection.Children.Add(holdPathPanel);

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
        _tapPathPanels[idx].Visibility = PathActions.Contains(action) ? Visibility.Visible : Visibility.Collapsed;
        _tapBrowseButtons[idx].Visibility = action == "launch_exe" ? Visibility.Visible : Visibility.Collapsed;
        _tapMacroPanels[idx].Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        _tapDevicePanels[idx].Visibility = action is "select_output" or "select_input" ? Visibility.Visible : Visibility.Collapsed;
        _tapProfilePanels[idx].Visibility = action == "switch_profile" ? Visibility.Visible : Visibility.Collapsed;
        _tapPowerPanels[idx].Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
        _tapKnobPanels[idx].Visibility = action == "mute_app_group" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSimpleVisibility(StackPanel pathPanel, Button browseBtn, string action)
    {
        pathPanel.Visibility = PathActions.Contains(action) ? Visibility.Visible : Visibility.Collapsed;
        browseBtn.Visibility = action == "launch_exe" ? Visibility.Visible : Visibility.Collapsed;
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

            btn.Action = GetComboActionValue(_tapCombos[i]);
            btn.Path = GetTextBoxValue(_tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetSelectedDeviceId(_tapDevicePickers[i]);
            btn.ProfileName = _tapProfilePickers[i].SelectedTag as string ?? "";
            btn.PowerAction = GetSelectedPowerValue(_tapPowerSegments[i]);
            btn.LinkedKnobIdx = int.TryParse(_tapKnobPickers[i].SelectedTag as string, out int ki) ? ki : -1;

            btn.DoublePressAction = GetComboActionValue(_dblCombos[i]);
            btn.DoublePressPath = GetTextBoxValue(_dblPathBoxes[i]);

            btn.HoldAction = GetComboActionValue(_holdCombos[i]);
            btn.HoldPath = GetTextBoxValue(_holdPathBoxes[i]);
        }

        _onSave(_config);
    }

    // ── Control factories ───────────────────────────────────────────

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
            _tapPowerSegments[i].AccentColor = accent;
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

    private ComboBox MakeActionCombo()
    {
        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
        };

        foreach (var (display, value) in Actions)
        {
            var item = new ComboBoxItem
            {
                Content = $"{ActionIcons.GetValueOrDefault(value, "—")}  {display}",
                Tag = value,
            };
            combo.Items.Add(item);
        }

        combo.SelectedIndex = 0; // "None"
        return combo;
    }

    private void SelectCombo(ComboBox combo, string actionValue)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag as string == actionValue)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private string GetComboActionValue(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "none";
        return "none";
    }

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
        };
        box.Tag = placeholder;
        box.Text = "";
        SetPlaceholder(box);
        container.Children.Add(box);
        return (container, box);
    }

    private (StackPanel panel, TextBox box, Button browseBtn) MakePathRow(string label, string placeholder)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));

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

        var box = new TextBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 11,
        };
        box.Tag = placeholder;
        box.Text = "";
        SetPlaceholder(box);
        row.Children.Add(box);

        container.Children.Add(row);
        return (container, box, browseBtn);
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

    private (StackPanel panel, ListPicker picker) MakeListPickerRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));
        var picker = new ListPicker
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        container.Children.Add(picker);
        return (container, picker);
    }

    private (StackPanel panel, SegmentedControl segment) MakePowerSegmentRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        container.Children.Add(MakeLabel(label));
        var segment = new SegmentedControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
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
            Style = FindStyle("SecondaryText"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 3),
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
