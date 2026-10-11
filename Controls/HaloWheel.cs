using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AmpUp.Services;
using Material.Icons;
using Material.Icons.WPF;
using Path = System.Windows.Shapes.Path;

namespace AmpUp.Controls;

/// <summary>
/// The "Halo" Quick Wheel drawing: glass backdrop, a ring with one segment per item
/// (no blanks) and a center card naming the highlighted item. Fixed 420x420 layout —
/// wrap in a Viewbox to scale (the OSD page's live mini wheel does this).
/// Used by RadialWheelOverlay (on-screen wheel) and OsdView (mini preview).
/// </summary>
public sealed class HaloWheel : Grid
{
    public const int MaxSlots = QuickWheelContent.MaxItems;

    /// <summary>Fires when a segment is clicked, with its index.</summary>
    public event Action<int>? SegmentClicked;

    private readonly List<QuickWheelItem> _items = new();
    private string _kind = "";
    private int _activeIndex = -1;
    private int _highlighted = -1;

    private readonly Canvas _ring = new() { Width = 420, Height = 420 };
    private readonly StackPanel _center = new()
    {
        Width = 200,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };
    private readonly Ellipse _centerDisc = new()
    {
        Width = 252, Height = 252,
        StrokeThickness = 1,
    };
    private readonly DropShadowEffect _glow = new() { BlurRadius = 36, Opacity = 0.22, ShadowDepth = 0 };
    private readonly Ellipse _backdrop = new()
    {
        Width = 404, Height = 404,
        Stroke = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
        StrokeThickness = 1,
    };

    /// <summary>Accent for glow / ring stroke / ACTIVE pill. Null = theme accent.</summary>
    public Color? AccentOverride { get; set; }
    /// <summary>0..1 multiplier on the glass backdrop + center disc alpha (1 = original look).</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    private QuickWheelLook _look = new();

    /// <summary>Apply the OSD page's look settings (color, background, ring options; size is the host's job).</summary>
    public void ApplyStyle(OsdConfig osd)
    {
        AccentOverride = OsdOverlay.ParseColor(osd.AccentColor);
        BackgroundOpacity = Math.Clamp(osd.BackgroundOpacity, 0, 100) / 100.0;
        _look = osd.WheelLook ?? new QuickWheelLook();
        (_ringInner, _ringOuter, _hlInner, _hlOuter, _iconSize) = _look.RingThickness switch
        {
            "Thin" => (162.0, 182.0, 158.0, 190.0, 14.0),
            "Thick" => (142.0, 190.0, 136.0, 200.0, 21.0),
            _ => (154.0, 186.0, 148.0, 196.0, 18.0),
        };
        _gapDeg = _look.SegmentGap switch { "None" => 0.4, "Wide" => 5.0, _ => 2.2 };
        Render();
    }

    /// <summary>Play the opening animation picked in the look settings (Fade is the host window's job).</summary>
    public void PlayIntro()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        switch (_look.Animation)
        {
            case "Pop":
            {
                var st = new ScaleTransform(0.75, 0.75);
                RenderTransformOrigin = new Point(0.5, 0.5);
                RenderTransform = st;
                var anim = new DoubleAnimation(0.75, 1, TimeSpan.FromMilliseconds(320))
                    { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                break;
            }
            case "Spin":
            {
                var rt = new RotateTransform(-120, CenterX, CenterY);
                _ring.RenderTransform = rt;
                rt.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(-120, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
                break;
            }
        }
    }

    // Geometry (420x420 canvas); ring sizes follow the Ring thickness option
    private const double CenterX = 210, CenterY = 210;
    private double _ringInner = 154, _ringOuter = 186, _hlInner = 148, _hlOuter = 196, _iconSize = 18;
    private double _gapDeg = 2.2;
    private double GlowScale => Math.Clamp(_look.Glow, 0, 200) / 100.0;

    private static readonly Color NameText = Color.FromRgb(0xE8, 0xE8, 0xE8);
    private static readonly Color DimText = Color.FromRgb(0x8A, 0x8A, 0x8A);

    public HaloWheel()
    {
        Width = 420;
        Height = 420;
        _backdrop.Effect = _glow;
        Children.Add(_backdrop);
        Children.Add(_ring);
        Children.Add(_centerDisc);
        Children.Add(_center);
    }

    public int Count => _items.Count;
    public int HighlightedIndex => _highlighted;

    public void SetContent(QuickWheelContent content)
    {
        _items.Clear();
        _items.AddRange(content.Items.Take(MaxSlots));
        _kind = content.Kind;
        _activeIndex = content.ActiveIndex;
        _highlighted = _items.Count == 0 ? -1 : Math.Clamp(content.CurrentIndex, 0, _items.Count - 1);
        Render();
    }

    /// <summary>Move the highlight to an item (wraps).</summary>
    public void Highlight(int index)
    {
        if (_items.Count == 0) return;
        index = ((index % _items.Count) + _items.Count) % _items.Count;
        if (index == _highlighted) return;
        _highlighted = index;
        Render();
    }

    private void Render()
    {
        var accent = AccentOverride ?? ThemeManager.Accent;
        _glow.Color = accent;
        _glow.Opacity = Math.Min(1, 0.22 * GlowScale);
        _backdrop.Effect = GlowScale <= 0.001 ? null : _glow;
        _backdrop.Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(0xD6 * BackgroundOpacity), 0x10, 0x10, 0x10));
        _centerDisc.Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(0xEB * BackgroundOpacity), 0x0C, 0x0C, 0x0C));
        _centerDisc.Stroke = new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B));
        BuildRing();
        BuildCenter(accent);
    }

    private void BuildRing()
    {
        _ring.Children.Clear();
        int n = _items.Count;
        if (n == 0) return;
        double step = 360.0 / n;

        for (int i = 0; i < n; i++)
        {
            var it = _items[i];
            bool hl = i == _highlighted;
            var c = SegmentColor(i, n, it.Color);
            bool outline = hl && _look.Highlight == "Outline";
            double mid = -90.0 + i * step;

            double gap = Math.Min(_gapDeg, step / 4);
            var path = new Path
            {
                Data = BuildRingSlice(mid - step / 2 + gap, mid + step / 2 - gap,
                    hl ? _hlInner : _ringInner, hl ? _hlOuter : _ringOuter),
                Fill = new SolidColorBrush(hl && !outline ? c : Color.FromArgb(outline ? (byte)0x40 : (byte)0x33, c.R, c.G, c.B)),
                Stroke = outline ? new SolidColorBrush(c) : null,
                StrokeThickness = outline ? 2 : 0,
                Cursor = Cursors.Hand,
            };
            if (hl && GlowScale > 0.001)
                path.Effect = new DropShadowEffect { Color = c, BlurRadius = 18, Opacity = Math.Min(1, 0.6 * GlowScale), ShadowDepth = 0 };
            int cap = i;
            path.MouseEnter += (_, _) => Highlight(cap);
            path.MouseLeftButtonDown += (_, e) => { e.Handled = true; SegmentClicked?.Invoke(cap); };
            _ring.Children.Add(path);

            double size = hl ? _iconSize + 4 : _iconSize;
            FrameworkElement? icon = null;
            if (it.Image != null)
            {
                // App icons / user images keep their own colors
                icon = new Image { Source = it.Image, Stretch = Stretch.Uniform };
            }
            else if (Enum.TryParse<MaterialIconKind>(it.Symbol, out var kind))
            {
                icon = new MaterialIcon
                {
                    Kind = kind,
                    // Dark glyph on the solid highlighted segment, colored glyph on the dim / outlined ones
                    Foreground = new SolidColorBrush(hl && !outline ? Color.FromRgb(0x0F, 0x0F, 0x0F) : c),
                };
            }
            if (icon != null)
            {
                double r = hl ? (_hlInner + _hlOuter) / 2 : (_ringInner + _ringOuter) / 2;
                double rad = mid * Math.PI / 180.0;
                icon.Width = size;
                icon.Height = size;
                icon.IsHitTestVisible = false;
                Canvas.SetLeft(icon, CenterX + r * Math.Cos(rad) - size / 2);
                Canvas.SetTop(icon, CenterY + r * Math.Sin(rad) - size / 2);
                _ring.Children.Add(icon);
            }
        }
    }

    private void BuildCenter(Color accent)
    {
        _center.Children.Clear();
        if (_highlighted < 0 || _highlighted >= _items.Count)
        {
            _center.Children.Add(new TextBlock
            {
                Text = "Nothing to show",
                FontSize = 17, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            _center.Children.Add(new TextBlock
            {
                Text = "No items for this wheel",
                FontSize = 12, Foreground = new SolidColorBrush(DimText),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
            return;
        }

        var it = _items[_highlighted];
        var itColor = SegmentColor(_highlighted, _items.Count, it.Color);
        bool iconOnly = _look.CenterInfo == "Icon";
        bool simple = _look.CenterInfo == "Simple";
        double centerIcon = iconOnly ? 64 : 34;
        if (it.Image != null)
        {
            _center.Children.Add(new Image
            {
                Source = it.Image,
                Width = centerIcon, Height = centerIcon,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }
        else if (Enum.TryParse<MaterialIconKind>(it.Symbol, out var kind))
        {
            _center.Children.Add(new MaterialIcon
            {
                Kind = kind,
                Width = centerIcon, Height = centerIcon,
                Foreground = new SolidColorBrush(itColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

        if (iconOnly) return;

        _center.Children.Add(new TextBlock
        {
            Text = it.Label,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(NameText),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 44, // two lines
        });

        if (simple) return;

        if (!string.IsNullOrEmpty(_kind))
        {
            _center.Children.Add(new TextBlock
            {
                Text = _kind,
                FontSize = 11,
                Foreground = new SolidColorBrush(DimText),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        if (_highlighted == _activeIndex)
        {
            _center.Children.Add(new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 2, 9, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock
                {
                    Text = "ACTIVE",
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(accent),
                },
            });
        }
    }

    /// <summary>Segment color per the Segment colors option: item's own, one accent, or a rainbow around the ring.</summary>
    private Color SegmentColor(int index, int count, Color itemColor) => _look.SegmentColors switch
    {
        "Accent" => AccentOverride ?? ThemeManager.Accent,
        "Rainbow" => HueColor(360.0 * index / Math.Max(1, count)),
        _ => itemColor,
    };

    private static Color HueColor(double hue)
    {
        // HSV with s = 0.75, v = 1 — bright but not neon
        double s = 0.75, h = (hue % 360) / 60.0;
        double c = s, x = c * (1 - Math.Abs(h % 2 - 1)), m = 1 - c;
        var (r, g, b) = (int)h switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    /// <summary>Annular slice between two angles (degrees, 0 = right, clockwise).</summary>
    private static PathGeometry BuildRingSlice(double startDeg, double endDeg, double innerR, double outerR)
    {
        double s = startDeg * Math.PI / 180.0, e = endDeg * Math.PI / 180.0;
        Point Pt(double r, double a) => new(CenterX + r * Math.Cos(a), CenterY + r * Math.Sin(a));
        bool large = endDeg - startDeg > 180;

        var fig = new PathFigure { StartPoint = Pt(innerR, s), IsClosed = true };
        fig.Segments.Add(new LineSegment(Pt(outerR, s), true));
        fig.Segments.Add(new ArcSegment(Pt(outerR, e), new Size(outerR, outerR), 0,
            large, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(Pt(innerR, e), true));
        fig.Segments.Add(new ArcSegment(Pt(innerR, s), new Size(innerR, innerR), 0,
            large, SweepDirection.Counterclockwise, true));
        return new PathGeometry(new[] { fig });
    }
}
