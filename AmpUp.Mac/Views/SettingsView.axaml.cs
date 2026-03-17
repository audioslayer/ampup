using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AmpUp.Core.Models;
using AmpUp.Core.Services;

namespace AmpUp.Mac.Views;

public partial class SettingsView : UserControl
{
    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    private bool _loading;

    public Action? OnNavigateToOverview { get; set; }

    public SettingsView()
    {
        InitializeComponent();

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

        // About
        BtnCheckUpdate.Click += (_, _) => OnCheckUpdate();
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

    private void OnAddProfile()
    {
        // TODO: Show input dialog for profile name
        if (_config == null) return;
        var name = $"Profile {_config.Profiles.Count + 1}";
        _config.Profiles.Add(name);
        _config.ActiveProfile = name;
        CollectAndSave();
        LoadConfig(_config, _onSave!);
    }

    private void OnRenameProfile()
    {
        // TODO: Show rename dialog
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

    // ── About ─────────────────────────────────────────────────────

    private async void OnCheckUpdate()
    {
        BtnCheckUpdate.IsEnabled = false;
        TxtUpdateStatus.IsVisible = true;
        TxtUpdateStatus.Text = "Checking for updates...";

        try
        {
            var update = await UpdateChecker.CheckForUpdateAsync();
            if (update == null)
            {
                TxtUpdateStatus.Text = "You're on the latest version.";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.Parse("#00DD77"));
            }
            else
            {
                var (tag, _) = update.Value;
                TxtUpdateStatus.Text = $"New version available: {tag}";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.Parse("#00E676"));
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

        _onSave(_config);
    }
}
