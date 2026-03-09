using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using WolfMixer.Views;

namespace WolfMixer;

public partial class MainWindow : FluentWindow
{
    private readonly MixerView _mixerView = new();
    private readonly ButtonsView _buttonsView = new();
    private readonly LightsView _lightsView = new();
    private readonly SettingsView _settingsView = new();
    private readonly HomeAssistantView _haView = new();

    private System.Windows.Controls.Button? _activeNavButton;

    private AppConfig _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onConfigChanged;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/ampuplogo.png", UriKind.Absolute));

        _config = ConfigManager.Load();
        NavigateTo(_mixerView, NavMixer);
        SetupTrafficLightHovers();
    }

    /// <summary>
    /// Wire up backend references and load config into all views.
    /// </summary>
    public void Initialize(AppConfig config, AudioMixer mixer, Action<AppConfig> onConfigChanged)
    {
        _config = config;
        _mixer = mixer;
        _onConfigChanged = onConfigChanged;
        RefreshViews();
    }

    private void RefreshViews()
    {
        Action<AppConfig> saveHandler = cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        };

        _settingsView.LoadConfig(_config, saveHandler);
        _lightsView.LoadConfig(_config, saveHandler);
        _buttonsView.LoadConfig(_config, _mixer!, saveHandler);
        _mixerView.LoadConfig(_config, _mixer!, saveHandler);
        _haView.LoadConfig(_config, saveHandler);
    }

    private void NavMixer_Click(object sender, RoutedEventArgs e) => NavigateTo(_mixerView, NavMixer);
    private void NavButtons_Click(object sender, RoutedEventArgs e) => NavigateTo(_buttonsView, NavButtons);
    private void NavLights_Click(object sender, RoutedEventArgs e)
    {
        // Refresh lights view to pick up label/color changes from mixer tab
        _lightsView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        });
        NavigateTo(_lightsView, NavLights);
    }
    private void NavHA_Click(object sender, RoutedEventArgs e)
    {
        _haView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        });
        NavigateTo(_haView, NavHA);
    }
    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);

    private void NavigateTo(System.Windows.Controls.UserControl view, System.Windows.Controls.Button navButton)
    {
        ContentArea.Content = view;

        // Update sidebar highlight (icon + label)
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var dimIcon = (SolidColorBrush)FindResource("TextSecBrush");
        var dimLabel = (SolidColorBrush)FindResource("TextDimBrush");

        if (_activeNavButton != null)
        {
            var (oldIcon, oldLabel) = FindNavChildren(_activeNavButton);
            if (oldIcon != null) oldIcon.Foreground = dimIcon;
            if (oldLabel != null) oldLabel.Foreground = dimLabel;
        }

        var (newIcon, newLabel) = FindNavChildren(navButton);
        if (newIcon != null) newIcon.Foreground = accent;
        if (newLabel != null) newLabel.Foreground = accent;

        _activeNavButton = navButton;
    }

    private static (SymbolIcon? Icon, System.Windows.Controls.TextBlock? Label) FindNavChildren(System.Windows.Controls.Button button)
    {
        if (button.Content is System.Windows.Controls.StackPanel sp)
        {
            SymbolIcon? icon = null;
            System.Windows.Controls.TextBlock? label = null;
            foreach (var child in sp.Children)
            {
                if (child is SymbolIcon si) icon = si;
                if (child is System.Windows.Controls.TextBlock tb) label = tb;
            }
            return (icon, label);
        }
        return (button.Content as SymbolIcon, null);
    }

    // ── Window drag ─────────────────────────────────────────────────

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    // ── Mac-style traffic light buttons ───────────────────────────

    private void SetupTrafficLightHovers()
    {
        // Show icons on hover over any of the 3 buttons
        var buttons = new[] { BtnMinimize, BtnMaximize, BtnClose };
        var icons = new[] { MinIcon, MaxIcon, CloseIcon };

        foreach (var btn in buttons)
        {
            btn.MouseEnter += (_, _) => { foreach (var ic in icons) ic.Opacity = 1; };
            btn.MouseLeave += (_, _) => { foreach (var ic in icons) ic.Opacity = 0; };
        }
    }

    private void BtnMinimize_Click(object sender, MouseButtonEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, MouseButtonEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, MouseButtonEventArgs e)
    {
        Close(); // triggers MainWindow_Closing → hides to tray
    }

    public void SetConnectionStatus(bool connected)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectionDot.Fill = connected
                ? (SolidColorBrush)FindResource("SuccessGrnBrush")
                : (SolidColorBrush)FindResource("TextDimBrush");
            ConnectionLabel.Text = connected ? "Connected" : "Disconnected";

            var pulse = (System.Windows.Media.Animation.Storyboard)FindResource("PulseAnimation");
            if (connected)
                pulse.Begin(this, true);
            else
            {
                pulse.Stop(this);
                ConnectionDot.Opacity = 1.0;
            }
        });
    }
}
