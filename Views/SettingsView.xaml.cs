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
        ("Green",   "#00E676"),
        ("Cyan",    "#00B4D8"),
        ("Blue",    "#448AFF"),
        ("Purple",  "#B388FF"),
        ("Pink",    "#FF4081"),
        ("Red",     "#FF5252"),
        ("Orange",  "#FF6E40"),
        ("Gold",    "#FFD740"),
        ("Mint",    "#69F0AE"),
        ("White",   "#E0E0E0"),
    };

    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    public Action? OnNavigateToOverview { get; set; }
    public Action<string>? OnEditProfile { get; set; }
    private readonly DispatcherTimer _debounceTimer;
    private bool _loading;

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

        // Clear LED preview when navigating away from Settings
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false && _calibPreviewColor != null)
                ClearCalibPreview();
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

        // LED Calibration
        SldGammaR.ValueChanged += OnGammaChanged;
        SldGammaG.ValueChanged += OnGammaChanged;
        SldGammaB.ValueChanged += OnGammaChanged;
        BtnGammaReset.Click += OnGammaReset;
        CalibTestRed.MouseLeftButtonDown += (_, _) => SetCalibPreview(255, 0, 0, CalibTestRed);
        CalibTestGreen.MouseLeftButtonDown += (_, _) => SetCalibPreview(0, 255, 0, CalibTestGreen);
        CalibTestBlue.MouseLeftButtonDown += (_, _) => SetCalibPreview(0, 0, 255, CalibTestBlue);
        CalibTestPurple.MouseLeftButtonDown += (_, _) => SetCalibPreview(136, 0, 255, CalibTestPurple);
        CalibTestWhite.MouseLeftButtonDown += (_, _) => SetCalibPreview(255, 255, 255, CalibTestWhite);
        CalibTestOff.MouseLeftButtonDown += (_, _) => ClearCalibPreview();

        // About
        TxtVersion.Text = $"Amp Up v{UpdateChecker.CurrentVersion}";
        BtnCheckUpdate.Click += OnCheckUpdate;
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
        RefreshPortList(selectPort: config.Serial.Port);
        ChkStartWithWindows.IsChecked = config.StartWithWindows;
        ChkAutoSuggestLayout.IsChecked = config.AutoSuggestLayout;

        // Profiles
        CmbProfiles.Items.Clear();
        foreach (var profile in config.Profiles)
            CmbProfiles.Items.Add(profile);
        CmbProfiles.SelectedItem = config.ActiveProfile;

        // Integrations — Home Assistant
        ChkHaEnabled.IsChecked = config.HomeAssistant.Enabled;
        TxtHaUrl.Text = config.HomeAssistant.Url;
        TxtHaToken.Password = config.HomeAssistant.Token;
        RefreshHaHeaderStatus();

        // Auto-test HA connection if enabled
        if (config.HomeAssistant.Enabled && !string.IsNullOrWhiteSpace(config.HomeAssistant.Token))
            _ = AutoTestHaAsync();

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

        // LED Calibration
        SldGammaR.Value = config.GammaR;
        SldGammaG.Value = config.GammaG;
        SldGammaB.Value = config.GammaB;
        LblGammaR.Text = config.GammaR.ToString("F1");
        LblGammaG.Text = config.GammaG.ToString("F1");
        LblGammaB.Text = config.GammaB.ToString("F1");

        _loading = false;
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
    }

    private void OnValueChanged(object sender, EventArgs e)
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
        loaded.Profiles = _config.Profiles;
        loaded.ProfileIcons = _config.ProfileIcons;
        loaded.Ducking = _config.Ducking;
        loaded.AutoSwitch = _config.AutoSwitch;
        loaded.Ambience = _config.Ambience;
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _config == null || CmbProfiles.SelectedItem == null) return;

        var selected = CmbProfiles.SelectedItem.ToString()!;
        if (selected == _config.ActiveProfile) return;

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
        CmbProfiles.Items.Clear();
        foreach (var p in _config!.Profiles)
            CmbProfiles.Items.Add(p);
        CmbProfiles.SelectedItem = _config.ActiveProfile;
        _loading = false;
    }

    // ── Port selector helpers ──────────────────────────────────────────

    private void RefreshPortList(string? selectPort = null)
    {
        _loading = true;
        var ports = SerialPort.GetPortNames();
        Array.Sort(ports, StringComparer.OrdinalIgnoreCase);

        CmbSerialPort.Items.Clear();
        foreach (var port in ports)
            CmbSerialPort.Items.Add(port);

        // Select the configured port if present
        var target = selectPort ?? TxtSerialPort.Text.Trim();
        if (!string.IsNullOrEmpty(target) && CmbSerialPort.Items.Contains(target))
            CmbSerialPort.SelectedItem = target;
        else if (CmbSerialPort.Items.Count > 0)
            CmbSerialPort.SelectedIndex = 0;

        _loading = false;
    }

    private void OnPortComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbSerialPort.SelectedItem is string port)
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
            GlassDialog.ShowInfo($"Turn Up found on {found}.", owner: Window.GetWindow(this));
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
                "Could not identify the Turn Up device automatically.\nSelect the correct COM port from the dropdown.",
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
            ConnectionDot.Fill = new SolidColorBrush(connected
                ? (Color)ColorConverter.ConvertFromString("#00E676")
                : (Color)ColorConverter.ConvertFromString("#FF4444"));
            TxtConnectionStatus.Text = connected
                ? $"Connected on {portName ?? "unknown"}"
                : "Disconnected";
        });
    }

    // ── Config collect/save ────────────────────────────────────────────

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null) return;

        _config.Serial.Port = TxtSerialPort.Text.Trim();
        if (int.TryParse(TxtBaudRate.Text.Trim(), out var baud))
            _config.Serial.Baud = baud;

        _config.StartWithWindows = ChkStartWithWindows.IsChecked == true;
        _config.AutoSuggestLayout = ChkAutoSuggestLayout.IsChecked == true;

        // Integrations
        _config.HomeAssistant.Enabled = ChkHaEnabled.IsChecked == true;
        _config.HomeAssistant.Url = TxtHaUrl.Text.Trim();
        _config.HomeAssistant.Token = TxtHaToken.Password;

        // Govee
        _config.Ambience.GoveeEnabled = ChkGoveeEnabled.IsChecked == true;
        _config.Ambience.GoveeCloudEnabled = ChkGoveeCloudEnabled.IsChecked == true;
        _config.Ambience.GoveeApiKey = TxtGoveeApiKey.Password;

        // LED Calibration
        _config.GammaR = Math.Round(SldGammaR.Value, 1);
        _config.GammaG = Math.Round(SldGammaG.Value, 1);
        _config.GammaB = Math.Round(SldGammaB.Value, 1);

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
        if (_config == null || CmbProfiles.SelectedItem == null) return;

        var profileName = CmbProfiles.SelectedItem.ToString()!;
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
        if (_config == null || CmbProfiles.SelectedItem == null) return;

        var profileName = CmbProfiles.SelectedItem.ToString()!;
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

            if (found.Count == 0)
            {
                TxtGoveeScanStatus.Text = "No devices found";
                GoveeStatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555555"));
            }
            else
            {
                // If Cloud API is available, enrich with friendly names
                if (!string.IsNullOrEmpty(_config.Ambience.GoveeApiKey))
                {
                    try
                    {
                        using var api = new GoveeCloudApi(_config.Ambience.GoveeApiKey);
                        var cloudDevices = await api.GetDevicesAsync();
                        GoveeCloudApi.EnrichLanDevicesWithCloudNames(found, cloudDevices);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Govee cloud name enrichment failed: {ex.Message}");
                    }
                }

                // Preserve sync modes from previously saved devices
                var existing = _config.Ambience.GoveeDevices;
                foreach (var dev in found)
                {
                    var prev = existing.FirstOrDefault(e => e.Ip == dev.Ip);
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

    // ── LED Calibration ────────────────────────────────────────────────

    private Border? _activeCalibSwatch;
    private (byte R, byte G, byte B)? _calibPreviewColor;

    private void OnGammaChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        LblGammaR.Text = SldGammaR.Value.ToString("F1");
        LblGammaG.Text = SldGammaG.Value.ToString("F1");
        LblGammaB.Text = SldGammaB.Value.ToString("F1");

        // Live-update gamma on hardware if previewing a test color
        if (_calibPreviewColor != null)
        {
            var rgb = App.Rgb;
            if (rgb != null)
            {
                rgb.SetGamma(
                    Math.Round(SldGammaR.Value, 1),
                    Math.Round(SldGammaG.Value, 1),
                    Math.Round(SldGammaB.Value, 1));
            }
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnGammaReset(object sender, RoutedEventArgs e)
    {
        SldGammaR.Value = 2.0;
        SldGammaG.Value = 2.0;
        SldGammaB.Value = 2.0;
    }

    private void SetCalibPreview(byte r, byte g, byte b, Border swatch)
    {
        // Highlight active swatch
        if (_activeCalibSwatch != null)
            _activeCalibSwatch.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        _activeCalibSwatch = swatch;
        swatch.BorderBrush = new SolidColorBrush(ThemeManager.Accent);

        _calibPreviewColor = (r, g, b);

        // Send test color to all LEDs via preview override
        App.Rgb?.SetPreviewColor(r, g, b);
    }

    private void ClearCalibPreview()
    {
        if (_activeCalibSwatch != null)
            _activeCalibSwatch.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        _activeCalibSwatch = null;
        _calibPreviewColor = null;

        // Resume normal LED effects
        App.Rgb?.ClearPreviewColor();
    }
}

