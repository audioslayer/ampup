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
            loaded.Profiles = _config.Profiles;
            _config = loaded;
            _onSave?.Invoke(_config);
            LoadConfig(_config, _onSave!);
        }
        else
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null) return;

        _config.Serial.Port = TxtSerialPort.Text.Trim();
        if (int.TryParse(TxtBaudRate.Text.Trim(), out var baud))
            _config.Serial.Baud = baud;

        _config.StartWithWindows = ChkStartWithWindows.IsChecked == true;

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

        MessageBox.Show($"Profile \"{profileName}\" saved.", "WolfMixer",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnLoadProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null || CmbProfiles.SelectedItem == null) return;

        var profileName = CmbProfiles.SelectedItem.ToString()!;
        var loaded = ConfigManager.LoadProfile(profileName);
        if (loaded != null)
        {
            loaded.ActiveProfile = profileName;
            loaded.Profiles = _config.Profiles;
            _config = loaded;
            _onSave?.Invoke(_config);
            LoadConfig(_config, _onSave!);

            MessageBox.Show($"Profile \"{profileName}\" loaded.", "WolfMixer",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Profile \"{profileName}\" not found on disk.",
                "WolfMixer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var dialog = new InputDialog("New Profile", "Enter profile name:");
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true)
        {
            var name = dialog.ResponseText.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (_config.Profiles.Contains(name))
            {
                MessageBox.Show($"Profile \"{name}\" already exists.",
                    "WolfMixer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _config.Profiles.Add(name);
            _config.ActiveProfile = name;
            CollectAndSave();
            ConfigManager.SaveProfile(_config, name);

            // Refresh dropdown
            _loading = true;
            CmbProfiles.Items.Clear();
            foreach (var p in _config.Profiles)
                CmbProfiles.Items.Add(p);
            CmbProfiles.SelectedItem = name;
            _loading = false;
        }
    }

    private void OnDeleteProfile(object sender, RoutedEventArgs e)
    {
        if (_config == null || CmbProfiles.SelectedItem == null) return;

        var profileName = CmbProfiles.SelectedItem.ToString()!;
        if (profileName == "Default")
        {
            MessageBox.Show("Cannot delete the Default profile.",
                "WolfMixer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Delete profile \"{profileName}\"? This cannot be undone.",
            "WolfMixer", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _config.Profiles.Remove(profileName);
        _config.ActiveProfile = "Default";

        // Delete profile file
        var path = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            $"profile_{profileName.ToLowerInvariant().Replace(' ', '_')}.json");
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }

        CollectAndSave();

        // Refresh dropdown
        _loading = true;
        CmbProfiles.Items.Clear();
        foreach (var p in _config.Profiles)
            CmbProfiles.Items.Add(p);
        CmbProfiles.SelectedItem = "Default";
        _loading = false;
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

                var result = MessageBox.Show(
                    $"A new version ({tag}) is available. Download and install?",
                    "Amp Up Update",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);

                if (result == System.Windows.MessageBoxResult.Yes)
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

/// <summary>
/// Simple input dialog for profile name entry.
/// </summary>
public class InputDialog : Window
{
    private readonly TextBox _textBox;
    public string ResponseText => _textBox.Text;

    public InputDialog(string title, string prompt)
    {
        Title = title;
        Width = 360;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C1C1C"));

        var stack = new StackPanel { Margin = new Thickness(16) };

        var label = new TextBlock
        {
            Text = prompt,
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8")),
            Margin = new Thickness(0, 0, 0, 8)
        };
        stack.Children.Add(label);

        _textBox = new TextBox
        {
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#242424")),
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E8E8E8")),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#363636")),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(_textBox);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okBtn.Click += (_, _) => { DialogResult = true; };
        btnPanel.Children.Add(okBtn);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 80,
            IsCancel = true
        };
        btnPanel.Children.Add(cancelBtn);

        stack.Children.Add(btnPanel);
        Content = stack;

        Loaded += (_, _) => { _textBox.Focus(); };
    }
}
