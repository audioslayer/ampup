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


    // ── Hero ────────────────────────────────────────────────────────────

    /// <summary>Compact page header, same pattern as the Lights page: tile + title + subtitle, tabs, help tip.</summary>
    private FrameworkElement BuildRoomHero()
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dock = BuildRoomTabDock();
        dock.Margin = new Thickness(0, 0, 12, 0);
        actions.Children.Add(dock);
        actions.Children.Add(RoomHeroButton(MaterialIconKind.CogOutline, "Integrations",
            "Set up Govee, Corsair iCUE and Home Assistant in Settings", () => NavigateToSettings?.Invoke()));
        var help = UiKit.HelpTip("Room effects, Music Reactive, VU Fill and Screen Sync all drive the same lights, "
            + "so turning one on hands the lights over from the others. Govee, Corsair iCUE and Home Assistant "
            + "lights are managed on the Devices tab.", "Room lighting");
        help.Margin = new Thickness(12, 0, 0, 0);
        help.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(help);
        return UiKit.PageHeader("Room", "Effects, devices and screen sync for your room lights", actions,
            icon: MaterialIconKind.LightbulbGroupOutline);
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
    /// Device card ("colour strip" design): one flat CardBg surface with a 1px hairline border,
    /// a rounded strip on the left in the light's colour (dimmed while off), then a single row of
    /// [logo] [name + On/Off pill + detail] [brightness] [controls]; body (Advanced etc.) below.
    /// Below <see cref="RoomDeviceCardWrapWidth"/> the brightness row wraps under the header.
    /// <paramref name="power"/> is the logical CheckBox model (null = always "on", e.g. Corsair).
    /// </summary>
    private UiKit.CardParts BuildRoomDeviceCard(Color color, FrameworkElement logo, string title, string? subtitle,
        FrameworkElement? controls, UIElement? body, CheckBox? power, FrameworkElement? brightness = null)
    {
        var card = new Border
        {
            Padding = new Thickness(12, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = outer;

        var strip = new Border { Width = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 14, 0) };
        outer.Children.Add(strip);

        var stack = new StackPanel();
        Grid.SetColumn(stack, 1);
        outer.Children.Add(stack);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Name column is Auto when a brightness row fills the star column, otherwise it takes the space.
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = brightness != null ? GridLength.Auto : new GridLength(1, GridUnitType.Star), MinWidth = 140 });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = brightness != null ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.Children.Add(header);

        logo.Margin = new Thickness(0, 0, 14, 0);
        logo.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(logo);

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), MaxWidth = 280 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var t = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        titleRow.Children.Add(t);
        var statusText = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var statusPill = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = statusText,
        };
        titleRow.Children.Add(statusPill);
        names.Children.Add(titleRow);
        var s = new TextBlock
        {
            Text = subtitle ?? "", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0),
            Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible,
        };
        s.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        names.Children.Add(s);
        Grid.SetColumn(names, 1);
        header.Children.Add(names);

        if (brightness != null)
        {
            brightness.VerticalAlignment = VerticalAlignment.Center;
            brightness.MinWidth = 180;
            header.Children.Add(brightness);
        }

        if (controls != null)
        {
            controls.VerticalAlignment = VerticalAlignment.Center;
            controls.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(controls, 3);
            header.Children.Add(controls);
        }

        // Wide: brightness sits in the row between the name and the switches. Narrow: it
        // drops to a second row under the name so the switches keep their space.
        void Layout(double width)
        {
            if (brightness == null) return;
            bool narrow = width < RoomDeviceCardWrapWidth;
            Grid.SetRow(brightness, narrow ? 1 : 0);
            Grid.SetColumn(brightness, narrow ? 1 : 2);
            Grid.SetColumnSpan(brightness, narrow ? 3 : 1);
            brightness.Margin = narrow ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        }
        Layout(double.PositiveInfinity);
        card.SizeChanged += (_, e) => { if (e.WidthChanged) Layout(e.NewSize.Width); };

        var bodyHost = new StackPanel();
        if (body != null)
        {
            bodyHost.Margin = new Thickness(0, 6, 0, 0);
            bodyHost.Children.Add(body);
        }
        stack.Children.Add(bodyHost);

        var stripOn = RoomFrozenBrush(color);
        var stripOff = RoomFrozenBrush(ThemeManager.WithAlpha(color, 0x55));
        void Paint()
        {
            bool on = power == null || power.IsChecked == true;
            statusText.Text = power == null ? "ACTIVE" : on ? "ON" : "OFF";
            statusText.Foreground = on ? RoomStatusOnFg : RoomStatusOffFg;
            statusPill.Background = on ? RoomStatusOnFill : RoomStatusOffFill;
            strip.Background = on ? stripOn : stripOff;
        }
        Paint();
        if (power != null)
        {
            power.Checked += (_, _) => Paint();
            power.Unchecked += (_, _) => Paint();
        }
        card.MouseEnter += (_, _) => card.BorderBrush = RoomDeviceCardHoverBorder;
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        return new UiKit.CardParts { Root = card, Title = t, Subtitle = s, Body = bodyHost };
    }

    private const double RoomDeviceCardWrapWidth = 860;
    private static readonly Brush RoomDeviceCardHoverBorder = RoomFrozenBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    /// <summary>Inline "Brightness [slider] 42%" for a device card's header row.</summary>
    private DockPanel RoomDeviceBrightnessRow(StyledSlider slider, TextBlock valueLabel)
    {
        var row = new DockPanel();
        var name = new TextBlock
        {
            Text = "Brightness", FontSize = 11, Margin = new Thickness(0, 0, 10, 0),
            Foreground = FindBrush("TextSecBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(valueLabel, Dock.Right);
        row.Children.Add(name);
        row.Children.Add(valueLabel);
        row.Children.Add(slider);
        return row;
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
