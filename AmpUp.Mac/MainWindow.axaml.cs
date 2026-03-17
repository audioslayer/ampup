using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Core.Models;
using AmpUp.Core.Services;
using AmpUp.Mac.Views;

namespace AmpUp.Mac;

public partial class MainWindow : Window
{
    private readonly MixerView _mixerView = new();
    private readonly ButtonsView _buttonsView = new();
    private readonly LightsView _lightsView = new();
    private readonly SettingsView _settingsView = new();
    private readonly AmbienceView _ambienceView = new();
    private readonly BindingsView _bindingsView = new();
    private readonly OsdView _osdView = new();

    private Button? _activeNavButton;
    private Border? _activeNavBar;
    private TextBlock? _activeNavIcon;
    private TextBlock? _activeNavLabel;

    // Map nav buttons to (bar, icon, label) by name
    private readonly record struct NavInfo(Border Bar, TextBlock Icon, TextBlock Label);

    public MainWindow()
    {
        InitializeComponent();
        NavigateTo(_mixerView, NavMixer);
    }

    // ── Called by App after backend is ready ─────────────────────

    public void SetViewDependencies(
        AppConfig config,
        Action<AppConfig> onSave,
        AmbienceSync? ambienceSync,
        DreamSyncController? dreamSync)
    {
        _mixerView.LoadConfig(config, onSave);
        _buttonsView.LoadConfig(config, onSave);
        _lightsView.LoadConfig(config, onSave);
        _settingsView.LoadConfig(config, onSave);
        _bindingsView.LoadConfig(config);
        _osdView.LoadConfig(config, onSave);
        _ambienceView.LoadConfig(config, onSave, ambienceSync, dreamSync);

        // Wire cross-view navigation
        _settingsView.OnNavigateToOverview = () => NavigateTo(_bindingsView, NavBindings);
        _ambienceView.NavigateToSettings = () => NavigateTo(_settingsView, NavSettings);
    }

    /// <summary>Called when DreamSync emits new zone colors — forwards to AmbienceView preview.</summary>
    public void UpdateDreamZones((byte R, byte G, byte B)[] zones)
    {
        _ambienceView.UpdateDreamZoneColors(zones);
    }

    /// <summary>Reload relevant views after a profile switch.</summary>
    public void RefreshViews(AppConfig config)
    {
        _mixerView.LoadConfig(config);
        _bindingsView.LoadConfig(config);
    }

    /// <summary>Update the connection status shown in SettingsView.</summary>
    public void UpdateSettingsConnectionStatus(bool connected, string? portName)
    {
        Dispatcher.UIThread.Post(() => _settingsView.UpdateConnectionStatus(connected, portName));
    }

    // ── Navigation click handlers ────────────────────────────────
    private void NavMixer_Click(object? sender, RoutedEventArgs e) => NavigateTo(_mixerView, NavMixer);
    private void NavButtons_Click(object? sender, RoutedEventArgs e) => NavigateTo(_buttonsView, NavButtons);
    private void NavLights_Click(object? sender, RoutedEventArgs e) => NavigateTo(_lightsView, NavLights);
    private void NavAmbience_Click(object? sender, RoutedEventArgs e) => NavigateTo(_ambienceView, NavAmbience);
    private void NavOsd_Click(object? sender, RoutedEventArgs e) => NavigateTo(_osdView, NavOsd);
    private void NavSettings_Click(object? sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);
    private void NavBindings_Click(object? sender, RoutedEventArgs e) => NavigateTo(_bindingsView, NavBindings);

    private Dictionary<Button, NavInfo> GetNavMap() => new()
    {
        { NavMixer,    new(NavMixerBar, NavMixerIcon, NavMixerLabel) },
        { NavButtons,  new(NavButtonsBar, NavButtonsIcon, NavButtonsLabel) },
        { NavLights,   new(NavLightsBar, NavLightsIcon, NavLightsLabel) },
        { NavAmbience, new(NavAmbienceBar, NavAmbienceIcon, NavAmbienceLabel) },
        { NavOsd,      new(NavOsdBar, NavOsdIcon, NavOsdLabel) },
        { NavSettings, new(NavSettingsBar, NavSettingsIcon, NavSettingsLabel) },
        { NavBindings, new(NavBindingsBar, NavBindingsIcon, NavBindingsLabel) },
    };

    private void NavigateTo(UserControl view, Button navButton)
    {
        ContentArea.Content = view;

        var accent = this.FindResource("AccentBrush") as ISolidColorBrush
                     ?? new SolidColorBrush(Color.Parse("#00E676"));
        var dim = this.FindResource("TextSecBrush") as ISolidColorBrush
                  ?? new SolidColorBrush(Color.Parse("#9A9A9A"));

        // Reset previous active
        if (_activeNavBar != null) _activeNavBar.IsVisible = false;
        if (_activeNavLabel != null) _activeNavLabel.Foreground = dim;

        // Activate new
        var map = GetNavMap();
        if (map.TryGetValue(navButton, out var info))
        {
            info.Bar.IsVisible = true;
            info.Label.Foreground = accent;
            _activeNavBar = info.Bar;
            _activeNavLabel = info.Label;
        }

        _activeNavButton = navButton;
    }

    // ── Connection status (called externally) ────────────────────
    public void SetConnectionStatus(bool connected)
    {
        var green = this.FindResource("SuccessGrnBrush") as ISolidColorBrush
                    ?? new SolidColorBrush(Color.Parse("#00DD77"));
        var dimBrush = this.FindResource("TextDimBrush") as ISolidColorBrush
                       ?? new SolidColorBrush(Color.Parse("#6A6A6A"));

        ConnectionDot.Fill = connected ? green : dimBrush;
        ConnectionLabel.Text = connected ? "Connected" : "Disconnected";

        // Mirror to tray icon
        App.Tray?.SetConnectionStatus(connected);
    }

    // ── Active profile (called externally) ───────────────────────
    public void SetActiveProfile(string profileName)
    {
        ProfileLabel.Text = profileName;
        App.Tray?.SetActiveProfile(profileName);
    }

    // ── Start minimized support ───────────────────────────────────
    /// <summary>
    /// If true, window starts hidden (minimized to menu bar tray).
    /// Called by the Mac orchestrator after loading config.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (StartMinimized)
            Hide();
    }
}
