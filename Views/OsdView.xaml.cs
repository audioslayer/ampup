using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using AmpUp.Core.Services;

namespace AmpUp.Views;

public partial class OsdView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private readonly DispatcherTimer _debounceTimer;
    private bool _loading;
    private bool _configLoaded;

    /// <summary>Set by MainWindow — called when Quick Wheel changes require a full view refresh.</summary>
    public Action? OnRequestRefresh;

    public OsdView()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); CollectAndSave(); };

        // OSD events
        ChkOsdVolume.Checked += OnValueChanged;
        ChkOsdVolume.Unchecked += OnValueChanged;
        ChkOsdProfile.Checked += OnValueChanged;
        ChkOsdProfile.Unchecked += OnValueChanged;
        ChkOsdDevice.Checked += OnValueChanged;
        ChkOsdDevice.Unchecked += OnValueChanged;
        // Duration sliders: half-second steps, value shown in separate label
        var sliderLabels = new[] {
            (SldOsdVolumeDur, LblOsdVolumeDur),
            (SldOsdProfileDur, LblOsdProfileDur),
            (SldOsdDeviceDur, LblOsdDeviceDur),
            (SldOsdWheelDur, LblOsdWheelDur),
        };
        foreach (var (sld, lbl) in sliderLabels)
        {
            sld.Step = 0.1;
            sld.ValueChanged += (s, _) =>
            {
                lbl.Text = $"{sld.Value:F1}s";
                OnValueChanged(s!, EventArgs.Empty);
            };
        }
        BtnOsdPreview.Click += OnOsdPreview;
        ChkHideInFullscreen.Checked += OnValueChanged;
        ChkHideInFullscreen.Unchecked += OnValueChanged;

        // Quick wheels
        BtnAddWheel.Click += (_, _) => AddWheelRow(new QuickWheelConfig { Enabled = true });

        // Refresh monitor list when display config changes (monitors added/removed)
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        IsVisibleChanged += OnVisibilityChanged;
        Unloaded += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_config == null) return;
            _loading = true;
            PopulateOsdMonitorPicker(_config.Osd.MonitorIndex);
            _loading = false;
        });
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _config != null)
        {
            _loading = true;
            PopulateOsdMonitorPicker(_config.Osd.MonitorIndex);
            _loading = false;
        }
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        // OSD
        ChkOsdVolume.IsChecked = config.Osd.ShowVolume;
        ChkOsdProfile.IsChecked = config.Osd.ShowProfileSwitch;
        ChkOsdDevice.IsChecked = config.Osd.ShowDeviceSwitch;
        SldOsdVolumeDur.Value = config.Osd.VolumeDuration;
        LblOsdVolumeDur.Text = $"{config.Osd.VolumeDuration:F1}s";
        SldOsdProfileDur.Value = config.Osd.ProfileDuration;
        LblOsdProfileDur.Text = $"{config.Osd.ProfileDuration:F1}s";
        SldOsdDeviceDur.Value = config.Osd.DeviceDuration;
        LblOsdDeviceDur.Text = $"{config.Osd.DeviceDuration:F1}s";
        SldOsdWheelDur.Value = config.Osd.WheelDuration;
        LblOsdWheelDur.Text = $"{config.Osd.WheelDuration:F1}s";
        HighlightOsdPosition(config.Osd.Position);
        PopulateOsdMonitorPicker(config.Osd.MonitorIndex);
        ChkHideInFullscreen.IsChecked = config.Osd.HideInFullscreen;

        // Quick wheels
        WheelRowsPanel.Children.Clear();
        foreach (var qw in config.Osd.QuickWheels)
            AddWheelRow(qw);

        _loading = false;
        _configLoaded = true;
    }

    private void OnValueChanged(object sender, EventArgs e)
    {
        if (_loading) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnValueChangedCombo(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }



    private void CollectAndSave()
    {
        if (_config == null || _onSave == null || !_configLoaded) return;

        _config.Osd.ShowVolume = ChkOsdVolume.IsChecked == true;
        _config.Osd.ShowProfileSwitch = ChkOsdProfile.IsChecked == true;
        _config.Osd.ShowDeviceSwitch = ChkOsdDevice.IsChecked == true;
        _config.Osd.VolumeDuration = Math.Round(SldOsdVolumeDur.Value, 1);
        _config.Osd.ProfileDuration = Math.Round(SldOsdProfileDur.Value, 1);
        _config.Osd.DeviceDuration = Math.Round(SldOsdDeviceDur.Value, 1);
        _config.Osd.WheelDuration = Math.Round(SldOsdWheelDur.Value, 1);
        _config.Osd.HideInFullscreen = ChkHideInFullscreen.IsChecked == true;

        // Collect quick wheels from dynamic rows
        var oldButtons = new HashSet<int>(_config.Osd.QuickWheels.Where(w => w.Enabled).Select(w => w.TriggerButton));
        _config.Osd.QuickWheels = CollectWheelConfigs();
        var newButtons = new HashSet<int>(_config.Osd.QuickWheels.Where(w => w.Enabled).Select(w => w.TriggerButton));

        // Sync button hold actions
        SyncWheelButtonActions(oldButtons, newButtons);

        _onSave(_config);

        // Refresh Buttons tab if wheel bindings changed
        if (!oldButtons.SetEquals(newButtons)) OnRequestRefresh?.Invoke();
    }

    // ── Quick Wheel dynamic rows ──────────────────────────────────────

    // Available actions for custom wheel slots
    private static readonly (string id, string label)[] CustomSlotActions =
    {
        ("media_play_pause", "Play / Pause"),
        ("media_next", "Next Track"),
        ("media_prev", "Previous Track"),
        ("mute_master", "Mute Master"),
        ("mute_mic", "Mute Mic"),
        ("mute_active_window", "Mute Active Window"),
        ("cycle_brightness", "Cycle LED Brightness"),
        ("power_sleep", "Sleep"),
        ("power_lock", "Lock"),
        ("power_off", "Shutdown"),
        ("power_restart", "Restart"),
        ("launch_exe", "Launch App"),
        ("macro", "Macro"),
    };

    private void AddWheelRow(QuickWheelConfig qw)
    {
        // Wrapper StackPanel holds the header row + custom slots panel
        var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var modeCombo = new ComboBox
        {
            Width = 155,
            Background = (System.Windows.Media.Brush)FindResource("InputBgBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("InputBorderBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            ToolTip = "What this wheel shows",
        };
        modeCombo.Items.Add(new ComboBoxItem { Content = "Profiles" });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Output Device" });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Media Controls" });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Custom" });
        modeCombo.SelectedIndex = (int)qw.Mode;
        Grid.SetColumn(modeCombo, 0);
        row.Children.Add(modeCombo);

        var btnCombo = new ComboBox
        {
            Width = 135,
            Background = (System.Windows.Media.Brush)FindResource("InputBgBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("InputBorderBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            ToolTip = "Which button to hold",
        };
        for (int i = 1; i <= 5; i++) btnCombo.Items.Add(new ComboBoxItem { Content = $"Button {i}" });
        btnCombo.SelectedIndex = Math.Clamp(qw.TriggerButton, 0, 4);
        btnCombo.SelectionChanged += (_, _) => { if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); } };
        Grid.SetColumn(btnCombo, 2);
        row.Children.Add(btnCombo);

        var removeBtn = new Wpf.Ui.Controls.Button
        {
            Content = "✕",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Width = 30, Height = 30,
            Padding = new Thickness(0),
            ToolTip = "Remove this Quick Wheel",
            VerticalAlignment = VerticalAlignment.Center,
        };
        removeBtn.Click += (_, _) =>
        {
            WheelRowsPanel.Children.Remove(wrapper);
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        Grid.SetColumn(removeBtn, 4);
        row.Children.Add(removeBtn);

        wrapper.Children.Add(row);

        // Custom slots panel (shown only when mode = Custom)
        var customPanel = new StackPanel
        {
            Margin = new Thickness(8, 8, 0, 0),
            Visibility = qw.Mode == QuickWheelMode.Custom ? Visibility.Visible : Visibility.Collapsed,
        };
        customPanel.Tag = "customPanel";

        // Populate existing custom slots
        foreach (var slot in qw.CustomSlots)
            AddCustomSlotRow(customPanel, slot);

        // Add slot button
        var addSlotBtn = new Wpf.Ui.Controls.Button
        {
            Content = "+ Add Slot",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = "Add a custom action slot (max 8)",
        };
        addSlotBtn.Tag = "addSlotBtn";
        addSlotBtn.Click += (_, _) =>
        {
            // Count existing slot rows (excludes the add button itself)
            int slotCount = 0;
            foreach (var c in customPanel.Children)
                if (c is Grid g && g.Tag is string s && s == "slotRow") slotCount++;
            if (slotCount >= 8) return;
            AddCustomSlotRow(customPanel, new CustomWheelSlot());
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        customPanel.Children.Add(addSlotBtn);

        wrapper.Children.Add(customPanel);

        // Toggle custom panel visibility when mode changes
        modeCombo.SelectionChanged += (_, _) =>
        {
            customPanel.Visibility = modeCombo.SelectedIndex == (int)QuickWheelMode.Custom
                ? Visibility.Visible : Visibility.Collapsed;
            if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); }
        };

        // Store config ref on the wrapper for collection
        wrapper.Tag = qw;

        WheelRowsPanel.Children.Add(wrapper);
    }

    private void AddCustomSlotRow(StackPanel customPanel, CustomWheelSlot slot)
    {
        var slotRow = new Grid { Margin = new Thickness(0, 0, 0, 4), Tag = "slotRow" };
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        slotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var actionCombo = new ComboBox
        {
            Width = 175,
            Background = (System.Windows.Media.Brush)FindResource("InputBgBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("InputBorderBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            ToolTip = "Action to execute when this slot is selected",
        };
        int selectedIdx = -1;
        for (int i = 0; i < CustomSlotActions.Length; i++)
        {
            actionCombo.Items.Add(new ComboBoxItem { Content = CustomSlotActions[i].label, Tag = CustomSlotActions[i].id });
            if (CustomSlotActions[i].id == slot.ActionId) selectedIdx = i;
        }
        actionCombo.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;
        actionCombo.SelectionChanged += (_, _) =>
        {
            // Auto-fill label if it was empty or matched the previous action label
            if (actionCombo.SelectedItem is ComboBoxItem ci && slotRow.Children[1] is TextBox labelBox)
            {
                if (string.IsNullOrEmpty(labelBox.Text) || CustomSlotActions.Any(a => a.label == labelBox.Text))
                    labelBox.Text = ci.Content?.ToString() ?? "";
            }
            if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); }
        };
        Grid.SetColumn(actionCombo, 0);
        slotRow.Children.Add(actionCombo);

        var labelBox = new TextBox
        {
            Text = string.IsNullOrEmpty(slot.Label) && selectedIdx >= 0 ? CustomSlotActions[selectedIdx].label : slot.Label,
            Width = 145,
            Background = (System.Windows.Media.Brush)FindResource("InputBgBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("InputBorderBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            ToolTip = "Display label on the wheel segment",
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 28,
        };
        labelBox.TextChanged += (_, _) => { if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); } };
        Grid.SetColumn(labelBox, 2);
        slotRow.Children.Add(labelBox);

        var removeSlotBtn = new Wpf.Ui.Controls.Button
        {
            Content = "✕",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            Width = 24, Height = 24,
            Padding = new Thickness(0),
            FontSize = 10,
            ToolTip = "Remove this slot",
            VerticalAlignment = VerticalAlignment.Center,
        };
        removeSlotBtn.Click += (_, _) =>
        {
            customPanel.Children.Remove(slotRow);
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        Grid.SetColumn(removeSlotBtn, 4);
        slotRow.Children.Add(removeSlotBtn);

        // Insert before the "Add Slot" button (last child)
        int insertIdx = customPanel.Children.Count - 1;
        if (insertIdx < 0) insertIdx = 0;
        // Find the add button — it's the last child with Tag "addSlotBtn"
        bool inserted = false;
        for (int i = customPanel.Children.Count - 1; i >= 0; i--)
        {
            if (customPanel.Children[i] is FrameworkElement fe && fe.Tag is string t && t == "addSlotBtn")
            {
                customPanel.Children.Insert(i, slotRow);
                inserted = true;
                break;
            }
        }
        if (!inserted) customPanel.Children.Add(slotRow);
    }

    private List<QuickWheelConfig> CollectWheelConfigs()
    {
        var list = new List<QuickWheelConfig>();
        foreach (var child in WheelRowsPanel.Children)
        {
            if (child is StackPanel wrapper && wrapper.Children.Count >= 1 && wrapper.Children[0] is Grid row && row.Children.Count >= 3)
            {
                var modeCombo = row.Children[0] as ComboBox;
                var btnCombo = row.Children[1] as ComboBox;
                int modeIdx = modeCombo?.SelectedIndex ?? 0;
                var cfg = new QuickWheelConfig
                {
                    Enabled = true,
                    Mode = (QuickWheelMode)Math.Clamp(modeIdx, 0, 3),
                    TriggerButton = btnCombo?.SelectedIndex ?? 0,
                    TriggerGesture = "hold",
                };

                // Collect custom slots if mode is Custom
                if (cfg.Mode == QuickWheelMode.Custom && wrapper.Children.Count >= 2
                    && wrapper.Children[1] is StackPanel customPanel)
                {
                    foreach (var slotChild in customPanel.Children)
                    {
                        if (slotChild is Grid slotRow && slotRow.Tag is string s && s == "slotRow"
                            && slotRow.Children.Count >= 2)
                        {
                            var actionCombo = slotRow.Children[0] as ComboBox;
                            var labelBox = slotRow.Children[1] as TextBox;
                            string actionId = "";
                            if (actionCombo?.SelectedItem is ComboBoxItem ci && ci.Tag is string aid)
                                actionId = aid;
                            cfg.CustomSlots.Add(new CustomWheelSlot
                            {
                                ActionId = actionId,
                                Label = labelBox?.Text ?? "",
                            });
                        }
                    }
                }

                list.Add(cfg);
            }
        }
        return list;
    }

    /// <summary>
    /// Sync button HoldActions with wheel configs. Clear old, set new.
    /// </summary>
    private void SyncWheelButtonActions(HashSet<int> oldButtons, HashSet<int> newButtons)
    {
        if (_config == null) return;
        var buttons = _config.Buttons;
        if (buttons == null) return;

        // Clear buttons that are no longer wheel triggers
        foreach (var idx in oldButtons.Except(newButtons))
        {
            if (idx >= 0 && idx < buttons.Count && buttons[idx].HoldAction == "quick_wheel")
                buttons[idx].HoldAction = "none";
        }

        // Set new wheel trigger buttons
        foreach (var idx in newButtons)
        {
            if (idx >= 0 && idx < buttons.Count)
                buttons[idx].HoldAction = "quick_wheel";
        }
    }

    private void OsdPosition_Click(object sender, MouseButtonEventArgs e)
    {
        if (_loading || _config == null) return;
        if (sender is Border border && border.Tag is string posStr)
        {
            if (Enum.TryParse<OsdPosition>(posStr, out var pos))
            {
                _config.Osd.Position = pos;
                HighlightOsdPosition(pos);
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
    }

    private void HighlightOsdPosition(OsdPosition active)
    {
        var accentBrush = (System.Windows.Media.SolidColorBrush)FindResource("AccentBrush");
        var dimBrush = (System.Windows.Media.SolidColorBrush)FindResource("TextDimBrush");
        var activeBg = new System.Windows.Media.SolidColorBrush(
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x30));
        var normalBg = (System.Windows.Media.SolidColorBrush)FindResource("CardBgBrush");
        var accentBorder = (System.Windows.Media.SolidColorBrush)FindResource("AccentDimBrush");
        var normalBorder = (System.Windows.Media.SolidColorBrush)FindResource("CardBorderBrush");

        var positions = new (Border Border, OsdPosition Pos)[]
        {
            (PosTopLeft, OsdPosition.TopLeft),
            (PosTopCenter, OsdPosition.TopCenter),
            (PosTopRight, OsdPosition.TopRight),
            (PosBottomLeft, OsdPosition.BottomLeft),
            (PosBottomCenter, OsdPosition.BottomCenter),
            (PosBottomRight, OsdPosition.BottomRight),
        };

        foreach (var (border, pos) in positions)
        {
            bool isActive = pos == active;
            border.Background = isActive ? activeBg : normalBg;
            border.BorderBrush = isActive ? accentBorder : normalBorder;
            if (border.Child is TextBlock tb)
                tb.Foreground = isActive ? accentBrush : dimBrush;
        }
    }

    private void PopulateOsdMonitorPicker(int selectedIndex)
    {
        CmbOsdMonitor.Items.Clear();
        var screens = System.Windows.Forms.Screen.AllScreens;
        var friendlyNames = NativeMethods.GetMonitorFriendlyNames();

        for (int i = 0; i < screens.Length; i++)
        {
            string name = friendlyNames.TryGetValue(screens[i].DeviceName, out var friendly)
                ? friendly
                : screens[i].DeviceName;
            string label = screens[i].Primary ? $"{name} (Primary)" : name;
            CmbOsdMonitor.Items.Add(label);
        }

        CmbOsdMonitor.SelectedIndex = (selectedIndex >= 0 && selectedIndex < screens.Length)
            ? selectedIndex : 0;
    }

    private void CmbOsdMonitor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config == null || CmbOsdMonitor.SelectedIndex < 0) return;
        _config.Osd.MonitorIndex = CmbOsdMonitor.SelectedIndex;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnOsdPreview(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        var overlay = new OsdOverlay();
        overlay.SetPosition(_config.Osd.Position, _config.Osd.MonitorIndex);
        overlay.ShowVolume("Preview", 75, "VolumeHigh");
    }
}
