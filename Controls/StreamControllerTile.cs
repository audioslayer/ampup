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
    public bool IsSelected { get; set; }

    // Null means "always track the active theme accent". Callers can still
    // force a per-tile color by assigning AccentColor explicitly, but every
    // caller today wants the live theme accent — so we read it lazily in
    // ApplyVisualState instead of capturing it at construction time.
    private Color? _accentOverride;
    public Color AccentColor
    {
        get => _accentOverride ?? ThemeManager.Accent;
        set => _accentOverride = value;
    }

    public event Action? OnClick;
    public event Action<MouseButtonEventArgs>? OnRightClick;

    /// <summary>
    /// Raised when another tile is dropped onto this one. Consumer swaps
    /// the configuration between the source and target slots.
    /// </summary>
    public event Action<StreamControllerTile>? OnTileDropped;

    /// <summary>
    /// Opaque payload the host can attach to a tile so the DnD handler
    /// can identify it on drop. Typically the slot/key index.
    /// </summary>
    public object? DragPayload { get; set; }

    /// <summary>
    /// Gate to disable drag-and-drop per-instance — the folder "Back"
    /// slot, side buttons, and encoders opt out.
    /// </summary>
    public bool AllowDragDrop { get; set; } = false;

    // Cached so hover/selection updates don't rebuild the whole tree.
    private bool _isHovered;
    private Point _dragStart;
    private bool _dragArmed;
    private bool _dragHover;

    public StreamControllerTile()
    {
        CornerRadius = new CornerRadius(14);
        // Constant 2px border — layout never shifts between states. When
        // not selected we paint the border transparent/subtle, when selected
        // we paint it in the accent color. This keeps the rounded corners
        // aligned with the inner preview and stops the corners "cutting off".
        BorderThickness = new Thickness(2);
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        SetResourceReference(BackgroundProperty, "CardBgBrush");
        SetResourceReference(BorderBrushProperty, "CardBorderBrush");

        MouseEnter += (_, _) => { _isHovered = true; ApplyVisualState(); };
        MouseLeave += (_, _) => { _isHovered = false; ApplyVisualState(); };

        MouseLeftButtonDown += (_, e) =>
        {
            if (!AllowDragDrop) return;
            _dragStart = e.GetPosition(this);
            _dragArmed = true;
        };
        MouseMove += (_, e) =>
        {
            if (!_dragArmed || !AllowDragDrop) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _dragArmed = false; return; }
            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            _dragArmed = false;
            var data = new DataObject("AmpUp.StreamControllerTile", this);
            try { DragDrop.DoDragDrop(this, data, DragDropEffects.Move); }
            catch { /* host may have unloaded */ }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            _dragArmed = false;
            OnClick?.Invoke();
            e.Handled = true;
        };
        MouseRightButtonUp += (_, e) =>
        {
            OnRightClick?.Invoke(e);
            // Don't mark handled — consumer chooses
        };

        // Drop target wiring — AllowDrop is toggled by the AllowDragDrop
        // flag at Refresh() time so empty/virtual tiles don't accept drops.
        DragEnter += (_, e) =>
        {
            if (!AllowDragDrop || !e.Data.GetDataPresent("AmpUp.StreamControllerTile"))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            var source = e.Data.GetData("AmpUp.StreamControllerTile") as StreamControllerTile;
            if (ReferenceEquals(source, this))
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            e.Effects = DragDropEffects.Move;
            _dragHover = true;
            ApplyVisualState();
        };
        DragLeave += (_, _) =>
        {
            if (!_dragHover) return;
            _dragHover = false;
            ApplyVisualState();
        };
        Drop += (_, e) =>
        {
            _dragHover = false;
            ApplyVisualState();
            if (!AllowDragDrop) return;
            if (e.Data.GetData("AmpUp.StreamControllerTile") is not StreamControllerTile source) return;
            if (ReferenceEquals(source, this)) return;
            OnTileDropped?.Invoke(source);
            e.Handled = true;
        };

        // Track theme/accent so selected tiles re-colour live when the
        // user swaps theme in Settings instead of being stuck on the
        // accent that was active when this tile was built.
        void OnThemeChanged() => Dispatcher.BeginInvoke((Action)ApplyVisualState);
        ThemeManager.OnAccentChanged += OnThemeChanged;
        ThemeManager.OnCardThemeChanged += OnThemeChanged;
        Unloaded += (_, _) =>
        {
            ThemeManager.OnAccentChanged -= OnThemeChanged;
            ThemeManager.OnCardThemeChanged -= OnThemeChanged;
        };

        Refresh();
    }

    /// <summary>Rebuild visuals after any property changes.</summary>
    public virtual void Refresh()
    {
        ClearValue(HeightProperty);
        ClearValue(MinHeightProperty);
        ClearValue(MinWidthProperty);

        // Only LCD-key tiles that opt-in accept drops — side buttons /
        // encoders / the virtual Back slot keep AllowDrop=false.
        AllowDrop = AllowDragDrop;

        switch (Kind)
        {
            case TileKind.LcdKey:
                // Fixed square size — a real N3 LCD is ~60px so keep the
                // on-screen mockup at a readable but hardware-proportional
                // ~120px. Centered in star-width columns rather than stretched.
                Width = 120;
                Height = 120;
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Center;
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
        // LCD tile is just the hardware screen render — no caption. The
        // tile's own rounded border + accent ring conveys selection.
        // Inner corner = outer(14) - border(2) = 12. Zero margin so the
        // inner edge is flush against the 2px border, keeping corners
        // visually aligned whether the border is transparent or accent.
        //
        // WPF's Border.ClipToBounds only clips to the rectangular bounds,
        // not to CornerRadius — the child Image would otherwise render
        // square edges that poke past the rounded corners. We set an
        // explicit RectangleGeometry clip that tracks the preview's size
        // so the image is actually rounded at the corners.
        var previewHost = new Border
        {
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Margin = new Thickness(0),
        };
        previewHost.SizeChanged += (_, e) =>
        {
            previewHost.Clip = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
                12, 12);
        };
        previewHost.SetResourceReference(BackgroundProperty, "BgDarkBrush");

        if (PreviewImage != null)
        {
            previewHost.Child = new Image
            {
                Source = PreviewImage,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetBitmapScalingMode(previewHost, BitmapScalingMode.HighQuality);
        }
        else
        {
            previewHost.Child = BuildPlaceholderIcon(40);
        }

        var root = new Grid();
        root.Children.Add(previewHost);

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
        var accent = AccentColor; // live theme lookup

        // Opacity: dim empties unless hovered/selected.
        Opacity = empty && !_isHovered && !IsSelected ? 0.7 : 1.0;

        // BorderThickness is fixed at 2 so the inner preview never shifts
        // between states — only the brush changes. Also keeps the rounded
        // corners pixel-aligned against the inner preview's corner radius.
        // Drag-hover state takes over when a tile is being dragged over us
        // so the user sees a clear drop target, regardless of selection.
        if (_dragHover)
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, accent.R, accent.G, accent.B));
            Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.85,
            };
            Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
            return;
        }

        if (IsSelected)
        {
            // Shimmery diagonal gradient border — bright at the top-left
            // and bottom-right "catch-light" corners, slightly dimmer in
            // the middle. Reads as a single continuous ring that wraps
            // the rounded corners instead of a flat hard outline.
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xFF, accent.R, accent.G, accent.B), 0.0),
                    new GradientStop(Color.FromArgb(0xC0, accent.R, accent.G, accent.B), 0.5),
                    new GradientStop(Color.FromArgb(0xFF, accent.R, accent.G, accent.B), 1.0),
                },
            };

            // Big, soft diffuse outer bloom — strong enough to read across
            // the corners so the glow feels like it's emanating from the
            // whole tile rather than four independent edges.
            Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.9,
            };

            // Subtle diagonal tinted fill so the inside catches some of
            // the same glow colour rather than staying flat dark.
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x22, accent.R, accent.G, accent.B), 0.0),
                    new GradientStop(Color.FromArgb(0x0C, accent.R, accent.G, accent.B), 1.0),
                },
            };
        }
        else if (_isHovered)
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0x99, accent.R, accent.G, accent.B));
            Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.45,
            };
            Background = new SolidColorBrush(Color.FromArgb(
                0x14, accent.R, accent.G, accent.B));
        }
        else
        {
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
