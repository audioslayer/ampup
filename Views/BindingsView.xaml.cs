using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AmpUp.Controls;
using Material.Icons;

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
        { "launch_exe",         Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "close_program",      Color.FromRgb(0xFF, 0x7C, 0x43) },
        { "cycle_output",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "cycle_input",        Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_output",      Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "select_input",       Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "macro",              Color.FromRgb(0xFF, 0xD5, 0x4F) },
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
        { "mute_app_group", "Mute Group" }, { "mute_device", "Mute Device" },
        { "launch_exe", "Launch" }, { "close_program", "Close App" },
        { "cycle_output", "Cycle Out" }, { "cycle_input", "Cycle In" },
        { "select_output", "Set Output" }, { "select_input", "Set Input" },
        { "macro", "Macro" }, { "switch_profile", "Profile" },
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

        Loaded += (_, _) => { HookOwnerWindow(); UpdateColorTimerForWindowState(); };
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

    private void Rebuild()
    {
        if (_config == null) return;
        _root.Children.Clear();
        Array.Clear(_knobCards);

        _root.Children.Add(UiKit.PageHeader("Overview", "Every assignment across all your profiles",
            icon: Material.Icons.MaterialIconKind.ViewDashboardOutline));

        // Interactive hardware device visualization — the widget only
        // renders the 5-knob Turn Up device, so hide it entirely when
        // the user's Active Surface isn't showing Turn Up.
        if (ShouldShowTurnUpOverview())
        {
            _hardwareWidget.LoadConfig(_config);
            _root.Children.Add(_hardwareWidget);
        }

        // Render current profile first, then other profiles
        var profiles = new List<string> { _config.ActiveProfile };
        foreach (var p in _config.Profiles)
        {
            if (p != _config.ActiveProfile) profiles.Add(p);
        }

        foreach (var profileName in profiles)
        {
            AppConfig profileConfig;
            if (profileName == _config.ActiveProfile)
            {
                profileConfig = _config;
            }
            else
            {
                var loaded = ConfigManager.LoadProfile(profileName);
                if (loaded == null)
                {
                    // Profile file doesn't exist yet — use defaults
                    loaded = new AppConfig();
                }
                profileConfig = loaded;
            }

            _root.Children.Add(BuildProfileSection(profileName, profileConfig));
        }
    }

    private UIElement BuildProfileSection(string profileName, AppConfig config)
    {
        bool isActive = _config?.ActiveProfile == profileName;

        var iconCfg = _config?.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        Color accentColor;
        try { accentColor = (Color)ColorConverter.ConvertFromString(iconCfg.Color); }
        catch { accentColor = ThemeManager.Accent; }

        var capturedName = profileName;

        // Right side actions — reorder, duplicate, preview OSD (icon buttons like Groups cards).
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isActive)
        {
            var badge = UiKit.StatusBadge(out var setBadge);
            setBadge("ACTIVE", true);
            actionRow.Children.Add(badge);
        }

        int profileIdx = _config?.Profiles.IndexOf(profileName) ?? -1;
        int profileCount = _config?.Profiles.Count ?? 0;

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

        actionRow.Children.Add(UiKit.IconButton(MaterialIconKind.ContentCopy, "Duplicate this profile",
            _ => OnDuplicateProfile?.Invoke(capturedName)));
        actionRow.Children.Add(UiKit.IconButton(MaterialIconKind.EyeOutline, "Preview the OSD for this profile",
            _ => _onPreviewOsd?.Invoke(capturedName)));

        if (!Enum.TryParse<Material.Icons.MaterialIconKind>(iconCfg.Symbol, out var profileKind))
            profileKind = MaterialIconKind.AccountCircleOutline;

        var parts = UiKit.Card(accentColor, profileKind, profileName,
            isActive ? "Active profile" : "Click to switch to this profile", actionRow);
        parts.Root.Margin = new Thickness(0, 0, 0, 14);
        parts.Body.Margin = new Thickness(0, 16, 0, 0);
        if (parts.Title.Parent is FrameworkElement names)
        {
            names.Cursor = Cursors.Hand;
            names.ToolTip = isActive ? null : $"Switch to {profileName}";
            names.MouseLeftButtonDown += (_, _) => _onSwitchProfile?.Invoke(capturedName);
        }
        var sectionContent = parts.Body;

        // Turn Up rows (KNOBS + BUTTONS) follow the Active Surface toggle
        // — hidden when the user has picked Stream Controller as their
        // surface, even if a Turn Up is physically connected.
        if (ShouldShowTurnUpOverview())
        {
            sectionContent.Children.Add(MakeSectionLabel("KNOBS", "5"));

            var knobsRow = new UniformGrid
            {
                Columns = 5,
                Margin = new Thickness(0, 0, 0, 16)
            };
            for (int i = 0; i < 5; i++)
            {
                var knob = config.Knobs.FirstOrDefault(k => k.Idx == i) ?? new KnobConfig { Idx = i };
                var light = config.Lights.FirstOrDefault(l => l.Idx == i);
                knobsRow.Children.Add(BuildKnobCard(i, knob, capturedName, light));
            }
            sectionContent.Children.Add(knobsRow);

            sectionContent.Children.Add(MakeSectionLabel("BUTTONS", "5"));

            var buttonsRow = new UniformGrid
            {
                Columns = 5,
                Margin = new Thickness(0, 0, 0, 0)
            };
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
        if (ShouldShowStreamControllerOverview())
            sectionContent.Children.Add(BuildStreamControllerSection(config, capturedName));

        return parts.Root;
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

        var card = new Border
        {
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            Cursor = Cursors.Hand,
            Opacity = hasContent ? 1.0 : 0.5,
        };
        card.MouseEnter += (_, _) =>
            card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x80));
        card.MouseLeave += (_, _) =>
            card.BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
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
            FontSize = 9,
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
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 90,
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

        var card = new Border
        {
            Background = cardBg,
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = isEmpty ? 0.45 : 1.0
        };

        var knobCardTransform = new TranslateTransform(0, 0);
        card.RenderTransform = knobCardTransform;
        card.MouseEnter += (_, _) =>
        {
            knobCardTransform.Y = -1;
            if (!isEmpty)
                card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x80));
            else
                card.BorderBrush = (SolidColorBrush)FindResource("InputBorderBrush");
        };
        card.MouseLeave += (_, _) =>
        {
            knobCardTransform.Y = 0;
            card.BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
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
            FontSize = 9,
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
                FontSize = 11,
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
                    Background = new SolidColorBrush(Color.FromArgb(0x20, accent.R, accent.G, accent.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, accent.R, accent.G, accent.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 3, 3),
                    Child = new TextBlock
                    {
                        Text = app,
                        FontSize = 9,
                    }.WithResourceForeground("TextSecBrush")
                });
            }
            if (knob.Apps.Count > 4)
            {
                chipWrap.Children.Add(new TextBlock
                {
                    Text = $"+{knob.Apps.Count - 4}",
                    FontSize = 9,
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
                FontSize = 11,
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

        var card = new Border
        {
            Background = (SolidColorBrush)FindResource("BgDarkBrush"),
            BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Opacity = isEmpty ? 0.45 : 1.0
        };

        var btnCardTransform = new TranslateTransform(0, 0);
        card.RenderTransform = btnCardTransform;
        card.MouseEnter += (_, _) =>
        {
            btnCardTransform.Y = -1;
            if (!isEmpty)
                card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x80));
            else
                card.BorderBrush = (SolidColorBrush)FindResource("InputBorderBrush");
        };
        card.MouseLeave += (_, _) =>
        {
            btnCardTransform.Y = 0;
            card.BorderBrush = (SolidColorBrush)FindResource("CardBorderBrush");
        };
        card.MouseLeftButtonDown += (_, _) => _onNavigateToButtons?.Invoke(profileName);

        var content = new StackPanel();

        // Button number + label
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"B{idx + 1}",
            FontSize = 9,
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
                FontSize = 11,
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
                FontSize = 10,
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
            "macro" when !string.IsNullOrEmpty(macroKeys)
                => macroKeys,
            _ => null
        };
        if (context != null)
            displayName = $"{displayName}: {context}";

        // Grid: [badge 36px] [icon+action fills]
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
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
                FontSize = 8,
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
            FontSize = 10,
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
            "macro" when !string.IsNullOrEmpty(macroKeys) => macroKeys,
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
            _ when t.StartsWith("ha_") => FormatHATarget(t),
            _ when t.StartsWith("govee:") => "Govee",
            _ when t == "govee" => "Govee",
            _ when t.StartsWith("vm_strip:") => $"VM Strip {t.Split(':')[1]}",
            _ when t.StartsWith("vm_bus:") => $"VM Bus {t.Split(':')[1]}",
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
