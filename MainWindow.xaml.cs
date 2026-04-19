using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Material.Icons;
using MiIcon = Material.Icons.WPF.MaterialIcon;
using Wpf.Ui.Controls;
using AmpUp.Views;
using AmpUp.Core.Services;

namespace AmpUp;

public partial class MainWindow : FluentWindow
{
    private readonly MixerView _mixerView = new();
    private readonly ButtonsView _buttonsView = new();
    private readonly LightsView _lightsView = new();
    private readonly SettingsView _settingsView = new();
    private readonly RoomView _ambienceView = new();
    private readonly BindingsView _bindingsView = new();
    private readonly OsdView _osdView = new();
    private readonly GroupsView _groupsView = new();

    private System.Windows.Controls.Button? _activeNavButton;
    private System.Windows.Controls.Border? _activeNavBar;

    private Window? _profileFlyout;
    private bool _profileFlyoutOpen = false;
    private bool _turnUpConnected;
    private bool _streamControllerConnected;

    private AppConfig _config;
    private AudioMixer? _mixer;
    private Action<AppConfig>? _onConfigChanged;

    private System.Windows.Threading.DispatcherTimer? _hwPreviewTimer;
    private volatile bool _windowActive = true; // safe to read from any thread

    public MainWindow()
    {
        InitializeComponent();
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/icon/ampup-48.png", UriKind.Absolute));

        _config = ConfigManager.Load();
        VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
        UpdateProfileButton();
        UpdateAccentDependentUI();
        _bindingsView.SetNavigationCallbacks(
            profileName =>
            {
                if (_config.ActiveProfile != profileName)
                {
                    _onConfigChanged?.Invoke(_config);
                    (Application.Current as App)?.SwitchToProfile(profileName);
                }
                NavigateTo(_mixerView, NavMixer);
            },
            profileName =>
            {
                if (_config.ActiveProfile != profileName)
                {
                    _onConfigChanged?.Invoke(_config);
                    (Application.Current as App)?.SwitchToProfile(profileName);
                }
                NavigateTo(_buttonsView, NavButtons);
            },
            profileName =>
            {
                _onConfigChanged?.Invoke(_config);
                (Application.Current as App)?.SwitchToProfile(profileName);
                // Stay on Overview — don't navigate away
                NavigateTo(_bindingsView, NavBindings);
            },
            profileName =>
            {
                // Preview OSD for this profile without switching
                var app = Application.Current as App;
                if (app == null) return;
                var profileConfig = ConfigManager.LoadProfile(profileName) ?? _config;
                var iconCfg = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
                app.PreviewProfileOsd(profileName, iconCfg, profileConfig);
            });

        _bindingsView.OnDuplicateProfile = profileName =>
        {
            DuplicateProfile(profileName);
        };
        _bindingsView.OnMoveProfile = (profileName, direction) =>
        {
            MoveProfile(profileName, direction);
        };

        NavigateTo(_mixerView, NavMixer);
        SetupTrafficLightHovers();

        ThemeManager.OnAccentChanged += () => Dispatcher.Invoke(UpdateAccentDependentUI);

        // Remove Win11 DWM border (the white/gray 1px border around the window)
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.RemoveDwmBorder(hwnd);
        };

        // Silent startup update check
        Loaded += async (_, _) => await CheckForUpdateOnStartup();
    }

    /// <summary>
    /// Update UI elements that depend on the accent color but can't use DynamicResource
    /// (DropShadowEffect Color, GradientStop, etc.).
    /// </summary>
    private void UpdateAccentDependentUI()
    {
        // Profile button border gradient
        ProfileButton.BorderBrush = new LinearGradientBrush(
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x88),
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x44),
            new Point(0, 0), new Point(1, 1));

        // Profile button drop shadow
        ProfileButton.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = ThemeManager.Accent,
            BlurRadius = 12,
            Opacity = 0.25,
            ShadowDepth = 0
        };

        // Connection dot glow
        ConnectionDotGlow.Color = ThemeManager.Accent;
    }

    /// <summary>
    /// Wire up backend references and load config into all views.
    /// </summary>
    public void Initialize(AppConfig config, AudioMixer mixer, Action<AppConfig> onConfigChanged)
    {
        _config = config;
        _mixer = mixer;
        _onConfigChanged = onConfigChanged;
        ApplyHardwareSurfaceFromState(persist: false);
        RefreshViews();
        StartHwPreviewTimer();
    }

    private void StartHwPreviewTimer()
    {
        // Subscribe to LED frame data from RgbController
        if (App.Rgb != null)
            App.Rgb.OnFrameReady += OnRgbFrameReady;

        // Wire click to navigate to Mixer tab
        HwPreview.OnKnobClicked = _ => NavigateTo(_mixerView, NavMixer);

        // 50ms timer for VU smoothing + position updates
        _hwPreviewTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _hwPreviewTimer.Tick += HwPreviewTimer_Tick;
        _hwPreviewTimer.Start();

        // Stop the timer when minimized or hidden to tray — WPF DispatcherTimers
        // keep firing even when the window is minimized, causing unnecessary WASAPI
        // peak calls + rendering at 20 FPS with nothing visible.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                _windowActive = false;
                _hwPreviewTimer.Stop();
            }
            else
            {
                _windowActive = true;
                _hwPreviewTimer.Start();
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _windowActive = false;
                _hwPreviewTimer.Stop();
            }
            else if (WindowState != WindowState.Minimized)
            {
                _windowActive = true;
                _hwPreviewTimer.Start();
            }
        };
    }

    private void OnRgbFrameReady(byte[] frame)
    {
        // Called from RgbController thread — _windowActive is volatile, safe to read here.
        // Don't touch any WPF dependency properties (e.g. WindowState, IsVisible) from this thread.
        if (!_windowActive) return;
        Dispatcher.BeginInvoke(() => HwPreview.SetLedFrame(frame));
    }

    private void HwPreviewTimer_Tick(object? sender, EventArgs e)
    {
        // Push current knob positions
        HwPreview.SetPositions(App.KnobPositions);

        // Push VU levels from mixer
        if (_mixer != null)
        {
            for (int i = 0; i < 5; i++)
            {
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
                if (knob != null)
                {
                    float peak = Math.Min(_mixer.GetPeakLevel(knob) * 2.3f, 1f);
                    HwPreview.SetVuLevel(i, peak);
                }
            }
        }

        HwPreview.Tick();
    }

    public void RefreshViews(AppConfig? newConfig = null)
    {
        if (newConfig != null) _config = newConfig;
        ApplyHardwareSurfaceFromState(persist: false);
        UpdateProfileButton();
        Action<AppConfig> saveHandler = cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
            ApplyHardwareSurfaceFromState(persist: false);

            // Update Ambience tab visibility when integrations are toggled
            bool showAmbience = cfg.Ambience.GoveeEnabled || cfg.Ambience.GoveeCloudEnabled || cfg.Corsair.Enabled;
            NavAmbience.Visibility = showAmbience ? Visibility.Visible : Visibility.Collapsed;
        };

        _settingsView.OnNavigateToOverview = () => NavigateTo(_bindingsView, NavBindings);
        _settingsView.OnEditProfile = profileName => ShowProfileEditor(profileName);
        _settingsView.OnActiveSurfaceChangedExternal = surface => ApplyDeviceSurface(surface, persist: true);
        _settingsView.OnHardwareModeChangedExternal = () => RefreshViews();
        _settingsView.LoadConfig(_config, saveHandler);
        _lightsView.LoadConfig(_config, saveHandler, _mixer);
        _buttonsView.LoadConfig(_config, _mixer!, saveHandler);
        _mixerView.LoadConfig(_config, _mixer!, saveHandler);
        _ambienceView.LoadConfig(_config, saveHandler);
        _bindingsView.LoadConfig(_config);
        _osdView.OnRequestRefresh = () => RefreshViews();
        _osdView.LoadConfig(_config, saveHandler);
        _groupsView.LoadConfig(_config, saveHandler);

        // Show/hide Ambience nav based on Govee or Corsair enabled state
        bool ambienceEnabled = _config.Ambience.GoveeEnabled || _config.Ambience.GoveeCloudEnabled || _config.Corsair.Enabled;
        NavAmbience.Visibility = ambienceEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Sync knob labels into the hardware preview strip
        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
            string label = knob != null && !string.IsNullOrWhiteSpace(knob.Label)
                ? knob.Label
                : (knob?.Target ?? (i + 1).ToString());
            HwPreview.SetLabel(i, label);
        }
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
        }, _mixer);
        NavigateTo(_lightsView, NavLights);
    }
    private void NavAmbience_Click(object sender, RoutedEventArgs e)
    {
        _ambienceView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        });
        NavigateTo(_ambienceView, NavAmbience);
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsView, NavSettings);
    private void NavBindings_Click(object sender, RoutedEventArgs e) => NavigateTo(_bindingsView, NavBindings);
    private void NavOsd_Click(object sender, RoutedEventArgs e) => NavigateTo(_osdView, NavOsd);
    private void NavGroups_Click(object sender, RoutedEventArgs e)
    {
        _groupsView.LoadConfig(_config, cfg =>
        {
            _config = cfg;
            _onConfigChanged?.Invoke(cfg);
        });
        NavigateTo(_groupsView, NavGroups);
    }

    public void NavigateToSettings() => NavigateTo(_settingsView, NavSettings);

    public void LaunchImportWizard()
    {
        var wizard = new ImportWizardWindow { Owner = this };
        wizard.ShowDialog();

        if (wizard.ImportedProfileName != null)
        {
            var profileName = wizard.ImportedProfileName;

            if (!_config.Profiles.Contains(profileName))
                _config.Profiles.Add(profileName);

            var loaded = ConfigManager.LoadProfile(profileName);
            if (loaded != null)
            {
                loaded.ActiveProfile = profileName;
                PreserveGlobalSettings(loaded);
                _config = loaded;
                _onConfigChanged?.Invoke(_config);
                RefreshViews();
                RefreshProfilePicker();
            }
        }
    }

    // Map nav buttons to their indicator bars
    private Dictionary<System.Windows.Controls.Button, System.Windows.Controls.Border> GetNavBars() => new()
    {
        { NavMixer,     NavMixerBar },
        { NavButtons,   NavButtonsBar },
        { NavLights,    NavLightsBar },
        { NavAmbience,  NavAmbienceBar },
        { NavOsd,       NavOsdBar },
        { NavGroups,    NavGroupsBar },
        { NavSettings,  NavSettingsBar },
        { NavBindings,  NavBindingsBar },
    };

    private void NavigateTo(System.Windows.Controls.UserControl view, System.Windows.Controls.Button navButton)
    {
        ContentArea.Content = view;

        // Update sidebar highlight (icon + label)
        var accent = (SolidColorBrush)FindResource("AccentBrush");
        var dimIcon = (SolidColorBrush)FindResource("TextSecBrush");
        var dimLabel = (SolidColorBrush)FindResource("TextSecBrush");

        if (_activeNavButton != null)
        {
            var (oldPhIcon, oldLabel) = FindNavChildren(_activeNavButton);
            if (oldPhIcon != null) oldPhIcon.IconColor = ((SolidColorBrush)dimIcon).Color;
            if (oldLabel != null) oldLabel.Foreground = dimLabel;

            if (_activeNavBar != null)
                _activeNavBar.Visibility = Visibility.Collapsed;
        }

        var (newPhIcon, newLabel) = FindNavChildren(navButton);
        if (newPhIcon != null) newPhIcon.IconColor = ((SolidColorBrush)accent).Color;
        if (newLabel != null) newLabel.Foreground = accent;

        // Show new indicator bar
        var bars = GetNavBars();
        if (bars.TryGetValue(navButton, out var bar))
        {
            bar.Visibility = Visibility.Visible;
            _activeNavBar = bar;
        }

        _activeNavButton = navButton;
    }

    private static (Controls.PhosphorIcon? Icon, System.Windows.Controls.TextBlock? Label) FindNavChildren(System.Windows.Controls.Button button)
    {
        var grid = button.Content as System.Windows.Controls.Grid;
        var sp = grid != null
            ? grid.Children.OfType<System.Windows.Controls.StackPanel>().FirstOrDefault()
            : button.Content as System.Windows.Controls.StackPanel;

        if (sp != null)
        {
            var phIcon = sp.Children.OfType<Controls.PhosphorIcon>().FirstOrDefault();
            var label = sp.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
            return (phIcon, label);
        }
        return (null, null);
    }

    // ── Window drag ─────────────────────────────────────────────────

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
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

    private bool _checkingUpdate;
    private (string Tag, string Url)? _pendingUpdate;

    private async Task CheckForUpdateOnStartup()
    {
        try
        {
            var update = await UpdateChecker.CheckForUpdateAsync();
            if (update != null)
            {
                _pendingUpdate = update;
                VersionLabel.Text = $"Update available: {update.Value.Tag}";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");
                // Notify tray popup
                if (Application.Current is App app)
                    app.NotifyUpdateAvailable();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Startup update check failed: {ex.Message}");
        }
    }

    private async void VersionLabel_Click(object sender, MouseButtonEventArgs e)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;

        VersionLabel.Text = "Checking...";
        VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");

        try
        {
            var update = _pendingUpdate ?? await UpdateChecker.CheckForUpdateAsync();
            _pendingUpdate = null;
            if (update == null)
            {
                VersionLabel.Text = "Up to date!";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("SuccessGrnBrush");
                await Task.Delay(2000);
                VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
            }
            else
            {
                var (tag, url) = update.Value;
                if (GlassDialog.Confirm($"A new version ({tag}) is available. Download and install?", "UPDATE", owner: this))
                {
                    VersionLabel.Text = "Downloading...";
                    await UpdateChecker.DownloadAndInstallAsync(url, progress =>
                    {
                        Dispatcher.Invoke(() => VersionLabel.Text = $"Downloading {progress}%");
                    });
                }
                else
                {
                    VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
                    VersionLabel.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check error: {ex.Message}");
            VersionLabel.Text = "Update failed";
            VersionLabel.Foreground = (SolidColorBrush)FindResource("DangerRedBrush");
            await Task.Delay(2000);
            VersionLabel.Text = $"v{UpdateChecker.CurrentVersion}";
            VersionLabel.Foreground = (SolidColorBrush)FindResource("TextDimBrush");
        }
        finally
        {
            _checkingUpdate = false;
        }
    }

    // ── Profile flyout ────────────────────────────────────────────

    // Icon options for profile picker — MaterialIconKind names
    private static readonly (string Category, string[] Symbols)[] ProfileIconCategories =
    {
        ("Audio & Music", new[] {
            "VolumeHigh", "VolumeOff", "VolumeMute", "Headphones",
            "MusicNote", "MusicNoteEighth", "Microphone", "MicrophoneOff"
        }),
        ("Gaming & Fun", new[] {
            "GamepadVariant", "Trophy", "Rocket", "Star",
            "Heart", "EmoticonHappy", "Robot", "AccountCircleOutline"
        }),
        ("Lights & Effects", new[] {
            "LightbulbOnOutline", "Flash", "Shimmer", "WeatherCloudy",
            "WeatherNight", "WeatherSunny", "WaterOutline", "Fire"
        }),
        ("Work & Streaming", new[] {
            "Monitor", "Laptop", "Keyboard", "Video",
            "RecordCircle", "Earth", "Bullhorn", "PresentationPlay"
        }),
        ("Home & System", new[] {
            "Home", "CogOutline", "Shield", "Lock",
            "Eye", "Power", "Bluetooth", "Wifi"
        }),
    };

    // Color presets for profile icons
    private static readonly (string Name, string Hex)[] ProfileIconColors =
    {
        ("Green",  "#00E676"),
        ("Cyan",   "#00B4D8"),
        ("Blue",   "#4FC3F7"),
        ("Purple", "#BB86FC"),
        ("Pink",   "#FF4081"),
        ("Red",    "#FF6B6B"),
        ("Orange", "#FF7043"),
        ("Amber",  "#FFB800"),
        ("Mint",   "#69F0AE"),
        ("White",  "#E8E8E8"),
    };

    private void UpdateProfileButton()
    {
        var icon = _config.ProfileIcons.GetValueOrDefault(_config.ActiveProfile) ?? new ProfileIconConfig();
        if (Enum.TryParse<MaterialIconKind>(icon.Symbol, out var kind))
            ProfileIcon.Kind = kind;
        try { ProfileIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(icon.Color)); } catch { }
        ProfileLabel.Text = _config.ActiveProfile;
    }

    private DeviceSurface GetPreferredSurface()
    {
        var preferred = _config.TabSelection.Buttons;
        return preferred switch
        {
            DeviceSurface.StreamController => DeviceSurface.StreamController,
            DeviceSurface.Both => DeviceSurface.Both,
            _ => DeviceSurface.TurnUp,
        };
    }

    private DeviceSurface GetEffectiveDeviceSurface()
    {
        return _config.HardwareMode switch
        {
            HardwareMode.TurnUpOnly => DeviceSurface.TurnUp,
            HardwareMode.StreamControllerOnly => DeviceSurface.StreamController,
            HardwareMode.DualMode => GetPreferredSurface(),
            HardwareMode.Auto when _turnUpConnected && !_streamControllerConnected => DeviceSurface.TurnUp,
            HardwareMode.Auto when !_turnUpConnected && _streamControllerConnected => DeviceSurface.StreamController,
            HardwareMode.Auto when _turnUpConnected && _streamControllerConnected => GetPreferredSurface(),
            _ => GetPreferredSurface(),
        };
    }

    public void ApplyDeviceSurface(DeviceSurface surface, bool persist)
    {
        _config.TabSelection.Mixer = surface;
        _config.TabSelection.Buttons = surface;
        _config.TabSelection.Lights = surface;
        UpdateNavLightsVisibility(surface);

        if (persist)
        {
            _onConfigChanged?.Invoke(_config);
            RefreshViews();
        }
    }

    private void ApplyHardwareSurfaceFromState(bool persist)
    {
        var surface = GetEffectiveDeviceSurface();
        _config.TabSelection.Mixer = surface;
        _config.TabSelection.Buttons = surface;
        _config.TabSelection.Lights = surface;
        UpdateNavLightsVisibility(surface);

        if (persist)
            _onConfigChanged?.Invoke(_config);
    }

    private void UpdateNavLightsVisibility(DeviceSurface surface)
    {
        bool showLights = surface is not DeviceSurface.StreamController;
        NavLights.Visibility = showLights ? Visibility.Visible : Visibility.Collapsed;

        // If Lights tab is active and now hidden, navigate away
        if (!showLights && ContentArea.Content == _lightsView)
            NavigateTo(_mixerView, NavMixer);
    }

    private void ProfileButton_Click(object sender, MouseButtonEventArgs e)
    {
        if (_profileFlyoutOpen)
            CloseProfileFlyout();
        else
            OpenProfileFlyout();
        e.Handled = true;
    }

    private void OpenProfileFlyout()
    {
        BuildProfileFlyout();

        // Detach panel from any previous parent (Border, Grid, Window, etc.)
        if (ProfilePopupPanel.Parent is System.Windows.Controls.Decorator oldDecorator)
            oldDecorator.Child = null;
        else if (ProfilePopupPanel.Parent is System.Windows.Controls.Panel oldPanel)
            oldPanel.Children.Remove(ProfilePopupPanel);
        else if (ProfilePopupPanel.Parent is System.Windows.Controls.ContentControl oldContent)
            oldContent.Content = null;

        var popupBorder = new System.Windows.Controls.Border
        {
            Background = (System.Windows.Media.Brush)FindResource("BgDarkBrush"),
            BorderBrush = (System.Windows.Media.SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            MinWidth = 200,
            Child = ProfilePopupPanel
        };
        ProfilePopupPanel.Visibility = System.Windows.Visibility.Visible;

        var screenPos = ProfileButton.PointToScreen(new Point(ProfileButton.ActualWidth + 4, 0));
        var dpiSource = PresentationSource.FromVisual(ProfileButton);
        if (dpiSource?.CompositionTarget != null)
        {
            var dpiX = dpiSource.CompositionTarget.TransformToDevice.M11;
            var dpiY = dpiSource.CompositionTarget.TransformToDevice.M22;
            screenPos = new Point(screenPos.X / dpiX, screenPos.Y / dpiY);
        }
        _profileFlyout = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = (System.Windows.Media.Brush)FindResource("BgDarkBrush"),
            Content = popupBorder,
            Left = screenPos.X,
            Top = screenPos.Y
        };
        _profileFlyout.Deactivated += (_, _) => CloseProfileFlyout();
        _profileFlyout.KeyDown += (_, e2) => { if (e2.Key == Key.Escape) CloseProfileFlyout(); };
        _profileFlyout.Show();
        _profileFlyoutOpen = true;
    }

    public DeviceSurface GetCurrentDeviceSurface() => GetEffectiveDeviceSurface();

    public (bool turnUp, bool streamController) GetHardwareConnectionState() =>
        (_turnUpConnected, _streamControllerConnected);

    private void CloseProfileFlyout()
    {
        if (!_profileFlyoutOpen) return;
        _profileFlyoutOpen = false;

        // Detach panel child before closing so it can be re-hosted next open
        if (_profileFlyout?.Content is System.Windows.Controls.Border b)
            b.Child = null;

        _profileFlyout?.Close();
        _profileFlyout = null;
    }

    private void BuildProfileFlyout()
    {
        ProfilePopupPanel.Children.Clear();

        // Header
        var header = new System.Windows.Controls.TextBlock
        {
            Text = "PROFILES",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            Margin = new Thickness(6, 4, 0, 8)
        };
        ProfilePopupPanel.Children.Add(header);

        // Profile items
        foreach (var profile in _config.Profiles)
        {
            var profileCapture = profile;
            bool isActive = profile == _config.ActiveProfile;
            var iconCfg = _config.ProfileIcons.GetValueOrDefault(profile) ?? new ProfileIconConfig();

            var row = new System.Windows.Controls.Grid();
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });

            // Icon button (clickable to change icon)
            var iconElement = new MiIcon
            {
                Width = 18, Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Change icon"
            };
            if (Enum.TryParse<MaterialIconKind>(iconCfg.Symbol, out var iconKind))
                iconElement.Kind = iconKind;
            try { iconElement.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(iconCfg.Color)); } catch { }
            iconElement.MouseLeftButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                ShowIconPicker(profileCapture);
            };
            System.Windows.Controls.Grid.SetColumn(iconElement, 0);
            row.Children.Add(iconElement);

            // Profile name
            var nameBlock = new System.Windows.Controls.TextBlock
            {
                Text = profile,
                FontSize = 12,
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isActive
                    ? (SolidColorBrush)FindResource("AccentBrush")
                    : (SolidColorBrush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(nameBlock, 1);
            row.Children.Add(nameBlock);

            // Edit + Delete buttons
            var actionPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Edit button (pencil)
            var editBtn = new System.Windows.Controls.TextBlock
            {
                Text = "\u270E", // ✎ pencil
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Edit profile"
            };
            editBtn.MouseEnter += (_, _) => editBtn.Foreground = (SolidColorBrush)FindResource("AccentBrush");
            editBtn.MouseLeave += (_, _) => editBtn.Foreground = (SolidColorBrush)FindResource("TextSecBrush");
            editBtn.MouseLeftButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                CloseProfileFlyout();
                ShowProfileEditor(profileCapture);
            };
            actionPanel.Children.Add(editBtn);

            // Delete button (not for Default)
            if (profile != "Default")
            {
                var deleteBtn = new System.Windows.Controls.TextBlock
                {
                    Text = "✕",
                    FontSize = 9,
                    Foreground = (SolidColorBrush)FindResource("DangerRedBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "Delete profile"
                };
                deleteBtn.MouseEnter += (_, _) => deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x77, 0x77));
                deleteBtn.MouseLeave += (_, _) => deleteBtn.Foreground = (SolidColorBrush)FindResource("DangerRedBrush");
                deleteBtn.MouseLeftButtonDown += (_, ev) =>
                {
                    ev.Handled = true;
                    DeleteProfile(profileCapture);
                };
                actionPanel.Children.Add(deleteBtn);
            }

            System.Windows.Controls.Grid.SetColumn(actionPanel, 2);
            row.Children.Add(actionPanel);

            var rowBorder = new System.Windows.Controls.Border
            {
                Padding = new Thickness(6, 6, 6, 6),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0xB4, 0xD8))
                    : System.Windows.Media.Brushes.Transparent,
                Child = row
            };

            // Hover
            rowBorder.MouseEnter += (_, _) =>
            {
                if (profileCapture != _config.ActiveProfile)
                    rowBorder.Background = (SolidColorBrush)FindResource("InputBgBrush");
            };
            rowBorder.MouseLeave += (_, _) =>
            {
                rowBorder.Background = profileCapture == _config.ActiveProfile
                    ? new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0xB4, 0xD8))
                    : System.Windows.Media.Brushes.Transparent;
            };

            // Click to switch
            rowBorder.MouseLeftButtonDown += (_, ev) =>
            {
                if (profileCapture == _config.ActiveProfile) return;
                SwitchToProfile(profileCapture);
                CloseProfileFlyout();
            };

            ProfilePopupPanel.Children.Add(rowBorder);
        }

        // Divider
        var divider = new System.Windows.Controls.Border
        {
            Height = 1,
            Background = (SolidColorBrush)FindResource("CardBorderBrush"),
            Margin = new Thickness(4, 6, 4, 6)
        };
        ProfilePopupPanel.Children.Add(divider);

        // Add new profile button
        var addRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        addRow.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "+",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0)
        });
        addRow.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "New Profile",
            FontSize = 12,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var addBorder = new System.Windows.Controls.Border
        {
            Padding = new Thickness(6, 6, 6, 6),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Child = addRow
        };
        addBorder.MouseEnter += (_, _) =>
        {
            addBorder.Background = (SolidColorBrush)FindResource("InputBgBrush");
        };
        addBorder.MouseLeave += (_, _) =>
        {
            addBorder.Background = System.Windows.Media.Brushes.Transparent;
        };
        addBorder.MouseLeftButtonDown += (_, _) =>
        {
            CloseProfileFlyout();
            AddNewProfile();
        };
        ProfilePopupPanel.Children.Add(addBorder);
    }

    private void ShowProfileEditor(string profileName)
    {
        var currentIcon = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        Window? editorWindow = null;
        bool closing = false;
        string currentProfileName = profileName; // tracks renames

        // Helper: save current icon/color state immediately
        Action saveIconColor = () =>
        {
            _config.ProfileIcons[currentProfileName] = new ProfileIconConfig
            {
                Symbol = currentIcon.Symbol,
                Color = currentIcon.Color
            };
            _onConfigChanged?.Invoke(_config);
            UpdateProfileButton();
        };

        // Helper: apply rename if name changed
        Action<string> tryRename = (newName) =>
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == currentProfileName) return;
            newName = newName.Trim();
            if (_config.Profiles.Contains(newName)) return;
            RenameProfile(currentProfileName, newName);
            currentProfileName = newName;
            _onConfigChanged?.Invoke(_config);
            UpdateProfileButton();
            RefreshViews();
        };

        Action closeEditor = () =>
        {
            if (closing) return;
            closing = true;
            // Apply any pending rename on close
            var finalName = (editorWindow?.Content as System.Windows.Controls.Border)?
                .FindName("_nameBox") as System.Windows.Controls.TextBox;
            editorWindow?.Close();
            editorWindow = null;
        };

        var outerPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(10) };

        // ── Rename section ──
        var renameLabel = new System.Windows.Controls.TextBlock
        {
            Text = "NAME",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            Margin = new Thickness(2, 0, 0, 4)
        };
        outerPanel.Children.Add(renameLabel);

        var nameBox = new System.Windows.Controls.TextBox
        {
            Text = profileName,
            FontSize = 12,
            Width = 280,
            Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
            Background = (SolidColorBrush)FindResource("InputBgBrush"),
            BorderBrush = (SolidColorBrush)FindResource("InputBorderBrush"),
            CaretBrush = (SolidColorBrush)FindResource("AccentBrush"),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        nameBox.GotFocus += (_, _) => nameBox.SelectAll();
        // Save name on Enter or lost focus
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                tryRename(nameBox.Text);
                Keyboard.ClearFocus();
            }
        };
        nameBox.LostFocus += (_, _) => tryRename(nameBox.Text);
        outerPanel.Children.Add(nameBox);

        // ── Color swatches ──
        var colorLabel = new System.Windows.Controls.TextBlock
        {
            Text = "COLOR",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            Margin = new Thickness(2, 12, 0, 4)
        };
        outerPanel.Children.Add(colorLabel);

        var colorWrap = new System.Windows.Controls.WrapPanel { Width = 280 };
        var allIconElements = new List<MiIcon>();

        foreach (var (name, hex) in ProfileIconColors)
        {
            var colorHex = hex;
            var swatch = new System.Windows.Controls.Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2),
                ToolTip = name,
                BorderThickness = new Thickness(colorHex == currentIcon.Color ? 2 : 0),
                BorderBrush = new SolidColorBrush(Colors.White)
            };
            try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); } catch { }

            swatch.MouseLeftButtonDown += (_, _) =>
            {
                currentIcon.Color = colorHex;
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                foreach (var mi in allIconElements) mi.Foreground = brush;
                foreach (System.Windows.Controls.Border child in colorWrap.Children)
                    child.BorderThickness = new Thickness(0);
                swatch.BorderThickness = new Thickness(2);
                saveIconColor();
            };
            colorWrap.Children.Add(swatch);
        }
        outerPanel.Children.Add(colorWrap);

        // ── Icon categories ──
        bool first = true;
        foreach (var (category, symbols) in ProfileIconCategories)
        {
            var header = new System.Windows.Controls.TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
                Margin = new Thickness(2, first ? 10 : 8, 0, 4)
            };
            first = false;
            outerPanel.Children.Add(header);

            var wrapPanel = new System.Windows.Controls.WrapPanel { Width = 280 };
            foreach (var symName in symbols)
            {
                var symbolCapture = symName;
                if (!Enum.TryParse<MaterialIconKind>(symName, out var parsedKind))
                    continue;

                var iconEl = new MiIcon
                {
                    Kind = parsedKind,
                    Width = 18, Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                try { iconEl.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(currentIcon.Color)); } catch { }
                allIconElements.Add(iconEl);

                var btn = new System.Windows.Controls.Border
                {
                    Width = 34, Height = 34,
                    CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(1),
                    Child = iconEl,
                    Background = symbolCapture == currentIcon.Symbol
                        ? (SolidColorBrush)FindResource("CardBorderBrush")
                        : System.Windows.Media.Brushes.Transparent
                };
                btn.MouseEnter += (_, _) => btn.Background = (SolidColorBrush)FindResource("CardBorderBrush");
                btn.MouseLeave += (_, _) =>
                {
                    btn.Background = symbolCapture == currentIcon.Symbol
                        ? (SolidColorBrush)FindResource("CardBorderBrush")
                        : System.Windows.Media.Brushes.Transparent;
                };
                btn.MouseLeftButtonDown += (_, _) =>
                {
                    currentIcon.Symbol = symbolCapture;
                    saveIconColor();
                };
                wrapPanel.Children.Add(btn);
            }
            outerPanel.Children.Add(wrapPanel);
        }

        // ── Window ──
        var popupBorder = new System.Windows.Controls.Border
        {
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = new System.Windows.Controls.ScrollViewer
            {
                Content = outerPanel,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                MaxHeight = 500
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, Opacity = 0.6, ShadowDepth = 6
            }
        };

        var screenPos = ProfileButton.PointToScreen(new Point(ProfileButton.ActualWidth + 4, 0));
        var dpiSource = PresentationSource.FromVisual(ProfileButton);
        if (dpiSource?.CompositionTarget != null)
        {
            var dpiX = dpiSource.CompositionTarget.TransformToDevice.M11;
            var dpiY = dpiSource.CompositionTarget.TransformToDevice.M22;
            screenPos = new Point(screenPos.X / dpiX, screenPos.Y / dpiY);
        }

        editorWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            Content = popupBorder,
            Left = screenPos.X,
            Top = screenPos.Y
        };
        editorWindow.Deactivated += (_, _) => closeEditor();
        editorWindow.KeyDown += (_, e) => { if (e.Key == Key.Escape) closeEditor(); };
        editorWindow.Show();
        nameBox.Focus();
    }

    private void RenameProfile(string oldName, string newName)
    {
        // Update profiles list
        int idx = _config.Profiles.IndexOf(oldName);
        if (idx >= 0) _config.Profiles[idx] = newName;

        // Move icon config
        if (_config.ProfileIcons.TryGetValue(oldName, out var iconCfg))
        {
            _config.ProfileIcons.Remove(oldName);
            _config.ProfileIcons[newName] = iconCfg;
        }

        // Update active profile if it was the renamed one
        if (_config.ActiveProfile == oldName)
            _config.ActiveProfile = newName;

        // Rename profile file
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmpUp");

        string SafeName(string n) => string.Concat(n.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));

        var oldPath = System.IO.Path.Combine(configDir, $"profile_{SafeName(oldName)}.json");
        var newPath = System.IO.Path.Combine(configDir, $"profile_{SafeName(newName)}.json");

        try
        {
            if (System.IO.File.Exists(oldPath) && !System.IO.File.Exists(newPath))
                System.IO.File.Move(oldPath, newPath);
        }
        catch { }
    }

    private void DuplicateProfile(string sourceProfileName)
    {
        // Generate unique name
        string baseName = sourceProfileName + " Copy";
        string newName = baseName;
        int counter = 2;
        while (_config.Profiles.Contains(newName))
        {
            newName = $"{baseName} {counter}";
            counter++;
        }

        // Copy profile data
        var sourceConfig = ConfigManager.LoadProfile(sourceProfileName);
        if (sourceConfig == null) sourceConfig = new AppConfig();

        _config.Profiles.Add(newName);
        _config.ProfileIcons[newName] = new ProfileIconConfig
        {
            Symbol = (_config.ProfileIcons.GetValueOrDefault(sourceProfileName) ?? new ProfileIconConfig()).Symbol,
            Color = (_config.ProfileIcons.GetValueOrDefault(sourceProfileName) ?? new ProfileIconConfig()).Color
        };

        // Save the duplicated profile
        sourceConfig.ActiveProfile = newName;
        ConfigManager.SaveProfile(sourceConfig, newName);

        _onConfigChanged?.Invoke(_config);
        UpdateProfileButton();
        RefreshViews();
    }

    private void MoveProfile(string profileName, int direction)
    {
        int idx = _config.Profiles.IndexOf(profileName);
        int newIdx = idx + direction;
        if (idx < 0 || newIdx < 0 || newIdx >= _config.Profiles.Count) return;

        _config.Profiles.RemoveAt(idx);
        _config.Profiles.Insert(newIdx, profileName);

        _onConfigChanged?.Invoke(_config);
        RefreshViews();
    }

    private void ShowIconPicker(string profileName)
    {
        // Close profile popup, show icon picker popup
        CloseProfileFlyout();

        var currentIcon = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();

        Window? iconPopupWindow = null;
        Action closeIconPopup = () => { iconPopupWindow?.Close(); iconPopupWindow = null; };

        var outerPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(8) };

        // ── Color swatches at top ──
        var colorLabel = new System.Windows.Controls.TextBlock
        {
            Text = "COLOR",
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            Margin = new Thickness(2, 0, 0, 6)
        };
        outerPanel.Children.Add(colorLabel);

        var colorWrap = new System.Windows.Controls.WrapPanel { Width = 280 };
        string selectedColor = currentIcon.Color;

        // We'll track all icon elements so we can update their color when user picks a new one
        var allIconElements = new List<MiIcon>();

        foreach (var (name, hex) in ProfileIconColors)
        {
            var colorHex = hex;
            var swatch = new System.Windows.Controls.Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Cursor = Cursors.Hand,
                Margin = new Thickness(2),
                ToolTip = name,
                BorderThickness = new Thickness(colorHex == selectedColor ? 2 : 0),
                BorderBrush = new SolidColorBrush(Colors.White)
            };
            try { swatch.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); } catch { }

            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = colorHex;
                _config.ProfileIcons[profileName] = new ProfileIconConfig
                {
                    Symbol = _config.ProfileIcons.GetValueOrDefault(profileName)?.Symbol ?? "VolumeHigh",
                    Color = colorHex
                };
                _onConfigChanged?.Invoke(_config);
                UpdateProfileButton();

                // Update all icon previews in the popup to show the new color
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                foreach (var mi in allIconElements) mi.Foreground = brush;

                // Update swatch borders
                foreach (System.Windows.Controls.Border child in colorWrap.Children)
                {
                    child.BorderThickness = new Thickness(0);
                }
                swatch.BorderThickness = new Thickness(2);
            };
            colorWrap.Children.Add(swatch);
        }
        outerPanel.Children.Add(colorWrap);

        // ── Icon categories ──
        bool first = true;
        foreach (var (category, symbols) in ProfileIconCategories)
        {
            var header = new System.Windows.Controls.TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
                Margin = new Thickness(2, first ? 10 : 8, 0, 4)
            };
            first = false;
            outerPanel.Children.Add(header);

            var wrapPanel = new System.Windows.Controls.WrapPanel { Width = 280 };
            foreach (var symName in symbols)
            {
                var symbolCapture = symName;
                if (!Enum.TryParse<MaterialIconKind>(symName, out var parsedKind))
                    continue;

                var iconEl = new MiIcon
                {
                    Kind = parsedKind,
                    Width = 18,
                    Height = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                try { iconEl.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectedColor)); } catch { }
                allIconElements.Add(iconEl);

                var btn = new System.Windows.Controls.Border
                {
                    Width = 34, Height = 34,
                    CornerRadius = new CornerRadius(6),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(1),
                    Child = iconEl,
                    Background = symbolCapture == currentIcon.Symbol
                        ? (SolidColorBrush)FindResource("CardBorderBrush")
                        : System.Windows.Media.Brushes.Transparent
                };
                btn.MouseEnter += (_, _) => btn.Background = (SolidColorBrush)FindResource("CardBorderBrush");
                btn.MouseLeave += (_, _) =>
                {
                    var cur = _config.ProfileIcons.GetValueOrDefault(profileName)?.Symbol ?? "";
                    btn.Background = symbolCapture == cur
                        ? (SolidColorBrush)FindResource("CardBorderBrush")
                        : System.Windows.Media.Brushes.Transparent;
                };
                btn.MouseLeftButtonDown += (_, _) =>
                {
                    _config.ProfileIcons[profileName] = new ProfileIconConfig
                    {
                        Symbol = symbolCapture,
                        Color = selectedColor
                    };
                    _onConfigChanged?.Invoke(_config);
                    UpdateProfileButton();
                    closeIconPopup();
                };
                wrapPanel.Children.Add(btn);
            }
            outerPanel.Children.Add(wrapPanel);
        }

        var popupBorder = new System.Windows.Controls.Border
        {
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = new System.Windows.Controls.ScrollViewer
            {
                Content = outerPanel,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                MaxHeight = 460
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, Opacity = 0.6, ShadowDepth = 6
            }
        };

        // Position to the right of the ProfileButton
        var screenPos = ProfileButton.PointToScreen(new Point(ProfileButton.ActualWidth + 4, 0));
        var dpiSource2 = PresentationSource.FromVisual(ProfileButton);
        if (dpiSource2?.CompositionTarget != null)
        {
            var dpiX = dpiSource2.CompositionTarget.TransformToDevice.M11;
            var dpiY = dpiSource2.CompositionTarget.TransformToDevice.M22;
            screenPos = new Point(screenPos.X / dpiX, screenPos.Y / dpiY);
        }

        iconPopupWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = false,
            Background = (System.Windows.Media.Brush)FindResource("BgDarkBrush"),
            Content = popupBorder,
            Left = screenPos.X,
            Top = screenPos.Y
        };
        iconPopupWindow.Deactivated += (_, _) => closeIconPopup();
        iconPopupWindow.KeyDown += (_, e) => { if (e.Key == Key.Escape) closeIconPopup(); };
        iconPopupWindow.Show();
    }

    /// <summary>
    /// Carry over global settings from current config to a loaded profile.
    /// </summary>
    private void PreserveGlobalSettings(AppConfig loaded)
    {
        loaded.Osd = _config.Osd;
        loaded.Serial = _config.Serial;
        loaded.StartWithWindows = _config.StartWithWindows;
        loaded.HomeAssistant = _config.HomeAssistant;
        loaded.Ambience = _config.Ambience;
        loaded.Profiles = _config.Profiles;
        loaded.ProfileIcons = _config.ProfileIcons;
        loaded.Groups = _config.Groups;
    }

    public void SetAmbienceSync(AmbienceSync sync)
    {
        _ambienceView.SetSync(sync);
        _ambienceView.NavigateToSettings = () => NavigateTo(_settingsView, NavSettings);
        _buttonsView.SetAmbienceSync(sync);
        _settingsView.SetAmbienceSync(sync);
    }

    public void SetDreamSync(DreamSyncController? dreamSync)
    {
        if (dreamSync != null)
            _ambienceView.SetDreamSync(dreamSync);
    }

    public void SetCorsairSync(CorsairSync corsairSync)
    {
        _ambienceView.SetCorsairSync(corsairSync);
        _groupsView.SetCorsairSync(corsairSync);
        _settingsView.SetCorsairSync(corsairSync);
    }

    public void SetLgMonitor(LgMonitorSync lgMonitor)
    {
        _ambienceView.SetLgMonitor(lgMonitor);
    }

    public RoomView? GetRoomView() => _ambienceView;
    public ButtonsView? GetButtonsView() => _buttonsView;

    public void SetHAIntegration(HAIntegration? ha)
    {
        _ambienceView.SetHAIntegration(ha);
        _groupsView.SetHAIntegration(ha);
    }

    public void UpdateGoveeDeviceBrightness(string? ip, float normalized, bool poweredOn)
    {
        if (ip != null)
            _ambienceView.UpdateDeviceBrightness(ip, normalized, poweredOn);
        else
            _ambienceView.UpdateAllDeviceBrightness(normalized, poweredOn);
    }

    private void SwitchToProfile(string profileName)
    {
        // Save current profile before switching
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);

        var loaded = ConfigManager.LoadProfile(profileName);
        if (loaded != null)
        {
            loaded.ActiveProfile = profileName;
            PreserveGlobalSettings(loaded);
            _config = loaded;
        }
        else
        {
            _config.ActiveProfile = profileName;
        }

        _onConfigChanged?.Invoke(_config);
        UpdateProfileButton();
        RefreshViews();
    }

    private void AddNewProfile()
    {
        var name = GlassDialog.Prompt("Enter profile name:", "NEW PROFILE", owner: this);
        if (!string.IsNullOrWhiteSpace(name))
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (_config.Profiles.Contains(name))
            {
                GlassDialog.ShowWarning($"Profile \"{name}\" already exists.", owner: this);
                return;
            }

            _config.Profiles.Add(name);
            _config.ProfileIcons[name] = new ProfileIconConfig();
            _config.ActiveProfile = name;
            _onConfigChanged?.Invoke(_config);
            ConfigManager.SaveProfile(_config, name);
            UpdateProfileButton();
            RefreshViews();
        }
    }

    private void DeleteProfile(string profileName)
    {
        CloseProfileFlyout();

        if (!GlassDialog.Confirm($"Delete profile \"{profileName}\"?", "DELETE PROFILE", dangerYes: true, owner: this))
            return;

        _config.Profiles.Remove(profileName);
        _config.ProfileIcons.Remove(profileName);

        // Delete profile file
        var configDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AmpUp");
        var safe = string.Concat(profileName.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        var path = System.IO.Path.Combine(configDir, $"profile_{safe}.json");
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }

        // Switch to Default if we deleted the active profile
        if (_config.ActiveProfile == profileName)
            SwitchToProfile("Default");
        else
        {
            _onConfigChanged?.Invoke(_config);
            UpdateProfileButton();
        }
    }

    /// <summary>
    /// Called externally when profiles list changes (new/delete from Settings).
    /// </summary>
    public void RefreshProfilePicker()
    {
        UpdateProfileButton();
    }

    /// <summary>
    /// Forward an immediate knob position update to the MixerView (bypasses the 50ms poll).
    /// </summary>
    public void UpdateKnobPosition(int idx, float position)
    {
        _mixerView.UpdateKnobPosition(idx, position);
    }

    public void SetConnectionStatus(bool connected, string? portName = null)
    {
        Dispatcher.Invoke(() =>
        {
            _turnUpConnected = connected;
            ConnectionDot.Fill = connected
                ? (SolidColorBrush)FindResource("SuccessGrnBrush")
                : (SolidColorBrush)FindResource("TextDimBrush");
            ConnectionLabel.Text = connected ? "Connected" : "Disconnected";
            _settingsView.UpdateConnectionStatus(connected, portName);
            HwPreview.SetConnected(connected);

            // Glow the dot when connected
            ConnectionDotGlow.BlurRadius = connected ? 8 : 0;
            ConnectionDotGlow.Opacity = connected ? 0.5 : 0;

            var pulse = (System.Windows.Media.Animation.Storyboard)FindResource("PulseAnimation");
            if (connected)
                pulse.Begin(this, true);
            else
            {
                pulse.Stop(this);
                ConnectionDot.Opacity = 1.0;
            }

            if (_config.HardwareMode == HardwareMode.Auto)
                RefreshViews();
            else
                _settingsView.RefreshActiveSurfaceVisibility();
        });
    }

    public void SetN3ConnectionStatus(bool connected, string? deviceName = null)
    {
        Dispatcher.Invoke(() =>
        {
            _streamControllerConnected = connected;
            _settingsView.UpdateN3ConnectionStatus(connected, deviceName);
            if (_config.HardwareMode == HardwareMode.Auto)
                RefreshViews();
            else
                _settingsView.RefreshActiveSurfaceVisibility();
        });
    }
}
