using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        Fill = new SolidColorBrush(Color.FromArgb(0xEB, 0x0C, 0x0C, 0x0C)),
        StrokeThickness = 1,
    };
    private readonly DropShadowEffect _glow = new() { BlurRadius = 36, Opacity = 0.22, ShadowDepth = 0 };

    // Geometry (420x420 canvas)
    private const double CenterX = 210, CenterY = 210;
    private const double RingInner = 154, RingOuter = 186;
    private const double HlInner = 148, HlOuter = 196;
    private const double GapDeg = 2.2;

    private static readonly Color NameText = Color.FromRgb(0xE8, 0xE8, 0xE8);
    private static readonly Color DimText = Color.FromRgb(0x8A, 0x8A, 0x8A);

    public HaloWheel()
    {
        Width = 420;
        Height = 420;
        Children.Add(new Ellipse
        {
            Width = 404, Height = 404,
            Fill = new SolidColorBrush(Color.FromArgb(0xD6, 0x10, 0x10, 0x10)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1,
            Effect = _glow,
        });
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
        var accent = ThemeManager.Accent;
        _glow.Color = accent;
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
            var c = it.Color;
            double mid = -90.0 + i * step;

            var path = new Path
            {
                Data = BuildRingSlice(mid - step / 2 + GapDeg, mid + step / 2 - GapDeg,
                    hl ? HlInner : RingInner, hl ? HlOuter : RingOuter),
                Fill = new SolidColorBrush(hl ? c : Color.FromArgb(0x33, c.R, c.G, c.B)),
                Cursor = Cursors.Hand,
            };
            if (hl)
                path.Effect = new DropShadowEffect { Color = c, BlurRadius = 18, Opacity = 0.6, ShadowDepth = 0 };
            int cap = i;
            path.MouseEnter += (_, _) => Highlight(cap);
            path.MouseLeftButtonDown += (_, e) => { e.Handled = true; SegmentClicked?.Invoke(cap); };
            _ring.Children.Add(path);

            if (Enum.TryParse<MaterialIconKind>(it.Symbol, out var kind))
            {
                double size = hl ? 22 : 18;
                double r = hl ? 172 : 170;
                double rad = mid * Math.PI / 180.0;
                var icon = new MaterialIcon
                {
                    Kind = kind,
                    Width = size, Height = size,
                    IsHitTestVisible = false,
                    // Dark glyph on the solid highlighted segment, colored glyph on the dim ones
                    Foreground = new SolidColorBrush(hl ? Color.FromRgb(0x0F, 0x0F, 0x0F) : c),
                };
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
        if (Enum.TryParse<MaterialIconKind>(it.Symbol, out var kind))
        {
            _center.Children.Add(new MaterialIcon
            {
                Kind = kind,
                Width = 34, Height = 34,
                Foreground = new SolidColorBrush(it.Color),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
            });
        }

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
