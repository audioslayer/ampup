using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

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

    // Power action options
    private static readonly string[] PowerActions = { "sleep", "lock", "shutdown", "restart", "logoff", "hibernate" };

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
    private readonly ComboBox[] _tapActionCombos = new ComboBox[5];
    private readonly TextBox[] _tapPathBoxes = new TextBox[5];
    private readonly StackPanel[] _tapPathPanels = new StackPanel[5];
    private readonly TextBox[] _tapMacroBoxes = new TextBox[5];
    private readonly StackPanel[] _tapMacroPanels = new StackPanel[5];
    private readonly ComboBox[] _tapDeviceCombos = new ComboBox[5];
    private readonly StackPanel[] _tapDevicePanels = new StackPanel[5];
    private readonly ComboBox[] _tapProfileCombos = new ComboBox[5];
    private readonly StackPanel[] _tapProfilePanels = new StackPanel[5];
    private readonly ComboBox[] _tapPowerCombos = new ComboBox[5];
    private readonly StackPanel[] _tapPowerPanels = new StackPanel[5];

    // --- DOUBLE controls ---
    private readonly ComboBox[] _dblActionCombos = new ComboBox[5];
    private readonly TextBox[] _dblPathBoxes = new TextBox[5];
    private readonly StackPanel[] _dblPathPanels = new StackPanel[5];

    // --- HOLD controls ---
    private readonly ComboBox[] _holdActionCombos = new ComboBox[5];
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
            PopulateDeviceCombo(_tapDeviceCombos[i]);
            PopulateProfileCombo(_tapProfileCombos[i], config);
        }

        for (int i = 0; i < 5; i++)
        {
            var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
            if (btn == null) continue;

            // TAP
            SelectActionCombo(_tapActionCombos[i], btn.Action);
            SetTextBoxValue(_tapPathBoxes[i], btn.Path);
            SetTextBoxValue(_tapMacroBoxes[i], btn.MacroKeys);
            SelectDeviceCombo(_tapDeviceCombos[i], btn.DeviceId);
            SelectProfileCombo(_tapProfileCombos[i], btn.ProfileName);
            SelectPowerCombo(_tapPowerCombos[i], btn.PowerAction);
            UpdateTapVisibility(i, btn.Action);

            // DOUBLE
            SelectActionCombo(_dblActionCombos[i], btn.DoublePressAction);
            SetTextBoxValue(_dblPathBoxes[i], btn.DoublePressPath);
            UpdateSimpleVisibility(_dblPathPanels[i], btn.DoublePressAction);

            // HOLD
            SelectActionCombo(_holdActionCombos[i], btn.HoldAction);
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

        var accent = (Color)ColorConverter.ConvertFromString("#00B4D8");
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

            // Left: action combo
            var tapLeft = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            tapLeft.Children.Add(MakeLabel("ACTION"));
            var tapCombo = MakeActionCombo();
            tapCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetSelectedActionValue(tapCombo);
                UpdateTapVisibility(idx, val);
                UpdateTileDisplay(idx);
                QueueSave();
            };
            _tapActionCombos[i] = tapCombo;
            tapLeft.Children.Add(tapCombo);
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

            var (devicePanel, deviceCombo) = MakeComboRow("DEVICE");
            deviceCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapDevicePanels[i] = devicePanel;
            _tapDeviceCombos[i] = deviceCombo;
            tapRight.Children.Add(devicePanel);

            var (profilePanel, profileCombo) = MakeComboRow("PROFILE");
            profileCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapProfilePanels[i] = profilePanel;
            _tapProfileCombos[i] = profileCombo;
            tapRight.Children.Add(profilePanel);

            var (powerPanel, powerCombo) = MakeComboRow("POWER ACTION");
            powerCombo.ItemsSource = PowerActions;
            powerCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
            _tapPowerPanels[i] = powerPanel;
            _tapPowerCombos[i] = powerCombo;
            tapRight.Children.Add(powerPanel);

            Grid.SetColumn(tapRight, 1);
            tapGrid.Children.Add(tapRight);

            tapContent.Children.Add(tapGrid);
            tapCard.Child = tapContent;
            panel.Children.Add(tapCard);

            // ── Double press + Hold in a side-by-side row ──
            var gestureRow = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            gestureRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gestureRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Double press card
            var dblCard = MakeCard();
            dblCard.Margin = new Thickness(0, 0, 6, 0);
            var dblContent = new StackPanel();
            dblContent.Children.Add(MakeGestureHeader("DOUBLE PRESS", "2nd press within 300ms"));
            dblContent.Children.Add(MakeLabel("ACTION"));
            var dblCombo = MakeActionCombo();
            dblCombo.Margin = new Thickness(0, 0, 0, 8);
            dblCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetSelectedActionValue(dblCombo);
                UpdateSimpleVisibility(_dblPathPanels[idx], val);
                QueueSave();
            };
            _dblActionCombos[i] = dblCombo;
            dblContent.Children.Add(dblCombo);

            var (dblPathPanel, dblPathBox) = MakeTextBoxRow("PATH", "process name or exe path");
            dblPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _dblPathPanels[i] = dblPathPanel;
            _dblPathBoxes[i] = dblPathBox;
            dblContent.Children.Add(dblPathPanel);

            dblCard.Child = dblContent;
            Grid.SetColumn(dblCard, 0);
            gestureRow.Children.Add(dblCard);

            // Hold card
            var holdCard = MakeCard();
            holdCard.Margin = new Thickness(6, 0, 0, 0);
            var holdContent = new StackPanel();
            holdContent.Children.Add(MakeGestureHeader("HOLD", "Held 500ms+"));
            holdContent.Children.Add(MakeLabel("ACTION"));
            var holdCombo = MakeActionCombo();
            holdCombo.Margin = new Thickness(0, 0, 0, 8);
            holdCombo.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var val = GetSelectedActionValue(holdCombo);
                UpdateSimpleVisibility(_holdPathPanels[idx], val);
                QueueSave();
            };
            _holdActionCombos[i] = holdCombo;
            holdContent.Children.Add(holdCombo);

            var (holdPathPanel, holdPathBox) = MakeTextBoxRow("PATH", "process name or exe path");
            holdPathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
            _holdPathPanels[i] = holdPathPanel;
            _holdPathBoxes[i] = holdPathBox;
            holdContent.Children.Add(holdPathPanel);

            holdCard.Child = holdContent;
            Grid.SetColumn(holdCard, 1);
            gestureRow.Children.Add(holdCard);

            panel.Children.Add(gestureRow);
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

            btn.Action = GetSelectedActionValue(_tapActionCombos[i]);
            btn.Path = GetTextBoxValue(_tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetSelectedDeviceId(_tapDeviceCombos[i]);
            btn.ProfileName = _tapProfileCombos[i].SelectedItem as string ?? "";
            btn.PowerAction = _tapPowerCombos[i].SelectedItem as string ?? "";

            btn.DoublePressAction = GetSelectedActionValue(_dblActionCombos[i]);
            btn.DoublePressPath = GetTextBoxValue(_dblPathBoxes[i]);

            btn.HoldAction = GetSelectedActionValue(_holdActionCombos[i]);
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

    private ComboBox MakeActionCombo()
    {
        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 4, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var (display, _) in Actions)
            combo.Items.Add(display);
        combo.SelectedIndex = 0;
        return combo;
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

    private (StackPanel panel, ComboBox combo) MakeComboRow(string label)
    {
        var container = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
        container.Children.Add(MakeLabel(label));
        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        container.Children.Add(combo);
        return (container, combo);
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

    // ── Combo helpers ──────────────────────────────────────────────

    private void SelectActionCombo(ComboBox combo, string actionValue)
    {
        for (int i = 0; i < Actions.Length; i++)
        {
            if (Actions[i].Value == actionValue)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private string GetSelectedActionValue(ComboBox combo)
    {
        int idx = combo.SelectedIndex;
        if (idx >= 0 && idx < Actions.Length)
            return Actions[idx].Value;
        return "none";
    }

    private void PopulateDeviceCombo(ComboBox combo)
    {
        combo.Items.Clear();
        foreach (var (id, name, isOutput) in _audioDevices)
        {
            var tag = isOutput ? "OUT" : "IN";
            combo.Items.Add(new ComboBoxItem { Content = $"[{tag}] {name}", Tag = id });
        }
    }

    private void SelectDeviceCombo(ComboBox combo, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) { combo.SelectedIndex = -1; return; }
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag as string == deviceId)
            { combo.SelectedIndex = i; return; }
        }
        combo.SelectedIndex = -1;
    }

    private string GetSelectedDeviceId(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item)
            return item.Tag as string ?? "";
        return "";
    }

    private void PopulateProfileCombo(ComboBox combo, AppConfig config)
    {
        combo.Items.Clear();
        foreach (var profile in config.Profiles)
            combo.Items.Add(profile);
    }

    private void SelectProfileCombo(ComboBox combo, string profileName)
    {
        if (string.IsNullOrEmpty(profileName)) { combo.SelectedIndex = -1; return; }
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] as string == profileName)
            { combo.SelectedIndex = i; return; }
        }
        combo.SelectedIndex = -1;
    }

    private void SelectPowerCombo(ComboBox combo, string powerAction)
    {
        if (string.IsNullOrEmpty(powerAction)) { combo.SelectedIndex = -1; return; }
        for (int i = 0; i < PowerActions.Length; i++)
        {
            if (PowerActions[i] == powerAction)
            { combo.SelectedIndex = i; return; }
        }
        combo.SelectedIndex = -1;
    }

    // ── Resource helpers ───────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
}
