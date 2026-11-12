using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AmpUp.Controls;
using AmpUp.Services;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// Room page chrome in the Settings / Overview / Groups look: glass hero header with live
/// status pills, segmented pill tab bar, large glass cards, device cards with integration
/// logos and an accent glow while the light is on. Pure presentation helpers; all device
/// control and config flow stays in RoomView.xaml.cs.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public partial class RoomView
{
    private static readonly string[] RoomTabNames = { "ROOM EFFECT", "DEVICES", "SCREEN SYNC" };
    private static readonly MaterialIconKind[] RoomTabIcons =
        { MaterialIconKind.PaletteOutline, MaterialIconKind.LightbulbGroupOutline, MaterialIconKind.MonitorShimmer };
    private static readonly string[] RoomTabTaglines =
    {
        "Pick an effect and colors for every light in the room",
        "Power, brightness and sync for every light",
        "Mirror your screen's colors to your room lights",
    };

    private TextBlock? _heroSubtitle;
    private Action? _heroPillPainter;

    // ── Hero ────────────────────────────────────────────────────────────

    private Border BuildRoomHero()
    {
        var accent = ThemeManager.Accent;
        var hero = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 18, 20, 18),
            Margin = new Thickness(0, 0, 0, 14),
        };
        var tile = new Border
        {
            Width = 56, Height = 56,
            CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new MaterialIcon { Kind = MaterialIconKind.LightbulbGroupOutline, Width = 28, Height = 28 },
        };
        ((MaterialIcon)tile.Child).SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        var glow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.55, Color = accent };
        tile.Effect = glow;

        void PaintHero()
        {
            var a = ThemeManager.Accent;
            hero.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x55));
            hero.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), 0),
                    new GradientStop(ThemeManager.WithAlpha(a, 0x10), 0.55),
                    new GradientStop(Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF), 1),
                },
                new Point(0, 0), new Point(1, 1));
            tile.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x26));
            tile.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x77));
            glow.Color = a;
        }
        PaintHero();
        Action onAccent = () => hero.Dispatcher.BeginInvoke(PaintHero);
        hero.Loaded += (_, _) => { ThemeManager.OnAccentChanged -= onAccent; ThemeManager.OnAccentChanged += onAccent; };
        hero.Unloaded += (_, _) => ThemeManager.OnAccentChanged -= onAccent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.Child = grid;
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var caption = new TextBlock { Text = "ROOM LIGHTING", FontSize = 10, FontWeight = FontWeights.SemiBold };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        text.Children.Add(caption);

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 2) };
        var title = new TextBlock { Text = "Room", FontSize = 22, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        titleRow.Children.Add(title);
        var help = UiKit.HelpTip("Room effects, Music Reactive, VU Fill and Screen Sync all drive the same lights, "
            + "so turning one on hands the lights over from the others. Govee, Corsair iCUE and Home Assistant "
            + "lights are managed on the Devices tab.", "Room lighting");
        help.Margin = new Thickness(8, 2, 0, 0);
        titleRow.Children.Add(help);
        text.Children.Add(titleRow);

        _heroSubtitle = new TextBlock
        {
            Text = RoomTabTaglines[Math.Clamp(_roomTabIndex, 0, RoomTabTaglines.Length - 1)],
            FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _heroSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        text.Children.Add(_heroSubtitle);

        var pills = new WrapPanel();
        text.Children.Add(pills);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        actions.Children.Add(RoomHeroButton(MaterialIconKind.CogOutline, "Integrations",
            "Set up Govee, Corsair iCUE and Home Assistant in Settings", () => NavigateToSettings?.Invoke()));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        _heroPillPainter = () =>
        {
            pills.Children.Clear();
            foreach (var p in BuildRoomStatusPills())
                pills.Children.Add(p);
        };
        _heroPillPainter();

        // Effect / mode state changes in many places (knob actions, Game Mode, button actions),
        // so repaint the pills on a slow timer while the hero is visible instead of hooking each one.
        var pillTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        pillTimer.Tick += (_, _) => _heroPillPainter?.Invoke();
        hero.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) { _heroPillPainter?.Invoke(); pillTimer.Start(); }
            else pillTimer.Stop();
        };
        hero.Unloaded += (_, _) => pillTimer.Stop();
        return hero;
    }

    private void RefreshRoomHero()
    {
        if (_heroSubtitle != null)
            _heroSubtitle.Text = RoomTabTaglines[Math.Clamp(_roomTabIndex, 0, RoomTabTaglines.Length - 1)];
        _heroPillPainter?.Invoke();
    }

    private List<Border> BuildRoomStatusPills()
    {
        var list = new List<Border>();
        if (_config == null) return list;

        var green = Color.FromRgb(0x00, 0xE6, 0x76);
        var dim = Color.FromRgb(0x9E, 0x9E, 0x9E);

        // Active effect
        string effect;
        bool effectOn = true;
        if (_activePattern == "__sync__") effect = "Mirroring knob LEDs";
        else if (_activePattern == RoomTemperaturePatternId) effect = $"White {_roomTemperatureKelvin}K";
        else if (!string.IsNullOrEmpty(_activePattern)) effect = HumanizeEffectName(_activePattern);
        else { effect = "No effect"; effectOn = false; }
        list.Add(RoomPill(effect, effectOn ? ThemeManager.Accent : dim, MaterialIconKind.PaletteOutline, "Active room effect"));

        bool screen = IsScreenSyncOn;
        list.Add(RoomPill(screen ? "Screen Sync on" : "Screen Sync off", screen ? Color.FromRgb(0x44, 0x8A, 0xFF) : dim,
            MaterialIconKind.MonitorShimmer, null));
        if (IsMusicReactiveOn)
            list.Add(RoomPill("Music Reactive", Color.FromRgb(0xFF, 0xB8, 0x00), MaterialIconKind.MusicNote, null));
        if (_vuFillActive)
            list.Add(RoomPill("VU Fill", Color.FromRgb(0xFF, 0x40, 0x81), MaterialIconKind.Equalizer, null));
        if (_config.Ambience.GameModeEnabled)
            list.Add(RoomPill("Game Mode", Color.FromRgb(0xFF, 0x6B, 0x35), MaterialIconKind.GamepadVariant, null));

        // Lights: how many of the configured lights are on.
        int total = 0, on = 0;
        if (_config.Ambience.GoveeEnabled || _config.Ambience.GoveeCloudEnabled)
            foreach (var d in _config.Ambience.GoveeDevices)
            {
                total++;
                if (d.PoweredOn) on++;
            }
        foreach (var l in _config.Ambience.HomeAssistantLights ?? new List<HALightConfig>())
        {
            if (!l.Enabled) continue;
            total++;
            if (l.PoweredOn) on++;
        }
        if (_config.Corsair.Enabled && _corsairSync?.IsAvailable == true)
        {
            int c = _corsairSync.Devices.Count;
            total += c;
            on += c;
        }
        list.Add(RoomPill(total == 0 ? "No lights" : $"{on} of {total} lights on", on > 0 ? green : dim,
            MaterialIconKind.LightbulbOnOutline, "Govee and Home Assistant power state; Corsair devices count as on while iCUE is connected"));
        return list;
    }

    private static string HumanizeEffectName(string id)
    {
        var sb = new System.Text.StringBuilder(id.Length + 4);
        for (int i = 0; i < id.Length; i++)
        {
            char ch = id[i];
            if (i > 0 && char.IsUpper(ch) && !char.IsUpper(id[i - 1])) sb.Append(' ');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static Border RoomPill(string text, Color color, MaterialIconKind icon, string? tooltip)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new MaterialIcon { Kind = icon, Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(color) });
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
            ToolTip = tooltip,
            Child = sp,
        };
    }

    private static Border RoomHeroButton(MaterialIconKind kind, string label, string tooltip, Action onClick)
    {
        var icon = new MaterialIcon { Kind = kind, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        icon.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        var tb = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(icon);
        sp.Children.Add(tb);

        var b = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 14, 7),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            VerticalAlignment = VerticalAlignment.Center,
            Child = sp,
        };
        void Paint(bool hover)
        {
            var a = ThemeManager.Accent;
            b.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, hover ? (byte)0x33 : (byte)0x1A));
            b.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, hover ? (byte)0xAA : (byte)0x55));
        }
        Paint(false);
        b.MouseEnter += (_, _) => Paint(true);
        b.MouseLeave += (_, _) => Paint(false);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        AddRoomHoverLift(b, 1.5);
        return b;
    }

    // ── Segmented pill tab bar ──────────────────────────────────────────

    private Border BuildRoomTabBar()
    {
        var bar = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5),
            Margin = new Thickness(0, 0, 0, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        bar.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        bar.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Child = row;
        var tabs = new Border[RoomTabNames.Length];
        for (int i = 0; i < RoomTabNames.Length; i++)
        {
            int idx = i;
            var icon = new MaterialIcon { Kind = RoomTabIcons[i], Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = RoomTabNames[i], FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(icon);
            sp.Children.Add(label);
            var tab = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 8, 20, 8),
                Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                Cursor = Cursors.Hand,
                Child = sp,
            };
            tabs[i] = tab;
            tab.MouseEnter += (_, _) => { if (idx != _roomTabIndex) PaintRoomPillTab(tab, false, true); };
            tab.MouseLeave += (_, _) => { if (idx != _roomTabIndex) PaintRoomPillTab(tab, false, false); };
            tab.MouseLeftButtonDown += (_, _) =>
            {
                if (_roomTabIndex == idx) return;
                _roomTabIndex = idx;
                for (int j = 0; j < tabs.Length; j++)
                    PaintRoomPillTab(tabs[j], j == idx, false);
                RebuildRoomTabContent();
            };
            row.Children.Add(tab);
        }
        for (int i = 0; i < tabs.Length; i++)
            PaintRoomPillTab(tabs[i], i == _roomTabIndex, false);

        Action onAccent = () => bar.Dispatcher.BeginInvoke(() =>
        {
            for (int i = 0; i < tabs.Length; i++)
                PaintRoomPillTab(tabs[i], i == _roomTabIndex, false);
        });
        bar.Loaded += (_, _) => { ThemeManager.OnAccentChanged -= onAccent; ThemeManager.OnAccentChanged += onAccent; };
        bar.Unloaded += (_, _) => ThemeManager.OnAccentChanged -= onAccent;
        return bar;
    }

    /// <summary>
    /// Pill tab visuals shared by the main Room tab bar and the effect category chips:
    /// active = accent tint + accent border + soft glow; hover = subtle lift in brightness.
    /// The tab's child is either a TextBlock or a StackPanel of [icon, label].
    /// </summary>
    private static void PaintRoomPillTab(Border tab, bool active, bool hover)
    {
        var a = ThemeManager.Accent;
        TextBlock? label = tab.Child as TextBlock;
        MaterialIcon? icon = null;
        if (tab.Child is StackPanel sp)
            foreach (var c in sp.Children)
            {
                if (c is TextBlock t) label = t;
                else if (c is MaterialIcon m) icon = m;
            }

        if (active)
        {
            tab.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x2A));
            tab.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x88));
            tab.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Opacity = 0.35, Color = a };
            if (label != null) { label.Foreground = new SolidColorBrush(a); label.FontWeight = FontWeights.Bold; }
            if (icon != null) icon.Foreground = new SolidColorBrush(a);
        }
        else
        {
            tab.Background = hover ? new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
            tab.BorderBrush = Brushes.Transparent;
            tab.Effect = null;
            if (label != null)
            {
                label.SetResourceReference(TextBlock.ForegroundProperty, hover ? "TextPrimaryBrush" : "TextSecBrush");
                label.FontWeight = FontWeights.SemiBold;
            }
            icon?.SetResourceReference(MaterialIcon.ForegroundProperty, hover ? "TextPrimaryBrush" : "TextDimBrush");
        }
    }

    // ── Cards ───────────────────────────────────────────────────────────

    /// <summary>UiKit.Card restyled as a big rounded glass card (Settings SetCard look).</summary>
    private static UiKit.CardParts RoomCard(Color color, MaterialIconKind icon, string title, string? subtitle,
        FrameworkElement? actions = null, UIElement? body = null, string? help = null)
    {
        var parts = UiKit.Card(color, icon, title, subtitle, actions, body, help);
        StyleRoomGlassCard(parts.Root);
        return parts;
    }

    private static void StyleRoomGlassCard(Border card)
    {
        card.CornerRadius = new CornerRadius(16);
        card.Margin = new Thickness(0, 0, 0, 14);
    }

    /// <summary>
    /// Large device card: integration logo tile, name + detail + On/Off pill, controls on the
    /// right, body below. Hover lift; accent glow in the device colour while the light is on.
    /// <paramref name="power"/> is the logical CheckBox model (null = always "on", e.g. Corsair).
    /// </summary>
    private UiKit.CardParts BuildRoomDeviceCard(Color color, FrameworkElement logo, string title, string? subtitle,
        FrameworkElement? controls, UIElement? body, CheckBox? power)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 0, 0, 12),
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");

        var stack = new StackPanel();
        card.Child = stack;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stack.Children.Add(header);

        logo.Margin = new Thickness(0, 0, 14, 0);
        logo.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(logo);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var t = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        titleRow.Children.Add(t);
        var statusText = new TextBlock { FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var statusPill = new Border
        {
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(10, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = statusText,
        };
        titleRow.Children.Add(statusPill);
        names.Children.Add(titleRow);
        var s = new TextBlock
        {
            Text = subtitle ?? "", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
            Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible,
        };
        s.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        names.Children.Add(s);
        Grid.SetColumn(names, 1);
        header.Children.Add(names);

        if (controls != null)
        {
            controls.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(controls, 2);
            header.Children.Add(controls);
        }

        var bodyHost = new StackPanel();
        if (body != null)
        {
            bodyHost.Margin = new Thickness(0, 14, 0, 0);
            bodyHost.Children.Add(body);
        }
        stack.Children.Add(bodyHost);

        void Paint()
        {
            bool on = power == null || power.IsChecked == true;
            var green = Color.FromRgb(0x00, 0xE6, 0x76);
            var dim = Color.FromRgb(0x9E, 0x9E, 0x9E);
            var pc = on ? green : dim;
            statusText.Text = power == null ? "ACTIVE" : on ? "ON" : "OFF";
            statusText.Foreground = new SolidColorBrush(pc);
            statusPill.Background = new SolidColorBrush(ThemeManager.WithAlpha(pc, 0x18));
            statusPill.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(pc, 0x55));
            if (on)
            {
                card.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x88));
                card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.28, Color = color };
            }
            else
            {
                card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
                card.Effect = null;
            }
        }
        Paint();
        if (power != null)
        {
            power.Checked += (_, _) => Paint();
            power.Unchecked += (_, _) => Paint();
        }
        AddRoomHoverLift(card, 2);

        return new UiKit.CardParts { Root = card, Title = t, Subtitle = s, Body = bodyHost };
    }

    /// <summary>Glass logo tile: bundled/installed app icon when available, otherwise a tinted glyph.</summary>
    private static Border IntegrationLogoTile(string? app, MaterialIconKind fallback, Color color, double size = 44)
    {
        System.Windows.Media.Imaging.BitmapSource? img = null;
        if (app != null)
        {
            try { img = IntegrationAppIcons.Get(app); } catch { img = null; }
        }
        FrameworkElement child = img != null
            ? new Image { Source = img, Width = size * 0.6, Height = size * 0.6, Stretch = Stretch.Uniform }
            : new MaterialIcon { Kind = fallback, Width = size * 0.52, Height = size * 0.52, Foreground = new SolidColorBrush(color) };
        if (child is Image) RenderOptions.SetBitmapScalingMode(child, BitmapScalingMode.HighQuality);
        return new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x22)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x66)),
            Child = child,
        };
    }

    /// <summary>Section header with the integration logo: [logo] GOVEE  3 devices.</summary>
    private static FrameworkElement IntegrationSectionHeader(string? app, MaterialIconKind fallback, Color color, string text, string? count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 10) };
        var logo = IntegrationLogoTile(app, fallback, color, 24);
        logo.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(logo);
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        row.Children.Add(label);
        var c = new TextBlock { Text = count ?? "", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        c.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        row.Children.Add(c);
        return row;
    }

    /// <summary>Accent call-to-action button used in empty states.</summary>
    private static Border RoomCtaButton(MaterialIconKind kind, string label, Action onClick) =>
        RoomHeroButton(kind, label, label, onClick);

    /// <summary>Big glass empty state with an accent icon tile and calls to action.</summary>
    private static Border RoomEmptyState(MaterialIconKind icon, string title, string body, params FrameworkElement[] actions)
    {
        var a = ThemeManager.Accent;
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 26, 28, 26),
            Margin = new Thickness(0, 0, 0, 14),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x44)),
            Background = new LinearGradientBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x04, 0xFF, 0xFF, 0xFF), 90),
        };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        card.Child = stack;
        var tileIcon = new MaterialIcon { Kind = icon, Width = 26, Height = 26, Foreground = new SolidColorBrush(a) };
        stack.Children.Add(new Border
        {
            Width = 52, Height = 52,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x22)),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x66)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.3, Color = a },
            Child = tileIcon,
        });
        var t1 = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        t1.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(t1);
        var t2 = new TextBlock { Text = body, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 420, Margin = new Thickness(0, 4, 0, 0) };
        t2.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        stack.Children.Add(t2);
        if (actions.Length > 0)
        {
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            foreach (var act in actions)
            {
                act.Margin = new Thickness(4, 0, 4, 4);
                row.Children.Add(act);
            }
            stack.Children.Add(row);
        }
        return card;
    }

    private static void AddRoomHoverLift(FrameworkElement el, double lift)
    {
        var tt = new TranslateTransform();
        el.RenderTransform = tt;
        el.MouseEnter += (_, _) => AnimateRoomLift(tt, -lift);
        el.MouseLeave += (_, _) => AnimateRoomLift(tt, 0);
    }

    private static void AnimateRoomLift(TranslateTransform tt, double y)
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
}
