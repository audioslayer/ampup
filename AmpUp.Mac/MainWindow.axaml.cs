using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly AudioDashboardView _audioDashView = new();

    private Button? _activeNavButton;
    private Border? _activeNavBar;
    private TextBlock? _activeNavLabel;

    // Map nav buttons to (bar, label) by name
    private readonly record struct NavInfo(Border Bar, TextBlock Label);

    public MainWindow()
    {
        InitializeComponent();
        NavigateTo(_mixerView, NavMixer);

        // Make header bar draggable
        HeaderBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        // Keyboard shortcuts: Cmd+1..7 for tabs
        KeyDown += OnWindowKeyDown;
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
        HwPreview.LoadConfig(config);
        _audioDashView.LoadConfig(config, onSave);

        // Wire cross-view navigation
        _settingsView.OnNavigateToOverview = () => NavigateTo(_bindingsView, NavBindings);
        _ambienceView.NavigateToSettings = () => NavigateTo(_settingsView, NavSettings);

        // Wire BindingsView navigation callbacks
        _bindingsView.SetNavigationCallbacks(
            onMixer: _ => NavigateTo(_mixerView, NavMixer),
            onButtons: _ => NavigateTo(_buttonsView, NavButtons));

        // Sync active profile label
        SetActiveProfile(config.ActiveProfile);

        // Apply start-minimized from config
        StartMinimized = config.StartMinimized;
    }

    /// <summary>Called from hardware knob event — pushes live position to MixerView.</summary>
    public void UpdateKnobPosition(int idx, float position)
    {
        _mixerView.UpdateKnobPosition(idx, position);
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
        HwPreview.LoadConfig(config);
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
    private void NavAudioDash_Click(object? sender, RoutedEventArgs e) => NavigateTo(_audioDashView, NavAudioDash);
    private void NavAmbience_Click(object? sender, RoutedEventArgs e) => NavigateTo(_ambienceView, NavAmbience);
    private void NavOsd_Click(object? sender, RoutedEventArgs e) => NavigateTo(_osdView, NavOsd);
    private void NavSettings_Click(object? sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);
    private void NavBindings_Click(object? sender, RoutedEventArgs e) => NavigateTo(_bindingsView, NavBindings);

    // ── Keyboard shortcuts ────────────────────────────────────────
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd+1-7 for tab navigation (⌘1=Knobs, ⌘2=Buttons, ⌘3=Lights,
        //   ⌘4=Ambience, ⌘5=OSD, ⌘6=Settings, ⌘7=Overview)
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!cmd) return;

        switch (e.Key)
        {
            case Key.D1: NavigateTo(_mixerView, NavMixer); e.Handled = true; break;
            case Key.D2: NavigateTo(_buttonsView, NavButtons); e.Handled = true; break;
            case Key.D3: NavigateTo(_lightsView, NavLights); e.Handled = true; break;
            case Key.D4: NavigateTo(_ambienceView, NavAmbience); e.Handled = true; break;
            case Key.D5: NavigateTo(_osdView, NavOsd); e.Handled = true; break;
            case Key.D6: NavigateTo(_settingsView, NavSettings); e.Handled = true; break;
            case Key.D7: NavigateTo(_bindingsView, NavBindings); e.Handled = true; break;
        }
    }

    private Dictionary<Button, NavInfo> GetNavMap() => new()
    {
        { NavMixer,     new(NavMixerBar,     NavMixerLabel)     },
        { NavButtons,   new(NavButtonsBar,   NavButtonsLabel)   },
        { NavLights,    new(NavLightsBar,    NavLightsLabel)    },
        { NavAudioDash, new(NavAudioDashBar, NavAudioDashLabel) },
        { NavAmbience,  new(NavAmbienceBar,  NavAmbienceLabel)  },
        { NavOsd,       new(NavOsdBar,       NavOsdLabel)       },
        { NavSettings,  new(NavSettingsBar,  NavSettingsLabel)  },
        { NavBindings,  new(NavBindingsBar,  NavBindingsLabel)  },
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

    /// <summary>Navigate to the Settings tab. Called from the macOS application menu.</summary>
    public void NavigateToSettings() => NavigateTo(_settingsView, NavSettings);

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
        ProfileIconText.Text = string.IsNullOrEmpty(profileName) ? "?" : profileName[..1].ToUpperInvariant();
        App.Tray?.SetActiveProfile(profileName);
    }

    // ── Start minimized support ───────────────────────────────────
    /// <summary>
    /// If true, window starts hidden (minimized to menu bar tray).
    /// Set from config.StartMinimized in SetViewDependencies.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (StartMinimized)
            Hide();
    }
}
