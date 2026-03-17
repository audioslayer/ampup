using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AmpUp.Core.Services;

namespace AmpUp.Views;

public partial class OsdView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private readonly DispatcherTimer _debounceTimer;
    private bool _loading;

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
        SldOsdVolumeDur.ValueChanged += (s, _) => OnValueChanged(s!, EventArgs.Empty);
        SldOsdProfileDur.ValueChanged += (s, _) => OnValueChanged(s!, EventArgs.Empty);
        SldOsdDeviceDur.ValueChanged += (s, _) => OnValueChanged(s!, EventArgs.Empty);
        BtnOsdPreview.Click += OnOsdPreview;
        ChkHideInFullscreen.Checked += OnValueChanged;
        ChkHideInFullscreen.Unchecked += OnValueChanged;

        // Quick wheels
        BtnAddWheel.Click += (_, _) => AddWheelRow(new QuickWheelConfig { Enabled = true });
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
        SldOsdProfileDur.Value = config.Osd.ProfileDuration;
        SldOsdDeviceDur.Value = config.Osd.DeviceDuration;
        HighlightOsdPosition(config.Osd.Position);
        PopulateOsdMonitorPicker(config.Osd.MonitorIndex);
        ChkHideInFullscreen.IsChecked = config.Osd.HideInFullscreen;

        // Quick wheels
        WheelRowsPanel.Children.Clear();
        foreach (var qw in config.Osd.QuickWheels)
            AddWheelRow(qw);

        _loading = false;
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
        if (_config == null || _onSave == null) return;

        _config.Osd.ShowVolume = ChkOsdVolume.IsChecked == true;
        _config.Osd.ShowProfileSwitch = ChkOsdProfile.IsChecked == true;
        _config.Osd.ShowDeviceSwitch = ChkOsdDevice.IsChecked == true;
        _config.Osd.VolumeDuration = Math.Round(SldOsdVolumeDur.Value * 2) / 2;
        _config.Osd.ProfileDuration = Math.Round(SldOsdProfileDur.Value * 2) / 2;
        _config.Osd.DeviceDuration = Math.Round(SldOsdDeviceDur.Value * 2) / 2;
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

    private void AddWheelRow(QuickWheelConfig qw)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
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
        modeCombo.SelectedIndex = (int)qw.Mode;
        modeCombo.SelectionChanged += (_, _) => { if (!_loading) { _debounceTimer.Stop(); _debounceTimer.Start(); } };
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
            WheelRowsPanel.Children.Remove(row);
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        Grid.SetColumn(removeBtn, 4);
        row.Children.Add(removeBtn);

        // Store config ref on the row for collection
        row.Tag = qw;

        WheelRowsPanel.Children.Add(row);
    }

    private List<QuickWheelConfig> CollectWheelConfigs()
    {
        var list = new List<QuickWheelConfig>();
        foreach (var child in WheelRowsPanel.Children)
        {
            if (child is Grid row && row.Children.Count >= 3)
            {
                var modeCombo = row.Children[0] as ComboBox;
                var btnCombo = row.Children[1] as ComboBox;
                // Children order: modeCombo(col0), btnCombo(col2), removeBtn(col4)
                // But Grid.Children order is add-order, so [0]=mode, [1]=btn, [2]=remove
                list.Add(new QuickWheelConfig
                {
                    Enabled = true,
                    Mode = (QuickWheelMode)Math.Clamp(modeCombo?.SelectedIndex ?? 0, 0, 1),
                    TriggerButton = btnCombo?.SelectedIndex ?? 0,
                    TriggerGesture = "hold",
                });
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
