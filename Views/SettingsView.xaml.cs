using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AmpUp.Core.Services;

namespace AmpUp.Views;

public partial class SettingsView : UserControl
{
    private static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Green",      "#00E676"),
        ("Cyan",       "#00B4D8"),
        ("Blue",       "#448AFF"),
        ("Purple",     "#B388FF"),
        ("Pink",       "#FF4081"),
        ("Red",        "#FF5252"),
        ("Orange",     "#FF6E40"),
        ("Gold",       "#FFD740"),
        ("Mint",       "#69F0AE"),
        ("White",      "#E0E0E0"),
        ("Lime",       "#C6FF00"),
        ("Teal",       "#1DE9B6"),
        ("Sky",        "#40C4FF"),
        ("Indigo",     "#536DFE"),
        ("Lavender",   "#CE93D8"),
        ("Coral",      "#FF8A80"),
        ("Peach",      "#FFAB91"),
        ("Amber",      "#FFCA28"),
        ("Aqua",       "#84FFFF"),
        ("Rose",       "#F48FB1"),
    };

    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private CorsairSync? _corsairSyncRef;
    public Action? OnNavigateToOverview { get; set; }
    public Action<string>? OnEditProfile { get; set; }
    public Action<DeviceSurface>? OnActiveSurfaceChangedExternal { get; set; }
    public Action? OnHardwareModeChangedExternal { get; set; }
    private readonly DispatcherTimer _debounceTimer;
    private bool _loading;
    private bool _configLoaded;
    private bool _turnUpConnected;
    private bool _streamControllerConnected;

    public SettingsView()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            CollectAndSave();
        };

        // Wire up change events
        TxtSerialPort.TextChanged += OnValueChanged;
        TxtBaudRate.TextChanged += OnValueChanged;
        ChkStartWithWindows.Checked += OnValueChanged;
        ChkStartWithWindows.Unchecked += OnValueChanged;
        ChkAutoSuggestLayout.Checked += OnValueChanged;
        ChkAutoSuggestLayout.Unchecked += OnValueChanged;
        CmbProfiles.SelectionChanged += OnProfileSelectionChanged;

        // Port selector
        SegHardwareMode.AddSegment("Auto", HardwareMode.Auto);
        SegHardwareMode.AddSegment("Turn Up", HardwareMode.TurnUpOnly);
        SegHardwareMode.AddSegment("Stream Controller", HardwareMode.StreamControllerOnly);
        SegHardwareMode.AddSegment("Both", HardwareMode.DualMode);
        SegHardwareMode.SelectionChanged += OnHardwareModeChanged;

        SegActiveSurface.AddSegment("Turn Up", DeviceSurface.TurnUp);
        SegActiveSurface.AddSegment("Stream Controller", DeviceSurface.StreamController);
        SegActiveSurface.AddSegment("Both", DeviceSurface.Both);
        SegActiveSurface.SelectionChanged += OnActiveSurfaceChanged;

        SldN3IdleSleep.ValueChanged += (_, _) =>
        {
            int secs = SnapN3IdleSeconds((int)Math.Round(SldN3IdleSleep.Value));
            TxtN3IdleSleepLabel.Text = $"Stream Controller Screen Sleep: {FormatN3IdleDuration(secs)}";
            if (!_loading && _config != null)
            {
                _config.N3.IdleSleepSeconds = secs;
                OnValueChanged(null, EventArgs.Empty);
            }
        };
        BtnN3SleepNow.Click += (_, _) => (Application.Current as App)?.ForceN3Sleep();
        CmbSerialPort.SelectionChanged += OnPortComboSelectionChanged;
        BtnRefreshPorts.Click += (_, _) => RefreshPortList();
        BtnAutoDetect.Click += OnAutoDetect;

        // Integration events
        ChkHaEnabled.Checked += OnValueChanged;
        ChkHaEnabled.Unchecked += OnValueChanged;
        TxtHaUrl.TextChanged += OnValueChanged;
        TxtHaToken.PasswordChanged += OnPasswordChanged;
        BtnHaTest.Click += OnHaTest;
        BtnHaRefresh.Click += OnHaRefresh;
        // Profile buttons
        BtnSaveProfile.Click += OnSaveProfile;
        BtnLoadProfile.Click += OnLoadProfile;
        BtnEditProfile.Click += (_, _) => OnEditProfile?.Invoke(_config?.ActiveProfile ?? "Default");
        BtnNewProfile.Click += OnNewProfile;
        BtnDeleteProfile.Click += OnDeleteProfile;
        BtnOverview.Click += (_, _) => OnNavigateToOverview?.Invoke();

        // Import / Export
        BtnImportTurnUp.Click += OnImportTurnUp;
        BtnExportProfile.Click += OnExportProfile;
        BtnImportProfile.Click += OnImportProfile;

        // Govee
        ChkGoveeEnabled.Checked += OnGoveeEnabledChanged;
        ChkGoveeEnabled.Unchecked += OnGoveeEnabledChanged;
        BtnGoveeScan.Click += OnGoveeScan;
        BtnGoveeLanHelp.Click += (_, _) => GlassDialog.ShowInfo(
            "Enable LAN Control in the Govee Home app:\n\n" +
            "1. Open Govee Home on your phone\n" +
            "2. Tap the device → ⚙ Settings\n" +
            "3. Find \"LAN Control\" and toggle ON\n" +
            "4. Repeat for each device\n\n" +
            "Then click Scan Network again.",
            owner: Window.GetWindow(this));
        ChkGoveeCloudEnabled.Checked += OnGoveeCloudEnabledChanged;
        ChkGoveeCloudEnabled.Unchecked += OnGoveeCloudEnabledChanged;
        TxtGoveeApiKey.PasswordChanged += OnPasswordChanged;
        BtnGoveeSetupGuide.Click += OnGoveeSetupGuide;

        // OBS Studio
        ChkObsEnabled.Checked += OnValueChanged;
        ChkObsEnabled.Unchecked += OnValueChanged;
        TxtObsHost.TextChanged += OnValueChanged;
        TxtObsPort.TextChanged += OnValueChanged;
        TxtObsPassword.PasswordChanged += OnPasswordChanged;
        BtnObsTest.Click += OnObsTest;

        // OBS Studio
        ChkObsEnabled.Checked += OnValueChanged;
        ChkObsEnabled.Unchecked += OnValueChanged;
        TxtObsHost.TextChanged += OnValueChanged;
        TxtObsPort.TextChanged += OnValueChanged;
        TxtObsPassword.PasswordChanged += OnPasswordChanged;
        BtnObsTest.Click += OnObsTest;

        // VoiceMeeter
        ChkVmEnabled.Checked += OnValueChanged;
        ChkVmEnabled.Unchecked += OnValueChanged;

        // Corsair iCUE
        ChkCorsairEnabled.Checked += OnCorsairEnabledChanged;
        ChkCorsairEnabled.Unchecked += OnCorsairEnabledChanged;

        // About
        TxtVersion.Text = $"Amp Up v{UpdateChecker.CurrentVersion}";
        BtnCheckUpdate.Click += OnCheckUpdate;

        // Buy Me a Coffee link
        CoffeeFooter.MouseLeftButtonDown += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.buymeacoffee.com/audioslayer") { UseShellExecute = true }); }
            catch { }
        };
        CoffeeFooter.MouseEnter += (_, _) => CoffeeFooter.Opacity = 1.0;
        CoffeeFooter.MouseLeave += (_, _) => CoffeeFooter.Opacity = 0.85;
    }

    // Reference to AmbienceSync for LAN scanning (set from App.xaml.cs)
    private AmbienceSync? _ambienceSync;

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        TxtSerialPort.Text = config.Serial.Port;
        TxtBaudRate.Text = config.Serial.Baud.ToString();
        SegHardwareMode.SelectedIndex = config.HardwareMode switch
        {
            HardwareMode.TurnUpOnly => 1,
            HardwareMode.StreamControllerOnly => 2,
            HardwareMode.DualMode => 3,
            _ => 0,
        };
        SegActiveSurface.SelectedIndex = config.TabSelection.PreferredSurface switch
        {
            DeviceSurface.StreamController => 1,
            DeviceSurface.Both => 2,
            _ => 0,
        };
        RefreshActiveSurfaceVisibility();
        SldN3IdleSleep.Value = Math.Clamp(config.N3.IdleSleepSeconds, 0, 3600);
        {
            int secs = SnapN3IdleSeconds((int)Math.Round(SldN3IdleSleep.Value));
            TxtN3IdleSleepLabel.Text = $"Stream Controller Screen Sleep: {FormatN3IdleDuration(secs)}";
        }
        RefreshPortList(selectPort: config.Serial.Port);
        ChkStartWithWindows.IsChecked = config.StartWithWindows;
        ChkAutoSuggestLayout.IsChecked = config.AutoSuggestLayout;

        // Profiles
        CmbProfiles.ClearItems();
        int activeProfileIdx = -1;
        for (int i = 0; i < config.Profiles.Count; i++)
        {
            var profile = config.Profiles[i];
            CmbProfiles.AddItem(profile, profile);
            if (profile == config.ActiveProfile) activeProfileIdx = i;
        }
        if (activeProfileIdx >= 0) CmbProfiles.SelectedIndex = activeProfileIdx;

        // Integrations — Home Assistant
        ChkHaEnabled.IsChecked = config.HomeAssistant.Enabled;
        TxtHaUrl.Text = config.HomeAssistant.Url;
        TxtHaToken.Password = config.HomeAssistant.Token;
        RefreshHaHeaderStatus();

        // Auto-test HA connection if enabled
        if (config.HomeAssistant.Enabled && !string.IsNullOrWhiteSpace(config.HomeAssistant.Token))
            _ = AutoTestHaAsync();

        // Integrations — OBS Studio
        ChkObsEnabled.IsChecked = config.Obs.Enabled;
        TxtObsHost.Text = config.Obs.Host;
        TxtObsPort.Text = config.Obs.Port.ToString();
        TxtObsPassword.Password = config.Obs.Password;
        RefreshObsHeaderStatus();

        // Integrations — VoiceMeeter
        ChkVmEnabled.IsChecked = config.VoiceMeeter.Enabled;
        RefreshVmHeaderStatus();

        // Integrations — Corsair iCUE
        ChkCorsairEnabled.IsChecked = config.Corsair.Enabled;
        RefreshCorsairStatus();

        // Integrations — Govee
        ChkGoveeEnabled.IsChecked = config.Ambience.GoveeEnabled;
        GoveeLanSection.Visibility = config.Ambience.GoveeEnabled ? Visibility.Visible : Visibility.Collapsed;
        ChkGoveeCloudEnabled.IsChecked = config.Ambience.GoveeCloudEnabled;
        GoveeCloudSection.Visibility = config.Ambience.GoveeCloudEnabled ? Visibility.Visible : Visibility.Collapsed;
        TxtGoveeApiKey.Password = config.Ambience.GoveeApiKey;
        RefreshGoveeStatus();
        RefreshGoveeCloudStatus();
        RefreshGoveeDeviceList();
        RefreshGoveeAmbienceHint();

        BuildAccentSwatches();
        BuildCardThemeSwatches();

        _loading = false;
        _configLoaded = true;
    }

    public void SetAmbienceSync(AmbienceSync sync) => _ambienceSync = sync;

    private void BuildAccentSwatches()
    {
        AccentSwatches.Children.Clear();
        foreach (var (name, hex) in AccentPresets)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                BorderBrush = hex == _config?.AccentColor
                    ? new SolidColorBrush(Colors.White)
                    : Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                if (_config == null || _onSave == null) return;
                _config.AccentColor = hex;
                ThemeManager.SetAccentColor(hex);
                BuildAccentSwatches(); // refresh selection indicator
                _onSave(_config);
            };
            AccentSwatches.Children.Add(swatch);
        }

        // Custom color picker swatch — always shows rainbow gradient + "+"
        var isCustomAccent = _config?.AccentColor != null
            && !AccentPresets.Any(p => p.Hex.Equals(_config.AccentColor, StringComparison.OrdinalIgnoreCase));
        var customSwatch = new Border
        {
            Width = 32, Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Colors.Red, 0.0), new(Colors.Yellow, 0.17), new(Colors.Lime, 0.33),
                    new(Colors.Cyan, 0.5), new(Colors.Blue, 0.67), new(Colors.Magenta, 0.83), new(Colors.Red, 1.0),
                }, new Point(0, 0), new Point(1, 1)),
            BorderThickness = new Thickness(2),
            BorderBrush = isCustomAccent
                ? new SolidColorBrush(Colors.White)
                : Brushes.Transparent,
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand,
            ToolTip = "Pick a custom color",
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        customSwatch.MouseLeftButtonDown += (_, _) =>
        {
            if (_config == null || _onSave == null) return;
            var initial = isCustomAccent ? ThemeManager.Accent : ThemeManager.Accent;
            var dialog = new ColorPickerDialog(initial) { Owner = Window.GetWindow(this) };
            dialog.ShowDialog();
            var hex = $"#{dialog.SelectedColor.R:X2}{dialog.SelectedColor.G:X2}{dialog.SelectedColor.B:X2}";
            _config.AccentColor = hex;
            ThemeManager.SetAccentColor(hex);
            BuildAccentSwatches();
            _onSave(_config);
        };
        AccentSwatches.Children.Add(customSwatch);
    }

    private void BuildCardThemeSwatches()
    {
        CardThemeSwatches.Children.Clear();
        var currentTheme = _config?.CardTheme ?? "Midnight";

        foreach (var theme in ThemeManager.CardThemes)
        {
            var bgColor = (Color)ColorConverter.ConvertFromString(theme.BgBase);
            var cardColor = (Color)ColorConverter.ConvertFromString(theme.CardBg);
            var inputColor = (Color)ColorConverter.ConvertFromString(theme.InputBg);
            var borderColor = (Color)ColorConverter.ConvertFromString(theme.CardBorder);
            var isSelected = theme.Name == currentTheme;

            // Outer wrapper: vertical stack with gradient swatch + label
            var wrapper = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 8),
                Cursor = Cursors.Hand,
            };

            // Gradient swatch showing the 3 theme layers
            var swatch = new Border
            {
                Width = 48, Height = 32,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = isSelected
                    ? new SolidColorBrush(ThemeManager.Accent)
                    : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(bgColor, 0.0),
                        new(cardColor, 0.5),
                        new(inputColor, 1.0),
                    },
                    new Point(0, 0), new Point(1, 1)),
            };

            // Hover effect
            swatch.MouseEnter += (_, _) =>
            {
                if (!isSelected)
                    swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
            };
            swatch.MouseLeave += (_, _) =>
            {
                if (!isSelected)
                    swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            };

            wrapper.Children.Add(swatch);

            // Label below
            var label = new TextBlock
            {
                Text = theme.Name,
                FontSize = 9,
                Foreground = isSelected
                    ? new SolidColorBrush(ThemeManager.Accent)
                    : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            wrapper.Children.Add(label);

            wrapper.MouseLeftButtonDown += (_, _) =>
            {
                if (_config == null || _onSave == null) return;
                _config.CardTheme = theme.Name;
                ThemeManager.SetCardTheme(theme.Name);
                BuildCardThemeSwatches();
                _onSave(_config);
            };

            CardThemeSwatches.Children.Add(wrapper);
        }
    }

    private void OnValueChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender == TxtGoveeApiKey) RefreshGoveeCloudStatus();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>
    /// Carry over global settings (OSD, serial, startup, integrations) from current config to a loaded profile.
    /// </summary>
    private void PreserveGlobalSettings(AppConfig loaded)
    {
        if (_config == null) return;
        loaded.Osd = _config.Osd;
        loaded.Serial = _config.Serial;
        loaded.StartWithWindows = _config.StartWithWindows;
        loaded.HomeAssistant = _config.HomeAssistant;
        loaded.Obs = _config.Obs;
        loaded.Profiles = _config.Profiles;
        loaded.ProfileIcons = _config.ProfileIcons;
        loaded.Ducking = _config.Ducking;
        loaded.AutoSwitch = _config.AutoSwitch;
        loaded.Ambience = _config.Ambience;
        loaded.VoiceMeeter = _config.VoiceMeeter;
        loaded.Groups = _config.Groups;
    }

    private void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_loading || _config == null || CmbProfiles.SelectedIndex < 0) return;

        var selected = CmbProfiles.SelectedTag as string ?? CmbProfiles.SelectedDisplay;
        if (string.IsNullOrEmpty(selected) || selected == _config.ActiveProfile) return;

        _config.ActiveProfile = selected;

        // Load the profile data
        var loaded = ConfigManager.LoadProfile(selected);
        if (loaded != null)
        {
            loaded.ActiveProfile = selected;
            PreserveGlobalSettings(loaded);
            _config = loaded;
            _onSave?.Invoke(_config);
            LoadConfig(_config, _onSave!);
        }
        else
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();
    }

    private void RefreshProfileDropdown()
    {
        _loading = true;
        CmbProfiles.ClearItems();
        int activeIdx = -1;
        for (int i = 0; i < _config!.Profiles.Count; i++)
        {
            var p = _config.Profiles[i];
            CmbProfiles.AddItem(p, p);
            if (p == _config.ActiveProfile) activeIdx = i;
        }
        if (activeIdx >= 0) CmbProfiles.SelectedIndex = activeIdx;
        _loading = false;
    }

    // ── Port selector helpers ──────────────────────────────────────────

    private void RefreshPortList(string? selectPort = null)
    {
        _loading = true;
        var ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.OrdinalIgnoreCase);

        CmbSerialPort.ClearItems();
        int targetIdx = -1;
        var target = selectPort ?? TxtSerialPort.Text.Trim();
        for (int i = 0; i < ports.Length; i++)
        {
            CmbSerialPort.AddItem(ports[i], ports[i]);
            if (!string.IsNullOrEmpty(target) && string.Equals(ports[i], target, StringComparison.OrdinalIgnoreCase))
                targetIdx = i;
        }

        if (targetIdx >= 0)
            CmbSerialPort.SelectedIndex = targetIdx;
        else if (CmbSerialPort.ItemCount > 0)
            CmbSerialPort.SelectedIndex = 0;

        _loading = false;
    }

    private void OnPortComboSelectionChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (CmbSerialPort.SelectedTag is string port)
        {
            _loading = true;
            TxtSerialPort.Text = port;
            _loading = false;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnAutoDetect(object sender, RoutedEventArgs e)
    {
        BtnAutoDetect.IsEnabled = false;
        BtnAutoDetect.Content = "Scanning...";

        // Look for Turn Up by checking port names for known CH34x signatures
        // (friendly names from registry aren't reliably available without WMI on .NET 8)
        var ports = SerialPort.GetPortNames();
        string? found = null;

        // Try to find a port whose registry description mentions CH343/CH340
        foreach (var port in ports)
        {
            var desc = GetPortDescription(port);
            if (desc != null &&
                (desc.Contains("CH343", StringComparison.OrdinalIgnoreCase) ||
                 desc.Contains("CH340", StringComparison.OrdinalIgnoreCase) ||
                 desc.Contains("USB-SERIAL", StringComparison.OrdinalIgnoreCase) ||
                 desc.Contains("Turn Up", StringComparison.OrdinalIgnoreCase)))
            {
                found = port;
                break;
            }
        }

        if (found != null)
        {
            RefreshPortList(selectPort: found);
            _loading = true;
            TxtSerialPort.Text = found;
            _loading = false;
            _debounceTimer.Stop();
            _debounceTimer.Start();
            GlassDialog.ShowInfo($"AmpUp hardware found on {found}.", owner: Window.GetWindow(this));
        }
        else if (ports.Length == 1)
        {
            // Only one port — select it automatically
            RefreshPortList(selectPort: ports[0]);
            _loading = true;
            TxtSerialPort.Text = ports[0];
            _loading = false;
            _debounceTimer.Stop();
            _debounceTimer.Start();
            GlassDialog.ShowInfo($"One port found — selected {ports[0]}.", owner: Window.GetWindow(this));
        }
        else
        {
            RefreshPortList();
            GlassDialog.ShowWarning(
                "Could not identify the AmpUp hardware automatically.\nSelect the correct COM port from the dropdown.",
                owner: Window.GetWindow(this));
        }

        BtnAutoDetect.IsEnabled = true;
        BtnAutoDetect.Content = "Auto-Detect";
    }

    /// <summary>
    /// Tries to read a friendly description for a COM port from the Windows registry.
    /// Returns null if not available.
    /// </summary>
    private static string? GetPortDescription(string port)
    {
        try
        {
            // Check HKLM\SYSTEM\CurrentControlSet\Enum for USB serial devices
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            string[] searchPaths = ["SYSTEM\\CurrentControlSet\\Enum\\USB", "SYSTEM\\CurrentControlSet\\Enum\\FTDIBUS"];
            foreach (var basePath in searchPaths)
            {
                using var usbKey = baseKey.OpenSubKey(basePath);
                if (usbKey == null) continue;
                foreach (var vidPid in usbKey.GetSubKeyNames())
                {
                    using var vidKey = usbKey.OpenSubKey(vidPid);
                    if (vidKey == null) continue;
                    foreach (var instanceId in vidKey.GetSubKeyNames())
                    {
                        using var instKey = vidKey.OpenSubKey(instanceId);
                        if (instKey == null) continue;
                        using var paramsKey = instKey.OpenSubKey("Device Parameters");
                        if (paramsKey?.GetValue("PortName") is string portName &&
                            string.Equals(portName, port, StringComparison.OrdinalIgnoreCase))
                        {
                            // Found the instance — return friendly name
                            return instKey.GetValue("FriendlyName") as string
                                ?? instKey.GetValue("DeviceDesc") as string;
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private void AdvancedToggle_Click(object sender, MouseButtonEventArgs e)
    {
        bool show = AdvancedSection.Visibility == Visibility.Collapsed;
        AdvancedSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedArrow.Text = show ? "▼" : "▶";
    }

    /// <summary>
    /// Updates the connection status indicator. Called from App.xaml.cs when device connects/disconnects.
    /// </summary>
    public void UpdateConnectionStatus(bool connected, string? portName = null)
    {
        Dispatcher.Invoke(() =>
        {
            _turnUpConnected = connected;
            ConnectionDot.Fill = new SolidColorBrush(connected
                ? (Color)ColorConverter.ConvertFromString("#00E676")
                : (Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtConnectionStatus.Text = connected
                ? $"Connected on {portName ?? "unknown"}"
                : "Disconnected";
            RefreshActiveSurfaceVisibility();
        });
    }

    // ── Config collect/save ────────────────────────────────────────────

    public void UpdateN3ConnectionStatus(bool connected, string? deviceName = null)
    {
        Dispatcher.Invoke(() =>
        {
            _streamControllerConnected = connected;
            N3ConnectionDot.Fill = new SolidColorBrush(connected
                ? (Color)ColorConverter.ConvertFromString("#00E676")
                : (Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtN3ConnectionStatus.Text = connected
                ? $"Connected over USB HID{(string.IsNullOrWhiteSpace(deviceName) ? "" : $" ({deviceName})")}"
                : "Not detected";
            RefreshActiveSurfaceVisibility();
        });
    }

    public void RefreshActiveSurfaceVisibility()
    {
        if (_config == null) return;
        bool show = _config.HardwareMode == HardwareMode.DualMode
            || (_config.HardwareMode == HardwareMode.Auto && _turnUpConnected && _streamControllerConnected);
        ActiveSurfacePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Snap a raw second value from the slider to a natural stop:
    /// 0 (never), 5/10/15/30s, 1m/2m/5m/10m/15m/30m/60m. Lets users pick
    /// short test durations (10s) but doesn't offer meaningless granularity.
    /// </summary>
    private static int SnapN3IdleSeconds(int raw)
    {
        int[] stops = { 0, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
        int best = stops[0];
        int bestDist = int.MaxValue;
        foreach (var s in stops)
        {
            int d = Math.Abs(raw - s);
            if (d < bestDist) { bestDist = d; best = s; }
        }
        return best;
    }

    private static string FormatN3IdleDuration(int seconds)
    {
        if (seconds <= 0) return "Never";
        if (seconds < 60) return $"{seconds}s";
        int mins = seconds / 60;
        int rem = seconds % 60;
        if (rem == 0) return $"{mins}m";
        return $"{mins}m {rem}s";
    }

    private void OnHardwareModeChanged(object? sender, EventArgs e)
    {
        if (_loading || _config == null || !_configLoaded)
        {
            RefreshActiveSurfaceVisibility();
            return;
        }

        if (SegHardwareMode.SelectedTag is HardwareMode mode)
            _config.HardwareMode = mode;

        RefreshActiveSurfaceVisibility();
        OnValueChanged(sender, e);
        OnHardwareModeChangedExternal?.Invoke();
    }

    private void OnActiveSurfaceChanged(object? sender, EventArgs e)
    {
        if (_loading || _config == null || _onSave == null || !_configLoaded) return;
        if (SegActiveSurface.SelectedTag is not DeviceSurface surface) return;

        // Persist the user's choice in PreferredSurface so the auto-detect
        // pathway (which rewrites Mixer/Buttons/Lights) doesn't clobber it
        // when Turn Up connects before the Stream Controller at startup.
        _config.TabSelection.PreferredSurface = surface;
        _config.TabSelection.Mixer = surface;
        _config.TabSelection.Buttons = surface;
        _config.TabSelection.Lights = surface;
        OnActiveSurfaceChangedExternal?.Invoke(surface);
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null || !_configLoaded) return;

        _config.Serial.Port = TxtSerialPort.Text.Trim();
        if (int.TryParse(TxtBaudRate.Text.Trim(), out var baud))
            _config.Serial.Baud = baud;
        if (SegHardwareMode.SelectedTag is HardwareMode hardwareMode)
            _config.HardwareMode = hardwareMode;

        _config.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _config.AutoSuggestLayout = ChkAutoSuggestLayout.IsChecked == true;

        // Integrations
        _config.HomeAssistant.Enabled = ChkHaEnabled.IsChecked == true;
        _config.HomeAssistant.Url = TxtHaUrl.Text.Trim();
        _config.HomeAssistant.Token = TxtHaToken.Password;

        // OBS Studio
        _config.Obs.Enabled = ChkObsEnabled.IsChecked == true;
        _config.Obs.Host = TxtObsHost.Text.Trim();
        if (int.TryParse(TxtObsPort.Text.Trim(), out var obsPort))
            _config.Obs.Port = obsPort;
        _config.Obs.Password = TxtObsPassword.Password;

        // Govee
        _config.Ambience.GoveeEnabled = ChkGoveeEnabled.IsChecked == true;
        _config.Ambience.GoveeCloudEnabled = ChkGoveeCloudEnabled.IsChecked == true;
        _config.Ambience.GoveeApiKey = TxtGoveeApiKey.Password;

        // VoiceMeeter
        _config.VoiceMeeter.Enabled = ChkVmEnabled.IsChecked == true;

        // Corsair iCUE
        _config.Corsair.Enabled = ChkCorsairEnabled.IsChecked == true;

        _onSave(_config);
    }

    private void OnSaveProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var profileName = _config.ActiveProfile;
        CollectAndSave();
        ConfigManager.SaveProfile(_config, profileName);

        GlassDialog.ShowInfo($"Profile \"{profileName}\" saved.", owner: Window.GetWindow(this));
    }

    private void OnLoadProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null || CmbProfiles.SelectedIndex < 0) return;

        var profileName = (CmbProfiles.SelectedTag as string) ?? CmbProfiles.SelectedDisplay;
        if (string.IsNullOrEmpty(profileName)) return;
        var loaded = ConfigManager.LoadProfile(profileName);
        if (loaded != null)
        {
            loaded.ActiveProfile = profileName;
            PreserveGlobalSettings(loaded);
            _config = loaded;
            _onSave?.Invoke(_config);
            LoadConfig(_config, _onSave!);

            GlassDialog.ShowInfo($"Profile \"{profileName}\" loaded.", owner: Window.GetWindow(this));
        }
        else
        {
            GlassDialog.ShowWarning($"Profile \"{profileName}\" not found on disk.", owner: Window.GetWindow(this));
        }
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var name = GlassDialog.Prompt("Enter profile name:", "NEW PROFILE", owner: Window.GetWindow(this));
        if (!string.IsNullOrWhiteSpace(name))
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (_config.Profiles.Contains(name))
            {
                GlassDialog.ShowWarning($"Profile \"{name}\" already exists.", owner: Window.GetWindow(this));
                return;
            }

            _config.Profiles.Add(name);
            _config.ActiveProfile = name;
            CollectAndSave();
            ConfigManager.SaveProfile(_config, name);

            // Refresh dropdowns
            RefreshProfileDropdown();
            (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();
        }
    }

    private void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null || CmbProfiles.SelectedIndex < 0) return;

        var profileName = (CmbProfiles.SelectedTag as string) ?? CmbProfiles.SelectedDisplay;
        if (string.IsNullOrEmpty(profileName)) return;
        if (profileName == "Default")
        {
            GlassDialog.ShowWarning("Cannot delete the Default profile.", owner: Window.GetWindow(this));
            return;
        }

        if (!GlassDialog.Confirm($"Delete profile \"{profileName}\"? This cannot be undone.",
            "DELETE PROFILE", dangerYes: true, owner: Window.GetWindow(this)))
            return;

        _config.Profiles.Remove(profileName);
        _config.ActiveProfile = "Default";

        // Delete profile file from AppData
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmpUp");
        var safe = string.Concat(profileName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        var path = System.IO.Path.Combine(configDir, $"profile_{safe}.json");
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }

        CollectAndSave();

        // Refresh dropdowns
        RefreshProfileDropdown();
        (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();
    }

    private void OnImportTurnUp(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var wizard = new ImportWizardWindow { Owner = Window.GetWindow(this) };
        wizard.ShowDialog();

        if (wizard.ImportedProfileName != null)
        {
            var profileName = wizard.ImportedProfileName;

            // Add to profile list if not already there
            if (!_config.Profiles.Contains(profileName))
                _config.Profiles.Add(profileName);

            // Switch to the imported profile
            var loaded = ConfigManager.LoadProfile(profileName);
            if (loaded != null)
            {
                loaded.ActiveProfile = profileName;
                PreserveGlobalSettings(loaded);
                _config = loaded;
                _onSave?.Invoke(_config);
                LoadConfig(_config, _onSave!);

                (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();
            }
        }
    }

    private void OnExportProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var profileName = _config.ActiveProfile;
        var srcPath = ConfigManager.GetProfilePath(profileName);

        // Save current state first
        CollectAndSave();
        ConfigManager.SaveProfile(_config, profileName);

        var dlg = new SaveFileDialog
        {
            Title = $"Export Profile \"{profileName}\"",
            FileName = $"ampup_profile_{profileName.ToLowerInvariant()}.json",
            Filter = "JSON profile (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(srcPath, dlg.FileName, overwrite: true);
                GlassDialog.ShowInfo($"Profile \"{profileName}\" exported.", owner: Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                GlassDialog.ShowWarning($"Export failed: {ex.Message}", owner: Window.GetWindow(this));
            }
        }
    }

    private void OnImportProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Import Profile",
            Filter = "JSON profile (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var imported = Newtonsoft.Json.JsonConvert.DeserializeObject<AppConfig>(json);
            if (imported == null)
            {
                GlassDialog.ShowWarning("File does not appear to be a valid AmpUp profile.", owner: Window.GetWindow(this));
                return;
            }

            // Derive a profile name from the filename
            var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
            // Strip "ampup_profile_" prefix if present
            if (baseName.StartsWith("ampup_profile_", StringComparison.OrdinalIgnoreCase))
                baseName = baseName["ampup_profile_".Length..];
            // Capitalise first letter
            var profileName = baseName.Length > 0
                ? char.ToUpperInvariant(baseName[0]) + baseName[1..]
                : "Imported";

            // Make unique if needed
            var finalName = profileName;
            int idx = 2;
            while (_config.Profiles.Contains(finalName))
                finalName = $"{profileName}{idx++}";

            _config.Profiles.Add(finalName);
            imported.ActiveProfile = finalName;
            PreserveGlobalSettings(imported);
            ConfigManager.SaveProfile(imported, finalName);

            _config.ActiveProfile = finalName;
            _config = imported;
            _onSave?.Invoke(_config);
            LoadConfig(_config, _onSave!);
            (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();

            GlassDialog.ShowInfo($"Profile imported as \"{finalName}\".", owner: Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            GlassDialog.ShowWarning($"Import failed: {ex.Message}", owner: Window.GetWindow(this));
        }
    }

    // ── Home Assistant settings ────────────────────────────────────

    private async void OnHaTest(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        BtnHaTest.IsEnabled = false;
        TxtHaStatus.Text = "Testing...";
        HaStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
        using var ha = new HAIntegration(_config.HomeAssistant);
        var ok = await ha.TestConnectionAsync();
        HaStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ok ? "#00E676" : "#FF4444"));
        TxtHaStatus.Text = ok ? "Connected" : "Connection failed";
        BtnHaTest.IsEnabled = true;
        UpdateHaHeaderStatus(ok);
    }

    private async void OnHaRefresh(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        BtnHaRefresh.IsEnabled = false;
        TxtHaStatus.Text = "Refreshing...";
        using var ha = new HAIntegration(_config.HomeAssistant);
        var ok = await ha.RefreshEntitiesAsync();
        HaStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ok ? "#00E676" : "#FF4444"));
        TxtHaStatus.Text = ok ? $"Connected — {ha.CachedEntities.Count} entities" : "Connection failed";
        BtnHaRefresh.IsEnabled = true;
        UpdateHaHeaderStatus(ok);
    }

    private async Task AutoTestHaAsync()
    {
        if (_config == null) return;
        UpdateHaHeaderStatus(null); // show "Testing..."
        using var ha = new HAIntegration(_config.HomeAssistant);
        var ok = await ha.TestConnectionAsync();
        Dispatcher.Invoke(() =>
        {
            UpdateHaHeaderStatus(ok);
            HaStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ok ? "#00E676" : "#FF4444"));
            TxtHaStatus.Text = ok ? "Connected" : "Connection failed";
        });
    }

    private void RefreshHaHeaderStatus()
    {
        if (_config == null) return;
        bool enabled = ChkHaEnabled.IsChecked == true;
        if (!enabled)
        {
            HaStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtHaStatusHeader.Text = "Disabled";
        }
        else
        {
            HaStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtHaStatusHeader.Text = "Enabled";
        }
    }

    private void UpdateHaHeaderStatus(bool? connected)
    {
        if (connected == null)
        {
            HaStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtHaStatusHeader.Text = "Testing...";
        }
        else if (connected == true)
        {
            HaStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtHaStatusHeader.Text = "Connected";
        }
        else
        {
            HaStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtHaStatusHeader.Text = "Disconnected";
        }
    }

    // ── OBS Studio settings ────────────────────────────────────────

    private async void OnObsTest(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        BtnObsTest.IsEnabled = false;
        TxtObsStatus.Text = "Testing...";
        ObsStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));

        // Build config from current UI state
        var testConfig = new ObsConfig
        {
            Enabled = true,
            Host = TxtObsHost.Text.Trim(),
            Port = int.TryParse(TxtObsPort.Text.Trim(), out var p) ? p : 4455,
            Password = TxtObsPassword.Password,
        };

        using var obs = new ObsIntegration(testConfig);
        var ok = await obs.TestConnectionAsync();
        ObsStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ok ? "#00E676" : "#FF4444"));
        TxtObsStatus.Text = ok ? "Connected" : "Connection failed";
        BtnObsTest.IsEnabled = true;
        UpdateObsHeaderStatus(ok);
    }

    private void RefreshObsHeaderStatus()
    {
        if (_config == null) return;
        bool enabled = ChkObsEnabled.IsChecked == true;
        if (!enabled)
        {
            ObsStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtObsStatusHeader.Text = "Disabled";
        }
        else
        {
            ObsStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtObsStatusHeader.Text = "Enabled";
        }
    }

    private void UpdateObsHeaderStatus(bool? connected)
    {
        if (connected == null)
        {
            ObsStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtObsStatusHeader.Text = "Testing...";
        }
        else if (connected == true)
        {
            ObsStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtObsStatusHeader.Text = "Connected";
        }
        else
        {
            ObsStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtObsStatusHeader.Text = "Disconnected";
        }
    }

    // ── VoiceMeeter settings ──────────────────────────────────────────

    private void RefreshVmHeaderStatus()
    {
        if (_config == null) return;
        bool enabled = ChkVmEnabled.IsChecked == true;
        if (!enabled)
        {
            VmStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtVmStatusHeader.Text = "Disabled";
        }
        else
        {
            VmStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtVmStatusHeader.Text = "Enabled";
        }
    }

    public void UpdateVmStatus(bool? connected)
    {
        if (connected == null)
        {
            VmStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtVmStatusHeader.Text = "Connecting...";
        }
        else if (connected == true)
        {
            VmStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtVmStatusHeader.Text = "Connected";
        }
        else
        {
            VmStatusDotHeader.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtVmStatusHeader.Text = "Not Found";
        }
    }

    // ── Corsair iCUE settings ──────────────────────────────────────────

    private void OnCorsairEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        RefreshCorsairStatus();
        CollectAndSave();
    }

    private void RefreshCorsairStatus()
    {
        bool enabled = ChkCorsairEnabled.IsChecked == true;
        if (!enabled)
        {
            CorsairStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtCorsairStatus.Text = "Disabled";
        }
        else
        {
            CorsairStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtCorsairStatus.Text = "Enabled";
        }
        PopulateCorsairDeviceList();
    }

    public void SetCorsairSync(CorsairSync corsairSync)
    {
        _corsairSyncRef = corsairSync;
        if (IsLoaded)
            PopulateCorsairDeviceList();
        else
            Loaded += (_, _) => PopulateCorsairDeviceList();
    }

    private void PopulateCorsairDeviceList()
    {
        CorsairDeviceList.Children.Clear();
        if (ChkCorsairEnabled.IsChecked != true || _corsairSyncRef == null) return;

        if (_corsairSyncRef.IsAvailable && _corsairSyncRef.Devices.Count > 0)
        {
            foreach (var dev in _corsairSyncRef.Devices)
                CorsairDeviceList.Children.Add(BuildSettingsCorsairDeviceRow(dev));
        }
        else
        {
            CorsairDeviceList.Children.Add(new TextBlock
            {
                Text = _corsairSyncRef.IsAvailable ? "Discovering devices..." : "Connecting to iCUE...",
                Style = FindResource("SecondaryText") as Style,
                FontSize = 11, Margin = new Thickness(0, 4, 0, 4),
            });
            _ = RefreshSettingsCorsairDevicesAsync();
        }
    }

    private async Task RefreshSettingsCorsairDevicesAsync()
    {
        if (_corsairSyncRef == null) return;
        await Task.Delay(800);
        var devices = await _corsairSyncRef.GetDevicesAsync();
        _ = Dispatcher.BeginInvoke(() =>
        {
            CorsairDeviceList.Children.Clear();
            if (devices.Count > 0)
            {
                foreach (var dev in devices)
                    CorsairDeviceList.Children.Add(BuildSettingsCorsairDeviceRow(dev));
            }
            else
            {
                CorsairDeviceList.Children.Add(new TextBlock
                {
                    Text = _corsairSyncRef.IsAvailable
                        ? "No devices found — check iCUE"
                        : "iCUE not detected — make sure it's running with SDK enabled",
                    Style = FindResource("SecondaryText") as Style,
                    FontSize = 11, Margin = new Thickness(0, 4, 0, 4),
                });
            }
        });
    }

    private Border BuildSettingsCorsairDeviceRow(CorsairDevice dev)
    {
        var row = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Brush)FindResource("CardBorderBrush"),
            Padding = new Thickness(0, 6, 0, 6),
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = dev.Name,
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x00)),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = dev.Type.Replace("_", " "),
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{dev.LedCount} LEDs",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Child = content;
        return row;
    }

    // ── Govee settings ──────────────────────────────────────────────

    private void OnGoveeEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        GoveeLanSection.Visibility = ChkGoveeEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshGoveeStatus();
        RefreshGoveeAmbienceHint();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnGoveeCloudEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        GoveeCloudSection.Visibility = ChkGoveeCloudEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshGoveeCloudStatus();
        RefreshGoveeAmbienceHint();
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void RefreshGoveeAmbienceHint()
    {
        bool lanOn = ChkGoveeEnabled.IsChecked == true;
        bool cloudOn = ChkGoveeCloudEnabled.IsChecked == true;
        if (lanOn || cloudOn)
        {
            TxtGoveeAmbienceHint.Text = "✓ Ambience tab is available in the sidebar";
            TxtGoveeAmbienceHint.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00DD77"));
        }
        else
        {
            TxtGoveeAmbienceHint.Text = "Enable Govee to unlock the Ambience tab in the sidebar";
            TxtGoveeAmbienceHint.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9A9A9A"));
        }
    }

    private async void OnGoveeScan(object sender, RoutedEventArgs e)
    {
        if (_ambienceSync == null || _config == null) return;

        BtnGoveeScan.IsEnabled = false;
        TxtGoveeScanStatus.Text = "Scanning...";
        GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));

        try
        {
            var found = await _ambienceSync.ScanDevicesAsync();

            // If Cloud API is available, enrich names AND merge devices not found via LAN
            if (!string.IsNullOrEmpty(_config.Ambience.GoveeApiKey))
            {
                try
                {
                    using var api = new GoveeCloudApi(_config.Ambience.GoveeApiKey);
                    var cloudDevices = await api.GetDevicesAsync();
                    GoveeCloudApi.EnrichLanDevicesWithCloudNames(found, cloudDevices);

                    // Add cloud-only devices that didn't respond to LAN scan
                    // Default PoweredOn=false so they don't interfere with room effects
                    foreach (var cloud in cloudDevices)
                    {
                        if (string.IsNullOrEmpty(cloud.Device)) continue;
                        bool alreadyFound = found.Any(f =>
                            !string.IsNullOrEmpty(f.DeviceId) && f.DeviceId == cloud.Device);
                        if (!alreadyFound)
                        {
                            var name = !string.IsNullOrWhiteSpace(cloud.DeviceName) ? cloud.DeviceName
                                : AmbienceSync.GetProductName(cloud.Sku);
                            found.Add(new GoveeDeviceConfig
                            {
                                Ip = "",  // No LAN IP — cloud-only device
                                Name = name,
                                Sku = cloud.Sku,
                                DeviceId = cloud.Device,
                                SyncMode = "off",
                                PoweredOn = false,
                            });
                            Logger.Log($"Govee scan: added cloud-only device: {name} ({cloud.Sku}, {cloud.Device})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"Govee cloud enrichment failed: {ex.Message}");
                }
            }

            // Check if LAN scan actually found any devices with IPs
            bool lanScanWorked = found.Any(f => !string.IsNullOrWhiteSpace(f.Ip));
            bool hadExistingLan = _config.Ambience.GoveeDevices.Any(g => !string.IsNullOrWhiteSpace(g.Ip));

            if (found.Count == 0 || (!lanScanWorked && hadExistingLan))
            {
                // LAN scan failed or found nothing — keep existing devices, don't wipe IPs
                TxtGoveeScanStatus.Text = lanScanWorked ? "No devices found" : "LAN scan failed — keeping existing devices";
                GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            }
            else
            {
                // Preserve sync modes from previously saved devices
                var existing = _config.Ambience.GoveeDevices;
                foreach (var dev in found)
                {
                    var prev = existing.FirstOrDefault(e =>
                        (!string.IsNullOrEmpty(e.Ip) && e.Ip == dev.Ip) ||
                        (!string.IsNullOrEmpty(e.DeviceId) && e.DeviceId == dev.DeviceId));
                    if (prev != null && prev.SyncMode != "off")
                        dev.SyncMode = prev.SyncMode;
                }

                _config.Ambience.GoveeDevices = found;
                TxtGoveeScanStatus.Text = $"{found.Count} device(s) found";
                RefreshGoveeDeviceList();
                RefreshGoveeStatus();
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee scan error: {ex.Message}");
            TxtGoveeScanStatus.Text = "Scan failed";
            GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
        }

        BtnGoveeScan.IsEnabled = true;
    }

    private void OnGoveeSetupGuide(object sender, RoutedEventArgs e)
    {
        var guide = new Controls.GoveeSetupGuide();
        guide.ValidateKeyAsync = async (key) =>
        {
            using var api = new GoveeCloudApi(key);
            var devices = await api.GetDevicesAsync();
            return devices != null && devices.Count > 0;
        };
        guide.Owner = Window.GetWindow(this);
        if (guide.ShowDialog() == true && !string.IsNullOrEmpty(guide.ApiKey))
        {
            TxtGoveeApiKey.Password = guide.ApiKey;
            RefreshGoveeStatus();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void RefreshGoveeStatus()
    {
        if (_config == null) return;
        bool enabled = ChkGoveeEnabled.IsChecked == true;
        int deviceCount = _config.Ambience.GoveeDevices.Count;

        if (!enabled)
        {
            GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtGoveeStatus.Text = "Disabled";
        }
        else if (deviceCount > 0)
        {
            GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtGoveeStatus.Text = $"{deviceCount} device(s)";
        }
        else
        {
            GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtGoveeStatus.Text = "Scan to find devices";
        }
    }

    private void RefreshGoveeCloudStatus()
    {
        if (_config == null) return;
        bool enabled = ChkGoveeCloudEnabled.IsChecked == true;
        bool hasKey = !string.IsNullOrEmpty(TxtGoveeApiKey.Password);

        if (!enabled)
        {
            GoveeCloudStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtGoveeCloudStatus.Text = "Disabled";
        }
        else if (hasKey)
        {
            GoveeCloudStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtGoveeCloudStatus.Text = "Connected";
        }
        else
        {
            GoveeCloudStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtGoveeCloudStatus.Text = "No API key";
        }

        TxtGoveeApiStatus.Text = hasKey ? "✓ API key configured" : "";
        TxtGoveeApiStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
    }

    private void RefreshGoveeDeviceList()
    {
        GoveeDeviceList.Children.Clear();
        if (_config == null) return;

        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            // Resolve a friendly name: use Name if it's not just the IP, otherwise look up SKU
            string friendlyName = dev.Name;
            bool nameIsIp = friendlyName == dev.Ip || System.Net.IPAddress.TryParse(friendlyName, out _);
            if (string.IsNullOrWhiteSpace(friendlyName) || nameIsIp)
                friendlyName = !string.IsNullOrEmpty(dev.Sku) ? AmbienceSync.GetProductName(dev.Sku) : "";

            string display = !string.IsNullOrWhiteSpace(friendlyName)
                ? $"{friendlyName}  —  {dev.Ip}"
                : dev.Ip;

            var row = new TextBlock
            {
                Text = display,
                Style = FindResource("SecondaryText") as Style,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = dev.Ip,
            };
            GoveeDeviceList.Children.Add(row);
        }
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        BtnCheckUpdate.Content = "Checking...";
        TxtUpdateStatus.Visibility = Visibility.Visible;
        TxtUpdateStatus.Text = "Checking for updates...";
        TxtUpdateStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("TextSecBrush");

        try
        {
            var update = await UpdateChecker.CheckForUpdateAsync();
            if (update == null)
            {
                TxtUpdateStatus.Text = "You're on the latest version.";
                TxtUpdateStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("SuccessGrnBrush");
            }
            else
            {
                var (tag, url) = update.Value;
                TxtUpdateStatus.Text = $"New version available: {tag}";
                TxtUpdateStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentBrush");

                if (GlassDialog.Confirm($"A new version ({tag}) is available. Download and install?",
                    "UPDATE", owner: Window.GetWindow(this)))
                {
                    TxtUpdateStatus.Text = "Downloading update...";
                    await UpdateChecker.DownloadAndInstallAsync(url, progress =>
                    {
                        Dispatcher.Invoke(() => TxtUpdateStatus.Text = $"Downloading... {progress}%");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check error: {ex.Message}");
            TxtUpdateStatus.Text = "Update check failed. Check your internet connection.";
            TxtUpdateStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("DangerRedBrush");
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
            BtnCheckUpdate.Content = "Check for Updates";
        }
    }

}
