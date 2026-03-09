using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WolfMixer.Controls;

namespace WolfMixer.Views;

public partial class ButtonsView : UserControl
{
    private AppConfig? _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private readonly DispatcherTimer _debounce;
    private int _selectedIdx = 0;

    // Action definitions: (DisplayName, ConfigValue)
    private static readonly (string Display, string Value)[] Actions =
    {
        ("None", "none"), ("Play / Pause", "media_play_pause"), ("Next Track", "media_next"),
        ("Prev Track", "media_prev"), ("Mute Volume", "mute_master"), ("Mute Mic", "mute_mic"),
        ("Mute App", "mute_program"), ("Mute Active Window", "mute_active_window"), ("Launch App", "launch_exe"),
        ("Close App", "close_program"), ("Cycle Output", "cycle_output"), ("Cycle Input", "cycle_input"),
        ("Set Output", "select_output"), ("Set Input", "select_input"), ("Keyboard Macro", "macro"),
        ("System Power", "system_power"), ("Switch Profile", "switch_profile")
    };

    // Actions that need a path textbox
    private static readonly string[] PathActions = { "mute_program", "launch_exe", "close_program" };

    // Power action options: (Display, ConfigValue)
    private static readonly (string Display, string Value)[] PowerOptions =
    {
        ("Sleep", "sleep"), ("Lock", "lock"), ("Off", "shutdown"),
        ("Restart", "restart"), ("Logoff", "logoff"), ("Hibernate", "hibernate")
    };

    // Action icon mapping for button tiles
    private static readonly Dictionary<string, string> ActionIcons = new()
    {
        { "none", "—" }, { "media_play_pause", "⏯" }, { "media_next", "⏭" },
        { "media_prev", "⏮" }, { "mute_master", "🔇" }, { "mute_mic", "🎤" },
        { "mute_program", "🔇" }, { "mute_active_window", "🔇" }, { "launch_exe", "🚀" },
        { "close_program", "✕" }, { "cycle_output", "🔊" }, { "cycle_input", "🎙" },
        { "select_output", "🔊" }, { "select_input", "🎙" }, { "macro", "⌨" },
        { "system_power", "⏻" }, { "switch_profile", "📋" }
    };

    // Button tile references
    private readonly Border[] _btnTiles = new Border[5];
    private readonly TextBlock[] _btnIcons = new TextBlock[5];
    private readonly TextBlock[] _btnLabels = new TextBlock[5];
    private readonly TextBlock[] _btnSubLabels = new TextBlock[5];

    // --- TAP controls ---
    private readonly GridPicker[] _tapActionPickers = new GridPicker[5];
    private readonly TextBox[] _tapPathBoxes = new TextBox[5];
    private readonly StackPanel[] _tapPathPanels = new StackPanel[5];
    private readonly TextBox[] _tapMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _tapMacroPanels = new StackPanel[5];
    private readonly ListPicker[] _tapDevicePickers = new ListPicker[5];
    private readonly StackPanel[] _tapDevicePanels = new StackPanel[5];
    private readonly ListPicker[] _tapProfilePickers = new ListPicker[5];
    private readonly StackPanel[] _tapProfilePanels = new StackPanel[5];
    private readonly SegmentedControl[] _tapPowerSegments = new SegmentedControl[5];
    private readonly StackPanel[] _tapPowerPanels = new StackPanel[5];

    // --- DOUBLE controls ---
    private readonly GridPicker[] _dblActionPickers = new GridPicker[5];
    private readonly TextBox[] _dblPathBoxes = new TextBox[5];
    private readonly StackPanel[] _dblPathPanels = new StackPanel[5];

    // --- HOLD controls ---
    private readonly GridPicker[] _holdActionPickers = new GridPicker[5];
    private readonly TextBox[] _holdPathBoxes = new TextBox[5];
    private readonly StackPanel[] _holdPathPanels = new StackPanel[5];

    // Audio devices cache
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

        BuildButtonStrip();
        BuildAllDetailPanels();
        SelectButton(0);
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
        }

        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // TAP
            SelectActionPicker(_tapActionPickers[i], btn.Action);
            SetTextBoxValue(_tapPathBoxes[i], btn.Path);
            SetTextBoxValue(_tapMacroBoxes[i], btn.MacroKeys);
            SelectDevicePicker(_tapDevicePickers[i], btn.DeviceId);
            SelectProfilePicker(_tapProfilePickers[i], btn.ProfileName);
            SelectPowerSegment(_tapPowerSegments[i], btn.PowerAction);
            UpdateTapVisibility(i, btn.Action);

            // DOUBLE
            SelectActionPicker(_dblActionPickers[i], btn.DoublePressAction);
            SetTextBoxValue(_dblPathBoxes[i], btn.DoublePressPath);
            UpdateSimpleVisibility(_dblPathPanels[i], btn.DoublePressAction);

            // HOLD
            SelectActionPicker(_holdActionPickers[i], btn.HoldAction);
            SetTextBoxValue(_holdPathBoxes[i], btn.HoldPath);
            UpdateSimpleVisibility(_holdPathPanels[i], btn.HoldAction);

            // Update tile display
            UpdateTileDisplay(i);
        }

        _loading = false;
    }

    // ── Button strip (top) ─────────────────────────────────────────

    private void BuildButtonStrip()
    {
        for (int i = 0; i < 5; i++)
        {
            int idx = i;

            var tile = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(4),
                Padding = new Thickness(8, 12, 8, 12),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinHeight = 80
            };

            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            var icon = new TextBlock
            {
                Text = "—",
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _btnIcons[i] = icon;
            content.Children.Add(icon);

            var label = new TextBlock
            {
                Text = $"BTN {i + 1}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextPrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _btnLabels[i] = label;
            content.Children.Add(label);

            var subLabel = new TextBlock
            {
                Text = "None",
                FontSize = 9,
                Foreground = FindBrush("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            _btnSubLabels[i] = subLabel;
            content.Children.Add(subLabel);

            tile.Child = content;
            tile.MouseLeftButtonDown += (_, _) => SelectButton(idx);
            _btnTiles[i] = tile;
            ButtonStrip.Children.Add(tile);
        }
    }

    private void SelectButton(int idx)
    {
        _selectedIdx = idx;

        var accent = (Color)ColorConverter.ConvertFromString("#00E676");
        var dim = Color.FromRgb(0x2A, 0x2A, 0x2A);
        var selectedBg = Color.FromRgb(0x1A, 0x2A, 0x30);
        var normalBg = Color.FromRgb(0x1C, 0x1C, 0x1C);

        for (int i = 0; i < 5; i++)
        {
            bool selected = i == idx;
            _btnTiles[i].BorderBrush = new SolidColorBrush(selected ? accent : dim);
            _btnTiles[i].Background = new SolidColorBrush(selected ? selectedBg : normalBg);
            _btnTiles[i].BorderThickness = new Thickness(selected ? 2 : 1);
            _btnLabels[i].Foreground = new SolidColorBrush(selected ? accent : Color.FromRgb(0xE8, 0xE8, 0xE8));
        }

        ShowDetailPanel(idx);
    }

    private void UpdateTileDisplay(int idx)
    {
        var btn = _config?.Buttons.FirstOrDefault(b => b.Idx == idx);
        if (btn == null) return;

        var actionDisplay = GetActionDisplay(btn.Action);
        _btnIcons[idx].Text = ActionIcons.GetValueOrDefault(btn.Action, "—");
        _btnSubLabels[idx].Text = actionDisplay;

        // Color the icon based on whether it has an action
        _btnIcons[idx].Foreground = btn.Action == "none"
            ? FindBrush("TextDimBrush")
            : FindBrush("AccentBrush");
    }

    private static string GetActionDisplay(string actionValue)
    {
        foreach (var (display, value) in Actions)
        {
            if (value == actionValue) return display;
        }
        return "None";
    }

    // ── Detail panels ──────────────────────────────────────────────

    private readonly StackPanel[] _detailPanels = new StackPanel[5];

    private void BuildAllDetailPanels()
    {
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var panel = new StackPanel { Visibility = Visibility.Collapsed };

            // ── Tap gesture card ──
            var tapCard = MakeCard();
            var tapContent = new StackPanel();
            tapContent.Children.Add(MakeGestureHeader("TAP", "Single press — releases < 500ms"));

            var tapGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            tapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: action picker
            var tapLeft = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            tapLeft.Children.Add(MakeLabel("ACTION"));
            var tapPicker = MakeActionPicker();
            tapPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetSelectedActionValue(tapPicker);
                UpdateTapVisibility(idx, val);
                UpdateTileDisplay(idx);
                QueueSave();
            };
            _tapActionPickers[i] = tapPicker;
            tapLeft.Children.Add(tapPicker);
            Grid.SetColumn(tapLeft, 0);
            tapGrid.Children.Add(tapLeft);

            // Right: context controls
            var tapRight = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };

            var (pathPanel, pathBox) = MakeTextBoxRow("PATH", "process name or exe path");
            pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPathPanels[i] = pathPanel;
            _tapPathBoxes[i] = pathBox;
            tapRight.Children.Add(pathPanel);

            var (macroPanel, macroBox) = MakeTextBoxRow("MACRO KEYS", "ctrl+shift+m");
            macroBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapMacroPanels[i] = macroPanel;
            _tapMacroBoxes[i] = macroBox;
            tapRight.Children.Add(macroPanel);

            var (devicePanel, devicePicker) = MakeListPickerRow("DEVICE");
            devicePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapDevicePanels[i] = devicePanel;
            _tapDevicePickers[i] = devicePicker;
            tapRight.Children.Add(devicePanel);

            var (profilePanel, profilePicker) = MakeListPickerRow("PROFILE");
            profilePicker.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapProfilePanels[i] = profilePanel;
            _tapProfilePickers[i] = profilePicker;
            tapRight.Children.Add(profilePanel);

            var (powerPanel, powerSegment) = MakePowerSegmentRow("POWER ACTION");
            powerSegment.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPowerPanels[i] = powerPanel;
            _tapPowerSegments[i] = powerSegment;
            tapRight.Children.Add(powerPanel);

            Grid.SetColumn(tapRight, 1);
            tapGrid.Children.Add(tapRight);

            tapContent.Children.Add(tapGrid);
            tapCard.Child = tapContent;
            panel.Children.Add(tapCard);

            // ── Double press card (2-column: action left, context right) ──
            var dblCard = MakeCard();
            var dblContent = new StackPanel();
            dblContent.Children.Add(MakeGestureHeader("DOUBLE PRESS", "2nd press within 300ms"));

            var dblGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            dblGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dblGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dblLeft = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            dblLeft.Children.Add(MakeLabel("ACTION"));
            var dblPicker = MakeActionPicker();
            dblPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetSelectedActionValue(dblPicker);
                UpdateSimpleVisibility(_dblPathPanels[idx], val);
                QueueSave();
            };
            _dblActionPickers[i] = dblPicker;
            dblLeft.Children.Add(dblPicker);
            Grid.SetColumn(dblLeft, 0);
            dblGrid.Children.Add(dblLeft);

            var dblRight = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            var (dblPathPanel, dblPathBox) = MakeTextBoxRow("PATH", "process name or exe path");
            dblPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblPathPanels[i] = dblPathPanel;
            _dblPathBoxes[i] = dblPathBox;
            dblRight.Children.Add(dblPathPanel);
            Grid.SetColumn(dblRight, 1);
            dblGrid.Children.Add(dblRight);

            dblContent.Children.Add(dblGrid);
            dblCard.Child = dblContent;
            panel.Children.Add(dblCard);

            // ── Hold card (2-column: action left, context right) ──
            var holdCard = MakeCard();
            var holdContent = new StackPanel();
            holdContent.Children.Add(MakeGestureHeader("HOLD", "Held 500ms+"));

            var holdGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            holdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            holdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var holdLeft = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            holdLeft.Children.Add(MakeLabel("ACTION"));
            var holdPicker = MakeActionPicker();
            holdPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetSelectedActionValue(holdPicker);
                UpdateSimpleVisibility(_holdPathPanels[idx], val);
                QueueSave();
            };
            _holdActionPickers[i] = holdPicker;
            holdLeft.Children.Add(holdPicker);
            Grid.SetColumn(holdLeft, 0);
            holdGrid.Children.Add(holdLeft);

            var holdRight = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            var (holdPathPanel, holdPathBox) = MakeTextBoxRow("PATH", "process name or exe path");
            holdPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdPathPanels[i] = holdPathPanel;
            _holdPathBoxes[i] = holdPathBox;
            holdRight.Children.Add(holdPathPanel);
            Grid.SetColumn(holdRight, 1);
            holdGrid.Children.Add(holdRight);

            holdContent.Children.Add(holdGrid);
            holdCard.Child = holdContent;
            panel.Children.Add(holdCard);
            _detailPanels[i] = panel;
            DetailPanel.Children.Add(panel);
        }
    }

    private void ShowDetailPanel(int idx)
    {
        for (int i = 0; i < 5; i++)
            _detailPanels[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Visibility logic ───────────────────────────────────────────

    private void UpdateTapVisibility(int idx, string action)
    {
        _tapPathPanels[idx].Visibility = PathActions.Contains(action) ? Visibility.Visible : Visibility.Collapsed;
        _tapMacroPanels[idx].Visibility = action == "macro" ? Visibility.Visible : Visibility.Collapsed;
        _tapDevicePanels[idx].Visibility = (action == "select_output" || action == "select_input") ? Visibility.Visible : Visibility.Collapsed;
        _tapProfilePanels[idx].Visibility = action == "switch_profile" ? Visibility.Visible : Visibility.Collapsed;
        _tapPowerPanels[idx].Visibility = action == "system_power" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSimpleVisibility(StackPanel pathPanel, string action)
    {
        pathPanel.Visibility = PathActions.Contains(action) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Collect and save ───────────────────────────────────────────

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

            btn.Action = GetSelectedActionValue(_tapActionPickers[i]);
            btn.Path = GetTextBoxValue(_tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetSelectedDeviceId(_tapDevicePickers[i]);
            btn.ProfileName = _tapProfilePickers[i].SelectedTag as string ?? "";
            btn.PowerAction = GetSelectedPowerValue(_tapPowerSegments[i]);

            btn.DoublePressAction = GetSelectedActionValue(_dblActionPickers[i]);
            btn.DoublePressPath = GetTextBoxValue(_dblPathBoxes[i]);

            btn.HoldAction = GetSelectedActionValue(_holdActionPickers[i]);
            btn.HoldPath = GetTextBoxValue(_holdPathBoxes[i]);
        }

        // Update tile for current button
        for (int i = 0; i < 5; i++)
            UpdateTileDisplay(i);

        _onSave(_config);
    }

    // ── Control factories ──────────────────────────────────────────

    private Border MakeCard()
    {
        return new Border
        {
            Style = FindStyle("CardPanel"),
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private StackPanel MakeGestureHeader(string title, string subtitle)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("AccentBrush")
        });
        header.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 10,
            Foreground = FindBrush("TextDimBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        return header;
    }

    private GridPicker MakeActionPicker()
    {
        var picker = new GridPicker
        {
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // "None" — uncategorized first item
        picker.AddItem("None", "none");

        // Media
        picker.AddCategory("Media");
        picker.AddItem("Play / Pause", "media_play_pause");
        picker.AddItem("Next Track", "media_next");
        picker.AddItem("Prev Track", "media_prev");

        // Mute
        picker.AddCategory("Mute");
        picker.AddItem("Mute Volume", "mute_master");
        picker.AddItem("Mute Mic", "mute_mic");
        picker.AddItem("Mute App", "mute_program");
        picker.AddItem("Mute Active Window", "mute_active_window");

        // App Control
        picker.AddCategory("App Control");
        picker.AddItem("Launch App", "launch_exe");
        picker.AddItem("Close App", "close_program");

        // Device
        picker.AddCategory("Device");
        picker.AddItem("Cycle Output", "cycle_output");
        picker.AddItem("Cycle Input", "cycle_input");
        picker.AddItem("Set Output", "select_output");
        picker.AddItem("Set Input", "select_input");

        // System
        picker.AddCategory("System");
        picker.AddItem("Keyboard Macro", "macro");
        picker.AddItem("System Power", "system_power");
        picker.AddItem("Switch Profile", "switch_profile");

        picker.SelectedIndex = 0;
        return picker;
    }

    private (StackPanel panel, TextBox box) MakeTextBoxRow(string label, string placeholder)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
        container.Children.Add(MakeLabel(label));
        var box = new TextBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(6, 4, 6, 4)
        };
        box.Tag = placeholder;
        box.Text = "";
        SetPlaceholder(box);
        container.Children.Add(box);
        return (container, box);
    }

    private (StackPanel panel, ListPicker picker) MakeListPickerRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
        container.Children.Add(MakeLabel(label));
        var picker = new ListPicker
        {
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        container.Children.Add(picker);
        return (container, picker);
    }

    private (StackPanel panel, SegmentedControl segment) MakePowerSegmentRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
        container.Children.Add(MakeLabel(label));
        var segment = new SegmentedControl
        {
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
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
            Margin = new Thickness(0, 0, 0, 3)
        };
    }

    // ── Placeholder simulation ─────────────────────────────────────

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
        var primaryBrush = FindBrush("TextPrimaryBrush");
        var dimBrush = FindBrush("TextDimBrush");

        if (!string.IsNullOrEmpty(value))
        {
            box.Text = value;
            box.Foreground = primaryBrush;
        }
        else
        {
            box.Text = placeholder;
            box.Foreground = dimBrush;
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

    private void SelectActionPicker(GridPicker picker, string actionValue)
    {
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == actionValue)
            {
                picker.SelectedIndex = i;
                return;
            }
        }
        picker.SelectedIndex = 0;
    }

    private string GetSelectedActionValue(GridPicker picker)
    {
        return picker.SelectedTag as string ?? "none";
    }

    private void PopulateDevicePicker(ListPicker picker)
    {
        picker.ClearItems();
        foreach (var (id, name, isOutput) in _audioDevices)
        {
            var tag = isOutput ? "OUT" : "IN";
            picker.AddItem($"[{tag}] {name}", id);
        }
    }

    private void SelectDevicePicker(ListPicker picker, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) { picker.SelectedIndex = -1; return; }
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == deviceId)
            { picker.SelectedIndex = i; return; }
        }
        picker.SelectedIndex = -1;
    }

    private string GetSelectedDeviceId(ListPicker picker)
    {
        return picker.SelectedTag as string ?? "";
    }

    private void PopulateProfilePicker(ListPicker picker, AppConfig config)
    {
        picker.ClearItems();
        foreach (var profile in config.Profiles)
            picker.AddItem(profile, profile);
    }

    private void SelectProfilePicker(ListPicker picker, string profileName)
    {
        if (string.IsNullOrEmpty(profileName)) { picker.SelectedIndex = -1; return; }
        for (int i = 0; i < picker.ItemCount; i++)
        {
            if (picker.GetTagAt(i) as string == profileName)
            { picker.SelectedIndex = i; return; }
        }
        picker.SelectedIndex = -1;
    }

    private void SelectPowerSegment(SegmentedControl segment, string powerAction)
    {
        if (string.IsNullOrEmpty(powerAction)) { segment.SelectedIndex = -1; return; }
        for (int i = 0; i < segment.SegmentCount; i++)
        {
            if (segment.GetTagAt(i) as string == powerAction)
            { segment.SelectedIndex = i; return; }
        }
        segment.SelectedIndex = -1;
    }

    private string GetSelectedPowerValue(SegmentedControl segment)
    {
        return segment.SelectedTag as string ?? "";
    }

    // ── Resource helpers ───────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
}
