using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WolfMixer.Views;

public partial class SettingsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
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

        // Wire up change events
        TxtSerialPort.TextChanged += OnValueChanged;
        TxtBaudRate.TextChanged += OnValueChanged;
        ChkStartWithWindows.Checked += OnValueChanged;
        ChkStartWithWindows.Unchecked += OnValueChanged;
        CmbProfiles.SelectionChanged += OnProfileSelectionChanged;

        // OSD events
        ChkOsdVolume.Checked += OnValueChanged;
        ChkOsdVolume.Unchecked += OnValueChanged;
        ChkOsdProfile.Checked += OnValueChanged;
        ChkOsdProfile.Unchecked += OnValueChanged;
        ChkOsdDevice.Checked += OnValueChanged;
        ChkOsdDevice.Unchecked += OnValueChanged;
        BtnOsdPreview.Click += OnOsdPreview;

        // Integration events
        ChkHaEnabled.Checked += OnValueChanged;
        ChkHaEnabled.Unchecked += OnValueChanged;
        TxtHaUrl.TextChanged += OnValueChanged;
        TxtHaToken.PasswordChanged += OnPasswordChanged;
        // Profile buttons
        BtnSaveProfile.Click += OnSaveProfile;
        BtnLoadProfile.Click += OnLoadProfile;
        BtnNewProfile.Click += OnNewProfile;
        BtnDeleteProfile.Click += OnDeleteProfile;

        // Import
        BtnImportTurnUp.Click += OnImportTurnUp;

        // About
        TxtVersion.Text = $"Amp Up v{UpdateChecker.CurrentVersion}";
        BtnCheckUpdate.Click += OnCheckUpdate;
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        TxtSerialPort.Text = config.Serial.Port;
        TxtBaudRate.Text = config.Serial.Baud.ToString();
        ChkStartWithWindows.IsChecked = config.StartWithWindows;

        // OSD
        ChkOsdVolume.IsChecked = config.Osd.ShowVolume;
        ChkOsdProfile.IsChecked = config.Osd.ShowProfileSwitch;
        ChkOsdDevice.IsChecked = config.Osd.ShowDeviceSwitch;
        HighlightOsdPosition(config.Osd.Position);

        // Profiles
        CmbProfiles.Items.Clear();
        foreach (var profile in config.Profiles)
            CmbProfiles.Items.Add(profile);
        CmbProfiles.SelectedItem = config.ActiveProfile;

        // Integrations — Home Assistant
        ChkHaEnabled.IsChecked = config.HomeAssistant.Enabled;
        TxtHaUrl.Text = config.HomeAssistant.Url;
        TxtHaToken.Password = config.HomeAssistant.Token;

        _loading = false;
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

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null) return;

        _config.Serial.Port = TxtSerialPort.Text.Trim();
        if (int.TryParse(TxtBaudRate.Text.Trim(), out var baud))
            _config.Serial.Baud = baud;

        _config.StartWithWindows = ChkStartWithWindows.IsChecked == true;

        // OSD
        _config.Osd.ShowVolume = ChkOsdVolume.IsChecked == true;
        _config.Osd.ShowProfileSwitch = ChkOsdProfile.IsChecked == true;
        _config.Osd.ShowDeviceSwitch = ChkOsdDevice.IsChecked == true;

        // Integrations
        _config.HomeAssistant.Enabled = ChkHaEnabled.IsChecked == true;
        _config.HomeAssistant.Url = TxtHaUrl.Text.Trim();
        _config.HomeAssistant.Token = TxtHaToken.Password;

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

    private void OsdPosition_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_loading || _config == null) return;
        if (sender is System.Windows.Controls.Border border && border.Tag is string posStr)
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
            System.Windows.Media.Color.FromArgb(0x30, 0x00, 0xE6, 0x76));
        var normalBg = (System.Windows.Media.SolidColorBrush)FindResource("CardBgBrush");
        var accentBorder = (System.Windows.Media.SolidColorBrush)FindResource("AccentDimBrush");
        var normalBorder = (System.Windows.Media.SolidColorBrush)FindResource("CardBorderBrush");

        var positions = new (System.Windows.Controls.Border Border, OsdPosition Pos)[]
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
            if (border.Child is System.Windows.Controls.TextBlock tb)
                tb.Foreground = isActive ? accentBrush : dimBrush;
        }
    }

    private void OnOsdPreview(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        // Show a preview OSD at the configured position
        var overlay = new OsdOverlay();
        overlay.SetPosition(_config.Osd.Position);
        overlay.ShowVolume("Preview", 75, "Speaker224");
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

