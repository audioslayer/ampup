using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AmpUp.Controls;

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
    private StyledSlider _brightnessSlider = null!;
    // brightness value is rendered by StyledSlider itself
    private CheckBox _warmToneShift = null!;

    // Cloud API state
    private GoveeCloudApi? _cloudApi;
    private List<GoveeDeviceInfo> _cloudDevices = new();
    private readonly Dictionary<string, List<GoveeScene>> _deviceScenes = new();

    // Section header elements (refreshed on accent change)
    private readonly List<(Border bar, TextBlock label)> _sectionHeaders = new();

    // Device rows — maps IP → device name
    private readonly Dictionary<string, string> _deviceCombos = new();

    public AmbienceView()
    {
        InitializeComponent();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Save(); };

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshAccentColors);

        BuildGoveeCard();
        BuildSyncCard();
        BuildApiKeyCard();
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
                AddDeviceRow(dev.Ip, dev.Name);

            _goveeDeviceList.Visibility = Visibility.Visible;
            _goveeScanStatus.Text = $"{ambience.GoveeDevices.Count} device(s) configured";
        }
        else
        {
            _goveeDeviceList.Visibility = Visibility.Collapsed;
            _goveeScanStatus.Text = "No devices found";
        }

        _brightnessSlider.Value = ambience.BrightnessScale;
        // StyledSlider renders its own value label
        _warmToneShift.IsChecked = ambience.WarmToneShift;

        _loading = false;

        // Initialize cloud API if key is configured
        if (!string.IsNullOrEmpty(ambience.GoveeApiKey))
        {
            _cloudApi?.Dispose();
            _cloudApi = new GoveeCloudApi(ambience.GoveeApiKey);
            RefreshApiKeyCard();
            _ = FetchCloudDevicesAsync();
        }
        else
        {
            RefreshApiKeyCard();
        }
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

        var helpBtn = new Button
        {
            Content = "?",
            Width = 24, Height = 24,
            Padding = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            ToolTip = "Not finding devices? Click for help",
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        helpBtn.Click += (_, _) => ShowLanControlHelp();
        _goveeScanRow.Children.Add(helpBtn);

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

    private void ShowLanControlHelp()
    {
        var accent = ThemeManager.Accent;
        var accentBrush = new SolidColorBrush(accent);

        var content = new StackPanel { MaxWidth = 380 };

        content.Children.Add(new TextBlock
        {
            Text = "Enable LAN Control",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = accentBrush,
            Margin = new Thickness(0, 0, 0, 12)
        });

        content.Children.Add(new TextBlock
        {
            Text = "Govee devices require LAN Control to be enabled before AmpUp can discover them on your network.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            Margin = new Thickness(0, 0, 0, 16)
        });

        var steps = new[]
        {
            ("1", "Open the Govee Home app on your phone"),
            ("2", "Tap on the device you want to control"),
            ("3", "Tap the ⚙ Settings gear icon (top right)"),
            ("4", "Scroll down and find \"LAN Control\""),
            ("5", "Toggle it ON"),
            ("6", "Repeat for each Govee device"),
        };

        foreach (var (num, text) in steps)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            var circle = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)),
                BorderBrush = accentBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = num,
                    FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = accentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            row.Children.Add(circle);

            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = (SolidColorBrush)FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            content.Children.Add(row);
        }

        content.Children.Add(new Border
        {
            Height = 1,
            Background = (SolidColorBrush)FindResource("CardBorderBrush"),
            Margin = new Thickness(0, 12, 0, 12)
        });

        content.Children.Add(new TextBlock
        {
            Text = "After enabling LAN Control, come back here and click \"Scan Network\" again. Devices should appear within a few seconds.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
        });

        GlassDialog.ShowInfo("Govee LAN Setup", content);
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
                AddDeviceRow(ip, name);
            }

            _goveeDeviceList.Visibility = Visibility.Visible;
            Save(); // persist newly found devices immediately
        }

        _goveeScanBtn.IsEnabled = true;
    }

    private void AddDeviceRow(string ip, string name)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Tag = ip };

        var displayName = string.IsNullOrWhiteSpace(name) ? ip : $"{name} — {ip}";
        var nameBlock = new TextBlock
        {
            Text = displayName,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = ip,
        };
        row.Children.Add(nameBlock);

        _goveeDeviceList.Children.Add(row);
        _deviceCombos[ip] = string.IsNullOrWhiteSpace(name) ? ip : name;
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

        _brightnessSlider = new StyledSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 75,
            Width = 260,
            Height = 40,
            Suffix = "%",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _brightnessSlider.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            QueueSave();
        };
        brightnessRow.Children.Add(_brightnessSlider);

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

    // ── API Key Card ──────────────────────────────────────────────────

    private void BuildApiKeyCard()
    {
        var grid = ApiKeyContent;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // content

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
            Text = "GOVEE ACCOUNT",
            Style = FindStyle("HeaderText"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerRow.Children.Add(titleBlock);
        _sectionHeaders.Add((accentBar, titleBlock));

        Grid.SetRow(headerRow, 0);
        grid.Children.Add(headerRow);

        // Row 1: content — placeholder, replaced by RefreshApiKeyCard()
        var placeholder = new StackPanel { Tag = "ApiKeyBody" };
        Grid.SetRow(placeholder, 1);
        grid.Children.Add(placeholder);
    }

    private void RefreshApiKeyCard()
    {
        // Find the body panel (row 1 of ApiKeyContent)
        StackPanel? body = null;
        foreach (UIElement el in ApiKeyContent.Children)
        {
            if (el is StackPanel sp && sp.Tag as string == "ApiKeyBody") { body = sp; break; }
        }
        if (body == null) return;

        body.Children.Clear();

        bool hasKey = !string.IsNullOrEmpty(_config?.Ambience.GoveeApiKey);

        if (!hasKey)
        {
            // No key — show prompt
            var descText = new TextBlock
            {
                Text = "Connect your Govee account to unlock scenes, segments, and music mode",
                Style = FindStyle("SecondaryText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            };
            body.Children.Add(descText);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            var setupBtn = new Button
            {
                Content = "Setup Guide",
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 10, 0),
            };
            setupBtn.Click += (_, _) => OpenSetupGuide();
            btnRow.Children.Add(setupBtn);

            body.Children.Add(btnRow);

            // Inline paste link
            var pasteLink = new TextBlock
            {
                Text = "or paste key directly ▾",
                Style = FindStyle("SecondaryText"),
                Foreground = FindBrush("AccentBrush"),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 8),
            };

            var inlineRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
            var keyBox = new TextBox
            {
                Width = 280,
                Background = FindBrush("InputBgBrush"),
                Foreground = FindBrush("TextPrimaryBrush"),
                BorderBrush = FindBrush("InputBorderBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Paste your Govee API key here",
            };
            var saveKeyBtn = new Button
            {
                Content = "Save",
                Padding = new Thickness(12, 4, 12, 4),
                FontSize = 12,
            };
            saveKeyBtn.Click += (_, _) =>
            {
                var key = keyBox.Text.Trim();
                if (!string.IsNullOrEmpty(key) && _config != null)
                {
                    _config.Ambience.GoveeApiKey = key;
                    _cloudApi?.Dispose();
                    _cloudApi = new GoveeCloudApi(key);
                    _onSave?.Invoke(_config);
                    RefreshApiKeyCard();
                    _ = FetchCloudDevicesAsync();
                }
            };

            inlineRow.Children.Add(keyBox);
            inlineRow.Children.Add(saveKeyBtn);

            pasteLink.MouseLeftButtonUp += (_, _) =>
            {
                inlineRow.Visibility = inlineRow.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                pasteLink.Text = inlineRow.Visibility == Visibility.Visible
                    ? "or paste key directly ▴"
                    : "or paste key directly ▾";
            };

            body.Children.Add(pasteLink);
            body.Children.Add(inlineRow);
        }
        else
        {
            // Key set — show connected state
            var connectedRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            connectedRow.Children.Add(dot);

            int deviceCount = _cloudDevices.Count;
            var connectedLabel = new TextBlock
            {
                Text = deviceCount > 0
                    ? $"Connected \u2713 — {deviceCount} device(s)"
                    : "Connected \u2713",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            connectedRow.Children.Add(connectedLabel);
            body.Children.Add(connectedRow);

            var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            var changeKeyLink = new TextBlock
            {
                Text = "Change Key",
                Style = FindStyle("SecondaryText"),
                Foreground = FindBrush("AccentBrush"),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(0, 0, 16, 0),
            };
            changeKeyLink.MouseLeftButtonUp += (_, _) =>
            {
                if (_config != null)
                {
                    _config.Ambience.GoveeApiKey = "";
                    _cloudApi?.Dispose();
                    _cloudApi = null;
                    _cloudDevices.Clear();
                    DevicePanel.Children.Clear();
                    _onSave?.Invoke(_config);
                    RefreshApiKeyCard();
                }
            };
            linkRow.Children.Add(changeKeyLink);

            var guideLink = new TextBlock
            {
                Text = "Setup Guide",
                Style = FindStyle("SecondaryText"),
                Foreground = FindBrush("AccentBrush"),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            guideLink.MouseLeftButtonUp += (_, _) => OpenSetupGuide();
            linkRow.Children.Add(guideLink);

            body.Children.Add(linkRow);

            var refreshBtn = new Button
            {
                Content = "Refresh Devices",
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 12,
            };
            refreshBtn.Click += (_, _) => _ = FetchCloudDevicesAsync();
            body.Children.Add(refreshBtn);
        }
    }

    private void OpenSetupGuide()
    {
        var guide = new Controls.GoveeSetupGuide();
        guide.ValidateKeyAsync = async (key) =>
        {
            using var api = new GoveeCloudApi(key);
            var devices = await api.GetDevicesAsync();
            return devices != null && devices.Count > 0;
        };
        guide.Owner = Window.GetWindow(this);
        if (guide.ShowDialog() == true)
        {
            var key = guide.ApiKey;
            if (!string.IsNullOrEmpty(key) && _config != null)
            {
                _config.Ambience.GoveeApiKey = key;
                _cloudApi?.Dispose();
                _cloudApi = new GoveeCloudApi(key);
                _onSave?.Invoke(_config);
                RefreshApiKeyCard();
                _ = FetchCloudDevicesAsync();
            }
        }
    }

    // ── Cloud Device Fetching ─────────────────────────────────────────

    private async Task FetchCloudDevicesAsync()
    {
        if (_cloudApi == null) return;

        try
        {
            var devices = await _cloudApi.GetDevicesAsync();
            if (devices == null) return;

            Dispatcher.Invoke(() =>
            {
                _cloudDevices = devices;
                RefreshApiKeyCard();
                RebuildDevicePanel();
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee cloud fetch error: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                ShowDevicePanelError($"Failed to fetch devices: {ex.Message}");
            });
        }
    }

    private void ShowDevicePanelError(string message)
    {
        DevicePanel.Children.Clear();
        var errBlock = new TextBlock
        {
            Text = message,
            Foreground = FindBrush("DangerRedBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        DevicePanel.Children.Add(errBlock);
    }

    private void RebuildDevicePanel()
    {
        DevicePanel.Children.Clear();

        if (_cloudDevices.Count == 0)
        {
            var emptyBlock = new TextBlock
            {
                Text = "No Govee Cloud devices found",
                Style = FindStyle("SecondaryText"),
                Foreground = FindBrush("TextDimBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
            };
            DevicePanel.Children.Add(emptyBlock);
            return;
        }

        foreach (var device in _cloudDevices)
            DevicePanel.Children.Add(BuildCloudDeviceCard(device));
    }

    // ── Cloud Device Card ─────────────────────────────────────────────

    private Border BuildCloudDeviceCard(GoveeDeviceInfo device)
    {
        var card = new Border
        {
            Style = FindStyle("CardPanel") as Style,
            Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = FindBrush("AccentBrush"),
        };

        var stack = new StackPanel();
        card.Child = stack;

        // ── Header row ──
        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameBlock = new TextBlock
        {
            Text = $"{device.DeviceName}  {device.Sku}",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameBlock, 0);
        headerRow.Children.Add(nameBlock);

        bool isOnline = false; // online state requires a separate state query; not available on device list
        var onlineDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isOnline ? "#00E676" : "#555555")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = isOnline ? "Online" : "Offline",
        };
        Grid.SetColumn(onlineDot, 1);
        headerRow.Children.Add(onlineDot);

        stack.Children.Add(headerRow);

        // ── Controls row ──
        var controlsRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };

        // On/Off toggle
        var onOffCheck = new CheckBox
        {
            Content = "On",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 4),
            ToolTip = "Toggle device power",
        };
        onOffCheck.Checked += async (_, _) => await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.TurnOnOff(true)));
        onOffCheck.Unchecked += async (_, _) => await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.TurnOnOff(false)));
        controlsRow.Children.Add(onOffCheck);

        // Brightness label + slider
        var brightnessLabel = new TextBlock
        {
            Text = "Brightness",
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4),
        };
        controlsRow.Children.Add(brightnessLabel);

        var brightnessValText = new TextBlock
        {
            Text = "100",
            Width = 30,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 4),
        };

        var brightnessSlider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Value = 100,
            Width = 140,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 4),
            ToolTip = "Device brightness (1-100)",
        };
        brightnessSlider.ValueChanged += (_, _) =>
        {
            brightnessValText.Text = ((int)brightnessSlider.Value).ToString();
        };
        var brightnessDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        brightnessDebounce.Tick += async (_, _) =>
        {
            brightnessDebounce.Stop();
            await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.SetBrightness((int)brightnessSlider.Value)));
        };
        brightnessSlider.ValueChanged += (_, _) =>
        {
            brightnessDebounce.Stop();
            brightnessDebounce.Start();
        };
        controlsRow.Children.Add(brightnessSlider);
        controlsRow.Children.Add(brightnessValText);

        // Color circle
        var colorCircle = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Colors.White),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 4),
            ToolTip = "Click to set device color",
        };
        colorCircle.MouseLeftButtonUp += (_, _) =>
        {
            var currentBrush = colorCircle.Background as SolidColorBrush;
            byte r = currentBrush?.Color.R ?? 255;
            byte g = currentBrush?.Color.G ?? 255;
            byte b = currentBrush?.Color.B ?? 255;

            ShowColorPicker(r, g, b, async (nr, ng, nb) =>
            {
                colorCircle.Background = new SolidColorBrush(Color.FromRgb(nr, ng, nb));
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.SetColor(nr, ng, nb)));
            });
        };
        controlsRow.Children.Add(colorCircle);

        stack.Children.Add(controlsRow);

        // ── Scene section ──
        if (device.Capabilities?.Contains("devices.capabilities.dynamic_scene") == true)
        {
            var sceneSection = BuildCollapsibleSection("SCENES", () => BuildScenesContent(device));
            stack.Children.Add(sceneSection);
        }

        // ── Segment section ──
        if (device.Capabilities?.Contains("devices.capabilities.segment_color_setting") == true)
        {
            var segSection = BuildCollapsibleSection("SEGMENTS", () => BuildSegmentsContent(device));
            stack.Children.Add(segSection);
        }

        // ── Music mode section ──
        if (device.Capabilities?.Contains("devices.capabilities.music_setting") == true)
        {
            var musicSection = BuildCollapsibleSection("MUSIC MODE", () => BuildMusicModeContent(device));
            stack.Children.Add(musicSection);
        }

        return card;
    }

    // ── Collapsible section ───────────────────────────────────────────

    private StackPanel BuildCollapsibleSection(string title, Func<UIElement> contentBuilder)
    {
        var section = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 6),
        };

        var chevron = new TextBlock
        {
            Text = "▶",
            Foreground = FindBrush("TextDimBrush"),
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        header.Children.Add(chevron);
        header.Children.Add(titleBlock);
        section.Children.Add(header);

        Border? contentBorder = null;

        header.MouseLeftButtonUp += (_, _) =>
        {
            if (contentBorder == null)
            {
                // Build content lazily on first expand
                var content = contentBuilder();
                contentBorder = new Border
                {
                    Child = content,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                section.Children.Add(contentBorder);
                chevron.Text = "▼";
            }
            else
            {
                bool isVisible = contentBorder.Visibility == Visibility.Visible;
                contentBorder.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                chevron.Text = isVisible ? "▶" : "▼";
            }
        };

        return section;
    }

    // ── Scene content ─────────────────────────────────────────────────

    private UIElement BuildScenesContent(GoveeDeviceInfo device)
    {
        var container = new StackPanel();

        var loadingText = new TextBlock
        {
            Text = "Loading scenes...",
            Style = FindStyle("SecondaryText"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
        };
        container.Children.Add(loadingText);

        // Fetch scenes (cached)
        _ = LoadScenesAsync(device, container, loadingText);

        return container;
    }

    private async Task LoadScenesAsync(GoveeDeviceInfo device, StackPanel container, TextBlock loadingText)
    {
        if (_deviceScenes.TryGetValue(device.Device, out var cached))
        {
            Dispatcher.Invoke(() => RenderSceneGrid(container, loadingText, device, cached));
            return;
        }

        try
        {
            var scenes = await _cloudApi!.GetDynamicScenesAsync(device.Device, device.Sku);
            if (scenes != null) _deviceScenes[device.Device] = scenes;

            Dispatcher.Invoke(() => RenderSceneGrid(container, loadingText, device, scenes ?? new List<GoveeScene>()));
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee scenes fetch error ({device.Device}): {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                loadingText.Text = "Failed to load scenes";
                loadingText.Foreground = FindBrush("DangerRedBrush");
            });
        }
    }

    private void RenderSceneGrid(StackPanel container, TextBlock loadingText, GoveeDeviceInfo device, List<GoveeScene> scenes)
    {
        container.Children.Remove(loadingText);

        if (scenes.Count == 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = "No scenes available",
                Style = FindStyle("SecondaryText"),
                FontSize = 11,
            });
            return;
        }

        string? activeSceneId = null;

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach (var scene in scenes)
        {
            var sceneId = scene.Id;
            var tile = new Border
            {
                Width = 100,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1C")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A")),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = scene.Name,
                Tag = sceneId,
            };

            var label = new TextBlock
            {
                Text = scene.Name,
                FontSize = 11,
                Foreground = FindBrush("TextSecBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            tile.Child = label;

            tile.MouseEnter += (_, _) =>
            {
                if (tile.Tag as string != activeSceneId)
                    tile.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
            };
            tile.MouseLeave += (_, _) =>
            {
                if (tile.Tag as string != activeSceneId)
                    tile.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1C"));
            };
            tile.MouseLeftButtonUp += async (_, _) =>
            {
                // Reset previous active tile
                foreach (var child in wrap.Children)
                {
                    if (child is Border b && b.Tag as string != sceneId)
                    {
                        b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1C"));
                        b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
                    }
                }

                // Highlight active tile
                tile.Background = new SolidColorBrush(ThemeManager.Accent) { Opacity = 0.25 };
                tile.BorderBrush = new SolidColorBrush(ThemeManager.Accent);
                activeSceneId = sceneId;

                var sc = scenes.FirstOrDefault(s => s.Id == sceneId);
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.SetScene(sceneId, sc?.Name ?? "")));
            };

            wrap.Children.Add(tile);
        }

        container.Children.Add(wrap);
    }

    // ── Segment content ───────────────────────────────────────────────

    private UIElement BuildSegmentsContent(GoveeDeviceInfo device)
    {
        var container = new StackPanel();

        // Segment count would ideally come from the capability parameters;
        // since Capabilities is List<string> (type strings only), use a safe default.
        int segmentCount = 6;

        var segColors = new (byte R, byte G, byte B)[segmentCount];
        for (int i = 0; i < segmentCount; i++) segColors[i] = (255, 255, 255);

        var segRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        for (int i = 0; i < segmentCount; i++)
        {
            var idx = i;
            var segBorder = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = FindBrush("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 4),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Segment {idx + 1}",
            };

            segBorder.MouseLeftButtonUp += (_, _) =>
            {
                var (r, g, b) = segColors[idx];
                ShowColorPicker(r, g, b, (nr, ng, nb) =>
                {
                    segColors[idx] = (nr, ng, nb);
                    segBorder.Background = new SolidColorBrush(Color.FromRgb(nr, ng, nb));
                });
            };

            segRow.Children.Add(segBorder);
        }

        container.Children.Add(segRow);

        var applyBtn = new Button
        {
            Content = "Apply Segments",
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 12,
        };
        applyBtn.Click += async (_, _) =>
        {
            for (int si = 0; si < segColors.Length; si++)
            {
                var (sr, sg, sb) = segColors[si];
                int capturedIdx = si;
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(
                    device.Device, device.Sku,
                    GoveeCloudApi.SetSegmentColor(capturedIdx, sr, sg, sb)));
            }
        };
        container.Children.Add(applyBtn);

        return container;
    }

    // ── Music mode content ────────────────────────────────────────────

    private UIElement BuildMusicModeContent(GoveeDeviceInfo device)
    {
        var container = new StackPanel();

        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

        var musicToggle = new CheckBox
        {
            Content = "Enable Music Mode",
            Foreground = FindBrush("TextPrimaryBrush"),
            FontSize = 13,
            ToolTip = "Sync device LEDs to audio via Govee Cloud",
        };
        musicToggle.Checked += async (_, _) =>
            await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.SetMusicMode(1, 50)));
        musicToggle.Unchecked += async (_, _) =>
            await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.SetMusicMode(0, 0)));

        toggleRow.Children.Add(musicToggle);
        container.Children.Add(toggleRow);

        // Sensitivity
        var sensLabel = new TextBlock
        {
            Text = "Sensitivity",
            Style = FindStyle("SecondaryText"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        };
        container.Children.Add(sensLabel);

        var sensRow = new StackPanel { Orientation = Orientation.Horizontal };

        var sensValText = new TextBlock
        {
            Text = "50",
            Width = 30,
            Style = FindStyle("SecondaryText"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var sensSlider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Value = 50,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Music mode sensitivity (1-100)",
        };
        sensSlider.ValueChanged += (_, _) =>
        {
            sensValText.Text = ((int)sensSlider.Value).ToString();
        };
        var sensDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        sensDebounce.Tick += async (_, _) =>
        {
            sensDebounce.Stop();
            if (musicToggle.IsChecked == true)
                await SafeCloudCall(() => _cloudApi!.ControlDeviceAsync(device.Device, device.Sku, GoveeCloudApi.SetMusicMode(1, (int)sensSlider.Value)));
        };
        sensSlider.ValueChanged += (_, _) =>
        {
            sensDebounce.Stop();
            sensDebounce.Start();
        };

        sensRow.Children.Add(sensSlider);
        sensRow.Children.Add(sensValText);
        container.Children.Add(sensRow);

        return container;
    }

    // ── Color picker helper ───────────────────────────────────────────

    private void ShowColorPicker(byte r, byte g, byte b, Action<byte, byte, byte> onColorSelected)
    {
        var win = new Window
        {
            Title = "Color",
            Width = 240,
            Height = 200,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C1C")),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
        };

        var stack = new StackPanel { Margin = new Thickness(16) };

        var rLabel = new TextBlock { Text = "R", Foreground = new SolidColorBrush(Colors.OrangeRed), FontSize = 11 };
        var rSlider = new Slider { Minimum = 0, Maximum = 255, Value = r, Margin = new Thickness(0, 0, 0, 8) };
        var gLabel = new TextBlock { Text = "G", Foreground = new SolidColorBrush(Colors.LightGreen), FontSize = 11 };
        var gSlider = new Slider { Minimum = 0, Maximum = 255, Value = g, Margin = new Thickness(0, 0, 0, 8) };
        var bLabel = new TextBlock { Text = "B", Foreground = new SolidColorBrush(Colors.CornflowerBlue), FontSize = 11 };
        var bSlider = new Slider { Minimum = 0, Maximum = 255, Value = b, Margin = new Thickness(0, 0, 0, 12) };

        var preview = new Border
        {
            Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
            Margin = new Thickness(0, 0, 0, 12),
        };

        Action updatePreview = () =>
        {
            preview.Background = new SolidColorBrush(Color.FromRgb(
                (byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value));
        };

        rSlider.ValueChanged += (_, _) => updatePreview();
        gSlider.ValueChanged += (_, _) => updatePreview();
        bSlider.ValueChanged += (_, _) => updatePreview();

        var okBtn = new Button
        {
            Content = "OK",
            Padding = new Thickness(20, 4, 20, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        okBtn.Click += (_, _) => { win.DialogResult = true; };

        stack.Children.Add(rLabel);
        stack.Children.Add(rSlider);
        stack.Children.Add(gLabel);
        stack.Children.Add(gSlider);
        stack.Children.Add(bLabel);
        stack.Children.Add(bSlider);
        stack.Children.Add(preview);
        stack.Children.Add(okBtn);

        win.Content = stack;

        if (win.ShowDialog() == true)
        {
            onColorSelected(
                (byte)rSlider.Value,
                (byte)gSlider.Value,
                (byte)bSlider.Value);
        }
    }

    // ── Safe API call wrapper ─────────────────────────────────────────

    private static async Task SafeCloudCall(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee cloud API error: {ex.Message}");
        }
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

        // Collect device configs from device rows — sync mode is always "global" (controlled via Mixer tab knob)
        cfg.GoveeDevices.Clear();
        foreach (var (ip, name) in _deviceCombos)
        {
            cfg.GoveeDevices.Add(new GoveeDeviceConfig { Ip = ip, Name = name, SyncMode = "global" });
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
        _brightnessSlider.AccentColor = accent;
    }

    // ── Resource helpers ──────────────────────────────────────────────

    private Brush FindBrush(string key) => (Brush)(FindResource(key) ?? Brushes.White);
    private Style? FindStyle(string key) => FindResource(key) as Style;
}
