using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Views;

public partial class OsdView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    public OsdView()
    {
        InitializeComponent();

        // OSD checkbox events
        ChkOsdVolume.IsCheckedChanged += OnValueChanged;
        ChkOsdProfile.IsCheckedChanged += OnValueChanged;
        ChkOsdDevice.IsCheckedChanged += OnValueChanged;
        ChkHideInFullscreen.IsCheckedChanged += OnValueChanged;

        // Duration sliders
        SldOsdVolumeDur.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                var v = Math.Round(SldOsdVolumeDur.Value * 2) / 2;
                LblOsdVolumeDur.Text = $"{v:F1}s";
                OnValueChanged(null, e);
            }
        };
        SldOsdProfileDur.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                var v = Math.Round(SldOsdProfileDur.Value * 2) / 2;
                LblOsdProfileDur.Text = $"{v:F1}s";
                OnValueChanged(null, e);
            }
        };
        SldOsdDeviceDur.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Value")
            {
                var v = Math.Round(SldOsdDeviceDur.Value * 2) / 2;
                LblOsdDeviceDur.Text = $"{v:F1}s";
                OnValueChanged(null, e);
            }
        };

        // Monitor selector
        CmbOsdMonitor.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null || CmbOsdMonitor.SelectedIndex < 0) return;
            _config.Osd.MonitorIndex = CmbOsdMonitor.SelectedIndex;
            CollectAndSave();
        };

        // Position grid clicks
        PosTopLeft.PointerPressed += OsdPosition_Click;
        PosTopCenter.PointerPressed += OsdPosition_Click;
        PosTopRight.PointerPressed += OsdPosition_Click;
        PosBottomLeft.PointerPressed += OsdPosition_Click;
        PosBottomCenter.PointerPressed += OsdPosition_Click;
        PosBottomRight.PointerPressed += OsdPosition_Click;

        // Quick wheels
        BtnAddWheel.Click += (_, _) => AddWheelRow(new QuickWheelConfig { Enabled = true });
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        ChkOsdVolume.IsChecked = config.Osd.ShowVolume;
        ChkOsdProfile.IsChecked = config.Osd.ShowProfileSwitch;
        ChkOsdDevice.IsChecked = config.Osd.ShowDeviceSwitch;

        SldOsdVolumeDur.Value = config.Osd.VolumeDuration;
        LblOsdVolumeDur.Text = $"{config.Osd.VolumeDuration:F1}s";
        SldOsdProfileDur.Value = config.Osd.ProfileDuration;
        LblOsdProfileDur.Text = $"{config.Osd.ProfileDuration:F1}s";
        SldOsdDeviceDur.Value = config.Osd.DeviceDuration;
        LblOsdDeviceDur.Text = $"{config.Osd.DeviceDuration:F1}s";

        HighlightOsdPosition(config.Osd.Position);
        PopulateOsdMonitorPicker(config.Osd.MonitorIndex);
        ChkHideInFullscreen.IsChecked = config.Osd.HideInFullscreen;

        // Quick wheels
        WheelRowsPanel.Children.Clear();
        foreach (var qw in config.Osd.QuickWheels)
            AddWheelRow(qw);

        _loading = false;
    }

    private void OnValueChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        CollectAndSave();
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null || _loading) return;

        _config.Osd.ShowVolume = ChkOsdVolume.IsChecked == true;
        _config.Osd.ShowProfileSwitch = ChkOsdProfile.IsChecked == true;
        _config.Osd.ShowDeviceSwitch = ChkOsdDevice.IsChecked == true;
        _config.Osd.VolumeDuration = Math.Round(SldOsdVolumeDur.Value * 2) / 2;
        _config.Osd.ProfileDuration = Math.Round(SldOsdProfileDur.Value * 2) / 2;
        _config.Osd.DeviceDuration = Math.Round(SldOsdDeviceDur.Value * 2) / 2;
        _config.Osd.HideInFullscreen = ChkHideInFullscreen.IsChecked == true;

        // Collect quick wheels
        _config.Osd.QuickWheels = CollectWheelConfigs();

        _onSave(_config);
    }

    // ── Position Picker ───────────────────────────────────────────

    private void OsdPosition_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_loading || _config == null) return;
        if (sender is Border border && border.Tag is string posStr)
        {
            if (Enum.TryParse<OsdPosition>(posStr, out var pos))
            {
                _config.Osd.Position = pos;
                HighlightOsdPosition(pos);
                CollectAndSave();
            }
        }
    }

    private void HighlightOsdPosition(OsdPosition active)
    {
        var accentBrush = new SolidColorBrush(Color.Parse("#00E676"));
        var dimBrush = new SolidColorBrush(Color.Parse("#6A6A6A"));
        var activeBg = new SolidColorBrush(Color.Parse("#3000E676"));
        var normalBg = new SolidColorBrush(Color.Parse("#1C1C1C"));
        var accentBorder = new SolidColorBrush(Color.Parse("#00A854"));
        var normalBorder = new SolidColorBrush(Color.Parse("#2A2A2A"));

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
            border.BorderThickness = new Thickness(1);
            if (border.Child is TextBlock tb)
                tb.Foreground = isActive ? accentBrush : dimBrush;
        }
    }

    private void PopulateOsdMonitorPicker(int selectedIndex)
    {
        CmbOsdMonitor.Items.Clear();
        // On macOS, use Avalonia screen enumeration
        var screens = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Screens?.All ?? Array.Empty<Avalonia.Platform.Screen>()
            : Array.Empty<Avalonia.Platform.Screen>();

        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            string label = screen.IsPrimary ? $"Display {i + 1} (Primary)" : $"Display {i + 1}";
            CmbOsdMonitor.Items.Add(label);
        }

        if (CmbOsdMonitor.Items.Count == 0)
            CmbOsdMonitor.Items.Add("Display 1 (Primary)");

        CmbOsdMonitor.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, CmbOsdMonitor.Items.Count - 1));
    }

    // ── Quick Wheel Rows ──────────────────────────────────────────

    private void AddWheelRow(QuickWheelConfig qw)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            ColumnDefinitions = new ColumnDefinitions("160,12,140,*,Auto"),
        };

        var modeCombo = new ComboBox { Width = 155 };
        modeCombo.Items.Add("Profiles");
        modeCombo.Items.Add("Output Device");
        modeCombo.SelectedIndex = (int)qw.Mode;
        modeCombo.SelectionChanged += (_, _) => { if (!_loading) CollectAndSave(); };
        Grid.SetColumn(modeCombo, 0);
        row.Children.Add(modeCombo);

        var btnCombo = new ComboBox { Width = 135 };
        for (int i = 1; i <= 5; i++) btnCombo.Items.Add($"Button {i}");
        btnCombo.SelectedIndex = Math.Clamp(qw.TriggerButton, 0, 4);
        btnCombo.SelectionChanged += (_, _) => { if (!_loading) CollectAndSave(); };
        Grid.SetColumn(btnCombo, 2);
        row.Children.Add(btnCombo);

        var removeBtn = new Button
        {
            Content = "✕",
            Width = 30, Height = 30,
            Padding = new Thickness(0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        removeBtn.Click += (_, _) =>
        {
            WheelRowsPanel.Children.Remove(row);
            CollectAndSave();
        };
        Grid.SetColumn(removeBtn, 4);
        row.Children.Add(removeBtn);

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
}
