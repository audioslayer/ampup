using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AmpUp.Controls;

/// <summary>
/// Shared "glass" look, defined once. Everything here is cheap to draw: frozen brushes only,
/// no blur, no per-card shadows, no animated effects. Brushes are cached and rebuilt only when
/// the card theme or accent changes. See Controls/GLASS_STYLE.md for the rules and a cheat sheet.
/// Call from the UI thread.
/// </summary>
public static class GlassStyle
{
    // ── Cache ────────────────────────────────────────────────────────
    private static Brush? _cardFill;
    private static Brush? _cardBorder;
    private static Brush? _cardBorderAccent;
    private static Brush? _flatBorder;
    private static Brush? _flatBorderAccent;
    private static Brush? _pillFill;
    private static Brush? _pillBorder;
    private static Color _pillAccent;
    private static readonly Dictionary<(Color, double), RadialGradientBrush> _glowCache = new();

    static GlassStyle()
    {
        ThemeManager.OnCardThemeChanged += () => { _cardFill = null; _flatBorder = null; PublishResources(); };
        ThemeManager.OnAccentChanged += () =>
        {
            _cardBorderAccent = null; _flatBorderAccent = null;
            _pillFill = null; _pillBorder = null;
            _glowCache.Clear();
            PublishResources();
        };
        ThemeManager.OnReduceEffectsChanged += () =>
        {
            _cardFill = null; _cardBorder = null; _cardBorderAccent = null;
            _flatBorder = null; _flatBorderAccent = null;
            _glowCache.Clear();
            PublishResources();
        };
    }

    /// <summary>
    /// Call once at startup after the theme is applied (App.xaml.cs). Publishes the cached brushes as
    /// app resources (GlassCardFillBrush, GlassCardBorderBrush, GlassCardBorderAccentBrush,
    /// GlassPillFillBrush, GlassPillBorderBrush) so Styles/GlassStyles.xaml can use them via
    /// DynamicResource, and keeps them current on theme / accent / Reduce effects changes.
    /// </summary>
    public static void Init() => PublishResources();

    private static void PublishResources()
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        res["GlassCardFillBrush"] = CardFill;
        res["GlassCardBorderBrush"] = CardBorder(false);
        res["GlassCardBorderAccentBrush"] = CardBorder(true);
        var (fill, border) = AccentPillBrushes();
        res["GlassPillFillBrush"] = fill;
        res["GlassPillBorderBrush"] = border;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static Color ResColor(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is Color c ? c : fallback;

    /// <summary>Alpha-composites <paramref name="top"/> over the opaque <paramref name="bottom"/>.</summary>
    private static Color Over(Color bottom, Color top)
    {
        double a = top.A / 255.0;
        return Color.FromRgb(
            (byte)(bottom.R + (top.R - bottom.R) * a),
            (byte)(bottom.G + (top.G - bottom.G) * a),
            (byte)(bottom.B + (top.B - bottom.B) * a));
    }

    private static Color White(byte alpha) => Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);

    private static LinearGradientBrush Vertical(Color top, Color bottom) =>
        new(top, bottom, new Point(0, 0), new Point(0, 1));

    // ── Brushes ──────────────────────────────────────────────────────

    /// <summary>Card fill: white 0x12 → 0x05 sheen baked over the theme card colour (one opaque brush, no layering cost).</summary>
    public static Brush CardFill => _cardFill ??= Frozen(BuildCardFill());

    private static Brush BuildCardFill()
    {
        var card = ResColor("CardBg", Color.FromRgb(0x1C, 0x1C, 0x1C));
        if (ThemeManager.ReduceEffects) return new SolidColorBrush(Over(card, White(0x08)));
        return Vertical(Over(card, White(0x12)), Over(card, White(0x05)));
    }

    /// <summary>1px card border: bright at the top (white 0x34) fading to 0x0C at the bottom; accent-tinted when <paramref name="accent"/>.</summary>
    public static Brush CardBorder(bool accent = false)
    {
        if (ThemeManager.ReduceEffects)
        {
            if (accent) return _flatBorderAccent ??= Frozen(new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x55)));
            return _flatBorder ??= Frozen(new SolidColorBrush(ResColor("CardBorder", Color.FromRgb(0x2A, 0x2A, 0x2A))));
        }
        if (accent)
            return _cardBorderAccent ??= Frozen(Vertical(ThemeManager.WithAlpha(ThemeManager.Accent, 0x66), ThemeManager.WithAlpha(ThemeManager.Accent, 0x18)));
        return _cardBorder ??= Frozen(Vertical(White(0x34), White(0x0C)));
    }

    /// <summary>Cached frozen (fill, border) brushes for an accent pill; rebuilt only when the colour changes.</summary>
    public static (Brush Fill, Brush Border) AccentPillBrushes(Color accent)
    {
        if (_pillFill == null || _pillBorder == null || _pillAccent != accent)
        {
            _pillAccent = accent;
            _pillFill = Frozen(new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x28)));
            _pillBorder = Frozen(new SolidColorBrush(ThemeManager.WithAlpha(accent, 0x96)));
        }
        return (_pillFill, _pillBorder);
    }

    /// <summary>Accent pill brushes for the current theme accent.</summary>
    public static (Brush Fill, Brush Border) AccentPillBrushes() => AccentPillBrushes(ThemeManager.Accent);

    /// <summary>
    /// Cheap "glow": a frozen radial brush (colour alpha 0x30 at the centre to 0 at the edge). Put it as the
    /// Background of a Border/Rectangle sized <paramref name="radius"/>*2 BEHIND an element instead of a
    /// DropShadowEffect. Returns a transparent brush when Reduce effects is on.
    /// </summary>
    public static Brush FauxGlow(Color c, double radius)
    {
        if (ThemeManager.ReduceEffects) return Brushes.Transparent;
        var key = (c, radius);
        if (_glowCache.TryGetValue(key, out var cached)) return cached;
        var g = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        g.GradientStops.Add(new GradientStop(ThemeManager.WithAlpha(c, 0x30), 0));
        g.GradientStops.Add(new GradientStop(ThemeManager.WithAlpha(c, 0x00), 1));
        g.Freeze();
        if (_glowCache.Count > 64) _glowCache.Clear();
        _glowCache[key] = g;
        return g;
    }

    /// <summary>
    /// The ONLY place to create a glow effect. Returns null when Reduce effects is on. Use it on single
    /// active elements only (selected tab, focused button), never on repeated list items/cards.
    /// </summary>
    public static Effect? GlowEffect(Color c, double blur, double opacity = 0.3)
    {
        if (ThemeManager.ReduceEffects) return null;
        return Frozen(new DropShadowEffect { BlurRadius = blur, ShadowDepth = 0, Opacity = opacity, Color = c });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Flat translucent glass card look on an existing Border. Clears any Effect. Uses resource references
    /// to the published Glass*Brush keys, so the card follows theme / accent / Reduce effects changes live.
    /// </summary>
    public static void ApplyGlassCard(Border b, double radius = 16, bool accentBorder = false)
    {
        b.CornerRadius = new CornerRadius(radius);
        b.BorderThickness = new Thickness(1);
        b.SetResourceReference(Border.BackgroundProperty, "GlassCardFillBrush");
        b.SetResourceReference(Border.BorderBrushProperty, accentBorder ? "GlassCardBorderAccentBrush" : "GlassCardBorderBrush");
        b.Effect = null;
    }

    /// <summary>Hover: 2px TranslateTransform lift (140 ms). No effects. Don't use on page-sized containers.</summary>
    public static void AttachHoverLift(Border b)
    {
        var tt = new TranslateTransform();
        b.RenderTransform = tt;
        void Animate(double y)
        {
            if (ThemeManager.ReduceEffects || !SystemParameters.ClientAreaAnimation)
            {
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.Y = ThemeManager.ReduceEffects ? 0 : y;
                return;
            }
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        b.MouseEnter += (_, _) => Animate(-2);
        b.MouseLeave += (_, _) => Animate(0);
    }

    /// <summary>Section title: same as <see cref="UiKit.SectionHeader(string, string?)"/> (accent bar + label).</summary>
    public static FrameworkElement SectionTitle(string text, string? count = null) => UiKit.SectionHeader(text, count);

    /// <summary>Setting row: title + dim hint on the left, <paramref name="control"/> on the right.</summary>
    public static Border SettingRow(string title, string? hint, FrameworkElement control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var t = new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        text.Children.Add(t);
        if (!string.IsNullOrEmpty(hint))
        {
            var h = new TextBlock { Text = hint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            h.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            text.Children.Add(h);
        }
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        var row = new Border { Child = grid, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8) };
        ApplyGlassCard(row, 12);
        return row;
    }
}
