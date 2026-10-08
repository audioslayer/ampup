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
    private volatile bool _windowActive = true; // HW preview strip is on-screen; safe to read from any thread
    private long _lastHwPreviewLedFrameTick;
    private bool _pulseRunning; // PulseAnimation storyboard begun (connected state)
    private DeviceSurface? _lastRenderedSurface; // surface the views were last rendered with

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
            BlurRadius = 10,
            Opacity = 0.25,
            ShadowDepth = 0
        };

        // Connection dot glow
        ConnectionDotGlow.Color = ThemeManager.Accent;

        // Active sidebar pill tint
        RefreshNavAccent();
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

        // 100ms timer for preview VU smoothing + position updates.
        // The preview is small and always visible while the window is open,
        // so avoiding 20 FPS WASAPI peak reads trims idle CPU without
        // affecting actual knob/volume handling.
        _hwPreviewTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _hwPreviewTimer.Tick += HwPreviewTimer_Tick;

        // Stop the timer when minimized or hidden to tray — WPF DispatcherTimers
        // keep firing even when the window is minimized, causing unnecessary WASAPI
        // peak calls + rendering with nothing visible. The strip itself is also
        // Collapsed in MainWindow.xaml; while it is, the timer (5 WASAPI peak
        // reads per tick) and LED-frame dispatches are pure waste, so gate on
        // the strip's own visibility too.
        StateChanged += (_, _) => UpdateHwPreviewActivity();
        IsVisibleChanged += (_, _) => UpdateHwPreviewActivity();
        HwPreview.IsVisibleChanged += (_, _) => UpdateHwPreviewActivity();
        UpdateHwPreviewActivity();
    }

    private void UpdateHwPreviewActivity()
    {
        bool windowShown = IsVisible && WindowState != WindowState.Minimized;
        // Pulse pause tracks the window only (the dot lives in the title bar).
        SetPulsePaused(!windowShown);
        ScheduleHiddenTrim(!windowShown);

        bool previewLive = windowShown && HwPreview.IsVisible;
        _windowActive = previewLive;
        if (_hwPreviewTimer == null) return;
        if (previewLive)
        {
            if (!_hwPreviewTimer.IsEnabled) _hwPreviewTimer.Start();
        }
        else
        {
            _hwPreviewTimer.Stop();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _hiddenTrimTimer;
    private bool _trimmedWhileHidden;

    /// <summary>
    /// Once the window has stayed hidden/minimized for a few seconds (e.g. sent
    /// to tray while gaming), compact the managed heap and release the working
    /// set back to the OS one time. Pages touched again will fault back in.
    /// </summary>
    private void ScheduleHiddenTrim(bool hidden)
    {
        if (!hidden)
        {
            _hiddenTrimTimer?.Stop();
            _trimmedWhileHidden = false;
            return;
        }
        if (_trimmedWhileHidden) return;
        if (_hiddenTrimTimer == null)
        {
            _hiddenTrimTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _hiddenTrimTimer.Tick += (_, _) =>
            {
                _hiddenTrimTimer!.Stop();
                if (IsVisible && WindowState != WindowState.Minimized) return;
                _trimmedWhileHidden = true;
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    using var proc = System.Diagnostics.Process.GetCurrentProcess();
                    EmptyWorkingSet(proc.Handle);
                }
                catch { }
            };
        }
        _hiddenTrimTimer.Stop();
        _hiddenTrimTimer.Start();
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>
    /// Pause/resume the connection-dot pulse storyboard alongside the window
    /// visibility hooks above — Hide() to tray never raises Unloaded, so a
    /// Forever storyboard would otherwise keep its clock running 24/7.
    /// Only acts when the storyboard is actually running (connected state).
    /// </summary>
    private void SetPulsePaused(bool paused)
    {
        if (!_pulseRunning) return;
        var pulse = (System.Windows.Media.Animation.Storyboard)FindResource("PulseAnimation");
        if (paused)
            pulse.Pause(this);
        else
            pulse.Resume(this);
    }

    private void OnRgbFrameReady(byte[] frame)
    {
        // Called from RgbController thread — _windowActive is volatile, safe to read here.
        // Don't touch any WPF dependency properties (e.g. WindowState, IsVisible) from this thread.
        if (!_windowActive) return;
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastHwPreviewLedFrameTick);
        if (now - last < 100) return;
        Interlocked.Exchange(ref _lastHwPreviewLedFrameTick, now);

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
        _lastRenderedSurface = GetEffectiveDeviceSurface();
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
        _groupsView.SetMixer(_mixer);
        _groupsView.OnAppGroupsChanged = () => _mixerView.RefreshAppGroups();
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

    /// <summary>Refresh only controls whose choices depend on Windows audio endpoints.</summary>
    public void RefreshAudioDeviceViews()
    {
        if (_mixer == null) return;

        _buttonsView.RefreshAudioDevices(_mixer);
        _mixerView.RefreshAudioDevices();
        _lightsView.RefreshAudioDevices(_mixer);
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
        bool importIntoDefault = _config.Profiles.Count == 1
            && string.Equals(_config.Profiles[0], "Default", StringComparison.OrdinalIgnoreCase)
            && ConfigManager.IsTurnUpProfileEmpty(_config);

        var wizard = new ImportWizardWindow
        {
            Owner = this,
            ExistingProfileNames = _config.Profiles.ToList(),
            ImportIntoDefault = importIntoDefault,
        };
        wizard.ShowDialog();

        if (wizard.ImportedProfileName != null)
        {
            var profileName = wizard.ImportedProfileName;

            if (ConfigManager.IsProfileNameAvailable(_config.Profiles, profileName))
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

    // ── Sidebar nav ─────────────────────────────────────────────────
    // Each item: outline icon when idle, filled icon when active (where MDI has a pair).
    private sealed record NavItem(System.Windows.Controls.Button Button, System.Windows.Controls.Border Bar,
        System.Windows.Controls.Border Pill, MiIcon Icon, System.Windows.Controls.TextBlock Label,
        MaterialIconKind Idle, MaterialIconKind Active);

    private Dictionary<System.Windows.Controls.Button, NavItem>? _navItems;

    private Dictionary<System.Windows.Controls.Button, NavItem> NavItems => _navItems ??= new()
    {
        { NavMixer,    new(NavMixer,    NavMixerBar,    NavMixerPill,    NavMixerIcon,    NavMixerLabel,    MaterialIconKind.TuneVertical,         MaterialIconKind.TuneVerticalVariant) },
        { NavButtons,  new(NavButtons,  NavButtonsBar,  NavButtonsPill,  NavButtonsIcon,  NavButtonsLabel,  MaterialIconKind.GestureTapButton,     MaterialIconKind.GestureTapButton) },
        { NavLights,   new(NavLights,   NavLightsBar,   NavLightsPill,   NavLightsIcon,   NavLightsLabel,   MaterialIconKind.LightbulbOutline,     MaterialIconKind.Lightbulb) },
        { NavAmbience, new(NavAmbience, NavAmbienceBar, NavAmbiencePill, NavAmbienceIcon, NavAmbienceLabel, MaterialIconKind.SofaOutline,          MaterialIconKind.Sofa) },
        { NavOsd,      new(NavOsd,      NavOsdBar,      NavOsdPill,      NavOsdIcon,      NavOsdLabel,      MaterialIconKind.MonitorDashboard,     MaterialIconKind.MonitorDashboard) },
        { NavGroups,   new(NavGroups,   NavGroupsBar,   NavGroupsPill,   NavGroupsIcon,   NavGroupsLabel,   MaterialIconKind.VectorLink,           MaterialIconKind.VectorLink) },
        { NavBindings, new(NavBindings, NavBindingsBar, NavBindingsPill, NavBindingsIcon, NavBindingsLabel, MaterialIconKind.ViewDashboardOutline, MaterialIconKind.ViewDashboard) },
        { NavSettings, new(NavSettings, NavSettingsBar, NavSettingsPill, NavSettingsIcon, NavSettingsLabel, MaterialIconKind.CogOutline,           MaterialIconKind.Cog) },
    };

    private bool _navHoverWired;

    private void WireNavHover()
    {
        if (_navHoverWired) return;
        _navHoverWired = true;
        foreach (var item in NavItems.Values)
        {
            var it = item;
            it.Button.MouseEnter += (_, _) => AnimateNavIconScale(it, 1.12);
            it.Button.MouseLeave += (_, _) => AnimateNavIconScale(it, 1.0);
        }
    }

    private static void AnimateNavIconScale(NavItem item, double to)
    {
        if (item.Icon.RenderTransform is not ScaleTransform st || st.IsFrozen)
            item.Icon.RenderTransform = st = new ScaleTransform();
        var anim = new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(120))
        { EasingFunction = new System.Windows.Media.Animation.QuadraticEase() };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    // Active row: accent gradient tint, strong on the left fading to the right.
    private static LinearGradientBrush NavPillBrush()
    {
        var b = new LinearGradientBrush(
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x33),
            ThemeManager.WithAlpha(ThemeManager.Accent, 0x0D),
            new Point(0, 0.5), new Point(1, 0.5));
        b.Freeze();
        return b;
    }

    private void ApplyNavItemState(NavItem item, bool active, bool animate)
    {
        item.Icon.Kind = active ? item.Active : item.Idle;
        if (active)
        {
            item.Icon.SetResourceReference(MiIcon.ForegroundProperty, "AccentBrush");
            item.Label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimaryBrush");
            item.Pill.Background = NavPillBrush();
        }
        else
        {
            item.Icon.SetResourceReference(MiIcon.ForegroundProperty, "TextSecBrush");
            item.Label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecBrush");
            item.Pill.Background = Brushes.Transparent;
        }

        // Indicator bar: grows from the centre + fades (cheap, layout-only on one element)
        double h = active ? 22 : 0, o = active ? 1 : 0;
        if (animate)
        {
            var dur = TimeSpan.FromMilliseconds(180);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            item.Bar.BeginAnimation(HeightProperty, new System.Windows.Media.Animation.DoubleAnimation(h, dur) { EasingFunction = ease });
            item.Bar.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(o, dur));
        }
        else
        {
            item.Bar.BeginAnimation(HeightProperty, null);
            item.Bar.BeginAnimation(OpacityProperty, null);
            item.Bar.Height = h;
            item.Bar.Opacity = o;
        }
    }

    private void RefreshNavAccent()
    {
        if (_activeNavButton != null && NavItems.TryGetValue(_activeNavButton, out var item))
            item.Pill.Background = NavPillBrush();
    }

    // ── Expand-on-hover sidebar ─────────────────────────────────────
    // Collapsed: 64px icon rail. Expanded: 200px overlay (spans the content column via
    // ColumnSpan + ZIndex, so the content never reflows) with labels, captions, profile card.
    private const double SidebarCollapsedWidth = 64, SidebarExpandedWidth = 200;
    private bool _sidebarExpanded;
    private System.Windows.Threading.DispatcherTimer? _sidebarCollapseTimer;

    /// <summary>
    /// Keyboard focus only keeps the sidebar open when the user is actually using the keyboard
    /// (Tab/arrow navigation). A mouse click also moves focus into the sidebar, which used to keep
    /// it expanded after clicking a page until focus happened to move elsewhere.
    /// </summary>
    private bool SidebarHeldByKeyboard =>
        SidebarPanel.IsKeyboardFocusWithin && InputManager.Current.MostRecentInputDevice is KeyboardDevice;

    private void SidebarPanel_MouseEnter(object sender, MouseEventArgs e) => ExpandSidebar();

    private void SidebarPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_profileFlyoutOpen || SidebarHeldByKeyboard) return;
        ScheduleSidebarCollapse();
    }

    private void SidebarPanel_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && InputManager.Current.MostRecentInputDevice is KeyboardDevice) ExpandSidebar();
        else if (!SidebarPanel.IsMouseOver && !_profileFlyoutOpen) ScheduleSidebarCollapse();
    }

    private void ScheduleSidebarCollapse()
    {
        _sidebarCollapseTimer ??= CreateSidebarCollapseTimer();
        _sidebarCollapseTimer.Stop();
        _sidebarCollapseTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateSidebarCollapseTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_profileFlyoutOpen || SidebarPanel.IsMouseOver || SidebarHeldByKeyboard) return;
            SetSidebarExpanded(false);
        };
        return timer;
    }

    private void ExpandSidebar()
    {
        _sidebarCollapseTimer?.Stop();
        SetSidebarExpanded(true);
    }

    private void SetSidebarExpanded(bool expanded)
    {
        if (_sidebarExpanded == expanded) return;
        _sidebarExpanded = expanded;

        bool animate = SystemParameters.ClientAreaAnimation && IsLoaded;
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var widthDur = TimeSpan.FromMilliseconds(180);
        var fadeDur = TimeSpan.FromMilliseconds(expanded ? 160 : 90);
        double width = expanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
        double show = expanded ? 1 : 0, hide = expanded ? 0 : 1;

        void Animate(UIElement el, DependencyProperty prop, double to, TimeSpan dur, TimeSpan? delay = null)
        {
            if (!animate)
            {
                el.BeginAnimation(prop, null);
                el.SetValue(prop, to);
                return;
            }
            el.BeginAnimation(prop, new System.Windows.Media.Animation.DoubleAnimation(to, dur)
            {
                EasingFunction = ease,
                BeginTime = delay ?? TimeSpan.Zero
            });
        }

        Animate(SidebarPanel, WidthProperty, width, widthDur);

        // Labels fade in slightly after the width starts growing; fade out immediately.
        var labelDelay = expanded ? TimeSpan.FromMilliseconds(40) : TimeSpan.Zero;
        foreach (var item in NavItems.Values)
            Animate(item.Label, OpacityProperty, show, fadeDur, labelDelay);
        foreach (var el in new UIElement[] { CapHardware, CapLighting, CapApp, ProfileCardBg, ProfileCardText, ProfileCardChevron })
            Animate(el, OpacityProperty, show, fadeDur, labelDelay);
        foreach (var el in new UIElement[] { SepHardware, SepLighting, SepApp })
            Animate(el, OpacityProperty, hide, fadeDur);

        // Soft right-edge shadow only while overlaying the content
        if (expanded)
        {
            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Direction = 0, Opacity = 0
            };
            SidebarPanel.Effect = shadow;
            if (animate)
                shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.45, widthDur));
            else
                shadow.Opacity = 0.45;
        }
        else
        {
            SidebarPanel.Effect = null;
        }
    }

    private void NavigateTo(System.Windows.Controls.UserControl view, System.Windows.Controls.Button navButton)
    {
        ContentArea.Content = view;
        WireNavHover();

        bool animate = IsLoaded;
        if (_activeNavButton != null && _activeNavButton != navButton
            && NavItems.TryGetValue(_activeNavButton, out var old))
            ApplyNavItemState(old, false, animate);

        if (NavItems.TryGetValue(navButton, out var item))
        {
            ApplyNavItemState(item, true, animate && _activeNavButton != navButton);
            _activeNavBar = item.Bar;
        }

        _activeNavButton = navButton;
    }

    // ── Window drag ─────────────────────────────────────────────────

    private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to toggle maximize
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
        }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                if (WindowState == WindowState.Maximized)
                    RestoreMaximizedWindowForDrag(e);

                DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException) { }
        }
    }

    private void RestoreMaximizedWindowForDrag(MouseButtonEventArgs e)
    {
        var mouseInWindow = e.GetPosition(this);
        var mouseOnScreen = PointToScreen(mouseInWindow);
        var source = System.Windows.PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            mouseOnScreen = source.CompositionTarget.TransformFromDevice.Transform(mouseOnScreen);

        double restoredWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Math.Max(ActualWidth * 0.8, 900);
        double restoredHeight = RestoreBounds.Height > 0 ? RestoreBounds.Height : Math.Max(ActualHeight * 0.8, 650);
        double xRatio = ActualWidth > 0 ? mouseInWindow.X / ActualWidth : 0.5;

        WindowState = WindowState.Normal;
        Width = restoredWidth;
        Height = restoredHeight;
        Left = mouseOnScreen.X - restoredWidth * Math.Clamp(xRatio, 0.05, 0.95);
        Top = mouseOnScreen.Y - Math.Min(mouseInWindow.Y, 36);
    }

    // ── Traffic-light window buttons ──────────────────────────────

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
        e.Handled = true;
    }

    private void BtnMaximize_Click(object sender, MouseButtonEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        e.Handled = true;
    }

    private void BtnClose_Click(object sender, MouseButtonEventArgs e)
    {
        Close(); // triggers MainWindow_Closing → hides to tray
    }

    private bool _checkingUpdate;
    private UpdateInfo? _pendingUpdate;

    private async Task CheckForUpdateOnStartup()
    {
        if (_config.AutoCheckUpdates != true) return;

        try
        {
            var update = await UpdateChecker.CheckForUpdateAsync();
            if (update != null)
            {
                _pendingUpdate = update;
                VersionLabel.Text = $"Update available: {update.Tag}";
                VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");
                // Notify tray popup
                if (Application.Current is App app)
                    app.NotifyUpdateAvailable(update);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Startup update check failed: {ex.Message}");
        }
    }

    private async void VersionLabel_Click(object sender, MouseButtonEventArgs e)
    {
        await PromptToInstallUpdateAsync();
    }

    public async Task PromptToInstallUpdateAsync(UpdateInfo? knownUpdate = null)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;

        if (knownUpdate == null && _pendingUpdate == null)
            VersionLabel.Text = "Checking...";
        VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");

        try
        {
            var update = knownUpdate ?? _pendingUpdate ?? await UpdateChecker.CheckForUpdateAsync();
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
                _pendingUpdate = update;
                VersionLabel.Text = $"Update available: {update.Tag}";
                if (GlassDialog.Confirm(
                    $"Amp Up {update.Tag} is available. Download it, install it, and restart Amp Up now?",
                    "UPDATE", owner: this))
                {
                    VersionLabel.Text = "Downloading...";
                    await UpdateChecker.DownloadAndInstallAsync(update, progress =>
                    {
                        Dispatcher.Invoke(() => VersionLabel.Text = $"Downloading {progress}%");
                    });
                    _pendingUpdate = null;
                }
                else
                {
                    VersionLabel.Text = $"Update available: {update.Tag}";
                    VersionLabel.Foreground = (SolidColorBrush)FindResource("AccentBrush");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check error: {ex.Message}");
            VersionLabel.Text = "Update failed";
            VersionLabel.Foreground = (SolidColorBrush)FindResource("DangerRedBrush");
            await Task.Delay(2000);
            VersionLabel.Text = _pendingUpdate != null
                ? $"Update available: {_pendingUpdate.Tag}"
                : $"v{UpdateChecker.CurrentVersion}";
            VersionLabel.Foreground = (SolidColorBrush)FindResource(
                _pendingUpdate != null ? "AccentBrush" : "TextDimBrush");
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
        // Read from the dedicated PreferredSurface field — this is the user's
        // choice that survives device-connect events at startup. Buttons /
        // Mixer / Lights on TabSelection reflect the currently-applied
        // effective surface and get rewritten by the auto-detect pathway.
        return _config.TabSelection.PreferredSurface;
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
            HardwareMode.Auto when _turnUpConnected && _streamControllerConnected => DeviceSurface.TurnUp,
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
            Padding = new Thickness(8, 10, 8, 8),
            MinWidth = 240,
            Child = ProfilePopupPanel
        };
        ProfilePopupPanel.Visibility = System.Windows.Visibility.Visible;

        var screenPos = ProfileCard.PointToScreen(new Point(ProfileCard.ActualWidth + 8, 0));
        var dpiSource = PresentationSource.FromVisual(ProfileCard);
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
        ExpandSidebar();
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

        // Collapse cleanly if the pointer already left the bar while the flyout was open
        if (!SidebarPanel.IsMouseOver && !SidebarHeldByKeyboard)
            ScheduleSidebarCollapse();
    }

    private void BuildProfileFlyout()
    {
        ProfilePopupPanel.Children.Clear();

        // Header — same caption style as the sidebar's HARDWARE / LIGHTING labels
        var header = new System.Windows.Controls.Grid { Margin = new Thickness(10, 2, 10, 6) };
        var caption = new System.Windows.Controls.TextBlock
        {
            Text = "PROFILES",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
        };
        header.Children.Add(caption);
        header.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = _config.Profiles.Count.ToString(),
            FontSize = 10,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        ProfilePopupPanel.Children.Add(header);

        foreach (var profile in _config.Profiles)
            ProfilePopupPanel.Children.Add(BuildProfileFlyoutRow(profile));

        ProfilePopupPanel.Children.Add(new System.Windows.Controls.Border
        {
            Height = 1,
            Background = (SolidColorBrush)FindResource("CardBorderBrush"),
            Margin = new Thickness(10, 6, 10, 6)
        });

        // New profile — nav-style row with a dashed icon tile
        var plusTile = new System.Windows.Controls.Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            Margin = new Thickness(6, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new MiIcon { Kind = MaterialIconKind.Plus, Width = 16, Height = 16,
                Foreground = (SolidColorBrush)FindResource("TextSecBrush") },
        };
        var addLabel = new System.Windows.Controls.TextBlock
        {
            Text = "New Profile",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        var addRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        addRow.Children.Add(plusTile);
        addRow.Children.Add(addLabel);
        var addBorder = new System.Windows.Controls.Border
        {
            Height = 40,
            CornerRadius = new CornerRadius(10),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = addRow
        };
        addBorder.MouseEnter += (_, _) =>
        {
            addBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentHover1ABrush");
            ((MiIcon)plusTile.Child).SetResourceReference(MiIcon.ForegroundProperty, "AccentBrush");
            plusTile.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "AccentBrush");
            addLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimaryBrush");
        };
        addBorder.MouseLeave += (_, _) =>
        {
            addBorder.Background = Brushes.Transparent;
            ((MiIcon)plusTile.Child).SetResourceReference(MiIcon.ForegroundProperty, "TextSecBrush");
            plusTile.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardBorderBrush");
            addLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecBrush");
        };
        addBorder.MouseLeftButtonDown += (_, _) =>
        {
            CloseProfileFlyout();
            AddNewProfile();
        };
        ProfilePopupPanel.Children.Add(addBorder);
    }

    /// <summary>
    /// One profile row, styled like a sidebar nav item: 40px rounded pill, accent gradient +
    /// left accent bar when active, accent tint on hover. Edit / delete appear on hover.
    /// </summary>
    private FrameworkElement BuildProfileFlyoutRow(string profile)
    {
        bool isActive = profile == _config.ActiveProfile;
        var iconCfg = _config.ProfileIcons.GetValueOrDefault(profile) ?? new ProfileIconConfig();
        Color tint = ThemeManager.Accent;
        try { tint = (Color)ColorConverter.ConvertFromString(iconCfg.Color); } catch { }

        var row = new System.Windows.Controls.Grid();
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        // Icon tile in the profile's colour — click to change the icon
        var icon = new MiIcon { Width = 16, Height = 16, Foreground = new SolidColorBrush(tint) };
        if (Enum.TryParse<MaterialIconKind>(iconCfg.Symbol, out var iconKind))
            icon.Kind = iconKind;
        var tile = new System.Windows.Controls.Border
        {
            Width = 28, Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(tint, 0x22)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(tint, isActive ? (byte)0x88 : (byte)0x44)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Change icon",
            Child = icon,
        };
        tile.MouseLeftButtonDown += (_, ev) => { ev.Handled = true; ShowIconPicker(profile); };
        row.Children.Add(tile);

        var nameBlock = new System.Windows.Controls.TextBlock
        {
            Text = profile,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        nameBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            isActive ? "TextPrimaryBrush" : "TextSecBrush");
        System.Windows.Controls.Grid.SetColumn(nameBlock, 1);
        row.Children.Add(nameBlock);

        // Hover-only actions
        var actions = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
            Opacity = 0,
        };
        actions.Children.Add(MakeFlyoutAction(MaterialIconKind.PencilOutline, "Edit profile", false, () =>
        {
            CloseProfileFlyout();
            ShowProfileEditor(profile);
        }));
        if (!string.Equals(profile, "Default", StringComparison.OrdinalIgnoreCase))
            actions.Children.Add(MakeFlyoutAction(MaterialIconKind.DeleteOutline, "Delete profile", true,
                () => DeleteProfile(profile)));
        System.Windows.Controls.Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        var pill = new System.Windows.Controls.Border
        {
            CornerRadius = new CornerRadius(10),
            IsHitTestVisible = false,
            Background = isActive ? NavPillBrush() : Brushes.Transparent,
        };
        var bar = new System.Windows.Controls.Border
        {
            Width = 3, Height = 18,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = isActive ? Visibility.Visible : Visibility.Collapsed,
        };
        bar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentBrush");

        var host = new System.Windows.Controls.Grid();
        host.Children.Add(pill);
        host.Children.Add(bar);
        host.Children.Add(row);

        var rowBorder = new System.Windows.Controls.Border
        {
            Height = 40,
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(10),
            Cursor = isActive ? Cursors.Arrow : Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = isActive ? null : $"Switch to {profile}",
            Child = host
        };
        rowBorder.MouseEnter += (_, _) =>
        {
            actions.Opacity = 1;
            if (!isActive)
            {
                rowBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentHover1ABrush");
                nameBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimaryBrush");
            }
        };
        rowBorder.MouseLeave += (_, _) =>
        {
            actions.Opacity = 0;
            rowBorder.Background = Brushes.Transparent;
            nameBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                isActive ? "TextPrimaryBrush" : "TextSecBrush");
        };
        rowBorder.MouseLeftButtonDown += (_, _) =>
        {
            if (profile == _config.ActiveProfile) return;
            SwitchToProfile(profile);
            CloseProfileFlyout();
        };
        return rowBorder;
    }

    private System.Windows.Controls.Border MakeFlyoutAction(MaterialIconKind kind, string tip, bool danger, Action onClick)
    {
        var ic = new MiIcon { Kind = kind, Width = 15, Height = 15 };
        ic.SetResourceReference(MiIcon.ForegroundProperty, "TextDimBrush");
        var b = new System.Windows.Controls.Border
        {
            Width = 26, Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = ic,
        };
        b.MouseEnter += (_, _) =>
        {
            ic.SetResourceReference(MiIcon.ForegroundProperty, danger ? "DangerRedBrush" : "AccentBrush");
            b.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBgBrush");
        };
        b.MouseLeave += (_, _) =>
        {
            ic.SetResourceReference(MiIcon.ForegroundProperty, "TextDimBrush");
            b.Background = Brushes.Transparent;
        };
        b.MouseLeftButtonDown += (_, ev) => { ev.Handled = true; onClick(); };
        return b;
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
            if (!ConfigManager.IsProfileNameAvailable(_config.Profiles, newName, currentProfileName)) return;
            if (!RenameProfile(currentProfileName, newName)) return;
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

        var screenPos = ProfileCard.PointToScreen(new Point(ProfileCard.ActualWidth + 8, 0));
        var dpiSource = PresentationSource.FromVisual(ProfileCard);
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

    private bool RenameProfile(string oldName, string newName)
    {
        try
        {
            ConfigManager.RenameProfileFile(oldName, newName);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to rename profile {oldName} to {newName}: {ex.Message}");
            GlassDialog.ShowWarning($"Could not rename profile: {ex.Message}", owner: this);
            return false;
        }

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

        return true;
    }

    private void DuplicateProfile(string sourceProfileName)
    {
        // Generate unique name
        string baseName = sourceProfileName + " Copy";
        string newName = baseName;
        int counter = 2;
        while (!ConfigManager.IsProfileNameAvailable(_config.Profiles, newName))
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
        loaded.Obs = _config.Obs;
        loaded.Ambience = _config.Ambience;
        loaded.Ducking = _config.Ducking;
        loaded.AutoSwitch = _config.AutoSwitch;
        loaded.VoiceMeeter = _config.VoiceMeeter;
        loaded.Corsair = _config.Corsair;
        loaded.SignalRgb = _config.SignalRgb;
        loaded.DiscordRpc = _config.DiscordRpc;
        loaded.Spotify = _config.Spotify;
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

    public void SetSignalRgbBridge(SignalRgbBridgeService signalRgbBridge)
    {
        _settingsView.SetSignalRgbBridge(signalRgbBridge);
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

            if (!ConfigManager.IsProfileNameAvailable(_config.Profiles, name))
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

        var remainingProfiles = _config.Profiles.Where(name =>
            !string.Equals(name, profileName, StringComparison.Ordinal)).ToList();
        try
        {
            ConfigManager.DeleteProfileFiles(profileName, remainingProfiles);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to delete profile {profileName}: {ex.Message}");
            GlassDialog.ShowWarning($"Could not delete profile: {ex.Message}", owner: this);
            return;
        }

        bool wasActive = string.Equals(_config.ActiveProfile, profileName, StringComparison.Ordinal);
        _config.Profiles.Remove(profileName);
        _config.ProfileIcons.Remove(profileName);

        if (wasActive)
        {
            // Do not call SwitchToProfile here: it saves the current profile first,
            // which would recreate the file we just deleted.
            var loaded = ConfigManager.LoadProfile("Default");
            if (loaded != null)
            {
                loaded.ActiveProfile = "Default";
                PreserveGlobalSettings(loaded);
                _config = loaded;
            }
            else
            {
                _config.ActiveProfile = "Default";
            }
        }

        _onConfigChanged?.Invoke(_config);
        UpdateProfileButton();
        RefreshViews();
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
            _settingsView.UpdateConnectionStatus(connected, portName);
            HwPreview.SetConnected(connected);
            UpdateAggregateConnectionUi();
            RefreshViewsIfEffectiveSurfaceChanged();
        });
    }

    public void SetN3ConnectionStatus(bool connected, string? deviceName = null)
    {
        Dispatcher.Invoke(() =>
        {
            _streamControllerConnected = connected;
            _settingsView.UpdateN3ConnectionStatus(connected, deviceName);
            UpdateAggregateConnectionUi();
            RefreshViewsIfEffectiveSurfaceChanged();
        });
    }

    public void SetVoiceMeeterStatus(bool? connected, bool available = true, bool requiresRestart = false)
    {
        Dispatcher.Invoke(() => _settingsView.UpdateVmStatus(connected, available, requiresRestart));
    }

    /// <summary>
    /// Connection flaps are frequent (COM port drops, N3 retries every 5s while
    /// disconnected). In Auto hardware mode the connect state feeds the computed
    /// surface, but a full RefreshViews() — reloading config into all 8 views —
    /// is only warranted when that computed surface actually changed. Everything
    /// else gets the cheap surface-visibility refresh the non-Auto modes use.
    /// </summary>
    private void RefreshViewsIfEffectiveSurfaceChanged()
    {
        if (_config.HardwareMode == HardwareMode.Auto
            && GetEffectiveDeviceSurface() != _lastRenderedSurface)
            RefreshViews();
        else
            _settingsView.RefreshActiveSurfaceVisibility();
    }

    private void UpdateAggregateConnectionUi()
    {
        bool anyConnected = _turnUpConnected || _streamControllerConnected;

        ConnectionDot.Fill = anyConnected
            ? (SolidColorBrush)FindResource("SuccessGrnBrush")
            : (SolidColorBrush)FindResource("TextDimBrush");
        ConnectionLabel.Text = anyConnected ? "Connected" : "Disconnected";

        ConnectionDotGlow.BlurRadius = anyConnected ? 8 : 0;
        ConnectionDotGlow.Opacity = anyConnected ? 0.5 : 0;

        var pulse = (System.Windows.Media.Animation.Storyboard)FindResource("PulseAnimation");
        if (anyConnected)
        {
            pulse.Begin(this, true);
            _pulseRunning = true;
            // Connection events fire while hidden in tray — don't leave the
            // freshly-begun clock running with nothing visible.
            if (!IsVisible || WindowState == WindowState.Minimized)
                pulse.Pause(this);
        }
        else
        {
            _pulseRunning = false;
            pulse.Stop(this);
            ConnectionDot.Opacity = 1.0;
        }
    }
}
