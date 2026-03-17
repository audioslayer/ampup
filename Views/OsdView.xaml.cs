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

        // Quick wheel events
        ChkWheelEnabled.Checked += OnWheelEnabledChanged;
        ChkWheelEnabled.Unchecked += OnWheelEnabledChanged;
        CmbWheelMode.SelectionChanged += OnValueChangedCombo;
        CmbWheelButton2.SelectionChanged += OnValueChangedCombo;
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

        // Quick wheel
        ChkWheelEnabled.IsChecked = config.Osd.QuickWheel.Enabled;
        CmbWheelMode.SelectedIndex = (int)config.Osd.QuickWheel.Mode;
        CmbWheelButton2.SelectedIndex = Math.Clamp(config.Osd.QuickWheel.TriggerButton, 0, 4);
        WheelOptions.Visibility = config.Osd.QuickWheel.Enabled ? Visibility.Visible : Visibility.Collapsed;

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

    private void OnWheelEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        WheelOptions.Visibility = ChkWheelEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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

        bool wasEnabled = _config.Osd.QuickWheel.Enabled;
        int oldButton = _config.Osd.QuickWheel.TriggerButton;

        _config.Osd.QuickWheel.Enabled = ChkWheelEnabled.IsChecked == true;
        _config.Osd.QuickWheel.Mode = (QuickWheelMode)Math.Clamp(CmbWheelMode.SelectedIndex, 0, 1);
        _config.Osd.QuickWheel.TriggerButton = CmbWheelButton2.SelectedIndex;
        _config.Osd.QuickWheel.TriggerGesture = "hold";

        // Sync: keep button HoldAction in sync with Quick Wheel config
        SyncButtonHoldAction(wasEnabled, oldButton);

        _onSave(_config);
    }

    /// <summary>
    /// Keep button HoldAction in sync with Quick Wheel config.
    /// When enabled, set the trigger button's hold to quick_wheel.
    /// When disabled or trigger changes, clear the old button's hold if it was quick_wheel.
    /// </summary>
    private void SyncButtonHoldAction(bool wasEnabled, int oldButton)
    {
        if (_config == null) return;
        var buttons = _config.Buttons;
        if (buttons == null) return;

        // Clear old button if it was set to quick_wheel
        if (wasEnabled && oldButton >= 0 && oldButton < buttons.Count)
        {
            var oldBtn = buttons[oldButton];
            if (oldBtn.HoldAction == "quick_wheel")
                oldBtn.HoldAction = "none";
        }

        // Set new trigger button's hold to quick_wheel
        if (_config.Osd.QuickWheel.Enabled)
        {
            int newButton = _config.Osd.QuickWheel.TriggerButton;
            if (newButton >= 0 && newButton < buttons.Count)
                buttons[newButton].HoldAction = "quick_wheel";
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
