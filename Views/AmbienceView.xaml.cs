using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmpUp.Views;

public partial class AmbienceView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private AmbienceSync? _sync;
    private bool _loading;
    private readonly DispatcherTimer _debounce;

    // Govee card controls
    private CheckBox _goveeEnabled = null!;
    private Border _goveeStatusDot = null!;
    private TextBlock _goveeStatusLabel = null!;
    private Button _goveeScanBtn = null!;
    private TextBlock _goveeScanStatus = null!;
    private StackPanel _goveeDeviceList = null!;
    private StackPanel _goveeScanRow = null!;

    // Sync settings controls
    private Slider _brightnessSlider = null!;
    private TextBlock _brightnessValue = null!;
    private CheckBox _warmToneShift = null!;

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    // Device rows — maps IP → sync mode ComboBox
    private readonly Dictionary<string, ComboBox> _deviceCombos = new();

    public AmbienceView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Save(); };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildGoveeCard();
        BuildSyncCard();
    }

    public void SetSync(AmbienceSync sync)
    {
        _sync = sync;
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        var ambience = config.Ambience;

        _goveeEnabled.IsChecked = ambience.GoveeEnabled;
        _goveeScanRow.Visibility = ambience.GoveeEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateGoveeStatusDot();

        // Restore saved devices immediately (don't require re-scan)
        if (ambience.GoveeDevices.Count > 0)
        {
            _goveeDeviceList.Children.Clear();
            _deviceCombos.Clear();
            foreach (var dev in ambience.GoveeDevices)
                AddDeviceRow(dev.Ip, dev.Name, dev.SyncMode);

            _goveeDeviceList.Visibility = Visibility.Visible;
            _goveeScanStatus.Text = $"{ambience.GoveeDevices.Count} device(s) configured";
        }
        else
        {
            _goveeDeviceList.Visibility = Visibility.Collapsed;
            _goveeScanStatus.Text = "No devices found";
        }

        _brightnessSlider.Value = ambience.BrightnessScale;
        _brightnessValue.Text = $"{ambience.BrightnessScale}%";
        _warmToneShift.IsChecked = ambience.WarmToneShift;

        _loading = false;
    }

    // ── Govee Card ───────────────────────────────────────────────────

    private void BuildGoveeCard()
    {
        var grid = GoveeContent;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // enable
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // scan row
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // device list

        // Row 0: Header
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        var accentBar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        headerRow.Children.Add(accentBar);
        _sectionHeaders.Add((accentBar, null!));

        var titleBlock = new TextBlock
        {
            Text = "GOVEE",
            Style = FindStyle("HeaderText"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(titleBlock);
        _sectionHeaders[_sectionHeaders.Count - 1] = (accentBar, titleBlock);

        _goveeStatusDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555")),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(_goveeStatusDot);

        _goveeStatusLabel = new TextBlock
        {
            Text = "Disabled",
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        headerRow.Children.Add(_goveeStatusLabel);

        Grid.SetRow(headerRow, 0);
        grid.Children.Add(headerRow);

        // Row 1: Enable checkbox
        _goveeEnabled = new CheckBox
        {
            Content = "Enable Govee LAN sync",
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13,
        };
        _goveeEnabled.Checked += (_, _) =>
        {
            if (_loading) return;
            _goveeScanRow.Visibility = Visibility.Visible;
            UpdateGoveeStatusDot();
            QueueSave();
        };
        _goveeEnabled.Unchecked += (_, _) =>
        {
            if (_loading) return;
            _goveeScanRow.Visibility = Visibility.Collapsed;
            UpdateGoveeStatusDot();
            QueueSave();
        };
        Grid.SetRow(_goveeEnabled, 1);
        grid.Children.Add(_goveeEnabled);

        // Row 2: Scan row (button + status)
        _goveeScanRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed,
        };

        _goveeScanBtn = new Button
        {
            Content = "Scan Network",
            Padding = new Thickness(12, 4, 12, 4),
            FontSize = 12,
        };
        _goveeScanBtn.Click += async (_, _) => await RunScanAsync();
        _goveeScanRow.Children.Add(_goveeScanBtn);

        _goveeScanStatus = new TextBlock
        {
            Text = "No devices found",
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        _goveeScanRow.Children.Add(_goveeScanStatus);

        Grid.SetRow(_goveeScanRow, 2);
        grid.Children.Add(_goveeScanRow);

        // Row 3: Device list
        _goveeDeviceList = new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        Grid.SetRow(_goveeDeviceList, 3);
        grid.Children.Add(_goveeDeviceList);
    }

    private async Task RunScanAsync()
    {
        if (_sync == null) return;

        _goveeScanBtn.IsEnabled = false;
        _goveeScanStatus.Text = "Scanning...";
        SetGoveeStatusDotColor("#FFB800"); // yellow = scanning

        List<(string Ip, string Name)> found;
        try
        {
            found = await _sync.ScanAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee scan error: {ex.Message}");
            _goveeScanStatus.Text = "Scan failed";
            SetGoveeStatusDotColor("#FF4444"); // red = error
            _goveeScanBtn.IsEnabled = true;
            return;
        }

        if (found.Count == 0)
        {
            _goveeScanStatus.Text = "No devices found";
            SetGoveeStatusDotColor("#555555");
        }
        else
        {
            _goveeScanStatus.Text = $"{found.Count} device(s) found";
            SetGoveeStatusDotColor("#00E676"); // green = found

            // Merge: keep existing configs for already-known IPs, add new ones
            var existingDevices = _config?.Ambience.GoveeDevices ?? new List<GoveeDeviceConfig>();

            _goveeDeviceList.Children.Clear();
            _deviceCombos.Clear();

            foreach (var (ip, name) in found)
            {
                var existing = existingDevices.FirstOrDefault(d => d.Ip == ip);
                string syncMode = existing?.SyncMode ?? "off";
                AddDeviceRow(ip, name, syncMode);
            }

            _goveeDeviceList.Visibility = Visibility.Visible;
            Save(); // persist newly found devices immediately
        }

        _goveeScanBtn.IsEnabled = true;
    }

    private void AddDeviceRow(string ip, string name, string syncMode)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        var displayName = string.IsNullOrWhiteSpace(name) ? ip : $"{name} — {ip}";
        var nameBlock = new TextBlock
        {
            Text = displayName,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = ip,
        };
        Grid.SetColumn(nameBlock, 0);
        row.Children.Add(nameBlock);

        var combo = new ComboBox
        {
            Background = FindBrush("InputBgBrush"),
            Foreground = FindBrush("TextPrimaryBrush"),
            BorderBrush = FindBrush("InputBorderBrush"),
            FontSize = 12,
            Tag = ip,
        };

        var syncModeOptions = new[]
        {
            ("off",   "Off"),
            ("global","Mirror Global"),
            ("knob0", "Knob 1"),
            ("knob1", "Knob 2"),
            ("knob2", "Knob 3"),
            ("knob3", "Knob 4"),
            ("knob4", "Knob 5"),
        };

        int selectedIdx = 0;
        for (int i = 0; i < syncModeOptions.Length; i++)
        {
            var (val, label) = syncModeOptions[i];
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = val });
            if (val == syncMode) selectedIdx = i;
        }
        combo.SelectedIndex = selectedIdx;
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            QueueSave();
        };

        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);

        _goveeDeviceList.Children.Add(row);
        _deviceCombos[ip] = combo;
    }

    private void UpdateGoveeStatusDot()
    {
        bool enabled = _goveeEnabled.IsChecked == true;
        if (!enabled)
        {
            SetGoveeStatusDotColor("#555555");
            _goveeStatusLabel.Text = "Disabled";
        }
        else if (_deviceCombos.Count > 0)
        {
            SetGoveeStatusDotColor("#00E676");
            _goveeStatusLabel.Text = $"{_deviceCombos.Count} device(s)";
        }
        else
        {
            SetGoveeStatusDotColor("#555555");
            _goveeStatusLabel.Text = "Scan to find devices";
        }
    }

    private void SetGoveeStatusDotColor(string hex)
    {
        _goveeStatusDot.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    // ── Sync Settings Card ────────────────────────────────────────────

    private void BuildSyncCard()
    {
        var grid = SyncContent;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // brightness
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // warm tone
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // note

        // Row 0: Header
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };

        var accentBar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = FindBrush("AccentBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        headerRow.Children.Add(accentBar);

        var titleBlock = new TextBlock
        {
            Text = "SYNC SETTINGS",
            Style = FindStyle("HeaderText"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(titleBlock);
        _sectionHeaders.Add((accentBar, titleBlock));

        Grid.SetRow(headerRow, 0);
        grid.Children.Add(headerRow);

        // Row 1: Brightness scale
        var brightnessSection = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var brightnessLabel = new TextBlock
        {
            Text = "Room Brightness Scale",
            Style = FindStyle("SecondaryText"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "Scales all sent colors — room lights often look better at lower intensity",
        };
        brightnessSection.Children.Add(brightnessLabel);

        var brightnessRow = new StackPanel { Orientation = Orientation.Horizontal };

        _brightnessSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 75,
            Width = 220,
            TickFrequency = 25,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.None,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _brightnessSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _brightnessValue.Text = $"{(int)_brightnessSlider.Value}%";
            QueueSave();
        };
        brightnessRow.Children.Add(_brightnessSlider);

        _brightnessValue = new TextBlock
        {
            Text = "75%",
            Width = 40,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        brightnessRow.Children.Add(_brightnessValue);

        brightnessSection.Children.Add(brightnessRow);
        Grid.SetRow(brightnessSection, 1);
        grid.Children.Add(brightnessSection);

        // Row 2: Warm tone shift
        _warmToneShift = new CheckBox
        {
            Content = "Warm tone shift",
            Foreground = FindBrush("TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13,
            ToolTip = "Boost reds and shift cool colors warmer (matches LED bulb color temperature)",
        };
        _warmToneShift.Checked += (_, _) => { if (!_loading) QueueSave(); };
        _warmToneShift.Unchecked += (_, _) => { if (!_loading) QueueSave(); };
        Grid.SetRow(_warmToneShift, 2);
        grid.Children.Add(_warmToneShift);

        // Row 3: Update rate note
        var noteBlock = new TextBlock
        {
            Text = "Syncs at 20 FPS alongside device LEDs",
            Style = FindStyle("SecondaryText"),
            Foreground = FindBrush("TextDimBrush"),
            FontSize = 11,
        };
        Grid.SetRow(noteBlock, 3);
        grid.Children.Add(noteBlock);
    }

    // ── Save ──────────────────────────────────────────────────────────

    private void QueueSave()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void Save()
    {
        if (_config == null || _onSave == null || _loading) return;

        var cfg = _config.Ambience;

        cfg.GoveeEnabled = _goveeEnabled.IsChecked == true;
        cfg.BrightnessScale = (int)_brightnessSlider.Value;
        cfg.WarmToneShift = _warmToneShift.IsChecked == true;

        // Collect device configs from device rows
        cfg.GoveeDevices.Clear();
        foreach (var (ip, combo) in _deviceCombos)
        {
            string syncMode = "off";
            if (combo.SelectedItem is ComboBoxItem item)
                syncMode = item.Tag as string ?? "off";

            // Recover name from the row's TextBlock
            string name = ip;
            foreach (var child in _goveeDeviceList.Children)
            {
                if (child is Grid rowGrid && rowGrid.Tag as string == ip)
                {
                    if (rowGrid.Children.Count > 0 && rowGrid.Children[0] is TextBlock tb)
                        name = tb.Text;
                    break;
                }
            }

            cfg.GoveeDevices.Add(new GoveeDeviceConfig { Ip = ip, Name = name, SyncMode = syncMode });
        }

        _sync?.UpdateConfig(cfg);
        _onSave(_config);
    }

    // ── Accent color refresh ──────────────────────────────────────────

    private void RefreshAccentColors()
    {
        var accent = ThemeManager.Accent;
        foreach (var (bar, label) in _sectionHeaders)
        {
            if (bar != null) bar.Background = new SolidColorBrush(accent);
            if (label != null) label.Foreground = new SolidColorBrush(accent);
        }
    }

    // ── Resource helpers ──────────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
}
