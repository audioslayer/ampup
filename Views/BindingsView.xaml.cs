using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

public class BindingsView : UserControl
{
    private AppConfig? _config;
    private Action<string>? _onNavigateToMixer;   // profile name
    private Action<string>? _onNavigateToButtons;  // profile name
    private Action<string>? _onSwitchProfile;
    private Action<string>? _onPreviewOsd;         // profile name → show OSD

    private static readonly Dictionary<string, Color> ActionColors = new()
    {
        { "none",               Color.FromRgb(0x44, 0x44, 0x44) },
        { "media_play_pause",   Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "media_next",         Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "media_prev",         Color.FromRgb(0x66, 0xBB, 0x6A) },
        { "mute_master",        Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_mic",           Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_program",       Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_active_window", Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_app_group",     Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_device",        Color.FromRgb(0xEF, 0x53, 0x50) },
        { "mute_input_device",  Color.FromRgb(0xEF, 0x53, 0x50) },
        { "launch_exe",         Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "close_program",      Color.FromRgb(0xFF, 0x7C, 0x43) },
        { "cycle_output",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "cycle_input",        Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_output",      Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_input",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "macro",              Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "macro_hold",         Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "switch_profile",     Color.FromRgb(0x29, 0xB6, 0xF6) },
        { "cycle_brightness",   Color.FromRgb(0xFF, 0xF1, 0x76) },
        { "power_sleep",        Color.FromRgb(0x7C, 0x8C, 0xF8) },
        { "power_lock",         Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "power_off",          Color.FromRgb(0xFF, 0x44, 0x44) },
        { "power_restart",      Color.FromRgb(0xFF, 0x8A, 0x3D) },
        { "power_logoff",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "power_hibernate",    Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "ha_toggle",          Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "ha_scene",           Color.FromRgb(0xFF, 0xA7, 0x26) },
        { "ha_color",           Color.FromRgb(0xE8, 0x6F, 0xFF) },
        { "ha_color_temp",      Color.FromRgb(0xFF, 0xD6, 0x8A) },
        { "ha_service",         Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "group_toggle",      Color.FromRgb(0x69, 0xF0, 0xAE) },
        { "corsair_toggle",     Color.FromRgb(0xFF, 0xD5, 0x4F) },
        { "vm_mute_strip",      Color.FromRgb(0xFF, 0x8F, 0x00) },
        { "vm_mute_bus",        Color.FromRgb(0xFF, 0x8F, 0x00) },
    };

    private static readonly Dictionary<string, string> ActionDisplayNames = new()
    {
        { "none", "None" }, { "media_play_pause", "Play/Pause" }, { "media_next", "Next" },
        { "media_prev", "Prev" }, { "mute_master", "Mute Vol" }, { "mute_mic", "Mute Mic" },
        { "mute_program", "Mute App" }, { "mute_active_window", "Mute Window" },
        { "mute_app_group", "Mute Group" }, { "mute_device", "Mute Device" }, { "mute_input_device", "Mute Input" },
        { "launch_exe", "Launch" }, { "close_program", "Close App" },
        { "cycle_output", "Cycle Out" }, { "cycle_input", "Cycle In" },
        { "select_output", "Set Output" }, { "select_input", "Set Input" },
        { "macro", "Keystroke" }, { "macro_hold", "Hold Key" }, { "switch_profile", "Profile" },
        { "cycle_brightness", "Brightness" },
        { "power_sleep", "Sleep" }, { "power_lock", "Lock" }, { "power_off", "Off" },
        { "power_restart", "Restart" }, { "power_logoff", "Logoff" }, { "power_hibernate", "Hibernate" },
        { "ha_toggle", "HA Toggle" }, { "ha_scene", "HA Scene" }, { "ha_color", "HA Color" }, { "ha_color_temp", "HA Temp" }, { "ha_service", "HA Service" },
        { "corsair_toggle", "iCUE Toggle" },
        { "vm_mute_strip", "VM Mute Strip" }, { "vm_mute_bus", "VM Mute Bus" },
    };

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _root;
    private readonly Border?[] _knobCards = new Border?[5]; // for live LED color sync
    private readonly System.Windows.Threading.DispatcherTimer _colorTimer;
    private readonly HardwareWidget _hardwareWidget;

    public BindingsView()
    {
        _root = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };

        _hardwareWidget = new HardwareWidget();
        _hardwareWidget.SetCallbacks(
            knobIdx => _onNavigateToMixer?.Invoke(_config?.ActiveProfile ?? "Default"),
            buttonIdx => _onNavigateToButtons?.Invoke(_config?.ActiveProfile ?? "Default")
        );

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _root
        };

        Content = _scroll;

        // View lives for the app's lifetime (MainWindow field), so a permanent subscription is fine.
        ThemeManager.OnAccentChanged += OnThemeChanged;
        ThemeManager.OnCardThemeChanged += OnThemeChanged;

        _colorTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _colorTimer.Tick += (_, _) =>
        {
            // Second line of defense — the window hooks below stop the timer
            // when hidden/minimized, but keep the per-tick guard regardless.
            var window = Window.GetWindow(this);
            if (IsVisible && window?.WindowState != WindowState.Minimized)
                UpdateCardColors();
        };

        Loaded += (_, _) =>
        {
            HookOwnerWindow();
            UpdateColorTimerForWindowState();
            if (_themeDirty) Rebuild();
        };
        Unloaded += (_, _) => { UnhookOwnerWindow(); _colorTimer.Stop(); };
    }

    // ── Hidden-window timer quiesce ─────────────────────────────────────
    // Hide-to-tray uses Hide(), which never raises Unloaded — without these
    // window hooks the 100ms color timer ticks forever with nothing visible.
    // Mirrors the _hwPreviewTimer pattern in MainWindow.

    private Window? _ownerWindow;

    private void HookOwnerWindow()
    {
        var window = Window.GetWindow(this);
        if (window == null || ReferenceEquals(window, _ownerWindow)) return;
        UnhookOwnerWindow(); // guard against duplicate handlers when Loaded re-fires
        _ownerWindow = window;
        window.IsVisibleChanged += OwnerWindow_IsVisibleChanged;
        window.StateChanged += OwnerWindow_StateChanged;
    }

    private void UnhookOwnerWindow()
    {
        if (_ownerWindow == null) return;
        _ownerWindow.IsVisibleChanged -= OwnerWindow_IsVisibleChanged;
        _ownerWindow.StateChanged -= OwnerWindow_StateChanged;
        _ownerWindow = null;
    }

    private void OwnerWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => UpdateColorTimerForWindowState();

    private void OwnerWindow_StateChanged(object? sender, EventArgs e)
        => UpdateColorTimerForWindowState();

    private void UpdateColorTimerForWindowState()
    {
        bool quiesced = _ownerWindow != null
            && (!_ownerWindow.IsVisible || _ownerWindow.WindowState == WindowState.Minimized);
        if (quiesced)
            _colorTimer.Stop();
        else
            _colorTimer.Start();
    }

    public Action<string>? OnDuplicateProfile { get; set; }
    public Action<string, int>? OnMoveProfile { get; set; } // profileName, direction (-1=up, +1=down)

    public void SetNavigationCallbacks(
        Action<string> onMixer, Action<string> onButtons,
        Action<string>? onSwitchProfile = null, Action<string>? onPreviewOsd = null)
    {
        _onNavigateToMixer = onMixer;
        _onNavigateToButtons = onButtons;
        _onSwitchProfile = onSwitchProfile;
        _onPreviewOsd = onPreviewOsd;
    }

    private readonly byte[] _lastCardR = new byte[5], _lastCardG = new byte[5], _lastCardB = new byte[5];

    private void UpdateCardColors()
    {
        if (App.Rgb == null) return;
        for (int i = 0; i < 5; i++)
        {
            if (_knobCards[i] == null) continue;
            var (r, g, b) = App.Rgb.GetCurrentColor(i);
            if (r <= 10 && g <= 10 && b <= 10) continue;
            // Skip if color hasn't changed — avoids brush allocation
            if (r == _lastCardR[i] && g == _lastCardG[i] && b == _lastCardB[i]) continue;
            _lastCardR[i] = r; _lastCardG[i] = g; _lastCardB[i] = b;
            var brush = new SolidColorBrush(Color.FromArgb(0x18, r, g, b));
            brush.Freeze();
            _knobCards[i]!.Background = brush;
        }
    }

    public void LoadConfig(AppConfig config)
    {
        _config = config;
        Rebuild();
    }

    /// <summary>
    /// Live device status for the hero pills and device cards. Called by
    /// MainWindow whenever either device connects or disconnects; repaints
    /// the pills in place (no full rebuild).
    /// </summary>
    public void SetDeviceConnection(bool turnUpConnected, bool streamControllerConnected)
    {
        _turnUpConnected = turnUpConnected;
        _streamControllerConnected = streamControllerConnected;
        foreach (var paint in _connectionPainters) paint();
    }

    private bool _turnUpConnected, _streamControllerConnected;
    private readonly List<Action> _connectionPainters = new();
    private bool _themeDirty;

    // Accent / card theme changed: rebuild now if on screen, otherwise on next Loaded.
    private void OnThemeChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsLoaded) Rebuild();
            else _themeDirty = true;
        }));
    }

    private void Rebuild()
    {
        if (_config == null) return;
        _themeDirty = false;
        _root.Children.Clear();
        Array.Clear(_knobCards);
        _connectionPainters.Clear();
        DetachHardwareWidget();

        bool showTurnUp = ShouldShowTurnUpOverview();
        bool showSc = ShouldShowStreamControllerOverview();

        // Load every profile once (active first) — shared by device stats and profile cards.
        var profiles = new List<(string Name, AppConfig Config)> { (_config.ActiveProfile, _config) };
        foreach (var p in _config.Profiles)
        {
            if (p == _config.ActiveProfile) continue;
            // Profile file doesn't exist yet — use defaults
            profiles.Add((p, ConfigManager.LoadProfile(p) ?? new AppConfig()));
        }

        _root.Children.Add(BuildHero(showTurnUp, showSc));

        if (!showTurnUp && !showSc)
        {
            _root.Children.Add(UiKit.EmptyState(MaterialIconKind.Devices, "No device selected",
                "Pick Turn Up or Stream Controller as your Active Surface in Settings › Devices to see its assignments here."));
            return;
        }

        // Device sections — photo centerpiece + live widget / stats for the active profile.
        if (showTurnUp)
        {
            int assigned = CountTurnUpAssigned(_config);
            _root.Children.Add(MakeSectionLabel("TURN UP", $"5 knobs · 5 buttons · {assigned} assigned"));
            _root.Children.Add(BuildTurnUpDeviceCard());
        }
        if (showSc)
        {
            int assigned = CountStreamControllerAssigned(_config);
            _root.Children.Add(MakeSectionLabel("STREAM CONTROLLER", $"6 keys · 3 buttons · 3 dials · {assigned} assigned"));
            _root.Children.Add(BuildStreamControllerDeviceCard(_config));
        }

        // Profiles — active profile expanded, the rest as compact chip summaries.
        var profilesHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        profilesHeader.Children.Add(MakeSectionLabel("PROFILES", profiles.Count.ToString()));
        var tip = UiKit.HelpTip("Click a profile name to switch to it. Click any knob, button or key to edit it in that profile. "
            + "The active profile shows full details; others show a compact summary.", "Profiles");
        tip.Margin = new Thickness(8, -6, 0, 0);
        profilesHeader.Children.Add(tip);
        _root.Children.Add(profilesHeader);

        foreach (var (name, cfg) in profiles)
            _root.Children.Add(BuildProfileSection(name, cfg, showTurnUp, showSc));
    }

    // The widget instance is reused across rebuilds; WPF throws if it still has a parent.
    private void DetachHardwareWidget()
    {
        switch (_hardwareWidget.Parent)
        {
            case Decorator d: d.Child = null; break;
            case Panel p: p.Children.Remove(_hardwareWidget); break;
        }
    }

    // ── Hero ────────────────────────────────────────────────────────────

    private UIElement BuildHero(bool showTurnUp, bool showSc)
    {
        var activeName = _config!.ActiveProfile;
        var (profileColor, profileKind) = GetProfileVisuals(activeName);
        // Plain heading row (no card chrome) — the page title for the Overview.
        var hero = new Border
        {
            Padding = new Thickness(2, 4, 2, 4),
            Margin = new Thickness(0, 0, 0, 18),
            Background = Brushes.Transparent,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.Child = grid;

        // Glass icon tile in the active profile's colour, with a soft glow.
        var tile = new Border
        {
            Width = 56, Height = 56,
            CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(profileColor, 0x26)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(profileColor, 0x77)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55, Color = profileColor },
            Child = new MaterialIcon { Kind = profileKind, Width = 28, Height = 28, Foreground = new SolidColorBrush(profileColor) },
        };
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var caption = new TextBlock { Text = "OVERVIEW · ACTIVE PROFILE", FontSize = 12, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        text.Children.Add(caption);
        var title = new TextBlock
        {
            Text = activeName, FontSize = 22, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 1, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        text.Children.Add(title);

        var pills = new WrapPanel();
        bool hwAllowsTurnUp = _config.HardwareMode != HardwareMode.StreamControllerOnly;
        bool hwAllowsSc = _config.HardwareMode != HardwareMode.TurnUpOnly;
        if (hwAllowsTurnUp)
            pills.Children.Add(BuildConnectionPill("Turn Up", () => _turnUpConnected, showTurnUp));
        if (hwAllowsSc)
            pills.Children.Add(BuildConnectionPill("Stream Controller", () => _streamControllerConnected, showSc));
        int count = _config.Profiles.Count;
        pills.Children.Add(Pill($"{count} profile{(count == 1 ? "" : "s")}", ThemeManager.Accent, MaterialIconKind.AccountMultiple));
        text.Children.Add(pills);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // Quick actions.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        actions.Children.Add(HeroButton(MaterialIconKind.EyeOutline, "Preview OSD", "Show the OSD for the active profile",
            _ => _onPreviewOsd?.Invoke(activeName)));
        if (count > 1)
        {
            var switchBtn = HeroButton(MaterialIconKind.AccountSwitchOutline, "Switch", "Switch to another profile",
                anchor => GlassContextMenuHost.Show(anchor, BuildSwitchMenu()));
            switchBtn.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(switchBtn);
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        return hero;
    }

    private IReadOnlyList<GlassMenuItem> BuildSwitchMenu()
    {
        var items = new List<GlassMenuItem>();
        if (_config == null) return items;
        foreach (var p in _config.Profiles)
        {
            var name = p;
            bool active = name == _config.ActiveProfile;
            items.Add(new GlassMenuItem(name, GetProfileVisuals(name).Kind,
                active ? null : new Action(() => _onSwitchProfile?.Invoke(name)), IsChecked: active));
        }
        return items;
    }

    private static Border HeroButton(MaterialIconKind kind, string label, string tooltip, Action<FrameworkElement> onClick)
    {
        var icon = new MaterialIcon { Kind = kind, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        icon.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        var tb = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(icon);
        sp.Children.Add(tb);

        var a = ThemeManager.Accent;
        var b = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 14, 7),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x1A)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x55)),
            Child = sp,
        };
        b.MouseEnter += (_, _) =>
        {
            b.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x33));
            b.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0xAA));
        };
        b.MouseLeave += (_, _) =>
        {
            b.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x1A));
            b.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x55));
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(b); };
        AddHoverLift(b, 1.5);
        return b;
    }

    /// <summary>Dot + label pill that repaints itself when connection state changes.</summary>
    private Border BuildConnectionPill(string deviceName, Func<bool> isConnected, bool shownOnSurface)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var tb = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(dot);
        sp.Children.Add(tb);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 4),
            Child = sp,
            ToolTip = shownOnSurface ? null : $"{deviceName} is hidden by your Active Surface choice",
        };
        void Paint()
        {
            bool on = isConnected();
            var c = on ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0x9E, 0x9E, 0x9E);
            if (TryFindResource(on ? "SuccessGrnBrush" : "TextDimBrush") is SolidColorBrush rb) c = rb.Color;
            tb.Text = $"{deviceName} · {(on ? "Connected" : "Offline")}";
            tb.Foreground = new SolidColorBrush(c);
            dot.Fill = new SolidColorBrush(c);
            dot.Effect = on ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.8, Color = c } : null;
            pill.Background = new SolidColorBrush(ThemeManager.WithAlpha(c, 0x18));
            pill.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(c, 0x55));
            pill.Opacity = shownOnSurface ? 1.0 : 0.6;
        }
        Paint();
        _connectionPainters.Add(Paint);
        return pill;
    }

    private static Border Pill(string text, Color color, MaterialIconKind? icon = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon is { } kind)
            sp.Children.Add(new MaterialIcon { Kind = kind, Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(color) });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(color) });
        return new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x18)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x55)),
            Child = sp,
        };
    }

    // ── Device sections ─────────────────────────────────────────────────

    /// <summary>Big glass card: device photo stage on the left, content on the right (stacks when narrow).</summary>
    private Border BuildDeviceCard(string deviceId, string deviceName, Func<bool> isConnected, FrameworkElement content)
    {
        var accent = ThemeManager.Accent;
        // One box for the section: fill only, no outline.
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 18),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");

        var glass = new Border
        {
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(18),
            Background = new LinearGradientBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 90),
        };
        card.Child = glass;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        glass.Child = grid;

        // Stage: accent radial glow behind the photo.
        var stage = new Grid { Width = 240, Height = 190, VerticalAlignment = VerticalAlignment.Center };
        stage.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new RadialGradientBrush(ThemeManager.WithAlpha(accent, 0x38), ThemeManager.WithAlpha(accent, 0x00))
            {
                Center = new Point(0.5, 0.48), GradientOrigin = new Point(0.5, 0.45), RadiusX = 0.6, RadiusY = 0.6,
            },
        });
        var photo = LoadDeviceImage(deviceId);
        if (photo != null)
        {
            stage.Children.Add(new Image
            {
                Source = photo,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(14, 10, 14, 30),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.45, Color = accent },
            });
        }
        else
        {
            var fallback = UiKit.IconTile(accent, deviceId == "n3" ? MaterialIconKind.ViewGridOutline : MaterialIconKind.Knob, 72);
            fallback.HorizontalAlignment = HorizontalAlignment.Center;
            fallback.Margin = new Thickness(0, 0, 0, 24);
            stage.Children.Add(fallback);
        }
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        var nameTb = new TextBlock { Text = deviceName, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        nameTb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        label.Children.Add(nameTb);
        var statusTb = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
        void PaintStatus()
        {
            bool on = isConnected();
            statusTb.Text = on ? "● CONNECTED" : "○ OFFLINE";
            statusTb.SetResourceReference(TextBlock.ForegroundProperty, on ? "SuccessGrnBrush" : "TextDimBrush");
        }
        PaintStatus();
        _connectionPainters.Add(PaintStatus);
        label.Children.Add(statusTb);
        stage.Children.Add(label);
        grid.Children.Add(stage);

        var contentHost = new Border { Child = content, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(contentHost);

        bool? wide = null;
        void Layout(double width)
        {
            bool w = width >= 640;
            if (wide == w) return;
            wide = w;
            Grid.SetColumn(contentHost, w ? 1 : 0);
            Grid.SetRow(contentHost, w ? 0 : 1);
            Grid.SetColumnSpan(contentHost, w ? 1 : 2);
            Grid.SetColumnSpan(stage, w ? 1 : 2);
            stage.HorizontalAlignment = w ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            contentHost.Margin = w ? new Thickness(18, 0, 0, 0) : new Thickness(0, 14, 0, 0);
        }
        Layout(1000);
        grid.SizeChanged += (_, e) => Layout(e.NewSize.Width);
        return card;
    }

    private UIElement BuildTurnUpDeviceCard()
    {
        _hardwareWidget.LoadConfig(_config!);
        // The widget is a fixed 620x250 drawing; scale down (never up) to fit the card.
        var viewbox = new Viewbox
        {
            Stretch = System.Windows.Media.Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Child = _hardwareWidget,
        };
        return BuildDeviceCard("turnup", "Turn Up", () => _turnUpConnected, viewbox);
    }

    private UIElement BuildStreamControllerDeviceCard(AppConfig config)
    {
        int keys = Enumerable.Range(0, 6).Count(i => IsDisplayKeyDesigned(config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i)));
        int keyActions = Enumerable.Range(0, 6).Count(i => HasAnyAction(config.N3.Buttons.FirstOrDefault(b => b.Idx == 100 + i)));
        int sides = Enumerable.Range(0, 3).Count(i => HasAnyAction(config.N3.Buttons.FirstOrDefault(b => b.Idx == 10000 + i)));
        int dials = Enumerable.Range(0, 3).Count(i => HasAnyAction(config.N3.Buttons.FirstOrDefault(b => b.Idx == 10003 + i)));

        var stats = new UniformGrid { Columns = 2 };
        stats.Children.Add(StatTile(MaterialIconKind.ViewGridOutline, "LCD keys designed", $"{keys} / 6"));
        stats.Children.Add(StatTile(MaterialIconKind.GestureTapButton, "Key actions", $"{keyActions} / 6"));
        stats.Children.Add(StatTile(MaterialIconKind.CircleOutline, "Side buttons", $"{sides} / 3"));
        stats.Children.Add(StatTile(MaterialIconKind.Knob, "Dial presses", $"{dials} / 3"));

        var wrap = new StackPanel();
        wrap.Children.Add(stats);
        var edit = UiKit.LinkRow(MaterialIconKind.PencilOutline, "Edit keys and controls", true,
            () => _onNavigateToButtons?.Invoke(_config?.ActiveProfile ?? "Default"));
        edit.Margin = new Thickness(4, 8, 0, 0);
        edit.HorizontalAlignment = HorizontalAlignment.Left;
        wrap.Children.Add(edit);

        return BuildDeviceCard("n3", "Stream Controller", () => _streamControllerConnected, wrap);
    }

    private static Border StatTile(MaterialIconKind kind, string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = UiKit.IconTile(ThemeManager.Accent, kind, 32);
        tile.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(tile);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var v = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold };
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var l = new TextBlock { Text = label, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        sp.Children.Add(v);
        sp.Children.Add(l);
        Grid.SetColumn(sp, 1);
        grid.Children.Add(sp);

        // Fill-only tile inside the device card (no outline).
        var b = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(4),
            Child = grid,
        };
        b.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        return b;
    }

    /// <summary>
    /// Assets/devices/&lt;deviceId&gt;.png (WPF Resource) when bundled; null otherwise so the
    /// caller can draw a placeholder. Mirrors SettingsView.LoadDeviceImage.
    /// </summary>
    private static ImageSource? LoadDeviceImage(string deviceId)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/devices/{deviceId}.png", UriKind.Absolute);
            if (Application.GetResourceStream(uri) is not { } info) return null;
            info.Stream.Dispose();

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 600;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ── Assignment counting ─────────────────────────────────────────────

    private static bool IsKnobAssigned(KnobConfig? k)
        => k != null && !string.IsNullOrEmpty(k.Target) && k.Target != "none";

    private static bool HasAnyAction(ButtonConfig? b)
        => b != null && (IsSet(b.Action) || IsSet(b.DoublePressAction) || IsSet(b.HoldAction));

    private static bool IsSet(string? action) => !string.IsNullOrEmpty(action) && action != "none";

    private static bool IsDisplayKeyDesigned(StreamControllerDisplayKeyConfig? key)
        => key != null && (!string.IsNullOrWhiteSpace(key.Title)
                           || !string.IsNullOrWhiteSpace(key.ImagePath)
                           || !string.IsNullOrWhiteSpace(key.PresetIconKind)
                           || key.DisplayType != DisplayKeyType.Normal);

    private static int CountTurnUpAssigned(AppConfig c)
        => Enumerable.Range(0, 5).Count(i => IsKnobAssigned(c.Knobs.FirstOrDefault(k => k.Idx == i)))
         + Enumerable.Range(0, 5).Count(i => HasAnyAction(c.Buttons.FirstOrDefault(b => b.Idx == i)));

    private static int CountStreamControllerAssigned(AppConfig c)
        => Enumerable.Range(0, 6).Count(i => IsDisplayKeyDesigned(c.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i))
                                             || HasAnyAction(c.N3.Buttons.FirstOrDefault(b => b.Idx == 100 + i)))
         + Enumerable.Range(0, 6).Count(i => HasAnyAction(c.N3.Buttons.FirstOrDefault(b => b.Idx == 10000 + i)));

    private (Color Color, MaterialIconKind Kind) GetProfileVisuals(string profileName)
    {
        var iconCfg = _config?.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(iconCfg.Color); }
        catch { color = ThemeManager.Accent; }
        if (!Enum.TryParse<MaterialIconKind>(iconCfg.Symbol, out var kind))
            kind = MaterialIconKind.AccountCircleOutline;
        return (color, kind);
    }

    private static void AddHoverLift(FrameworkElement el, double lift)
    {
        var tt = new TranslateTransform();
        el.RenderTransform = tt;
        el.MouseEnter += (_, _) => AnimateLift(tt, -lift);
        el.MouseLeave += (_, _) => AnimateLift(tt, 0);
    }

    private static void AnimateLift(TranslateTransform tt, double y)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.Y = y;
            return;
        }
        tt.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    // ── Profile cards ───────────────────────────────────────────────────

    private UIElement BuildProfileSection(string profileName, AppConfig config, bool showTurnUp, bool showSc)
    {
        bool isActive = _config?.ActiveProfile == profileName;
        var (accentColor, profileKind) = GetProfileVisuals(profileName);
        var capturedName = profileName;

        // One card per profile, fill only (no outline, no side stripe). The active
        // profile reads via an accent-tinted fill + soft glow instead of a bright border.
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 14),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");

        Brush GlassFill(byte topAlpha, Color tint) => new LinearGradientBrush(
            ThemeManager.WithAlpha(tint, topAlpha),
            ThemeManager.WithAlpha(tint, (byte)(topAlpha / 3)), 90);

        var glass = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = isActive ? GlassFill(0x1C, accentColor) : GlassFill(0x0C, Colors.White),
        };
        card.Child = glass;

        if (isActive)
        {
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.18, Color = accentColor };
        }
        else
        {
            card.MouseEnter += (_, _) => glass.Background = GlassFill(0x12, accentColor);
            card.MouseLeave += (_, _) => glass.Background = GlassFill(0x0C, Colors.White);
            AddHoverLift(card, 2);
        }

        var stack = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        glass.Child = stack;

        // Header: icon tile | name + Active pill / summary | actions
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stack.Children.Add(header);

        var tile = UiKit.IconTile(accentColor, profileKind, 42);
        tile.Margin = new Thickness(0, 0, 14, 0);
        if (isActive)
            tile.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6, Color = accentColor };
        header.Children.Add(tile);

        var names = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Background = Brushes.Transparent, // hit-testable across the whole block
            Cursor = Cursors.Hand,
            ToolTip = isActive ? null : $"Switch to {profileName}",
        };
        names.MouseLeftButtonDown += (_, _) => _onSwitchProfile?.Invoke(capturedName);
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        var title = new TextBlock
        {
            Text = profileName, FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        nameRow.Children.Add(title);
        if (isActive)
        {
            var activePill = Pill("Active", accentColor, MaterialIconKind.Check);
            activePill.Margin = new Thickness(10, 0, 0, 0);
            activePill.Padding = new Thickness(7, 1, 9, 1);
            nameRow.Children.Add(activePill);
        }
        names.Children.Add(nameRow);

        int assigned = 0, total = 0;
        if (showTurnUp) { assigned += CountTurnUpAssigned(config); total += 10; }
        if (showSc) { assigned += CountStreamControllerAssigned(config); total += 12; }
        var sub = new TextBlock
        {
            Text = $"{assigned} of {total} controls assigned" + (isActive ? "" : " · Click to switch"),
            FontSize = 12, Margin = new Thickness(0, 3, 0, 0),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        names.Children.Add(sub);
        Grid.SetColumn(names, 1);
        header.Children.Add(names);

        // Right side actions — preview OSD, duplicate, reorder.
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        int profileIdx = _config?.Profiles.IndexOf(profileName) ?? -1;
        int profileCount = _config?.Profiles.Count ?? 0;

        actionRow.Children.Add(UiKit.IconButton(MaterialIconKind.EyeOutline, "Preview the OSD for this profile",
            _ => _onPreviewOsd?.Invoke(capturedName)));
        actionRow.Children.Add(UiKit.IconButton(MaterialIconKind.ContentCopy, "Duplicate this profile",
            _ => OnDuplicateProfile?.Invoke(capturedName)));

        var moveUpBtn = UiKit.IconButton(MaterialIconKind.ArrowUp, "Move up",
            _ => { if (profileIdx > 0) OnMoveProfile?.Invoke(capturedName, -1); });
        moveUpBtn.Opacity = profileIdx > 0 ? 1.0 : 0.3;
        moveUpBtn.IsEnabled = profileIdx > 0;
        actionRow.Children.Add(moveUpBtn);

        var moveDownBtn = UiKit.IconButton(MaterialIconKind.ArrowDown, "Move down",
            _ => { if (profileIdx >= 0 && profileIdx < profileCount - 1) OnMoveProfile?.Invoke(capturedName, 1); });
        moveDownBtn.Opacity = profileIdx >= 0 && profileIdx < profileCount - 1 ? 1.0 : 0.3;
        moveDownBtn.IsEnabled = profileIdx >= 0 && profileIdx < profileCount - 1;
        actionRow.Children.Add(moveDownBtn);

        Grid.SetColumn(actionRow, 2);
        header.Children.Add(actionRow);

        var sectionContent = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        stack.Children.Add(sectionContent);

        if (!isActive)
        {
            sectionContent.Children.Add(BuildCompactSummary(config, capturedName, showTurnUp, showSc));
            return card;
        }

        // Active profile: full detail. Turn Up rows (KNOBS + BUTTONS) follow the
        // Active Surface toggle — hidden when the user has picked Stream Controller.
        if (showTurnUp)
        {
            sectionContent.Children.Add(MakeSectionLabel("KNOBS", "5"));

            var knobsRow = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 16) };
            for (int i = 0; i < 5; i++)
            {
                var knob = config.Knobs.FirstOrDefault(k => k.Idx == i) ?? new KnobConfig { Idx = i };
                var light = config.Lights.FirstOrDefault(l => l.Idx == i);
                knobsRow.Children.Add(BuildKnobCard(i, knob, capturedName, light));
            }
            sectionContent.Children.Add(knobsRow);

            sectionContent.Children.Add(MakeSectionLabel("BUTTONS", "5"));

            var buttonsRow = new UniformGrid { Columns = 5 };
            for (int i = 0; i < 5; i++)
            {
                var btn = config.Buttons.FirstOrDefault(b => b.Idx == i) ?? new ButtonConfig { Idx = i };
                buttonsRow.Children.Add(BuildButtonCard(i, btn, capturedName));
            }
            sectionContent.Children.Add(buttonsRow);
        }

        // Stream Controller block — 2x3 LCD preview grid + 3 side buttons
        // + 3 encoder-press cards. Only rendered when the user has the
        // N3 enabled; hidden for pure Turn Up setups.
        if (showSc)
            sectionContent.Children.Add(BuildStreamControllerSection(config, capturedName));

        return card;
    }

    /// <summary>
    /// Compact summary for inactive profiles: chips for every assigned knob / button / key.
    /// Clicking a chip opens that profile in Mixer (knobs) or Buttons (everything else).
    /// </summary>
    private UIElement BuildCompactSummary(AppConfig config, string profileName, bool showTurnUp, bool showSc)
    {
        var wrap = new WrapPanel();
        int empty = 0;

        if (showTurnUp)
        {
            for (int i = 0; i < 5; i++)
            {
                var knob = config.Knobs.FirstOrDefault(k => k.Idx == i);
                if (!IsKnobAssigned(knob)) { empty++; continue; }
                var light = config.Lights.FirstOrDefault(l => l.Idx == i);
                Color? led = light != null && (light.R > 10 || light.G > 10 || light.B > 10)
                    ? Color.FromRgb((byte)Math.Clamp(light.R, 0, 255), (byte)Math.Clamp(light.G, 0, 255), (byte)Math.Clamp(light.B, 0, 255))
                    : null;
                var text = string.IsNullOrEmpty(knob!.Label) ? FormatTarget(knob) : knob.Label;
                wrap.Children.Add(Chip($"K{i + 1}", text, GetTargetIcon(knob.Target), led, null,
                    () => _onNavigateToMixer?.Invoke(profileName)));
            }
            for (int i = 0; i < 5; i++)
            {
                var btn = config.Buttons.FirstOrDefault(b => b.Idx == i);
                if (!HasAnyAction(btn)) { empty++; continue; }
                wrap.Children.Add(ButtonChip($"B{i + 1}", btn!, profileName));
            }
        }

        if (showSc)
        {
            for (int i = 0; i < 6; i++)
            {
                var key = config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i);
                var btn = config.N3.Buttons.FirstOrDefault(b => b.Idx == 100 + i);
                bool designed = IsDisplayKeyDesigned(key);
                if (!designed && !HasAnyAction(btn)) { empty++; continue; }

                ImageSource? thumb = null;
                if (designed)
                {
                    try { thumb = StreamControllerDisplayRenderer.CreateEditorPreview(key!, 48); }
                    catch { /* preview renderer failed — chip without thumbnail */ }
                }
                string action = btn?.Action ?? "none";
                string text = !string.IsNullOrWhiteSpace(key?.Title) ? key!.Title!
                    : ActionDisplayNames.TryGetValue(action, out var d) ? d : action;
                wrap.Children.Add(Chip($"K{i + 1}", text, MaterialIconKind.ViewGridOutline, null, thumb,
                    () => _onNavigateToButtons?.Invoke(profileName)));
            }
            for (int i = 0; i < 6; i++)
            {
                int idx = i < 3 ? 10000 + i : 10003 + (i - 3); // side buttons, then encoder presses
                var btn = config.N3.Buttons.FirstOrDefault(b => b.Idx == idx);
                if (!HasAnyAction(btn)) { empty++; continue; }
                wrap.Children.Add(ButtonChip(i < 3 ? $"SB{i + 1}" : $"E{i - 2}", btn!, profileName));
            }
        }

        if (wrap.Children.Count == 0)
        {
            var none = UiKit.MutedText("Nothing assigned yet. Switch to this profile to set it up.");
            return none;
        }
        if (empty > 0)
        {
            var more = UiKit.MutedText($"+{empty} empty", 12);
            more.VerticalAlignment = VerticalAlignment.Center;
            more.Margin = new Thickness(4, 0, 0, 6);
            wrap.Children.Add(more);
        }
        return wrap;
    }

    private Border ButtonChip(string slot, ButtonConfig btn, string profileName)
    {
        string action = IsSet(btn.Action) ? btn.Action : IsSet(btn.DoublePressAction) ? btn.DoublePressAction : btn.HoldAction;
        string text = !string.IsNullOrEmpty(btn.Label) ? btn.Label
            : ActionDisplayNames.TryGetValue(action, out var d) ? d
            : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(action.Replace("_", " "));
        Color? dot = ActionColors.TryGetValue(action, out var c) ? c : null;
        return Chip(slot, text, MaterialIconKind.GestureTapButton, dot, null,
            () => _onNavigateToButtons?.Invoke(profileName));
    }

    /// <summary>Rounded chip: [colour dot | thumbnail | icon] SLOT name. Hover lifts + tints the border.</summary>
    private static Border Chip(string slot, string text, MaterialIconKind icon, Color? dotColor, ImageSource? thumb, Action onClick)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (thumb != null)
        {
            var img = new Border
            {
                Width = 18, Height = 18,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new ImageBrush(thumb) { Stretch = System.Windows.Media.Stretch.UniformToFill },
            };
            sp.Children.Add(img);
        }
        else
        {
            var ic = UiKit.Icon(icon, 13, "TextSecBrush");
            ic.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(ic);
        }
        var slotTb = new TextBlock { Text = slot, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        slotTb.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        sp.Children.Add(slotTb);
        var tb = new TextBlock { Text = text, FontSize = 11, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        sp.Children.Add(tb);
        if (dotColor is { } dc)
        {
            sp.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7, Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(dc),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.8, Color = dc },
            });
        }

        // Fill-only chip; hover tints the fill instead of drawing an outline.
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            ToolTip = $"{slot}: {text}",
            Child = sp,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        var tint = dotColor ?? ThemeManager.Accent;
        chip.MouseEnter += (_, _) => chip.Background = new SolidColorBrush(ThemeManager.WithAlpha(tint, 0x26));
        chip.MouseLeave += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        AddHoverLift(chip, 1);
        return chip;
    }

    private static MaterialIconKind GetTargetIcon(string? target)
    {
        var t = target ?? "";
        return t switch
        {
            "master" => MaterialIconKind.VolumeHigh,
            "mic" => MaterialIconKind.Microphone,
            "system" => MaterialIconKind.BellOutline,
            "any" or "active_window" => MaterialIconKind.Application,
            "apps" => MaterialIconKind.Apps,
            "output_device" => MaterialIconKind.Speaker,
            "input_device" => MaterialIconKind.MicrophoneVariant,
            "monitor" => MaterialIconKind.MonitorShimmer,
            "led_brightness" => MaterialIconKind.Brightness6,
            "discord_input_volume" => MaterialIconKind.Microphone,
            "discord_output_volume" => MaterialIconKind.Headset,
            _ when t.StartsWith("ha_") => MaterialIconKind.HomeAssistant,
            _ when t.StartsWith("govee") => MaterialIconKind.LightbulbOutline,
            _ when t.StartsWith("corsair_") => MaterialIconKind.Fan,
            _ when t.StartsWith("vm_") => MaterialIconKind.TuneVertical,
            _ when t.StartsWith("obs:") => MaterialIconKind.Broadcast,
            _ => MaterialIconKind.Knob,
        };
    }

    /// <summary>
    /// Which surfaces to render on the Overview. Driven by:
    ///   1. HardwareMode (hard gates — SC-only never shows Turn Up, etc.)
    ///   2. Active Surface selection (PreferredSurface) when HardwareMode
    ///      permits multiple devices.
    /// </summary>
    private bool ShouldShowTurnUpOverview()
    {
        if (_config == null) return false;
        if (_config.HardwareMode == HardwareMode.StreamControllerOnly) return false;
        if (_config.HardwareMode == HardwareMode.TurnUpOnly) return true;
        // Auto / DualMode — follow the user's Active Surface preference.
        var surface = _config.TabSelection.PreferredSurface;
        return surface == DeviceSurface.TurnUp || surface == DeviceSurface.Both;
    }

    private bool ShouldShowStreamControllerOverview()
    {
        if (_config == null) return false;
        if (_config.HardwareMode == HardwareMode.TurnUpOnly) return false;
        if (_config.HardwareMode == HardwareMode.StreamControllerOnly) return true;
        // Auto / DualMode — follow the user's Active Surface preference.
        var surface = _config.TabSelection.PreferredSurface;
        return surface == DeviceSurface.StreamController || surface == DeviceSurface.Both;
    }

    /// <summary>
    /// Stream Controller (N3) overview — 6 LCD preview tiles in a 2×3
    /// grid, then a row of 3 side buttons + 3 encoder-press cards. Uses
    /// <see cref="StreamControllerDisplayRenderer.CreateEditorPreview"/>
    /// so tiles get the crisp vector render, not the 60×60 device JPEG.
    /// </summary>
    private UIElement BuildStreamControllerSection(AppConfig config, string profileName)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

        stack.Children.Add(MakeSectionLabel("STREAM CONTROLLER KEYS", "6"));

        // LCD grid (2 rows × 3 cols) — iterate root-folder display keys.
        var lcdGrid = new UniformGrid
        {
            Columns = 3, Rows = 2,
            Margin = new Thickness(-4, 0, -4, 12),
        };
        for (int i = 0; i < 6; i++)
        {
            var key = config.N3.DisplayKeys.FirstOrDefault(k => k.Idx == i)
                      ?? new StreamControllerDisplayKeyConfig { Idx = i };
            var btnIdx = 100 + i; // StreamControllerDisplayKeyBase
            var btn = config.N3.Buttons.FirstOrDefault(b => b.Idx == btnIdx);
            lcdGrid.Children.Add(BuildStreamControllerLcdTile(i, key, btn, profileName));
        }
        stack.Children.Add(lcdGrid);

        stack.Children.Add(MakeSectionLabel("STREAM CONTROLLER CONTROLS", "6"));

        // Side buttons + encoder presses in a single row.
        var controlsRow = new UniformGrid
        {
            Columns = 6,
            Margin = new Thickness(0, 0, 0, 0),
        };
        for (int i = 0; i < 3; i++)
        {
            int btnIdx = 10000 + i; // N3SideButtonBase
            var btn = config.N3.Buttons.FirstOrDefault(b => b.Idx == btnIdx)
                      ?? new ButtonConfig { Idx = btnIdx };
            var card = BuildButtonCard(i, btn, profileName);
            // Swap the card's default "B{n}" label for something meaningful.
            RelabelOverviewButtonCard(card, $"SB{i + 1}");
            controlsRow.Children.Add(card);
        }
        for (int i = 0; i < 3; i++)
        {
            int btnIdx = 10003 + i; // N3EncoderPressBase
            var btn = config.N3.Buttons.FirstOrDefault(b => b.Idx == btnIdx)
                      ?? new ButtonConfig { Idx = btnIdx };
            var card = BuildButtonCard(i + 3, btn, profileName);
            RelabelOverviewButtonCard(card, $"E{i + 1}");
            controlsRow.Children.Add(card);
        }
        stack.Children.Add(controlsRow);

        return stack;
    }

    /// <summary>
    /// Small preview tile for one N3 LCD key in the overview. Renders the
    /// configured design via the high-quality editor preview path, plus a
    /// small action badge underneath (same style as the Turn Up button card).
    /// </summary>
    private UIElement BuildStreamControllerLcdTile(int slot,
                                                    StreamControllerDisplayKeyConfig key,
                                                    ButtonConfig? btn,
                                                    string profileName)
    {
        bool hasContent = !string.IsNullOrWhiteSpace(key.Title)
                          || !string.IsNullOrWhiteSpace(key.ImagePath)
                          || !string.IsNullOrWhiteSpace(key.PresetIconKind)
                          || key.DisplayType != DisplayKeyType.Normal;

        // Fill-only tile inside the profile card; hover tints the fill.
        var card = new Border
        {
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            Opacity = hasContent ? 1.0 : 0.5,
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        card.MouseEnter += (_, _) =>
            card.Background = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x1E));
        card.MouseLeave += (_, _) =>
            card.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        card.MouseLeftButtonDown += (_, _) => _onNavigateToButtons?.Invoke(profileName);

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        // Preview bitmap — clipped to a rounded rect to match the tile shape.
        var previewHost = new Border
        {
            Height = 80,
            Width = 80,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
            ClipToBounds = true,
        };
        previewHost.SizeChanged += (_, e) =>
            previewHost.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 8, 8);

        if (hasContent)
        {
            try
            {
                var img = new Image
                {
                    Source = StreamControllerDisplayRenderer.CreateEditorPreview(key, 160),
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                previewHost.Child = img;
            }
            catch { /* preview renderer failed — leave empty slot */ }
        }
        stack.Children.Add(previewHost);

        // Slot label ("K1", "K2", ...).
        stack.Children.Add(new TextBlock
        {
            Text = $"K{slot + 1}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
        });

        // Action summary.
        string actionKey = btn?.Action ?? "none";
        string actionLabel = ActionDisplayNames.TryGetValue(actionKey, out var d) ? d : actionKey;
        stack.Children.Add(new TextBlock
        {
            Text = hasContent ? actionLabel : "Empty",
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 110,
            Foreground = (SolidColorBrush)FindResource(hasContent ? "TextSecBrush" : "TextDimBrush"),
        });

        card.Child = stack;
        return card;
    }

    /// <summary>
    /// BuildButtonCard hard-codes a "B{idx+1}" label for Turn Up buttons.
    /// For the N3 overview we want "SB1"/"E1" labels instead — swap the
    /// first TextBlock in the first row's StackPanel after the card is built.
    /// </summary>
    private static void RelabelOverviewButtonCard(UIElement cardElement, string newLabel)
    {
        if (cardElement is not Border card) return;
        if (card.Child is not StackPanel content) return;
        if (content.Children.Count == 0) return;
        if (content.Children[0] is not StackPanel titleRow) return;
        foreach (var child in titleRow.Children)
        {
            if (child is TextBlock tb)
            {
                tb.Text = newLabel;
                return;
            }
        }
    }

    private static FrameworkElement MakeSectionLabel(string text, string? count = null)
        => UiKit.SectionHeader(text, count);

    private UIElement BuildKnobCard(int idx, KnobConfig knob, string profileName, LightConfig? light = null)
    {
        bool isEmpty = string.IsNullOrEmpty(knob.Target) || knob.Target == "none";
        bool isActiveProfile = profileName == _config?.ActiveProfile;

        // Tint card background with the knob's LED color (very subtle)
        Brush cardBg;
        if (isActiveProfile && App.Rgb != null)
        {
            // Use live LED color for active profile
            var (r, g, b) = App.Rgb.GetCurrentColor(idx);
            cardBg = (r > 10 || g > 10 || b > 10)
                ? new SolidColorBrush(Color.FromArgb(0x18, r, g, b))
                : (SolidColorBrush)FindResource("BgDarkBrush");
        }
        else if (light != null && (light.R > 10 || light.G > 10 || light.B > 10))
        {
            cardBg = new SolidColorBrush(Color.FromArgb(0x18,
                (byte)Math.Clamp(light.R, 0, 255),
                (byte)Math.Clamp(light.G, 0, 255),
                (byte)Math.Clamp(light.B, 0, 255)));
        }
        else
        {
            cardBg = (SolidColorBrush)FindResource("BgDarkBrush");
        }

        // Fill-only tile (no outline). The fill is the live LED tint, rewritten by
        // UpdateCardColors, so hover feedback is lift + slight opacity bump only.
        double restOpacity = isEmpty ? 0.45 : 1.0;
        var card = new Border
        {
            Background = cardBg,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = restOpacity
        };

        var knobCardTransform = new TranslateTransform(0, 0);
        card.RenderTransform = knobCardTransform;
        card.MouseEnter += (_, _) =>
        {
            knobCardTransform.Y = -1;
            if (isEmpty) card.Opacity = 0.65;
        };
        card.MouseLeave += (_, _) =>
        {
            knobCardTransform.Y = 0;
            card.Opacity = restOpacity;
        };
        card.MouseLeftButtonDown += (_, _) => _onNavigateToMixer?.Invoke(profileName);

        // Track active profile knob cards for live LED color sync
        if (isActiveProfile && idx >= 0 && idx < 5)
            _knobCards[idx] = card;

        var content = new StackPanel();

        // Knob number + label
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"K{idx + 1}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });

        if (!string.IsNullOrEmpty(knob.Label))
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = knob.Label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        content.Children.Add(titleRow);

        // Target
        var targetDisplay = FormatTarget(knob);

        if (knob.Target == "apps" && knob.Apps.Count > 0)
        {
            // Show apps as chips
            var chipWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            foreach (var app in knob.Apps.Take(4))
            {
                var accent = ThemeManager.Accent;
                chipWrap.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 3, 3),
                    Child = new TextBlock
                    {
                        Text = app,
                        FontSize = 11,
                    }.WithResourceForeground("TextSecBrush")
                });
            }
            if (knob.Apps.Count > 4)
            {
                chipWrap.Children.Add(new TextBlock
                {
                    Text = $"+{knob.Apps.Count - 4}",
                    FontSize = 11,
                    Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 0, 3),
                });
            }
            content.Children.Add(chipWrap);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = targetDisplay,
                FontSize = 12,
                Foreground = isEmpty
                    ? (SolidColorBrush)FindResource("TextDimBrush")
                    : (SolidColorBrush)FindResource("AccentBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        card.Child = content;
        return card;
    }

    private UIElement BuildButtonCard(int idx, ButtonConfig btn, string profileName)
    {
        bool hasTap = btn.Action != "none" && !string.IsNullOrEmpty(btn.Action);
        bool hasDouble = btn.DoublePressAction != "none" && !string.IsNullOrEmpty(btn.DoublePressAction);
        bool hasHold = btn.HoldAction != "none" && !string.IsNullOrEmpty(btn.HoldAction);
        bool isEmpty = !hasTap && !hasDouble && !hasHold;

        // Fill-only tile (no outline); hover tints the fill.
        var card = new Border
        {
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = isEmpty ? 0.45 : 1.0
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");

        var btnCardTransform = new TranslateTransform(0, 0);
        card.RenderTransform = btnCardTransform;
        card.MouseEnter += (_, _) =>
        {
            btnCardTransform.Y = -1;
            if (!isEmpty)
                card.Background = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x1E));
            else
                card.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        };
        card.MouseLeave += (_, _) =>
        {
            btnCardTransform.Y = 0;
            card.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        };
        card.MouseLeftButtonDown += (_, _) => _onNavigateToButtons?.Invoke(profileName);

        var content = new StackPanel();

        // Button number + label
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"B{idx + 1}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });

        if (!string.IsNullOrEmpty(btn.Label))
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = btn.Label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 90
            });
        }
        content.Children.Add(titleRow);

        if (isEmpty)
        {
            content.Children.Add(new TextBlock
            {
                Text = "No actions",
                FontSize = 12,
                Foreground = (SolidColorBrush)FindResource("TextDimBrush")
            });
        }
        else
        {
            if (hasTap)
                content.Children.Add(BuildGestureRow("TAP", btn.Action, btn.Path, btn.MacroKeys, btn.ProfileName, Color.FromRgb(0x66, 0xBB, 0x6A)));

            if (hasDouble)
                content.Children.Add(BuildGestureRow("DBL", btn.DoublePressAction, btn.DoublePressPath, btn.DoublePressMacroKeys, btn.DoublePressProfileName, Color.FromRgb(0xFF, 0xD5, 0x4F)));

            if (hasHold)
                content.Children.Add(BuildGestureRow("HOLD", btn.HoldAction, btn.HoldPath, btn.HoldMacroKeys, btn.HoldProfileName, Color.FromRgb(0xFF, 0x8A, 0x3D)));
        }

        card.Child = content;
        return card;
    }

    private static UIElement BuildGestureRow(string gestureLabel, string action, string path, string macroKeys, string profileName, Color gestureColor)
    {
        if (!ActionColors.TryGetValue(action, out var actionColor))
            actionColor = Color.FromRgb(0xCC, 0xCC, 0xCC); // fallback for unknown actions
        ActionDisplayNames.TryGetValue(action, out var displayName);

        displayName ??= System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(action.Replace("_", " "));

        // Append context for actions that need it
        var context = action switch
        {
            "launch_exe" or "close_program" or "mute_program" when !string.IsNullOrEmpty(path)
                => System.IO.Path.GetFileNameWithoutExtension(path),
            "switch_profile" when !string.IsNullOrEmpty(profileName)
                => profileName,
            "switch_profile" when !string.IsNullOrEmpty(path)
                => path,
            "macro" or "macro_hold" when !string.IsNullOrEmpty(macroKeys)
                => macroKeys,
            _ => null
        };
        if (context != null)
            displayName = $"{displayName}: {context}";

        // Grid: [badge 36px] [icon+action fills]
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Gesture badge — fixed width so TAP/DBL/HOLD all align
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, gestureColor.R, gestureColor.G, gestureColor.B)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = gestureLabel,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(gestureColor)
            }
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        // Action name (no icon — cleaner)
        var actionText = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(actionColor),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(actionText, 1);
        grid.Children.Add(actionText);

        return grid;
    }

    private static string GetActionDetail(string action, string path, string macroKeys, string profileName)
    {
        return action switch
        {
            "macro" or "macro_hold" when !string.IsNullOrEmpty(macroKeys) => macroKeys,
            "switch_profile" when !string.IsNullOrEmpty(profileName) => profileName,
            "mute_program" or "launch_exe" or "close_program" or "mute_active_window"
                when !string.IsNullOrEmpty(path) => System.IO.Path.GetFileName(path),
            "ha_toggle" or "ha_scene" or "ha_color" or "ha_color_temp" or "ha_service"
                when !string.IsNullOrEmpty(path) => path.Length > 20 ? path[..20] + "…" : path,
            "vm_mute_strip" when !string.IsNullOrEmpty(path) => $"Strip {path}",
            "vm_mute_bus" when !string.IsNullOrEmpty(path) => $"Bus {path}",
            _ => ""
        };
    }

    private static string FormatTarget(KnobConfig knob)
    {
        var t = knob.Target ?? "none";
        return t switch
        {
            "none" or "" => "—",
            "master" => "Master Volume",
            "mic" => "Microphone",
            "system" => "System Sounds",
            "any" => "Any Active",
            "active_window" => "Active Window",
            "apps" => knob.Apps.Count > 0
                ? string.Join(", ", knob.Apps.Select(FormatAppName))
                : "App Group",
            "output_device" => "Output Device",
            "input_device" => "Input Device",
            "monitor" => "Monitor Brightness",
            "led_brightness" => "LED Brightness",
            "discord_input_volume" => "Discord Input Volume",
            "discord_output_volume" => "Discord Output Volume",
            _ when t.StartsWith("ha_") => FormatHATarget(t),
            _ when t.StartsWith("govee:") => "Govee",
            _ when t == "govee" => "Govee",
            _ when t.StartsWith("vm_strip:") => $"VM Strip {t.Split(':')[1]}",
            _ when t.StartsWith("vm_bus:") => $"VM Bus {t.Split(':')[1]}",
            _ when t.StartsWith("obs:") => $"OBS · {t.Substring(4)}",
            "corsair_pump_fan" => "Corsair Pump Fan",
            "corsair_case_fan" => "Corsair Case Fans",
            _ => CamelToTitle(t)
        };
    }

    private static string FormatHATarget(string target)
    {
        // Config stores "ha_light:light.office_lamp" — extract domain and entity
        var parts = target.Split(':', 2);
        var domain = parts[0];
        var entityId = parts.Length > 1 ? parts[1] : "";

        var domainName = domain switch
        {
            "ha_light" => "Light",
            "ha_media" => "Media Player",
            "ha_fan" => "Fan",
            "ha_cover" => "Cover",
            _ => domain.Replace("ha_", "")
        };

        if (!string.IsNullOrEmpty(entityId))
        {
            // Convert "light.office_lamp" → "Office Lamp"
            var name = entityId.Contains('.') ? entityId.Split('.', 2)[1] : entityId;
            name = name.Replace("_", " ");
            name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
            return name;
        }

        return $"Home Assistant ({domainName})";
    }

    private static string FormatAppName(string app)
    {
        if (string.IsNullOrEmpty(app)) return app;
        var name = System.IO.Path.GetFileNameWithoutExtension(app);
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static string CamelToTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var result = new System.Text.StringBuilder();
        result.Append(char.ToUpperInvariant(s[0]));
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] == '_' || s[i] == '.')
            {
                result.Append(' ');
            }
            else
            {
                result.Append(s[i]);
            }
        }
        return result.ToString();
    }
}

internal static class BindingsViewTextExtensions
{
    public static TextBlock WithResourceForeground(this TextBlock tb, string key)
    {
        tb.SetResourceReference(TextBlock.ForegroundProperty, key);
        return tb;
    }
}
