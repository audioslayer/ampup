using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Controls;

/// <summary>
/// Unified visual tile for the Stream Controller editor. Represents one of:
///   - an LCD key (with live preview image),
///   - a physical side button, or
///   - an encoder press target (with optional rotate hint).
///
/// Shares the Room tab's card aesthetic so the Buttons tab matches the rest
/// of the app.
///
/// API contract — all parallel agents integrate against this surface:
///   Kind            — visual mode
///   Title           — caption under the tile
///   Subtitle        — small secondary line (e.g. "None" or action summary)
///   PreviewImage    — optional BitmapSource (for LCD keys)
///   IconKind        — MaterialIconKind name for a glyph-only tile (buttons/encoders)
///   AccentColor     — border/ring color; defaults to ThemeManager.Accent
///   IsSelected      — highlight state
///   OnClick         — raised when the tile is left-clicked
///   OnRightClick    — raised when the tile is right-clicked (for context menus)
/// </summary>
public class StreamControllerTile : Border
{
    public enum TileKind
    {
        LcdKey,
        SideButton,
        Encoder,
    }

    public TileKind Kind { get; set; } = TileKind.LcdKey;
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public System.Windows.Media.Imaging.BitmapSource? PreviewImage { get; set; }
    public string IconKind { get; set; } = "";
    public Color AccentColor { get; set; } = ThemeManager.Accent;
    public bool IsSelected { get; set; }

    public event Action? OnClick;
    public event Action<MouseButtonEventArgs>? OnRightClick;

    // Cached so hover/selection updates don't rebuild the whole tree.
    private bool _isHovered;

    public StreamControllerTile()
    {
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        SetResourceReference(BackgroundProperty, "CardBgBrush");
        SetResourceReference(BorderBrushProperty, "CardBorderBrush");

        MouseEnter += (_, _) => { _isHovered = true; ApplyVisualState(); };
        MouseLeave += (_, _) => { _isHovered = false; ApplyVisualState(); };

        MouseLeftButtonUp += (_, e) =>
        {
            OnClick?.Invoke();
            e.Handled = true;
        };
        MouseRightButtonUp += (_, e) =>
        {
            OnRightClick?.Invoke(e);
            // Don't mark handled — consumer chooses
        };

        Refresh();
    }

    /// <summary>Rebuild visuals after any property changes.</summary>
    public virtual void Refresh()
    {
        ClearValue(HeightProperty);
        ClearValue(MinHeightProperty);
        ClearValue(MinWidthProperty);

        switch (Kind)
        {
            case TileKind.LcdKey:
                Height = 140;
                MinWidth = 140;
                Padding = new Thickness(0);
                Child = BuildLcdKeyContent();
                break;

            case TileKind.SideButton:
                Height = 70;
                MinWidth = 160;
                Padding = new Thickness(12, 8, 12, 8);
                Child = BuildSideButtonContent(isEncoder: false);
                break;

            case TileKind.Encoder:
                Height = 78;
                MinWidth = 160;
                Padding = new Thickness(12, 8, 12, 8);
                Child = BuildSideButtonContent(isEncoder: true);
                break;
        }

        ApplyVisualState();
    }

    // ── Layouts ─────────────────────────────────────────────────────────

    private UIElement BuildLcdKeyContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Preview area — rounded-top image or placeholder icon.
        var previewHost = new Border
        {
            CornerRadius = new CornerRadius(13, 13, 0, 0),
            ClipToBounds = true,
            Margin = new Thickness(1, 1, 1, 0),
        };
        previewHost.SetResourceReference(BackgroundProperty, "BgDarkBrush");

        if (PreviewImage != null)
        {
            previewHost.Child = new Image
            {
                Source = PreviewImage,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(previewHost, BitmapScalingMode.HighQuality);
        }
        else
        {
            previewHost.Child = BuildPlaceholderIcon(36);
        }

        Grid.SetRow(previewHost, 0);
        root.Children.Add(previewHost);

        // Caption area — title + subtitle.
        var caption = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 4),
        };

        var titleText = new TextBlock
        {
            Text = string.IsNullOrEmpty(Title) ? "—" : Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        caption.Children.Add(titleText);

        if (!string.IsNullOrEmpty(Subtitle))
        {
            var subText = new TextBlock
            {
                Text = Subtitle,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            };
            subText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            caption.Children.Add(subText);
        }

        Grid.SetRow(caption, 1);
        root.Children.Add(caption);

        return root;
    }

    private UIElement BuildSideButtonContent(bool isEncoder)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Glyph badge on the left — subtle accent tinted background.
        var iconSize = isEncoder ? 44 : 40;
        var iconBadge = new Border
        {
            Width = iconSize,
            Height = iconSize,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x20, AccentColor.R, AccentColor.G, AccentColor.B)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, AccentColor.R, AccentColor.G, AccentColor.B)),
        };

        UIElement glyph;
        if (isEncoder)
        {
            glyph = BuildEncoderGlyph(22);
        }
        else
        {
            glyph = BuildGlyph(22) ?? BuildFallbackDot(10);
        }
        iconBadge.Child = glyph;

        Grid.SetColumn(iconBadge, 0);
        root.Children.Add(iconBadge);

        // Text column
        var textStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var titleText = new TextBlock
        {
            Text = string.IsNullOrEmpty(Title) ? "—" : Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        textStack.Children.Add(titleText);

        var subText = new TextBlock
        {
            Text = string.IsNullOrEmpty(Subtitle) ? "None" : Subtitle,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        };
        subText.SetResourceReference(TextBlock.ForegroundProperty,
            string.IsNullOrEmpty(Subtitle) ? "TextDimBrush" : "TextSecBrush");
        textStack.Children.Add(subText);

        Grid.SetColumn(textStack, 1);
        root.Children.Add(textStack);

        return root;
    }

    // ── Glyphs ──────────────────────────────────────────────────────────

    private UIElement? BuildGlyph(double size)
    {
        if (!string.IsNullOrEmpty(IconKind) &&
            Enum.TryParse<MaterialIconKind>(IconKind, out var kind))
        {
            var icon = new MaterialIcon
            {
                Kind = kind,
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(AccentColor),
            };
            return icon;
        }
        return null;
    }

    private UIElement BuildPlaceholderIcon(double size)
    {
        // LCD placeholder: try IconKind first, else dotted-circle/square icon.
        var glyph = BuildGlyph(size);
        if (glyph is MaterialIcon mi)
        {
            mi.Opacity = 0.55;
            return mi;
        }

        // Fallback: a soft square icon representing an empty LCD.
        var fallback = new MaterialIcon
        {
            Kind = MaterialIconKind.RectangleOutline,
            Width = size,
            Height = size,
            Opacity = 0.35,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        fallback.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
        return fallback;
    }

    private UIElement BuildFallbackDot(double size)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(Color.FromArgb(0x88, AccentColor.R, AccentColor.G, AccentColor.B)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return dot;
    }

    /// <summary>
    /// Encoder glyph = a dial icon (user-chosen IconKind if any) with
    /// two small arrows hinting at rotation.
    /// </summary>
    private UIElement BuildEncoderGlyph(double size)
    {
        var wrap = new Grid
        {
            Width = size + 10,
            Height = size + 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        MaterialIconKind kind = MaterialIconKind.CircleOutline;
        if (!string.IsNullOrEmpty(IconKind) &&
            Enum.TryParse<MaterialIconKind>(IconKind, out var parsed))
        {
            kind = parsed;
        }

        var dial = new MaterialIcon
        {
            Kind = kind,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(AccentColor),
        };
        wrap.Children.Add(dial);

        // Inner dot marks the "press" affordance.
        var pressDot = new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = new SolidColorBrush(AccentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(pressDot);

        return wrap;
    }

    // ── State styling ───────────────────────────────────────────────────

    private void ApplyVisualState()
    {
        bool empty = IsEmpty();

        // Opacity: dim empties unless hovered/selected.
        Opacity = empty && !_isHovered && !IsSelected ? 0.7 : 1.0;

        // Border + glow priority: Selected > Hover > Default.
        if (IsSelected)
        {
            BorderThickness = new Thickness(2);
            BorderBrush = new SolidColorBrush(AccentColor);
            Effect = new DropShadowEffect
            {
                Color = AccentColor,
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.55,
            };
            Background = new SolidColorBrush(Color.FromArgb(
                0x10, AccentColor.R, AccentColor.G, AccentColor.B));
        }
        else if (_isHovered)
        {
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0xAA, AccentColor.R, AccentColor.G, AccentColor.B));
            Effect = null;
            Background = new SolidColorBrush(Color.FromArgb(
                0x14, AccentColor.R, AccentColor.G, AccentColor.B));
        }
        else
        {
            BorderThickness = new Thickness(1);
            SetResourceReference(BorderBrushProperty, "CardBorderBrush");
            Effect = null;
            SetResourceReference(BackgroundProperty, "CardBgBrush");
        }
    }

    private bool IsEmpty()
    {
        // Treat "None" or blank subtitle as empty.
        if (string.IsNullOrWhiteSpace(Subtitle)) return true;
        if (string.Equals(Subtitle, "None", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
