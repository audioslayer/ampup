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
/// status pills, icon dock tab bar, large glass cards, device cards with integration
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
        var hero = new Border
        {
            Padding = new Thickness(20, 18, 20, 18),
            Margin = new Thickness(0, 0, 0, 14),
        };
        GlassStyle.ApplyGlassCard(hero, 16, accentBorder: true);
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
        // Hero card chrome comes from GlassStyle (resource references, live); only the single hero
        // icon tile keeps a glow, created via GlassStyle.GlowEffect so Reduce effects removes it.
        void PaintHero()
        {
            var a = ThemeManager.Accent;
            var (fill, border) = GlassStyle.AccentPillBrushes(a);
            tile.Background = fill;
            tile.BorderBrush = border;
            tile.Effect = GlassStyle.GlowEffect(a, 22, 0.55);
        }
        PaintHero();
        Action onAccent = () => hero.Dispatcher.BeginInvoke(PaintHero);
        hero.Loaded += (_, _) =>
        {
            ThemeManager.OnAccentChanged -= onAccent; ThemeManager.OnAccentChanged += onAccent;
            ThemeManager.OnReduceEffectsChanged -= onAccent; ThemeManager.OnReduceEffectsChanged += onAccent;
        };
        hero.Unloaded += (_, _) => { ThemeManager.OnAccentChanged -= onAccent; ThemeManager.OnReduceEffectsChanged -= onAccent; };

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
        var dock = BuildRoomTabDock();
        dock.Margin = new Thickness(0, 0, 12, 0);
        actions.Children.Add(dock);
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
        var fg = RoomFrozenBrush(color);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new MaterialIcon { Kind = icon, Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = fg });
        sp.Children.Add(new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = fg });
        return new Border
        {
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 10, 3),
            Margin = new Thickness(0, 0, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = RoomFrozenBrush(ThemeManager.WithAlpha(color, 0x18)),
            BorderBrush = RoomFrozenBrush(ThemeManager.WithAlpha(color, 0x55)),
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
        // Cached frozen accent-alpha brushes from ThemeManager (resource references: no per-hover
        // allocation and they follow accent changes live).
        void Paint(bool hover)
        {
            b.SetResourceReference(Border.BackgroundProperty, hover ? "AccentBg30Brush" : "AccentHover1ABrush");
            b.SetResourceReference(Border.BorderBrushProperty, hover ? "AccentScrollAABrush" : "AccentLabel66Brush");
        }
        Paint(false);
        b.MouseEnter += (_, _) => Paint(true);
        b.MouseLeave += (_, _) => Paint(false);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        GlassStyle.AttachHoverLift(b);
        return b;
    }

    // ── Icon dock tab bar (hero header) ─────────────────────────────────

    private IconDock BuildRoomTabDock()
    {
        var dock = new IconDock();
        for (int i = 0; i < RoomTabNames.Length; i++)
            dock.AddItem(RoomTabNames[i], RoomTabNames[i], RoomTabIcons[i]);
        dock.SelectedIndex = _roomTabIndex;
        dock.SelectionChanged += (_, _) =>
        {
            if (dock.SelectedIndex < 0 || _roomTabIndex == dock.SelectedIndex) return;
            _roomTabIndex = dock.SelectedIndex;
            RebuildRoomTabContent();
        };
        return dock;
    }

    /// <summary>
    /// Pill tab visuals shared by the main Room tab bar and the effect category chips:
    /// active = accent tint + accent border + soft glow (only the one active pill, via
    /// GlassStyle.GlowEffect); hover = subtle lift in brightness. Brushes are cached/frozen.
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
            var (fill, border) = GlassStyle.AccentPillBrushes(a);
            tab.Background = fill;
            tab.BorderBrush = border;
            tab.Effect = GlassStyle.GlowEffect(a, 14, 0.35);
            if (label != null) { label.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); label.FontWeight = FontWeights.Bold; }
            icon?.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        }
        else
        {
            tab.Background = hover ? RoomPillHoverBrush : Brushes.Transparent;
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

    private static readonly Brush RoomPillHoverBrush = RoomFrozenBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    private static SolidColorBrush RoomFrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ── Cards ───────────────────────────────────────────────────────────

    /// <summary>UiKit.Card restyled as the shared GlassStyle glass card.</summary>
    private static UiKit.CardParts RoomCard(Color color, MaterialIconKind icon, string title, string? subtitle,
        FrameworkElement? actions = null, UIElement? body = null, string? help = null)
    {
        var parts = UiKit.Card(color, icon, title, subtitle, actions, body, help);
        StyleRoomGlassCard(parts.Root);
        return parts;
    }

    private static void StyleRoomGlassCard(Border card)
    {
        GlassStyle.ApplyGlassCard(card, 16);
        card.Margin = new Thickness(0, 0, 0, 14);
    }

    /// <summary>
    /// Large device card: integration logo tile, name + detail + On/Off pill, controls on the
    /// right, body below. Glass card + hover lift; brighter border in the device colour while the
    /// light is on (no per-card shadow effect: these repeat in lists).
    /// <paramref name="power"/> is the logical CheckBox model (null = always "on", e.g. Corsair).
    /// </summary>
    private UiKit.CardParts BuildRoomDeviceCard(Color color, FrameworkElement logo, string title, string? subtitle,
        FrameworkElement? controls, UIElement? body, CheckBox? power)
    {
        var card = new Border
        {
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 0, 0, 12),
        };
        GlassStyle.ApplyGlassCard(card, 16);
        card.BorderThickness = new Thickness(1.5);

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

        var onBorder = RoomFrozenBrush(ThemeManager.WithAlpha(color, 0xAA));
        void Paint()
        {
            bool on = power == null || power.IsChecked == true;
            statusText.Text = power == null ? "ACTIVE" : on ? "ON" : "OFF";
            statusText.Foreground = on ? RoomStatusOnFg : RoomStatusOffFg;
            statusPill.Background = on ? RoomStatusOnFill : RoomStatusOffFill;
            statusPill.BorderBrush = on ? RoomStatusOnBorder : RoomStatusOffBorder;
            if (on) card.BorderBrush = onBorder;
            else card.SetResourceReference(Border.BorderBrushProperty, "GlassCardBorderBrush");
        }
        Paint();
        if (power != null)
        {
            power.Checked += (_, _) => Paint();
            power.Unchecked += (_, _) => Paint();
        }
        GlassStyle.AttachHoverLift(card);

        return new UiKit.CardParts { Root = card, Title = t, Subtitle = s, Body = bodyHost };
    }

    private static readonly Brush RoomStatusOnFg = RoomFrozenBrush(Color.FromRgb(0x00, 0xE6, 0x76));
    private static readonly Brush RoomStatusOnFill = RoomFrozenBrush(Color.FromArgb(0x18, 0x00, 0xE6, 0x76));
    private static readonly Brush RoomStatusOnBorder = RoomFrozenBrush(Color.FromArgb(0x55, 0x00, 0xE6, 0x76));
    private static readonly Brush RoomStatusOffFg = RoomFrozenBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly Brush RoomStatusOffFill = RoomFrozenBrush(Color.FromArgb(0x18, 0x9E, 0x9E, 0x9E));
    private static readonly Brush RoomStatusOffBorder = RoomFrozenBrush(Color.FromArgb(0x55, 0x9E, 0x9E, 0x9E));

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
            : new MaterialIcon { Kind = fallback, Width = size * 0.52, Height = size * 0.52, Foreground = RoomFrozenBrush(color) };
        if (child is Image) RenderOptions.SetBitmapScalingMode(child, BitmapScalingMode.HighQuality);
        return new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Background = RoomFrozenBrush(ThemeManager.WithAlpha(color, 0x22)),
            BorderBrush = RoomFrozenBrush(ThemeManager.WithAlpha(color, 0x66)),
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
            Padding = new Thickness(28, 26, 28, 26),
            Margin = new Thickness(0, 0, 0, 14),
        };
        GlassStyle.ApplyGlassCard(card, 16, accentBorder: true);
        var (tileFill, tileBorder) = GlassStyle.AccentPillBrushes(a);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        card.Child = stack;
        var tileIcon = new MaterialIcon { Kind = icon, Width = 26, Height = 26 };
        tileIcon.SetResourceReference(MaterialIcon.ForegroundProperty, "AccentBrush");
        // Cheap faux glow behind the tile instead of a DropShadowEffect.
        var tileHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        tileHost.Children.Add(new Border
        {
            Width = 84, Height = 84, Margin = new Thickness(-16),
            Background = GlassStyle.FauxGlow(a, 42),
            IsHitTestVisible = false,
        });
        tileHost.Children.Add(new Border
        {
            Width = 52, Height = 52,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = tileFill,
            BorderBrush = tileBorder,
            Child = tileIcon,
        });
        stack.Children.Add(tileHost);
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

}
