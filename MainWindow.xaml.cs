using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WolfMixer.Views;

namespace WolfMixer;

public partial class MainWindow : FluentWindow
{
    private readonly MixerView _mixerView = new();
    private readonly ButtonsView _buttonsView = new();
    private readonly LightsView _lightsView = new();
    private readonly SettingsView _settingsView = new();

    private System.Windows.Controls.Button? _activeNavButton;

    private AppConfig _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onConfigChanged;

    public MainWindow()
    {
        InitializeComponent();
        _config = ConfigManager.Load();
        NavigateTo(_mixerView, NavMixer);
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
    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);

    private void NavigateTo(System.Windows.Controls.UserControl view, System.Windows.Controls.Button navButton)
    {
        ContentArea.Content = view;

        // Update sidebar highlight
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var dim = (SolidColorBrush)FindResource("TextSecBrush");

        if (_activeNavButton != null)
        {
            var oldIcon = FindSymbolIcon(_activeNavButton);
            if (oldIcon != null) oldIcon.Foreground = dim;
        }

        var newIcon = FindSymbolIcon(navButton);
        if (newIcon != null) newIcon.Foreground = accent;

        _activeNavButton = navButton;
    }

    private static SymbolIcon? FindSymbolIcon(System.Windows.Controls.Button button)
    {
        return button.Content as SymbolIcon;
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
