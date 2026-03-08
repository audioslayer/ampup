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

    // Action definitions: (DisplayName, ConfigValue)
    private static readonly (string Display, string Value)[] Actions =
    {
        ("None", "none"), ("Play/Pause", "media_play_pause"), ("Next Track", "media_next"),
        ("Prev Track", "media_prev"), ("Mute Vol", "mute_master"), ("Mute Mic", "mute_mic"),
        ("Mute App", "mute_program"), ("Mute Active", "mute_active_window"), ("Launch App", "launch_exe"),
        ("Close App", "close_program"), ("Cycle Output", "cycle_output"), ("Cycle Input", "cycle_input"),
        ("Set Output", "select_output"), ("Set Input", "select_input"), ("Macro", "macro"),
        ("Sys Power", "system_power"), ("Switch Profile", "switch_profile")
    };

    // Actions that need a path textbox
    private static readonly string[] PathActions = { "mute_program", "launch_exe", "close_program" };

    // Power action options
    private static readonly string[] PowerActions = { "sleep", "lock", "shutdown", "restart", "logoff", "hibernate" };

    // --- TAP controls (all sub-control types) ---
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

    // --- DOUBLE controls (path only) ---
    private readonly ComboBox[] _dblActionCombos = new ComboBox[5];
    private readonly TextBox[] _dblPathBoxes = new TextBox[5];
    private readonly StackPanel[] _dblPathPanels = new StackPanel[5];

    // --- HOLD controls (path only) ---
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

        BuildControls();
    }

    public void LoadConfig(AppConfig config, AudioMixer mixer, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _mixer = mixer;
        _onSave = onSave;

        _audioDevices = mixer.GetAudioDevices();

        // Populate device combos with fresh device list
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
        }

        _loading = false;
    }

    private void BuildControls()
    {
        var panels = new[] { Btn0Panel, Btn1Panel, Btn2Panel, Btn3Panel, Btn4Panel };

        for (int i = 0; i < 5; i++)
        {
            int idx = i; // capture
            var panel = panels[i];

            // Header
            panel.Children.Add(new TextBlock
            {
                Text = $"BTN {i + 1}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("AccentBrush"),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // --- TAP SECTION ---
            BuildTapSection(panel, idx);

            // Separator
            panel.Children.Add(MakeSeparator());

            // --- DOUBLE SECTION ---
            BuildDoubleSection(panel, idx);

            // Separator
            panel.Children.Add(MakeSeparator());

            // --- HOLD SECTION ---
            BuildHoldSection(panel, idx);
        }
    }

    private void BuildTapSection(StackPanel panel, int idx)
    {
        panel.Children.Add(MakeSectionHeader("TAP"));

        // Action combo
        panel.Children.Add(MakeLabel("ACTION"));
        var combo = MakeActionCombo();
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var val = GetSelectedActionValue(combo);
            UpdateTapVisibility(idx, val);
            QueueSave();
        };
        _tapActionCombos[idx] = combo;
        panel.Children.Add(combo);

        // Path
        var (pathPanel, pathBox) = MakeTextBoxRow("PATH", "process name or exe path");
        pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
        _tapPathPanels[idx] = pathPanel;
        _tapPathBoxes[idx] = pathBox;
        panel.Children.Add(pathPanel);

        // Macro
        var (macroPanel, macroBox) = MakeTextBoxRow("MACRO KEYS", "ctrl+shift+m");
        macroBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
        _tapMacroPanels[idx] = macroPanel;
        _tapMacroBoxes[idx] = macroBox;
        panel.Children.Add(macroPanel);

        // Device
        var (devicePanel, deviceCombo) = MakeComboRow("DEVICE");
        deviceCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        _tapDevicePanels[idx] = devicePanel;
        _tapDeviceCombos[idx] = deviceCombo;
        panel.Children.Add(devicePanel);

        // Profile
        var (profilePanel, profileCombo) = MakeComboRow("PROFILE");
        profileCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        _tapProfilePanels[idx] = profilePanel;
        _tapProfileCombos[idx] = profileCombo;
        panel.Children.Add(profilePanel);

        // Power
        var (powerPanel, powerCombo) = MakeComboRow("POWER ACTION");
        powerCombo.ItemsSource = PowerActions;
        powerCombo.SelectionChanged += (_, _) => { if (!_loading) QueueSave(); };
        _tapPowerPanels[idx] = powerPanel;
        _tapPowerCombos[idx] = powerCombo;
        panel.Children.Add(powerPanel);
    }

    private void BuildDoubleSection(StackPanel panel, int idx)
    {
        panel.Children.Add(MakeSectionHeader("DOUBLE"));

        panel.Children.Add(MakeLabel("ACTION"));
        var combo = MakeActionCombo();
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var val = GetSelectedActionValue(combo);
            UpdateSimpleVisibility(_dblPathPanels[idx], val);
            QueueSave();
        };
        _dblActionCombos[idx] = combo;
        panel.Children.Add(combo);

        // Path only for double
        var (pathPanel, pathBox) = MakeTextBoxRow("PATH", "process name or exe path");
        pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
        _dblPathPanels[idx] = pathPanel;
        _dblPathBoxes[idx] = pathBox;
        panel.Children.Add(pathPanel);
    }

    private void BuildHoldSection(StackPanel panel, int idx)
    {
        panel.Children.Add(MakeSectionHeader("HOLD"));

        panel.Children.Add(MakeLabel("ACTION"));
        var combo = MakeActionCombo();
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var val = GetSelectedActionValue(combo);
            UpdateSimpleVisibility(_holdPathPanels[idx], val);
            QueueSave();
        };
        _holdActionCombos[idx] = combo;
        panel.Children.Add(combo);

        // Path only for hold
        var (pathPanel, pathBox) = MakeTextBoxRow("PATH", "process name or exe path");
        pathBox.TextChanged += (_, _) => { if (!_loading) QueueSave(); };
        _holdPathPanels[idx] = pathPanel;
        _holdPathBoxes[idx] = pathBox;
        panel.Children.Add(pathPanel);
    }

    // --- Visibility logic ---

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

    // --- Collect and save ---

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

            // TAP
            btn.Action = GetSelectedActionValue(_tapActionCombos[i]);
            btn.Path = GetTextBoxValue(_tapPathBoxes[i]);
            btn.MacroKeys = GetTextBoxValue(_tapMacroBoxes[i]);
            btn.DeviceId = GetSelectedDeviceId(_tapDeviceCombos[i]);
            btn.ProfileName = _tapProfileCombos[i].SelectedItem as string ?? "";
            btn.PowerAction = _tapPowerCombos[i].SelectedItem as string ?? "";

            // DOUBLE
            btn.DoublePressAction = GetSelectedActionValue(_dblActionCombos[i]);
            btn.DoublePressPath = GetTextBoxValue(_dblPathBoxes[i]);

            // HOLD
            btn.HoldAction = GetSelectedActionValue(_holdActionCombos[i]);
            btn.HoldPath = GetTextBoxValue(_holdPathBoxes[i]);
        }

        _onSave(_config);
    }

    // --- Control factory helpers ---

    private ComboBox MakeActionCombo()
    {
        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            Margin = new Thickness(0, 0, 0, 8),
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
            Padding = new Thickness(4, 3, 4, 3)
        };
        // WPF doesn't have built-in placeholder, use tag + events
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
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 3)
        };
    }

    private TextBlock MakeSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextSecBrush"),
            Margin = new Thickness(0, 4, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private Rectangle MakeSeparator()
    {
        return new Rectangle
        {
            Height = 1,
            Fill = FindBrush("CardBorderBrush"),
            Margin = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    // --- Placeholder simulation for TextBox ---

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

        // Initialize
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
        // If showing placeholder, return empty
        if (box.Text == placeholder && Equals(box.Foreground, dimBrush))
            return "";
        return box.Text.Trim();
    }

    // --- Combo helpers ---

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
        combo.SelectedIndex = 0; // default to "None"
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
        if (string.IsNullOrEmpty(deviceId))
        {
            combo.SelectedIndex = -1;
            return;
        }
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag as string == deviceId)
            {
                combo.SelectedIndex = i;
                return;
            }
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
        if (string.IsNullOrEmpty(profileName))
        {
            combo.SelectedIndex = -1;
            return;
        }
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] as string == profileName)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = -1;
    }

    private void SelectPowerCombo(ComboBox combo, string powerAction)
    {
        if (string.IsNullOrEmpty(powerAction))
        {
            combo.SelectedIndex = -1;
            return;
        }
        for (int i = 0; i < PowerActions.Length; i++)
        {
            if (PowerActions[i] == powerAction)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = -1;
    }

    // --- Resource helpers ---

    private Brush FindBrush(string key)
    {
        return (Brush)(FindResource(key) ?? Brushes.White);
    }

    private Style? FindStyle(string key)
    {
        return FindResource(key) as Style;
    }
}
