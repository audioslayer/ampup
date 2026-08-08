using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using AmpUp.Core.Services;
using AmpUp.Services;

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
    private SignalRgbBridgeService? _signalRgbBridgeRef;
    public Action? OnNavigateToOverview { get; set; }
    public Action<string>? OnEditProfile { get; set; }
    public Action<DeviceSurface>? OnActiveSurfaceChangedExternal { get; set; }
    public Action? OnHardwareModeChangedExternal { get; set; }
    private readonly DispatcherTimer _debounceTimer;
    private bool _loading;
    private bool _loadingSignalRgbProfileMapping;
    private bool _configLoaded;
    private bool _turnUpConnected;
    private bool _streamControllerConnected;
    private readonly List<Border> _settingsTabs = new();
    private int _settingsTabIndex;

    public SettingsView()
    {
        InitializeComponent();
        BuildSettingsTabs();
        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(RefreshSettingsTabColors);

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
        SegHardwareMode.AddSegment("Default", HardwareMode.Auto);
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
            int idx = Math.Clamp((int)Math.Round(SldN3IdleSleep.Value), 0, N3IdleStops.Length - 1);
            int secs = N3IdleStops[idx];
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
        BtnBackupSettings.Click += OnBackupSettings;
        BtnRestoreSettings.Click += OnRestoreSettings;

        // Govee
        ChkGoveeEnabled.Checked += OnGoveeEnabledChanged;
        ChkGoveeEnabled.Unchecked += OnGoveeEnabledChanged;
        BtnGoveeScan.Click += OnGoveeScan;
        BtnGoveeRestoreRemoved.Click += OnGoveeRestoreRemoved;
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

        // SignalRGB
        ChkSignalRgbEnabled.Checked += OnSignalRgbEnabledChanged;
        ChkSignalRgbEnabled.Unchecked += OnSignalRgbEnabledChanged;
        TxtSignalRgbPort.TextChanged += OnValueChanged;
        CmbSignalRgbCanvasShape.SelectionChanged += OnValueChanged;
        ChkSignalRgbProfileSync.Checked += OnValueChanged;
        ChkSignalRgbProfileSync.Unchecked += OnValueChanged;
        CmbSignalRgbProfile.SelectionChanged += OnSignalRgbProfileSelectionChanged;
        TxtSignalRgbProfileEffect.TextChanged += OnSignalRgbProfileMappingChanged;
        TxtSignalRgbProfileLayout.TextChanged += OnSignalRgbProfileMappingChanged;
        BtnSignalRgbApplyProfileSync.Click += OnSignalRgbApplyProfileSync;
        foreach (var checkbox in SignalRgbIgnoreKnobChecks())
        {
            checkbox.Checked += OnValueChanged;
            checkbox.Unchecked += OnValueChanged;
        }
        BtnSignalRgbInstallPlugin.Click += OnSignalRgbInstallPlugin;
        BtnSignalRgbOpenPluginFolder.Click += OnSignalRgbOpenPluginFolder;

        // Spotify
        BtnDiscordConnect.Click += OnDiscordConnect;
        BtnDiscordDisconnect.Click += OnDiscordDisconnect;
        TxtSpotifyClientId.TextChanged += OnValueChanged;
        BtnSpotifySetupGuide.Click += OnSpotifySetupGuide;
        BtnSpotifyConnect.Click += OnSpotifyConnect;
        BtnSpotifyDisconnect.Click += OnSpotifyDisconnect;

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

    private void BuildSettingsTabs()
    {
        string[] tabNames = { "HARDWARE", "APP", "PROFILES", "LIGHTING", "SERVICES", "ABOUT" };
        SettingsTabRow.Children.Clear();
        _settingsTabs.Clear();

        for (int i = 0; i < tabNames.Length; i++)
        {
            int index = i;
            var tab = new Border
            {
                Padding = new Thickness(18, 10, 18, 10),
                Cursor = Cursors.Hand,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = tabNames[i],
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };

            tab.MouseEnter += (_, _) =>
            {
                if (index != _settingsTabIndex && tab.Child is TextBlock label)
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            };
            tab.MouseLeave += (_, _) =>
            {
                if (index != _settingsTabIndex && tab.Child is TextBlock label)
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
            };
            tab.MouseLeftButtonDown += (_, _) => SelectSettingsTab(index);

            _settingsTabs.Add(tab);
            SettingsTabRow.Children.Add(tab);
        }

        SelectSettingsTab(_settingsTabIndex, scrollToTop: false);
    }

    private void SelectSettingsTab(int index, bool scrollToTop = true)
    {
        _settingsTabIndex = Math.Clamp(index, 0, _settingsTabs.Count - 1);
        RefreshSettingsTabColors();

        ConnectionCard.Visibility = _settingsTabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        GeneralCard.Visibility = _settingsTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppearanceCard.Visibility = _settingsTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ProfilesCard.Visibility = _settingsTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        BackupCard.Visibility = _settingsTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        IntegrationsCard.Visibility = _settingsTabIndex is 3 or 4 ? Visibility.Visible : Visibility.Collapsed;
        AboutCard.Visibility = _settingsTabIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

        bool showLighting = _settingsTabIndex == 3;
        bool showServices = _settingsTabIndex == 4;
        IntegrationHeaderText.Text = showLighting ? "LIGHTING INTEGRATIONS" : "SERVICE INTEGRATIONS";

        GoveeIntegration.Visibility = showLighting ? Visibility.Visible : Visibility.Collapsed;
        CorsairIntegration.Visibility = showLighting ? Visibility.Visible : Visibility.Collapsed;
        SignalRgbIntegration.Visibility = showLighting ? Visibility.Visible : Visibility.Collapsed;
        GoveeIntegration.BorderThickness = new Thickness(0);

        HomeAssistantIntegration.Visibility = showServices ? Visibility.Visible : Visibility.Collapsed;
        ObsIntegration.Visibility = showServices ? Visibility.Visible : Visibility.Collapsed;
        VoiceMeeterIntegration.Visibility = showServices ? Visibility.Visible : Visibility.Collapsed;
        DiscordIntegration.Visibility = showServices ? Visibility.Visible : Visibility.Collapsed;
        SpotifyIntegration.Visibility = showServices ? Visibility.Visible : Visibility.Collapsed;

        if (scrollToTop)
            SettingsScrollViewer.ScrollToTop();
    }

    private void RefreshSettingsTabColors()
    {
        for (int i = 0; i < _settingsTabs.Count; i++)
        {
            bool active = i == _settingsTabIndex;
            Border tab = _settingsTabs[i];
            tab.BorderBrush = active ? new SolidColorBrush(ThemeManager.Accent) : Brushes.Transparent;
            if (tab.Child is not TextBlock label) continue;

            label.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            if (active)
                label.Foreground = new SolidColorBrush(ThemeManager.Accent);
            else
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        }
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
        {
            int idx = NearestN3IdleStopIndex(config.N3.IdleSleepSeconds);
            SldN3IdleSleep.Value = idx;
            TxtN3IdleSleepLabel.Text = $"Stream Controller Screen Sleep: {FormatN3IdleDuration(N3IdleStops[idx])}";
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

        // Integrations — SignalRGB
        ChkSignalRgbEnabled.IsChecked = config.SignalRgb.Enabled;
        TxtSignalRgbPort.Text = config.SignalRgb.BridgePort.ToString();
        SelectSignalRgbCanvasShape(config.SignalRgb.CanvasShape);
        LoadSignalRgbIgnoredKnobs(config.SignalRgb.IgnoredLedIndexes);
        ChkSignalRgbProfileSync.IsChecked = config.SignalRgb.ProfileSyncEnabled;
        LoadSignalRgbProfileMappings(config);
        RefreshSignalRgbStatus();

        // Integrations — Spotify
        TxtSpotifyClientId.Text = config.Spotify.ClientId;
        RefreshDiscordStatus();
        RefreshSpotifyStatus();

        // Integrations — Govee
        ChkGoveeEnabled.IsChecked = config.Ambience.GoveeEnabled;
        GoveeLanSection.Visibility = config.Ambience.GoveeEnabled ? Visibility.Visible : Visibility.Collapsed;
        ChkGoveeCloudEnabled.IsChecked = config.Ambience.GoveeCloudEnabled;
        GoveeCloudSection.Visibility = config.Ambience.GoveeCloudEnabled ? Visibility.Visible : Visibility.Collapsed;
        TxtGoveeApiKey.Password = config.Ambience.GoveeApiKey;
        RefreshGoveeStatus();
        RefreshGoveeCloudStatus();
        RefreshGoveeDeviceList();
        RefreshGoveeHiddenDevicesUi();
        RefreshGoveeAmbienceHint();

        BuildAccentSwatches();
        BuildCardThemeSwatches();

        _loading = false;
        _configLoaded = true;
    }

    public void SetAmbienceSync(AmbienceSync sync) => _ambienceSync = sync;

    public void SetSignalRgbBridge(SignalRgbBridgeService bridge)
    {
        _signalRgbBridgeRef = bridge;
        bridge.StatusChanged += _ => Dispatcher.BeginInvoke(RefreshSignalRgbStatus);
        RefreshSignalRgbStatus();
    }

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
        loaded.SignalRgb = _config.SignalRgb;
        loaded.DiscordRpc = _config.DiscordRpc;
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
        bool show = _config.HardwareMode == HardwareMode.DualMode;
        ActiveSurfacePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Discrete time stops for the idle-sleep slider. The slider's value is
    /// the index into this array — each stop gets equal slider real estate
    /// so short durations aren't cramped against 0 like a linear 0..3600 range.
    /// </summary>
    private static readonly int[] N3IdleStops = { 0, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };

    private static int NearestN3IdleStopIndex(int seconds)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        for (int i = 0; i < N3IdleStops.Length; i++)
        {
            int d = Math.Abs(seconds - N3IdleStops[i]);
            if (d < bestDist) { bestDist = d; best = i; }
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
        _config.Spotify.ClientId = TxtSpotifyClientId.Text.Trim();

        // VoiceMeeter
        _config.VoiceMeeter.Enabled = ChkVmEnabled.IsChecked == true;

        // Corsair iCUE
        _config.Corsair.Enabled = ChkCorsairEnabled.IsChecked == true;

        // SignalRGB
        _config.SignalRgb.Enabled = ChkSignalRgbEnabled.IsChecked == true;
        if (int.TryParse(TxtSignalRgbPort.Text.Trim(), out var signalRgbPort))
            _config.SignalRgb.BridgePort = Math.Clamp(signalRgbPort, 1024, 65535);
        _config.SignalRgb.CanvasShape = GetSelectedSignalRgbCanvasShape();
        _config.SignalRgb.IgnoredLedIndexes = GetSignalRgbIgnoredLedIndexes();
        _config.SignalRgb.ProfileSyncEnabled = ChkSignalRgbProfileSync.IsChecked == true;
        SaveCurrentSignalRgbProfileMapping();

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

            if (!ConfigManager.IsProfileNameAvailable(_config.Profiles, name))
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
        if (string.Equals(profileName, "Default", StringComparison.OrdinalIgnoreCase))
        {
            GlassDialog.ShowWarning("Cannot delete the Default profile.", owner: Window.GetWindow(this));
            return;
        }

        if (!GlassDialog.Confirm($"Delete profile \"{profileName}\"? This cannot be undone.",
            "DELETE PROFILE", dangerYes: true, owner: Window.GetWindow(this)))
            return;

        var remainingProfiles = _config.Profiles.Where(name =>
            !string.Equals(name, profileName, StringComparison.Ordinal)).ToList();
        try
        {
            ConfigManager.DeleteProfileFiles(profileName, remainingProfiles);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to delete profile {profileName}: {ex.Message}");
            GlassDialog.ShowWarning($"Could not delete profile: {ex.Message}", owner: Window.GetWindow(this));
            return;
        }

        _config.Profiles.Remove(profileName);
        _config.ProfileIcons.Remove(profileName);

        var loadedDefault = ConfigManager.LoadProfile("Default");
        if (loadedDefault != null)
        {
            loadedDefault.ActiveProfile = "Default";
            PreserveGlobalSettings(loadedDefault);
            _config = loadedDefault;
        }
        else
        {
            _config.ActiveProfile = "Default";
        }

        CollectAndSave();
        LoadConfig(_config, _onSave!);

        // Refresh dropdowns
        RefreshProfileDropdown();
        (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();
    }

    private void OnImportTurnUp(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var wizard = new ImportWizardWindow
        {
            Owner = Window.GetWindow(this),
            ExistingProfileNames = _config.Profiles.ToList()
        };
        wizard.ShowDialog();

        if (wizard.ImportedProfileName != null)
        {
            var profileName = wizard.ImportedProfileName;

            // Add to profile list if not already there
            if (ConfigManager.IsProfileNameAvailable(_config.Profiles, profileName))
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
            var imported = ConfigManager.DeserializeAndNormalize(json);

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
            var finalName = ConfigManager.GetUniqueProfileName(_config.Profiles, profileName);

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

    private void OnBackupSettings(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        CollectAndSave();
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        var dlg = new SaveFileDialog
        {
            Title = "Backup Amp Up Settings",
            FileName = $"AmpUp-Backup-{timestamp}.ampupbackup",
            Filter = "Amp Up backup (*.ampupbackup)|*.ampupbackup|Zip archive (*.zip)|*.zip|All files (*.*)|*.*",
            DefaultExt = "ampupbackup"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            ConfigManager.CreateBackup(dlg.FileName, _config);
            GlassDialog.ShowInfo("Amp Up settings backup saved.", owner: Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            GlassDialog.ShowWarning($"Backup failed: {ex.Message}", owner: Window.GetWindow(this));
        }
    }

    private void OnRestoreSettings(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Restore Amp Up Settings",
            Filter = "Amp Up backup (*.ampupbackup;*.zip)|*.ampupbackup;*.zip|All files (*.*)|*.*",
            DefaultExt = "ampupbackup"
        };

        if (dlg.ShowDialog() != true) return;

        if (!GlassDialog.Confirm(
            "Restore this backup? Current app settings and saved profiles will be replaced.",
            "RESTORE SETTINGS",
            dangerYes: true,
            owner: Window.GetWindow(this)))
            return;

        try
        {
            CollectAndSave();
            var preRestorePath = Path.Combine(
                ConfigManager.AppDataDir,
                $"AmpUp-Before-Restore-{DateTime.Now:yyyyMMdd-HHmmss}.ampupbackup");
            ConfigManager.CreateBackup(preRestorePath, _config);

            var restored = ConfigManager.RestoreBackup(dlg.FileName);
            _config = restored;
            _onSave?.Invoke(_config);
            LoadConfig(_config, _onSave!);
            (Window.GetWindow(this) as MainWindow)?.RefreshProfilePicker();
            (Window.GetWindow(this) as MainWindow)?.RefreshViews(_config);

            GlassDialog.ShowInfo(
                $"Amp Up settings restored.\n\nA safety backup of the previous settings was saved to:\n{preRestorePath}",
                owner: Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            GlassDialog.ShowWarning($"Restore failed: {ex.Message}", owner: Window.GetWindow(this));
        }
    }

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

    // ── Spotify ────────────────────────────────────────────────────────

    private void OnSignalRgbEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        RefreshSignalRgbStatus();
        CollectAndSave();
    }

    private void RefreshSignalRgbStatus()
    {
        bool enabled = ChkSignalRgbEnabled.IsChecked == true;
        bool pluginInstalled = File.Exists(SignalRgbBridgeService.UserPluginPath);

        if (!enabled)
        {
            SignalRgbStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtSignalRgbStatus.Text = "Disabled";
        }
        else if (_signalRgbBridgeRef?.HasActiveFrame == true)
        {
            SignalRgbStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            TxtSignalRgbStatus.Text = "Receiving";
        }
        else if (_signalRgbBridgeRef?.IsRunning == true)
        {
            SignalRgbStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB800"));
            TxtSignalRgbStatus.Text = "Listening";
        }
        else
        {
            SignalRgbStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtSignalRgbStatus.Text = "Not listening";
        }

        TxtSignalRgbPluginStatus.Text = pluginInstalled
            ? "Plugin installed"
            : "Plugin not installed";
    }

    private void LoadSignalRgbProfileMappings(AppConfig config)
    {
        _loadingSignalRgbProfileMapping = true;

        CmbSignalRgbProfile.ClearItems();
        int activeIdx = -1;
        for (int i = 0; i < config.Profiles.Count; i++)
        {
            string profile = config.Profiles[i];
            CmbSignalRgbProfile.AddItem(profile, profile);
            if (string.Equals(profile, config.ActiveProfile, StringComparison.OrdinalIgnoreCase))
                activeIdx = i;
        }

        CmbSignalRgbProfile.SelectedIndex = activeIdx >= 0 ? activeIdx : 0;
        LoadSelectedSignalRgbProfileMapping();
        RefreshSignalRgbProfileSyncHint();

        _loadingSignalRgbProfileMapping = false;
    }

    private void OnSignalRgbProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_config == null || _loading) return;

        _loadingSignalRgbProfileMapping = true;
        LoadSelectedSignalRgbProfileMapping();
        RefreshSignalRgbProfileSyncHint();
        _loadingSignalRgbProfileMapping = false;
    }

    private void LoadSelectedSignalRgbProfileMapping()
    {
        if (_config == null) return;

        string profileName = CmbSignalRgbProfile.SelectedTag as string ?? CmbSignalRgbProfile.SelectedDisplay;
        if (string.IsNullOrWhiteSpace(profileName))
        {
            TxtSignalRgbProfileEffect.Text = "";
            TxtSignalRgbProfileLayout.Text = "";
            return;
        }

        TxtSignalRgbProfileEffect.Text = _config.SignalRgb.ProfileEffects.TryGetValue(profileName, out var effect)
            ? effect
            : "";
        TxtSignalRgbProfileLayout.Text = _config.SignalRgb.ProfileLayouts.TryGetValue(profileName, out var layout)
            ? layout
            : "";
    }

    private void OnSignalRgbProfileMappingChanged(object? sender, EventArgs e)
    {
        if (_loading || _loadingSignalRgbProfileMapping) return;

        SaveCurrentSignalRgbProfileMapping();
        OnValueChanged(sender, e);
    }

    private void SaveCurrentSignalRgbProfileMapping()
    {
        if (_config == null) return;

        string profileName = CmbSignalRgbProfile.SelectedTag as string ?? CmbSignalRgbProfile.SelectedDisplay;
        if (string.IsNullOrWhiteSpace(profileName)) return;

        string effect = TxtSignalRgbProfileEffect.Text.Trim();
        string layout = TxtSignalRgbProfileLayout.Text.Trim();

        if (string.IsNullOrWhiteSpace(effect))
            _config.SignalRgb.ProfileEffects.Remove(profileName);
        else
            _config.SignalRgb.ProfileEffects[profileName] = effect;

        if (string.IsNullOrWhiteSpace(layout))
            _config.SignalRgb.ProfileLayouts.Remove(profileName);
        else
            _config.SignalRgb.ProfileLayouts[profileName] = layout;
    }

    private void OnSignalRgbApplyProfileSync(object sender, RoutedEventArgs e)
    {
        SaveCurrentSignalRgbProfileMapping();

        string effect = TxtSignalRgbProfileEffect.Text.Trim();
        string layout = TxtSignalRgbProfileLayout.Text.Trim();

        if (!string.IsNullOrWhiteSpace(effect))
            SignalRgbEffectCatalog.ApplyEffect(effect);
        if (!string.IsNullOrWhiteSpace(layout))
            SignalRgbEffectCatalog.ApplyLayout(layout);
    }

    private void RefreshSignalRgbProfileSyncHint()
    {
        int effectCount = SignalRgbEffectCatalog.GetInstalledEffects().Count;
        int layoutCount = SignalRgbEffectCatalog.GetInstalledLayouts().Count;

        TxtSignalRgbProfileSyncHint.Text = layoutCount > 0
            ? $"{effectCount} effects, {layoutCount} layouts found"
            : $"{effectCount} effects found; enter layout names manually";
    }

    private void OnSignalRgbInstallPlugin(object sender, RoutedEventArgs e)
    {
        try
        {
            CollectAndSave();
            string path = SignalRgbBridgeService.InstallUserPlugin(_config?.SignalRgb);
            RefreshSignalRgbStatus();
            GlassDialog.ShowInfo($"SignalRGB plugin updated:\n{path}\n\nRestart SignalRGB or reload devices to pick it up.", owner: Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            GlassDialog.ShowWarning($"SignalRGB plugin install failed:\n{ex.Message}", owner: Window.GetWindow(this));
        }
    }

    private void SelectSignalRgbCanvasShape(string? canvasShape)
    {
        string target = NormalizeSignalRgbCanvasShape(canvasShape);
        foreach (var item in CmbSignalRgbCanvasShape.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                CmbSignalRgbCanvasShape.SelectedItem = item;
                return;
            }
        }

        CmbSignalRgbCanvasShape.SelectedIndex = 0;
    }

    private string GetSelectedSignalRgbCanvasShape()
    {
        if (CmbSignalRgbCanvasShape.SelectedItem is ComboBoxItem item)
            return NormalizeSignalRgbCanvasShape(item.Content?.ToString());

        return "Classic Strip";
    }

    private static string NormalizeSignalRgbCanvasShape(string? canvasShape) => canvasShape switch
    {
        "Knob Grid" => "Knob Grid",
        "Arc" => "Arc",
        "Matrix" => "Matrix",
        "Wide Strip" => "Wide Strip",
        _ => "Classic Strip",
    };

    private CheckBox[] SignalRgbIgnoreKnobChecks() =>
    [
        ChkSignalRgbIgnoreKnob1,
        ChkSignalRgbIgnoreKnob2,
        ChkSignalRgbIgnoreKnob3,
        ChkSignalRgbIgnoreKnob4,
        ChkSignalRgbIgnoreKnob5,
    ];

    private void LoadSignalRgbIgnoredKnobs(List<int>? ignoredLedIndexes)
    {
        var ignored = ignoredLedIndexes == null
            ? new HashSet<int>()
            : new HashSet<int>(ignoredLedIndexes.Where(i => i is >= 0 and < 15));

        var checks = SignalRgbIgnoreKnobChecks();
        for (int knob = 0; knob < checks.Length; knob++)
            checks[knob].IsChecked = Enumerable.Range(knob * 3, 3).All(ignored.Contains);
    }

    private List<int> GetSignalRgbIgnoredLedIndexes()
    {
        var indexes = new List<int>();
        var checks = SignalRgbIgnoreKnobChecks();
        for (int knob = 0; knob < checks.Length; knob++)
        {
            if (checks[knob].IsChecked != true) continue;

            int baseIndex = knob * 3;
            indexes.Add(baseIndex);
            indexes.Add(baseIndex + 1);
            indexes.Add(baseIndex + 2);
        }

        return indexes;
    }

    private void OnSignalRgbOpenPluginFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SignalRgbBridgeService.UserPluginDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SignalRgbBridgeService.UserPluginDirectory)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            GlassDialog.ShowWarning($"Could not open plugin folder:\n{ex.Message}", owner: Window.GetWindow(this));
        }
    }

    private async void OnDiscordConnect(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        if (!DiscordRpcIntegration.VoiceControlAvailable)
        {
            GlassDialog.ShowInfo(DiscordRpcIntegration.VoiceControlUnavailableReason,
                owner: Window.GetWindow(this));
            return;
        }

        var discord = App.DiscordRpc;
        if (discord == null)
        {
            GlassDialog.ShowInfo("Discord service is not available.", owner: Window.GetWindow(this));
            return;
        }

        TxtDiscordStatus.Text = "Waiting for Discord...";
        BtnDiscordConnect.IsEnabled = false;
        try
        {
            await discord.ConnectAsync();
            RefreshDiscordStatus();
            GlassDialog.ShowInfo("Discord connected. Discord button actions are ready.", owner: Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            TxtDiscordStatus.Text = "Error";
            GlassDialog.ShowWarning($"Discord connect failed:\n{ex.Message}", owner: Window.GetWindow(this));
        }
        finally
        {
            BtnDiscordConnect.IsEnabled = true;
            RefreshDiscordStatus();
        }
    }

    private void OnDiscordDisconnect(object sender, RoutedEventArgs e)
    {
        App.DiscordRpc?.Disconnect();
        RefreshDiscordStatus();
    }

    private void RefreshDiscordStatus()
    {
        if (_config == null) return;

        if (!DiscordRpcIntegration.VoiceControlAvailable)
        {
            DiscordStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtDiscordStatus.Text = "Unavailable";
            BtnDiscordConnect.Content = "Unavailable";
            BtnDiscordConnect.IsEnabled = false;
            // Allow users to clear a token saved by an older AmpUp build even
            // though starting a new partner-only OAuth flow is disabled.
            BtnDiscordDisconnect.IsEnabled = !string.IsNullOrWhiteSpace(_config.DiscordRpc.AccessToken)
                                               || !string.IsNullOrWhiteSpace(_config.DiscordRpc.RefreshToken);
            return;
        }

        bool connected = !string.IsNullOrWhiteSpace(_config.DiscordRpc.AccessToken);
        if (connected)
        {
            DiscordStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5865F2"));
            TxtDiscordStatus.Text = string.IsNullOrEmpty(_config.DiscordRpc.ConnectedUser)
                ? "Connected"
                : $"Connected as {_config.DiscordRpc.ConnectedUser}";
            BtnDiscordConnect.Content = "Reconnect";
            BtnDiscordDisconnect.IsEnabled = true;
        }
        else
        {
            DiscordStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtDiscordStatus.Text = "Not connected";
            BtnDiscordConnect.Content = "Connect";
            BtnDiscordDisconnect.IsEnabled = false;
        }
    }

    private async void OnSpotifySetupGuide(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        var guide = new AmpUp.Controls.SpotifySetupGuide
        {
            Owner = Window.GetWindow(this),
        };
        var ok = guide.ShowDialog();
        if (ok != true || !guide.WasSuccessful) return;

        // Push the pasted Client ID into the field + config, then kick
        // straight into Connect so the user doesn't have to press another
        // button. Most of the time the guide is the Connect flow.
        TxtSpotifyClientId.Text = guide.ClientId;
        _config.Spotify.ClientId = guide.ClientId;
        _onSave?.Invoke(_config);
        OnSpotifyConnect(this, e);
    }

    private async void OnSpotifyConnect(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        _config.Spotify.ClientId = TxtSpotifyClientId.Text.Trim();
        _onSave?.Invoke(_config);

        var sp = App.Spotify;
        if (sp == null)
        {
            GlassDialog.ShowInfo("Spotify service is not available.", owner: Window.GetWindow(this));
            return;
        }

        TxtSpotifyStatus.Text = "Opening browser...";
        BtnSpotifyConnect.IsEnabled = false;
        try
        {
            await sp.ConnectAsync();
            RefreshSpotifyStatus();
            GlassDialog.ShowInfo($"Connected to Spotify as {_config.Spotify.ConnectedUser}.", owner: Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            TxtSpotifyStatus.Text = "Error";
            GlassDialog.ShowWarning($"Spotify connect failed:\n{ex.Message}", owner: Window.GetWindow(this));
        }
        finally
        {
            BtnSpotifyConnect.IsEnabled = true;
            RefreshSpotifyStatus();
        }
    }

    private void OnSpotifyDisconnect(object sender, RoutedEventArgs e)
    {
        var sp = App.Spotify;
        sp?.Disconnect();
        RefreshSpotifyStatus();
    }

    private void RefreshSpotifyStatus()
    {
        if (_config == null) return;
        bool connected = !string.IsNullOrWhiteSpace(_config.Spotify.RefreshToken);
        if (connected)
        {
            SpotifyStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1DB954"));
            TxtSpotifyStatus.Text = string.IsNullOrEmpty(_config.Spotify.ConnectedUser)
                ? "Connected"
                : $"Connected as {_config.Spotify.ConnectedUser}";
            BtnSpotifyConnect.Content = "Reconnect";
            BtnSpotifyDisconnect.IsEnabled = true;
        }
        else
        {
            SpotifyStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            TxtSpotifyStatus.Text = "Not connected";
            BtnSpotifyConnect.Content = "Connect";
            BtnSpotifyDisconnect.IsEnabled = false;
        }
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
            int hiddenCloudCount = 0;

            // Drop anything the user has explicitly removed — otherwise deleted
            // devices (e.g. a "Test" one still registered in the Govee account)
            // silently reappear on every rescan.
            var hidden = _config.Ambience.HiddenGoveeDeviceIds ??= new List<string>();
            var hiddenKeys = new HashSet<string>(
                hidden.Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);

            bool IsHidden(GoveeDeviceConfig d) =>
                (!string.IsNullOrEmpty(d.Ip) && hiddenKeys.Contains(d.Ip)) ||
                (!string.IsNullOrEmpty(d.DeviceId) && hiddenKeys.Contains(d.DeviceId));

            int hiddenLanCount = found.Count(IsHidden);
            found.RemoveAll(d => IsHidden(d));

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
                        if (hiddenKeys.Contains(cloud.Device))
                        {
                            hiddenCloudCount++;
                            continue;
                        }
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
            int hiddenSkippedCount = hiddenLanCount + hiddenCloudCount;

            if (found.Count == 0 || (!lanScanWorked && hadExistingLan))
            {
                // LAN scan failed or found nothing — keep existing devices, don't wipe IPs
                TxtGoveeScanStatus.Text = hiddenSkippedCount > 0 && found.Count == 0
                    ? "Removed device(s) hidden - use Restore Removed to scan them again"
                    : found.Count == 0
                        ? "No devices found"
                        : "LAN scan failed - keeping existing devices";
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
                    if (prev != null)
                    {
                        if (prev.SyncMode != "off")
                            dev.SyncMode = prev.SyncMode;
                        dev.SyncWithAmpUp = prev.SyncWithAmpUp;
                    }
                }

                _config.Ambience.GoveeDevices = found;
                TxtGoveeScanStatus.Text = $"{found.Count} device(s) found";
                RefreshGoveeDeviceList();
                RefreshGoveeStatus();
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            RefreshGoveeHiddenDevicesUi();
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee scan error: {ex.Message}");
            TxtGoveeScanStatus.Text = "Scan failed";
            GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4444"));
        }

        BtnGoveeScan.IsEnabled = true;
    }

    private void OnGoveeRestoreRemoved(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        _config.Ambience.HiddenGoveeDeviceIds.Clear();
        TxtGoveeScanStatus.Text = "Removed devices can appear on the next scan";
        RefreshGoveeHiddenDevicesUi();

        _debounceTimer.Stop();
        _debounceTimer.Start();
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
        if (_config == null)
        {
            BtnGoveeRestoreRemoved.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            var devRef = dev; // captured for the remove closure

            string friendlyName = dev.Name;
            bool nameIsIp = friendlyName == dev.Ip || System.Net.IPAddress.TryParse(friendlyName, out _);
            if (string.IsNullOrWhiteSpace(friendlyName) || nameIsIp)
                friendlyName = !string.IsNullOrEmpty(dev.Sku) ? AmbienceSync.GetProductName(dev.Sku) : "";

            bool hasLan = !string.IsNullOrWhiteSpace(dev.Ip);
            string display = !string.IsNullOrWhiteSpace(friendlyName)
                ? (hasLan ? $"{friendlyName}  \u2014  {dev.Ip}" : friendlyName)
                : (hasLan ? dev.Ip : dev.DeviceId);

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = display,
                Style = FindResource("SecondaryText") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = hasLan ? dev.Ip : dev.DeviceId,
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            if (!hasLan)
            {
                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, 0x42, 0xA5, 0xF5)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 1, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "API",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                    },
                };
                Grid.SetColumn(badge, 1);
                row.Children.Add(badge);
            }

            var remove = new System.Windows.Controls.Button
            {
                Content = "\u2715",
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                FontSize = 10,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.SolidColorBrush)FindResource("TextDimBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Remove this device (won't reappear on rescan)",
            };
            remove.Click += (_, _) =>
            {
                if (_config == null) return;
                if (!string.IsNullOrWhiteSpace(devRef.DeviceId)
                    && !_config.Ambience.HiddenGoveeDeviceIds.Contains(devRef.DeviceId))
                    _config.Ambience.HiddenGoveeDeviceIds.Add(devRef.DeviceId);
                if (!string.IsNullOrWhiteSpace(devRef.Ip)
                    && !_config.Ambience.HiddenGoveeDeviceIds.Contains(devRef.Ip))
                    _config.Ambience.HiddenGoveeDeviceIds.Add(devRef.Ip);
                _config.Ambience.GoveeDevices.Remove(devRef);
                TxtGoveeScanStatus.Text = "Device removed - use Restore Removed to scan it again";
                RefreshGoveeDeviceList();
                _debounceTimer.Stop();
                _debounceTimer.Start();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);

            GoveeDeviceList.Children.Add(row);
        }

        RefreshGoveeHiddenDevicesUi();
    }

    private void RefreshGoveeHiddenDevicesUi()
    {
        if (_config == null)
        {
            BtnGoveeRestoreRemoved.Visibility = Visibility.Collapsed;
            return;
        }

        bool hasHidden = _config.Ambience.HiddenGoveeDeviceIds
            .Any(key => !string.IsNullOrWhiteSpace(key));
        BtnGoveeRestoreRemoved.Visibility = hasHidden ? Visibility.Visible : Visibility.Collapsed;
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
                TxtUpdateStatus.Text = $"New version available: {update.Tag}";
                TxtUpdateStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("AccentBrush");

                if (GlassDialog.Confirm(
                    $"Amp Up {update.Tag} is available. Download it, install it, and restart Amp Up now?",
                    "UPDATE", owner: Window.GetWindow(this)))
                {
                    TxtUpdateStatus.Text = "Downloading update...";
                    await UpdateChecker.DownloadAndInstallAsync(update, progress =>
                    {
                        Dispatcher.Invoke(() => TxtUpdateStatus.Text = $"Downloading... {progress}%");
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check error: {ex.Message}");
            TxtUpdateStatus.Text = $"Update failed: {ex.Message}";
            TxtUpdateStatus.Foreground = (System.Windows.Media.SolidColorBrush)FindResource("DangerRedBrush");
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
            BtnCheckUpdate.Content = "Check for Updates";
        }
    }

}
