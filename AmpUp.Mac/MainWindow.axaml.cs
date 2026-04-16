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

    private AppConfig? _config;
    private Action<AppConfig>? _onSave;
    public Action<string>? OnProfileSwitch;

    // Map nav buttons to (bar, label) by name
    private readonly record struct NavInfo(Border Bar, TextBlock Label);

    public MainWindow()
    {
        InitializeComponent();
        NavigateTo(_mixerView, NavMixer);

        // Red button hides to tray instead of quitting (handled by TrayIconManager)
        Closing += (_, e) =>
        {
            if (App.Tray?.IsQuitting == true) return;
            e.Cancel = true;
            Hide();
        };

        // Make header bar draggable
        HeaderBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        // Profile button click → show profile picker
        ProfileButton.PointerPressed += (_, _) => ShowProfilePopup();

        // Keyboard shortcuts: Cmd+1..4 for core tabs
        KeyDown += OnWindowKeyDown;
    }

    private void ShowProfilePopup()
    {
        if (_config == null) return;
        var accent = this.FindResource("AccentBrush") as ISolidColorBrush
                     ?? new SolidColorBrush(Color.Parse("#00E676"));

        // Position near the profile button
        var screenPos = ProfileButton.PointToScreen(new Point(ProfileButton.Bounds.Width + 8, 0));

        var popup = new Window
        {
            Width = 220,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint((int)screenPos.X, (int)screenPos.Y),
            SystemDecorations = SystemDecorations.None,
            Background = new SolidColorBrush(Color.Parse("#151515")),
            CanResize = false,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(8) };

        // Header
        panel.Children.Add(new TextBlock
        {
            Text = "SWITCH PROFILE",
            FontSize = 9, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#555555")),
            Margin = new Thickness(8, 4, 0, 8),
        });

        foreach (var profileName in _config.Profiles)
        {
            var name = profileName;
            bool isActive = name == _config.ActiveProfile;

            var row = new Border
            {
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(30, accent.Color.R, accent.Color.G, accent.Color.B))
                    : Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };

            var rowContent = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            if (isActive)
            {
                rowContent.Children.Add(new TextBlock
                {
                    Text = "●",
                    FontSize = 8,
                    Foreground = accent,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                });
            }
            rowContent.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = isActive ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isActive ? accent : new SolidColorBrush(Color.Parse("#CCCCCC")),
            });

            row.Child = rowContent;

            row.PointerEntered += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(25, accent.Color.R, accent.Color.G, accent.Color.B));
            row.PointerExited += (_, _) => row.Background = isActive
                ? new SolidColorBrush(Color.FromArgb(30, accent.Color.R, accent.Color.G, accent.Color.B))
                : Brushes.Transparent;

            row.PointerPressed += (_, _) =>
            {
                popup.Close();
                if (name != _config.ActiveProfile)
                    OnProfileSwitch?.Invoke(name);
            };

            panel.Children.Add(row);
        }

        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#151515")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A2A2A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = panel,
        };
        popup.Content = border;

        popup.Deactivated += (_, _) => popup.Close();
        popup.ShowDialog(this);
    }

    // ── Called by App after backend is ready ─────────────────────

    public void SetViewDependencies(
        AppConfig config,
        Action<AppConfig> onSave,
        AmbienceSync? ambienceSync,
        DreamSyncController? dreamSync)
    {
        _config = config;
        _onSave = onSave;
        _mixerView.LoadConfig(config, onSave);
        _buttonsView.LoadConfig(config, onSave);
        _lightsView.LoadConfig(config, onSave);
        _settingsView.LoadConfig(config, onSave);
        HwPreview.LoadConfig(config);

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
        _buttonsView.LoadConfig(config, _onSave ?? (_ => { }));
        _lightsView.LoadConfig(config, _onSave ?? (_ => { }));
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
    private void NavSettings_Click(object? sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);

    // ── Keyboard shortcuts ────────────────────────────────────────
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd+1-4 for core tab navigation (⌘1=Mixer, ⌘2=Buttons, ⌘3=Lights, ⌘4=Settings)
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!cmd) return;

        switch (e.Key)
        {
            case Key.D1: NavigateTo(_mixerView, NavMixer); e.Handled = true; break;
            case Key.D2: NavigateTo(_buttonsView, NavButtons); e.Handled = true; break;
            case Key.D3: NavigateTo(_lightsView, NavLights); e.Handled = true; break;
            case Key.D4: NavigateTo(_settingsView, NavSettings); e.Handled = true; break;
        }
    }

    private Dictionary<Button, NavInfo> GetNavMap() => new()
    {
        { NavMixer,    new(NavMixerBar,    NavMixerLabel)    },
        { NavButtons,  new(NavButtonsBar,  NavButtonsLabel)  },
        { NavLights,   new(NavLightsBar,   NavLightsLabel)   },
        { NavSettings, new(NavSettingsBar, NavSettingsLabel) },
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
