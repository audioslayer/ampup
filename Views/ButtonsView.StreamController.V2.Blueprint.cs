using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// N3 "blueprint" device drawing for the Buttons tab. Follows the real TreasLin / MiraBox N3
/// layout — 3x2 LCD block top-left, one large knob top-right, three round buttons and two
/// small knobs along the bottom — drawn as accent line art on a drafting grid. The LCD
/// screens show the live key previews; each button / knob shows its assigned action's icon.
/// Everything sits on a fixed canvas inside a Viewbox so it scales with the pane.
/// </summary>
public partial class ButtonsView
{
    // Which encoder index is the large knob. N3Controller reports encoders 0-2 by HID code;
    // encoder 0 (codes 0x90/0x91) is treated as the big top-right knob.
    private const int V2BigKnobEncoder = 0;

    // Canvas geometry. The body keeps the real 129 x 78 mm proportions (≈1.65 : 1).
    private const double BpCanvasW = 860, BpCanvasH = 690;
    private const double BpBodyX = 62, BpBodyY = 100, BpBodyW = 760, BpBodyH = 460;
    private const double BpKeySize = 120, BpKeyGap = 12, BpKeyInset = 44;
    private const double BpBigKnobD = 232, BpSmallKnobD = 118, BpButtonD = 72;
    private static readonly FontFamily BpMono = new("Cascadia Mono, Consolas, Courier New");

    private sealed class V2HwControl
    {
        public int ButtonIdx;
        public int EncoderIdx = -1;     // -1 for the side buttons
        public string Name = "";
        public Ellipse Outline = null!;
        public DropShadowEffect Glow = null!;
        public MaterialIcon Icon = null!;
        public MaterialIcon? PressIcon;
        public TextBlock Sub = null!;
        public bool Hover;
    }

    private readonly List<V2HwControl> _v2HwControls = new();

    private static Color BpLine(byte alpha) => ThemeManager.WithAlpha(ThemeManager.Accent, alpha);

    private Border BuildV2HardwareDeviceBody()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Background = new LinearGradientBrush(Color.FromRgb(0x0C, 0x16, 0x22), Color.FromRgb(0x07, 0x0D, 0x15), 90),
        };
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        // Drafting grid behind everything
        var gridLayer = new Border { Background = BuildBlueprintGridBrush(), Padding = new Thickness(14, 12, 14, 10) };
        card.Child = gridLayer;

        var root = new StackPanel();
        gridLayer.Child = root;

        // Pages: add / remove on the left, navigation on the right
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var addRemove = BuildV2PageAddRemoveRow();
        addRemove.HorizontalAlignment = HorizontalAlignment.Left;
        addRemove.Margin = new Thickness(0);
        top.Children.Add(addRemove);
        var nav = BuildV2PageToolbar();
        nav.HorizontalAlignment = HorizontalAlignment.Right;
        nav.Margin = new Thickness(0);
        Grid.SetColumn(nav, 1);
        top.Children.Add(nav);
        root.Children.Add(top);

        var canvas = new Canvas { Width = BpCanvasW, Height = BpCanvasH };
        root.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 4, 0, 0) });

        DrawBlueprintBody(canvas);
        DrawBlueprintDimensions(canvas);
        PlaceBlueprintKeys(canvas);

        _v2HwControls.Clear();
        _v2ButtonTiles.Clear();
        _v2EncoderTiles.Clear();

        // Large knob, top right — centred on the key block's vertical middle
        double keyBlockW = 3 * BpKeySize + 2 * BpKeyGap;
        double keyBlockH = 2 * BpKeySize + BpKeyGap;
        double bigCx = BpBodyX + BpBodyW - 44 - BpBigKnobD / 2;
        double topCy = BpBodyY + BpKeyInset + keyBlockH / 2;
        var big = AddBlueprintKnob(canvas, V2BigKnobEncoder, bigCx, topCy, BpBigKnobD);
        AddBlueprintCallout(canvas, big, bigCx, topCy - BpBigKnobD / 2, above: true);

        // Bottom row: three buttons under the keys, then the two small knobs
        double rowCy = BpBodyY + BpBodyH - 40 - BpSmallKnobD / 2;
        double keysX = BpBodyX + BpKeyInset;
        for (int i = 0; i < 3; i++)
        {
            double cx = keysX + 26 + BpButtonD / 2 + i * 112;
            var b = AddBlueprintButton(canvas, i, cx, rowCy);
            AddBlueprintCallout(canvas, b, cx, rowCy + BpButtonD / 2, above: false);
        }
        var small = Enumerable.Range(0, 3).Where(e => e != V2BigKnobEncoder).ToList();
        double[] smallCx = { keysX + keyBlockW - BpSmallKnobD / 2 + 20, bigCx };
        for (int s = 0; s < small.Count; s++)
        {
            var k = AddBlueprintKnob(canvas, small[s], smallCx[s], rowCy, BpSmallKnobD);
            AddBlueprintCallout(canvas, k, smallCx[s], rowCy + BpSmallKnobD / 2, above: false);
        }

        RefreshV2HardwareTiles();
        return card;
    }

    // ── Static drawing ────────────────────────────────────────────────

    private static Brush BuildBlueprintGridBrush()
    {
        var minor = new Pen(new SolidColorBrush(BpLine(0x10)), 1);
        var major = new Pen(new SolidColorBrush(BpLine(0x22)), 1);
        var g = new DrawingGroup();
        for (int i = 0; i < 5; i++)
        {
            double p = i * 12;
            var pen = i == 0 ? major : minor;
            g.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(p, 0), new Point(p, 60))));
            g.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, p), new Point(60, p))));
        }
        var brush = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 60, 60),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 60, 60),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static void At(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
    }

    private static void DrawBlueprintBody(Canvas c)
    {
        var body = new Rectangle
        {
            Width = BpBodyW, Height = BpBodyH, RadiusX = 30, RadiusY = 30,
            Stroke = new SolidColorBrush(BpLine(0xCC)), StrokeThickness = 2,
            Fill = new RadialGradientBrush(Color.FromArgb(0xE6, 0x10, 0x1C, 0x2A), Color.FromArgb(0xE6, 0x08, 0x0F, 0x18))
                { GradientOrigin = new Point(0.3, 0.2), Center = new Point(0.4, 0.3), RadiusX = 0.9, RadiusY = 1.1 },
            Effect = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 28, ShadowDepth = 0, Opacity = 0.25 },
        };
        At(body, BpBodyX, BpBodyY);
        c.Children.Add(body);

        // Panel seam — the faceplate edge, dashed like a hidden line
        var seam = new Rectangle
        {
            Width = BpBodyW - 24, Height = BpBodyH - 24, RadiusX = 20, RadiusY = 20,
            Stroke = new SolidColorBrush(BpLine(0x40)), StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 5, 4 },
        };
        At(seam, BpBodyX + 12, BpBodyY + 12);
        c.Children.Add(seam);

        // Screen bezel around the LCD block
        double kx = BpBodyX + BpKeyInset, ky = BpBodyY + BpKeyInset;
        var bezel = new Rectangle
        {
            Width = 3 * BpKeySize + 2 * BpKeyGap + 20, Height = 2 * BpKeySize + BpKeyGap + 20,
            RadiusX = 16, RadiusY = 16,
            Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)),
            Stroke = new SolidColorBrush(BpLine(0x55)), StrokeThickness = 1,
        };
        At(bezel, kx - 10, ky - 10);
        c.Children.Add(bezel);

        var lcdTag = BpText("LCD 3 × 2", 10, BpLine(0x88));
        At(lcdTag, kx - 8, ky + 2 * BpKeySize + BpKeyGap + 14);
        c.Children.Add(lcdTag);

        // Corner registration marks
        foreach (var (x, y, sx, sy) in new[] { (12.0, 12.0, 1, 1), (BpCanvasW - 12, 12.0, -1, 1), (12.0, BpCanvasH - 12, 1, -1), (BpCanvasW - 12, BpCanvasH - 12, -1, -1) })
        {
            c.Children.Add(BpLineShape(x, y, x + 14 * sx, y, BpLine(0x66)));
            c.Children.Add(BpLineShape(x, y, x, y + 14 * sy, BpLine(0x66)));
        }
        var tag = BpText("AMPUP · N3 STREAM CONTROLLER", 10, BpLine(0x77));
        At(tag, 32, BpCanvasH - 26);
        c.Children.Add(tag);
    }

    private static void DrawBlueprintDimensions(Canvas c)
    {
        var col = BpLine(0x99);
        // Width — along the bottom edge of the drawing
        double y = BpBodyY + BpBodyH + 96;
        c.Children.Add(BpLineShape(BpBodyX, BpBodyY + BpBodyH + 6, BpBodyX, y + 6, BpLine(0x44)));
        c.Children.Add(BpLineShape(BpBodyX + BpBodyW, BpBodyY + BpBodyH + 6, BpBodyX + BpBodyW, y + 6, BpLine(0x44)));
        c.Children.Add(BpArrowLine(BpBodyX, y, BpBodyX + BpBodyW, y, col));
        var w = BpDimLabel("129 mm");
        w.Loaded += (_, _) => At(w, BpBodyX + BpBodyW / 2 - w.ActualWidth / 2, y - w.ActualHeight / 2);
        At(w, BpBodyX + BpBodyW / 2 - 30, y - 9);
        c.Children.Add(w);

        // Depth — down the left side
        double x = BpBodyX - 32;
        c.Children.Add(BpLineShape(BpBodyX - 6, BpBodyY, x - 6, BpBodyY, BpLine(0x44)));
        c.Children.Add(BpLineShape(BpBodyX - 6, BpBodyY + BpBodyH, x - 6, BpBodyY + BpBodyH, BpLine(0x44)));
        c.Children.Add(BpArrowLine(x, BpBodyY, x, BpBodyY + BpBodyH, col));
        var h = BpDimLabel("78 mm");
        h.LayoutTransform = new RotateTransform(-90);
        h.Loaded += (_, _) => At(h, x - h.ActualHeight / 2, BpBodyY + BpBodyH / 2 - h.ActualWidth / 2);
        At(h, x - 9, BpBodyY + BpBodyH / 2 - 26);
        c.Children.Add(h);
    }

    private void PlaceBlueprintKeys(Canvas c)
    {
        _v2KeyGrid?.Children.Clear();
        for (int i = 0; i < _v2KeyTiles.Count; i++)
        {
            var tile = _v2KeyTiles[i];
            if (tile.Parent is Panel p) p.Children.Remove(tile);
            tile.Margin = new Thickness(0);
            At(tile, BpBodyX + BpKeyInset + (i % 3) * (BpKeySize + BpKeyGap), BpBodyY + BpKeyInset + (i / 3) * (BpKeySize + BpKeyGap));
            c.Children.Add(tile);
        }
    }

    // ── Controls ──────────────────────────────────────────────────────

    private V2HwControl AddBlueprintKnob(Canvas c, int encoderIdx, double cx, double cy, double d)
    {
        var ctl = new V2HwControl
        {
            ButtonIdx = StreamControllerEncoderPressBase + encoderIdx,
            EncoderIdx = encoderIdx,
            Name = $"Knob {encoderIdx + 1}",
        };
        var g = new Grid { Width = d, Height = d, Cursor = Cursors.Hand, Background = Brushes.Transparent };

        // Knurled skirt — a thick dashed stroke reads as grip ridges
        g.Children.Add(new Ellipse
        {
            Stroke = new SolidColorBrush(BpLine(0x50)), StrokeThickness = d * 0.05,
            StrokeDashArray = new DoubleCollection { 0.5, 1.2 },
            Margin = new Thickness(d * 0.025),
        });
        ctl.Glow = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 22, ShadowDepth = 0, Opacity = 0 };
        ctl.Outline = new Ellipse
        {
            Margin = new Thickness(d * 0.07),
            Fill = new RadialGradientBrush(Color.FromRgb(0x18, 0x26, 0x36), Color.FromRgb(0x08, 0x0E, 0x16))
                { GradientOrigin = new Point(0.35, 0.28), RadiusX = 0.7, RadiusY = 0.7 },
            Effect = ctl.Glow,
        };
        g.Children.Add(ctl.Outline);
        // Machined top face
        g.Children.Add(new Ellipse
        {
            Margin = new Thickness(d * 0.17),
            Stroke = new SolidColorBrush(BpLine(0x3A)), StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        });
        // Pointer
        g.Children.Add(new Border
        {
            Width = Math.Max(3, d * 0.018), Height = d * 0.09, CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, d * 0.1, 0, 0),
            Background = new SolidColorBrush(ThemeManager.Accent),
            Effect = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
            IsHitTestVisible = false,
        });

        ctl.Icon = new MaterialIcon { Width = d * 0.3, Height = d * 0.3, IsHitTestVisible = false,
            Margin = new Thickness(0, 0, 0, d * 0.06) };
        g.Children.Add(ctl.Icon);

        // Press-action badge at the bottom of the cap
        double bd = Math.Max(24, d * 0.2);
        ctl.PressIcon = new MaterialIcon { Width = bd * 0.56, Height = bd * 0.56 };
        var badge = new Border
        {
            Width = bd, Height = bd, CornerRadius = new CornerRadius(bd / 2),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, d * 0.13),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x12, 0x1C)),
            BorderBrush = new SolidColorBrush(BpLine(0x66)),
            Child = ctl.PressIcon,
            IsHitTestVisible = false,
        };
        g.Children.Add(badge);

        // Centre marks
        c.Children.Add(BpDashedLine(cx - d / 2 - 14, cy, cx + d / 2 + 14, cy));
        c.Children.Add(BpDashedLine(cx, cy - d / 2 - 14, cx, cy + d / 2 + 14));

        HookBlueprintControl(g, ctl);
        At(g, cx - d / 2, cy - d / 2);
        c.Children.Add(g);
        _v2HwControls.Add(ctl);
        return ctl;
    }

    private V2HwControl AddBlueprintButton(Canvas c, int i, double cx, double cy)
    {
        double d = BpButtonD;
        var ctl = new V2HwControl { ButtonIdx = StreamControllerSideButtonBase + i, Name = $"Button {i + 1}" };
        var g = new Grid { Width = d, Height = d, Cursor = Cursors.Hand, Background = Brushes.Transparent };

        // Bezel ring
        g.Children.Add(new Ellipse
        {
            Stroke = new SolidColorBrush(BpLine(0x4A)), StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
        });
        ctl.Glow = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0 };
        ctl.Outline = new Ellipse
        {
            Margin = new Thickness(8),
            Fill = new RadialGradientBrush(Color.FromRgb(0x16, 0x22, 0x30), Color.FromRgb(0x08, 0x0E, 0x16))
                { GradientOrigin = new Point(0.35, 0.3), RadiusX = 0.7, RadiusY = 0.7 },
            Effect = ctl.Glow,
        };
        g.Children.Add(ctl.Outline);
        ctl.Icon = new MaterialIcon { Width = d * 0.36, Height = d * 0.36, IsHitTestVisible = false };
        g.Children.Add(ctl.Icon);

        HookBlueprintControl(g, ctl);
        At(g, cx - d / 2, cy - d / 2);
        c.Children.Add(g);
        _v2HwControls.Add(ctl);
        return ctl;
    }

    private void HookBlueprintControl(FrameworkElement hit, V2HwControl ctl)
    {
        hit.MouseEnter += (_, _) => { ctl.Hover = true; ApplyBlueprintState(ctl); };
        hit.MouseLeave += (_, _) => { ctl.Hover = false; ApplyBlueprintState(ctl); };
        hit.MouseLeftButtonUp += (_, _) =>
        {
            string name = ctl.EncoderIdx >= 0 ? $"Encoder Press {ctl.EncoderIdx + 1}" : ctl.Name;
            SelectStreamControllerItem(new StreamControllerSelection(ctl.ButtonIdx, name, null));
        };
    }

    /// <summary>Leader line from the control to a mono title + action caption.</summary>
    private void AddBlueprintCallout(Canvas c, V2HwControl ctl, double x, double edgeY, bool above)
    {
        const double labelW = 150;
        double labelTop = above ? BpBodyY - 86 : BpBodyY + BpBodyH + 20;
        double lineEnd = above ? labelTop + 40 : labelTop - 2;
        double lineStart = above ? edgeY - 4 : edgeY + 4;
        c.Children.Add(BpLineShape(x, lineStart, x, lineEnd, BpLine(0x66), dashed: true));
        var dot = new Ellipse { Width = 5, Height = 5, Fill = new SolidColorBrush(BpLine(0xCC)) };
        At(dot, x - 2.5, lineStart - 2.5);
        c.Children.Add(dot);

        var stack = new StackPanel { Width = labelW };
        stack.Children.Add(new TextBlock
        {
            Text = ctl.Name.ToUpperInvariant(), FontFamily = BpMono, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(BpLine(0xEE)), HorizontalAlignment = HorizontalAlignment.Center,
        });
        ctl.Sub = new TextBlock
        {
            FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = labelW,
        };
        ctl.Sub.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        stack.Children.Add(ctl.Sub);
        At(stack, x - labelW / 2, labelTop);
        c.Children.Add(stack);
    }

    // ── Refresh ───────────────────────────────────────────────────────

    private void RefreshV2HardwareTiles()
    {
        if (_config == null) return;
        foreach (var ctl in _v2HwControls)
        {
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == ctl.ButtonIdx);
            if (ctl.EncoderIdx >= 0)
            {
                var rotation = GetEffectiveEditorEncoderBinding(ctl.EncoderIdx);
                string rotateLabel = FormatN3EncoderTarget(rotation?.Target);
                string pressLabel = GetStreamActionDisplay(press);
                if (ctl.Sub != null)
                {
                    ctl.Sub.Text = pressLabel == "None" ? rotateLabel : $"{rotateLabel} / {pressLabel}";
                    ctl.Sub.ToolTip = $"Turn: {rotateLabel}\nPress: {pressLabel}";
                }
                SetBlueprintIcon(ctl.Icon, BlueprintTargetIcon(rotation?.Target));
                if (ctl.PressIcon != null)
                {
                    var pk = BlueprintActionIcon(press?.Action);
                    ctl.PressIcon.Kind = pk ?? MaterialIconKind.GestureTap;
                    ctl.PressIcon.Foreground = new SolidColorBrush(pk != null ? ThemeManager.Accent : BpLine(0x66));
                }
            }
            else
            {
                if (ctl.Sub != null) ctl.Sub.Text = GetStreamActionDisplay(press);
                SetBlueprintIcon(ctl.Icon, BlueprintActionIcon(press?.Action));
            }
            ApplyBlueprintState(ctl);
        }
    }

    private void ApplyBlueprintState(V2HwControl ctl)
    {
        bool selected = _scSelectedButtonIdx == ctl.ButtonIdx;
        ctl.Outline.Stroke = new SolidColorBrush(selected ? ThemeManager.Accent : BpLine(ctl.Hover ? (byte)0xDD : (byte)0x99));
        ctl.Outline.StrokeThickness = selected ? 2.6 : 1.6;
        ctl.Glow.Color = ThemeManager.Accent;
        ctl.Glow.Opacity = selected ? 0.95 : ctl.Hover ? 0.5 : 0;
    }

    private static void SetBlueprintIcon(MaterialIcon icon, MaterialIconKind? kind)
    {
        icon.Kind = kind ?? MaterialIconKind.Plus;
        icon.Foreground = new SolidColorBrush(kind != null ? ThemeManager.Accent : BpLine(0x55));
        icon.Effect = kind != null
            ? new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.6 }
            : null;
    }

    private static MaterialIconKind? BlueprintActionIcon(string? action)
    {
        if (string.IsNullOrWhiteSpace(action) || action == "none") return null;
        return ActionPicker.ActionMeta.TryGetValue(action, out var meta) ? meta.Kind : MaterialIconKind.GestureTapButton;
    }

    private static MaterialIconKind? BlueprintTargetIcon(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "none") return null;
        return target switch
        {
            "sc_space_cycle" => MaterialIconKind.ViewDashboardOutline,
            "sc_page_cycle" => MaterialIconKind.BookOpenPageVariantOutline,
            _ => OsdOverlay.GetTargetIcon(target) ?? MaterialIconKind.Tune,
        };
    }

    // ── Drawing helpers ───────────────────────────────────────────────

    private static TextBlock BpText(string text, double size, Color color) => new()
    {
        Text = text, FontFamily = BpMono, FontSize = size, Foreground = new SolidColorBrush(color),
    };

    private static Border BpDimLabel(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x13, 0x1E)),
        Padding = new Thickness(6, 1, 6, 1),
        Child = BpText(text, 12, BpLine(0xCC)),
    };

    private static Line BpLineShape(double x1, double y1, double x2, double y2, Color color, bool dashed = false)
    {
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = new SolidColorBrush(color), StrokeThickness = 1, IsHitTestVisible = false };
        if (dashed) l.StrokeDashArray = new DoubleCollection { 4, 3 };
        return l;
    }

    private static Line BpDashedLine(double x1, double y1, double x2, double y2)
    {
        var l = BpLineShape(x1, y1, x2, y2, BpLine(0x30));
        l.StrokeDashArray = new DoubleCollection { 10, 4, 2, 4 }; // centre-line pattern
        return l;
    }

    /// <summary>Dimension line with arrowheads at both ends.</summary>
    private static UIElement BpArrowLine(double x1, double y1, double x2, double y2, Color color)
    {
        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(x1, y1) };
        fig.Segments.Add(new LineSegment(new Point(x2, y2), true));
        geo.Figures.Add(fig);
        var v = new Vector(x2 - x1, y2 - y1);
        v.Normalize();
        var n = new Vector(-v.Y, v.X);
        foreach (var (tip, dir) in new[] { (new Point(x1, y1), v), (new Point(x2, y2), -v) })
        {
            var head = new PathFigure { StartPoint = tip + dir * 9 + n * 4, IsClosed = true, IsFilled = true };
            head.Segments.Add(new LineSegment(tip, true));
            head.Segments.Add(new LineSegment(tip + dir * 9 - n * 4, true));
            geo.Figures.Add(head);
        }
        return new System.Windows.Shapes.Path
        {
            Data = geo, Stroke = new SolidColorBrush(color), Fill = new SolidColorBrush(color),
            StrokeThickness = 1, IsHitTestVisible = false,
        };
    }
}
