using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Core.Models;
using AmpUp.Core.Services;

namespace AmpUp.Mac.Views;

public partial class SettingsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;
    private bool _downloading;

    public Action? OnNavigateToOverview { get; set; }

    public SettingsView()
    {
        InitializeComponent();

        // Version display
        TxtVersion.Text = $"AmpUp {UpdateChecker.CurrentVersion}";

        // Advanced toggle
        AdvancedToggle.PointerPressed += AdvancedToggle_Click;

        // Port controls
        BtnRefreshPorts.Click += (_, _) => RefreshPortList();
        BtnAutoDetect.Click += (_, _) => OnAutoDetect();

        // Profile buttons
        BtnAddProfile.Click += (_, _) => OnAddProfile();
        BtnRenameProfile.Click += (_, _) => OnRenameProfile();
        BtnDeleteProfile.Click += (_, _) => OnDeleteProfile();
        BtnOverview.Click += (_, _) => OnNavigateToOverview?.Invoke();

        // Home Assistant
        ChkHaEnabled.IsCheckedChanged += OnHaEnabledChanged;
        BtnHaTest.Click += (_, _) => OnHaTestAsync();

        // Govee
        ChkGoveeEnabled.IsCheckedChanged += OnGoveeEnabledChanged;

        // Updates
        BtnCheckUpdate.Click += (_, _) => OnCheckUpdate();
        BtnDownloadUpdate.Click += (_, _) => OnDownloadUpdate();
        ChkAutoUpdate.IsCheckedChanged += OnAutoUpdateChanged;

        // Subscribe to background update checks (from App.axaml.cs periodic timer)
        MacUpdateService.UpdateAvailable += tag =>
            Dispatcher.UIThread.Post(() => ShowUpdateBanner(tag));

        // If an update was already found before this view was opened, show it immediately
        if (MacUpdateService.HasPendingUpdate && MacUpdateService.PendingTag != null)
            ShowUpdateBanner(MacUpdateService.PendingTag);
    }

    public void LoadConfig(AppConfig config, Action<AppConfig> onSave)
    {
        _loading = true;
        _config = config;
        _onSave = onSave;

        TxtSerialPort.Text = config.Serial.Port;
        TxtBaudRate.Text = config.Serial.Baud.ToString();
        RefreshPortList(config.Serial.Port);

        ChkStartWithSystem.IsChecked = config.StartWithWindows;
        ChkStartMinimized.IsChecked = config.StartMinimized;

        // Profiles
        CmbProfiles.Items.Clear();
        foreach (var profile in config.Profiles)
            CmbProfiles.Items.Add(profile);
        CmbProfiles.SelectedItem = config.ActiveProfile;

        // Home Assistant
        ChkHaEnabled.IsChecked = config.HomeAssistant.Enabled;
        TxtHaUrl.Text = config.HomeAssistant.Url;
        TxtHaToken.Text = config.HomeAssistant.Token;
        HaSection.IsVisible = config.HomeAssistant.Enabled;
        UpdateHaStatus(null);

        // Govee
        ChkGoveeEnabled.IsChecked = config.Ambience.GoveeCloudEnabled;
        TxtGoveeApiKey.Text = config.Ambience.GoveeApiKey;
        GoveeSection.IsVisible = config.Ambience.GoveeCloudEnabled;
        UpdateGoveeStatus();

        // Auto-update (default on)
        ChkAutoUpdate.IsChecked = config.AutoCheckUpdates;
        MacUpdateService.AutoCheckEnabled = config.AutoCheckUpdates;

        _loading = false;
    }

    public void UpdateConnectionStatus(bool connected, string? portName = null)
    {
        ConnectionDot.Fill = new SolidColorBrush(
            connected ? Color.Parse("#00E676") : Color.Parse("#FF4444"));
        TxtConnectionStatus.Text = connected
            ? $"Connected on {portName ?? "unknown"}"
            : "Disconnected";
    }

    // ── Advanced Toggle ───────────────────────────────────────────

    private void AdvancedToggle_Click(object? sender, PointerPressedEventArgs e)
    {
        bool show = !AdvancedSection.IsVisible;
        AdvancedSection.IsVisible = show;
        AdvancedArrow.Text = show ? "▼" : "▶";
    }

    // ── Port Helpers ──────────────────────────────────────────────

    private void RefreshPortList(string? selectPort = null)
    {
        _loading = true;
        CmbSerialPort.Items.Clear();

        // On macOS, list /dev/tty.* serial ports
        try
        {
            var ports = System.IO.Directory.GetFiles("/dev", "tty.*");
            Array.Sort(ports);
            foreach (var port in ports)
                CmbSerialPort.Items.Add(port);
        }
        catch { }

        var target = selectPort ?? TxtSerialPort.Text?.Trim();
        if (!string.IsNullOrEmpty(target) && CmbSerialPort.Items.Contains(target))
            CmbSerialPort.SelectedItem = target;
        else if (CmbSerialPort.Items.Count > 0)
            CmbSerialPort.SelectedIndex = 0;

        _loading = false;
    }

    private void OnAutoDetect()
    {
        // Look for Turn Up CH343 on macOS
        try
        {
            var ports = System.IO.Directory.GetFiles("/dev", "tty.usbmodem*");
            if (ports.Length > 0)
            {
                var port = ports[0];
                RefreshPortList(port);
                _loading = true;
                TxtSerialPort.Text = port;
                _loading = false;
                CollectAndSave();
            }
        }
        catch { }
    }

    // ── Profiles ──────────────────────────────────────────────────

    private async void OnAddProfile()
    {
        if (_config == null) return;
        var suggested = $"Profile {_config.Profiles.Count + 1}";
        var name = await ShowInputDialog("New Profile", "Enter profile name:", suggested);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (_config.Profiles.Contains(name)) return;
        _config.Profiles.Add(name);
        _config.ActiveProfile = name;
        CollectAndSave();
        LoadConfig(_config, _onSave!);
    }

    private async void OnRenameProfile()
    {
        if (_config == null || CmbProfiles.SelectedItem == null) return;
        var current = CmbProfiles.SelectedItem.ToString()!;
        var newName = await ShowInputDialog("Rename Profile", "Enter new profile name:", current);
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        if (newName == current || _config.Profiles.Contains(newName)) return;
        var idx = _config.Profiles.IndexOf(current);
        if (idx >= 0) _config.Profiles[idx] = newName;
        if (_config.ActiveProfile == current) _config.ActiveProfile = newName;
        // Rename profile icon config key if present
        if (_config.ProfileIcons.TryGetValue(current, out var icon))
        {
            _config.ProfileIcons.Remove(current);
            _config.ProfileIcons[newName] = icon;
        }
        CollectAndSave();
        LoadConfig(_config, _onSave!);
    }

    /// <summary>Shows a small modal input dialog and returns the entered text (or null on cancel).</summary>
    private async Task<string?> ShowInputDialog(string title, string prompt, string defaultValue)
    {
        string? result = null;

        var win = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            SystemDecorations = Avalonia.Controls.SystemDecorations.Full,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#141414")),
        };

        var promptLabel = new Avalonia.Controls.TextBlock
        {
            Text = prompt,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9A9A9A")),
            FontSize = 12,
            Margin = new Avalonia.Thickness(16, 16, 16, 6),
        };

        var input = new Avalonia.Controls.TextBox
        {
            Text = defaultValue,
            Margin = new Avalonia.Thickness(16, 0, 16, 0),
            FontSize = 13,
            Padding = new Avalonia.Thickness(8, 6),
        };

        var okBtn = new Avalonia.Controls.Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 0, 12, 0),
            Width = 80, Height = 32,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#00A854")),
            Foreground = Avalonia.Media.Brushes.White,
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Width = 80, Height = 32,
        };

        var btnRow = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 12, 16, 0),
            Spacing = 8,
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);

        var root = new Avalonia.Controls.StackPanel();
        root.Children.Add(promptLabel);
        root.Children.Add(input);
        root.Children.Add(btnRow);
        win.Content = root;

        okBtn.Click += (_, _) => { result = input.Text; win.Close(); };
        cancelBtn.Click += (_, _) => win.Close();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Return) { result = input.Text; win.Close(); }
            else if (e.Key == Avalonia.Input.Key.Escape) win.Close();
        };

        // Focus input after open
        win.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        var ownerWindow = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (ownerWindow != null)
            await win.ShowDialog(ownerWindow);
        else
            win.Show();

        return result;
    }

    private void OnDeleteProfile()
    {
        if (_config == null || CmbProfiles.SelectedItem == null) return;
        var name = CmbProfiles.SelectedItem.ToString()!;
        if (name == "Default") return;

        _config.Profiles.Remove(name);
        _config.ActiveProfile = "Default";
        CollectAndSave();
        LoadConfig(_config, _onSave!);
    }

    // ── Home Assistant ────────────────────────────────────────────

    private void OnHaEnabledChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading) return;
        HaSection.IsVisible = ChkHaEnabled.IsChecked == true;
        UpdateHaStatus(null);
        CollectAndSave();
    }

    private async void OnHaTestAsync()
    {
        if (_config == null) return;
        TxtHaTestResult.Text = "Testing...";
        HaStatusDot.Fill = new SolidColorBrush(Color.Parse("#FFB800"));

        CollectAndSave(); // Ensure latest URL/token are saved
        using var ha = new HAIntegration(_config.HomeAssistant);
        var ok = await ha.TestConnectionAsync();

        UpdateHaStatus(ok);
        TxtHaTestResult.Text = ok ? "Connected ✓" : "Connection failed ✗";
    }

    private void UpdateHaStatus(bool? connected)
    {
        if (ChkHaEnabled.IsChecked != true)
        {
            HaStatusDot.Fill = new SolidColorBrush(Color.Parse("#555555"));
            TxtHaStatus.Text = "Disabled";
        }
        else if (connected == true)
        {
            HaStatusDot.Fill = new SolidColorBrush(Color.Parse("#00E676"));
            TxtHaStatus.Text = "Connected";
        }
        else if (connected == false)
        {
            HaStatusDot.Fill = new SolidColorBrush(Color.Parse("#FF4444"));
            TxtHaStatus.Text = "Disconnected";
        }
        else
        {
            HaStatusDot.Fill = new SolidColorBrush(Color.Parse("#FFB800"));
            TxtHaStatus.Text = "Enabled";
        }
    }

    // ── Govee ─────────────────────────────────────────────────────

    private void OnGoveeEnabledChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading) return;
        GoveeSection.IsVisible = ChkGoveeEnabled.IsChecked == true;
        UpdateGoveeStatus();
        CollectAndSave();
    }

    private void UpdateGoveeStatus()
    {
        bool enabled = ChkGoveeEnabled.IsChecked == true;
        bool hasKey = !string.IsNullOrWhiteSpace(TxtGoveeApiKey.Text);

        if (!enabled)
        {
            GoveeStatusDot.Fill = new SolidColorBrush(Color.Parse("#555555"));
            TxtGoveeStatus.Text = "Disabled";
            TxtGoveeApiStatus.Text = "";
        }
        else if (hasKey)
        {
            GoveeStatusDot.Fill = new SolidColorBrush(Color.Parse("#00E676"));
            TxtGoveeStatus.Text = "Connected";
            TxtGoveeApiStatus.Text = "✓ API key configured";
        }
        else
        {
            GoveeStatusDot.Fill = new SolidColorBrush(Color.Parse("#FFB800"));
            TxtGoveeStatus.Text = "No API key";
            TxtGoveeApiStatus.Text = "";
        }
    }

    // ── Updates ───────────────────────────────────────────────────

    private void ShowUpdateBanner(string tag)
    {
        TxtUpdateTag.Text = tag;
        UpdateBanner.IsVisible = true;
        TxtUpdateStatus.IsVisible = false;
    }

    private async void OnCheckUpdate()
    {
        BtnCheckUpdate.IsEnabled = false;
        TxtUpdateStatus.IsVisible = true;
        TxtUpdateStatus.Foreground = new SolidColorBrush(Color.Parse("#9A9A9A"));
        TxtUpdateStatus.Text = "Checking for updates...";
        UpdateBanner.IsVisible = false;

        try
        {
            await MacUpdateService.CheckAsync();

            if (MacUpdateService.HasPendingUpdate && MacUpdateService.PendingTag != null)
            {
                TxtUpdateStatus.IsVisible = false;
                ShowUpdateBanner(MacUpdateService.PendingTag);
            }
            else
            {
                TxtUpdateStatus.Text = "You're on the latest version.";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.Parse("#00DD77"));
            }
        }
        catch
        {
            TxtUpdateStatus.Text = "Update check failed.";
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.Parse("#FF4444"));
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private async void OnDownloadUpdate()
    {
        if (_downloading) return;
        _downloading = true;
        BtnDownloadUpdate.IsEnabled = false;
        DownloadProgress.IsVisible = true;
        TxtDownloadStatus.IsVisible = true;
        TxtDownloadStatus.Text = "Downloading...";
        TxtDownloadStatus.Foreground = new SolidColorBrush(Color.Parse("#9A9A9A"));

        try
        {
            await MacUpdateService.DownloadAndOpenAsync(progress =>
                Dispatcher.UIThread.Post(() => DownloadProgress.Value = progress));

            TxtDownloadStatus.Text = "Done! Drag AmpUp.app to Applications to update.";
            TxtDownloadStatus.Foreground = new SolidColorBrush(Color.Parse("#00DD77"));
        }
        catch (Exception ex)
        {
            TxtDownloadStatus.Text = $"Download failed: {ex.Message}";
            TxtDownloadStatus.Foreground = new SolidColorBrush(Color.Parse("#FF4444"));
            BtnDownloadUpdate.IsEnabled = true;
        }
        finally
        {
            _downloading = false;
            DownloadProgress.IsVisible = false;
        }
    }

    private void OnAutoUpdateChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loading || _config == null) return;
        var enabled = ChkAutoUpdate.IsChecked == true;
        _config.AutoCheckUpdates = enabled;
        MacUpdateService.AutoCheckEnabled = enabled;
        CollectAndSave();
    }

    // ── Collect & Save ────────────────────────────────────────────

    private void CollectAndSave()
    {
        if (_config == null || _onSave == null || _loading) return;

        _config.Serial.Port = TxtSerialPort.Text?.Trim() ?? "";
        if (int.TryParse(TxtBaudRate.Text?.Trim(), out var baud))
            _config.Serial.Baud = baud;

        _config.StartWithWindows = ChkStartWithSystem.IsChecked == true;
        _config.StartMinimized = ChkStartMinimized.IsChecked == true;

        // Home Assistant
        _config.HomeAssistant.Enabled = ChkHaEnabled.IsChecked == true;
        _config.HomeAssistant.Url = TxtHaUrl.Text?.Trim() ?? "";
        _config.HomeAssistant.Token = TxtHaToken.Text ?? "";

        // Govee
        _config.Ambience.GoveeCloudEnabled = ChkGoveeEnabled.IsChecked == true;
        _config.Ambience.GoveeApiKey = TxtGoveeApiKey.Text ?? "";

        // Updates
        _config.AutoCheckUpdates = ChkAutoUpdate.IsChecked == true;

        _onSave(_config);
    }
}
