using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AmpUp.Controls;
using Material.Icons;
using Material.Icons.WPF;

namespace AmpUp.Views;

/// <summary>
/// N3 "blueprint" device drawing for the Buttons tab. Follows the real TreasLin / MiraBox N3
/// layout — 3x2 LCD block top-left, one large knob top-right, three round buttons and two
/// small knobs along the bottom — drawn as accent line art on a drafting grid.
/// Controls carry numbered badges (no leader lines); a CONTROLS legend beside the drawing
/// spells out each control's Twist / Press / Tap / Double / Hold actions. Knobs show their
/// live level as an arc + pointer; side buttons show where they go (Spotify, Home, …).
/// Clicking a control or its legend row selects it in the editor.
/// </summary>
public partial class ButtonsView
{
    // Which encoder index is the large knob. N3Controller reports encoders 0-2 by HID code;
    // encoder 0 (codes 0x90/0x91) is treated as the big top-right knob.
    private const int V2BigKnobEncoder = 0;

    // Canvas geometry. The body keeps the real 129 x 78 mm proportions (≈1.65 : 1).
    private const double BpCanvasW = 860, BpCanvasH = 610;
    private const double BpBodyX = 62, BpBodyY = 40, BpBodyW = 760, BpBodyH = 460;
    private const double BpKeySize = 120, BpKeyGap = 12, BpKeyInset = 44;
    private const double BpBigKnobD = 220, BpSmallKnobD = 112, BpButtonD = 72;
    private static readonly FontFamily BpMono = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Color BpSpotifyGreen = Color.FromRgb(0x1D, 0xB9, 0x54);

    private sealed class V2HwControl
    {
        public int ButtonIdx;
        public int EncoderIdx = -1;     // -1 for the side buttons
        public int Number;              // badge number (1 = big knob, 2-4 buttons, 5-6 small knobs)
        public string Name = "";
        public double Diameter;
        public Color Color;
        public Ellipse Outline = null!;
        public DropShadowEffect Glow = null!;
        public MaterialIcon Icon = null!;
        public Path? ValueArc;
        public RotateTransform? Pointer;
        public Border Badge = null!;
        public TextBlock BadgeText = null!;
        // Legend row
        public Border Row = null!;
        public Border RowBadge = null!;
        public TextBlock RowBadgeText = null!;
        public TextBlock RowName = null!;
        public WrapPanel RowChips = null!;
        public bool Hover;
        public double LastLevel = -1;
    }

    private readonly List<V2HwControl> _v2HwControls = new();
    private DispatcherTimer? _bpLevelTimer;

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

        // Drawing on the left, CONTROLS legend on the right
        var body = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(body);

        var canvas = new Canvas { Width = BpCanvasW, Height = BpCanvasH };
        var viewbox = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
        body.Children.Add(viewbox);

        var legend = new StackPanel { Width = 260, Margin = new Thickness(12, 6, 0, 0) };
        legend.Children.Add(BpText("CONTROLS", 11, BpLine(0xAA)));
        ((FrameworkElement)legend.Children[0]).Margin = new Thickness(4, 0, 0, 8);
        Grid.SetColumn(legend, 1);
        body.Children.Add(legend);

        DrawBlueprintBody(canvas);
        DrawBlueprintDimensions(canvas);
        PlaceBlueprintKeys(canvas);

        _v2HwControls.Clear();
        _v2ButtonTiles.Clear();
        _v2EncoderTiles.Clear();

        // Large knob, top right — centred on the key block's vertical middle
        double keyBlockH = 2 * BpKeySize + BpKeyGap;
        double bigCx = BpBodyX + BpBodyW - 44 - BpBigKnobD / 2;
        double topCy = BpBodyY + BpKeyInset + keyBlockH / 2;
        AddBlueprintKnob(canvas, V2BigKnobEncoder, bigCx, topCy, BpBigKnobD, number: 1);

        // Bottom row: one button under each LCD column, then the two small knobs to the right,
        // under the big knob. Centred in the band between the key block and the body edge.
        double keysX = BpBodyX + BpKeyInset;
        double bandTop = BpBodyY + BpKeyInset + keyBlockH + 10;
        double rowCy = (bandTop + BpBodyY + BpBodyH - 12) / 2;
        for (int i = 0; i < 3; i++)
        {
            double cx = keysX + BpKeySize / 2 + i * (BpKeySize + BpKeyGap);
            AddBlueprintButton(canvas, i, cx, rowCy, number: 2 + i);
        }
        var small = Enumerable.Range(0, 3).Where(e => e != V2BigKnobEncoder).ToList();
        double smallRight = BpBodyX + BpBodyW - 40 - BpSmallKnobD / 2;
        double[] smallCx = { smallRight - 150, smallRight };
        for (int s = 0; s < small.Count; s++)
            AddBlueprintKnob(canvas, small[s], smallCx[s], rowCy, BpSmallKnobD, number: 5 + s);

        foreach (var ctl in _v2HwControls.OrderBy(c => c.Number))
            legend.Children.Add(BuildBlueprintLegendRow(ctl));

        // Knob arcs follow the live encoder positions while the drawing is on screen.
        canvas.Loaded += (_, _) =>
        {
            _bpLevelTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _bpLevelTimer.Tick -= BpLevelTick;
            _bpLevelTimer.Tick += BpLevelTick;
            _bpLevelTimer.Start();
        };
        canvas.Unloaded += (_, _) => _bpLevelTimer?.Stop();

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
        double y = BpBodyY + BpBodyH + 46;
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

    private V2HwControl AddBlueprintKnob(Canvas c, int encoderIdx, double cx, double cy, double d, int number)
    {
        var ctl = new V2HwControl
        {
            ButtonIdx = StreamControllerEncoderPressBase + encoderIdx,
            EncoderIdx = encoderIdx,
            Number = number,
            Name = $"Knob {encoderIdx + 1}",
            Diameter = d,
            Color = ThemeManager.Accent,
        };
        var g = new Grid { Width = d, Height = d, Cursor = Cursors.Hand, Background = Brushes.Transparent };

        // Level track + live value arc (270° sweep, gap at the bottom like a real pot)
        double arcT = Math.Max(3, d * 0.035);
        g.Children.Add(new Path { Data = BpArcGeometry(d, arcT, 270), Stroke = new SolidColorBrush(BpLine(0x30)), StrokeThickness = arcT, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false });
        ctl.ValueArc = new Path
        {
            Stroke = new SolidColorBrush(ThemeManager.Accent), StrokeThickness = arcT,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.7 },
        };
        g.Children.Add(ctl.ValueArc);

        ctl.Glow = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 22, ShadowDepth = 0, Opacity = 0 };
        ctl.Outline = new Ellipse
        {
            Margin = new Thickness(d * 0.11),
            Fill = new RadialGradientBrush(Color.FromRgb(0x1C, 0x2B, 0x3C), Color.FromRgb(0x08, 0x0E, 0x16))
                { GradientOrigin = new Point(0.35, 0.28), RadiusX = 0.7, RadiusY = 0.7 },
            Effect = ctl.Glow,
        };
        g.Children.Add(ctl.Outline);
        // Machined top face
        g.Children.Add(new Ellipse
        {
            Margin = new Thickness(d * 0.2),
            Stroke = new SolidColorBrush(BpLine(0x3A)), StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            IsHitTestVisible = false,
        });
        // Pointer — rotates with the knob's level
        ctl.Pointer = new RotateTransform(0);
        var pointerHost = new Grid { RenderTransform = ctl.Pointer, RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false };
        pointerHost.Children.Add(new Border
        {
            Width = Math.Max(3, d * 0.022), Height = d * 0.1, CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, d * 0.135, 0, 0),
            Background = new SolidColorBrush(ThemeManager.Accent),
            Effect = new DropShadowEffect { Color = ThemeManager.Accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
        });
        g.Children.Add(pointerHost);

        ctl.Icon = new MaterialIcon { Width = d * 0.28, Height = d * 0.28, IsHitTestVisible = false };
        g.Children.Add(ctl.Icon);

        // Centre marks
        c.Children.Add(BpDashedLine(cx - d / 2 - 14, cy, cx + d / 2 + 14, cy));
        c.Children.Add(BpDashedLine(cx, cy - d / 2 - 14, cx, cy + d / 2 + 14));

        HookBlueprintControl(g, ctl);
        At(g, cx - d / 2, cy - d / 2);
        c.Children.Add(g);
        AddBlueprintBadge(c, ctl, cx, cy, d);
        _v2HwControls.Add(ctl);
        return ctl;
    }

    private V2HwControl AddBlueprintButton(Canvas c, int i, double cx, double cy, int number)
    {
        double d = BpButtonD;
        var ctl = new V2HwControl
        {
            ButtonIdx = StreamControllerSideButtonBase + i, Number = number, Name = $"Button {i + 1}",
            Diameter = d, Color = ThemeManager.Accent,
        };
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
        ctl.Icon = new MaterialIcon { Width = d * 0.38, Height = d * 0.38, IsHitTestVisible = false };
        g.Children.Add(ctl.Icon);

        HookBlueprintControl(g, ctl);
        At(g, cx - d / 2, cy - d / 2);
        c.Children.Add(g);
        AddBlueprintBadge(c, ctl, cx, cy, d);
        _v2HwControls.Add(ctl);
        return ctl;
    }

    /// <summary>Small numbered badge at the control's top-right, matching its legend row.</summary>
    private void AddBlueprintBadge(Canvas c, V2HwControl ctl, double cx, double cy, double d)
    {
        const double bd = 26;
        ctl.BadgeText = new TextBlock
        {
            Text = ctl.Number.ToString(), FontFamily = BpMono, FontSize = 13, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ctl.Badge = new Border
        {
            Width = bd, Height = bd, CornerRadius = new CornerRadius(bd / 2), BorderThickness = new Thickness(1.5),
            Child = ctl.BadgeText, IsHitTestVisible = false,
        };
        double off = d / 2 * 0.71; // 45° on the rim
        At(ctl.Badge, cx + off - bd / 2 + 4, cy - off - bd / 2 - 4);
        c.Children.Add(ctl.Badge);
    }

    private FrameworkElement BuildBlueprintLegendRow(V2HwControl ctl)
    {
        ctl.RowBadgeText = new TextBlock
        {
            Text = ctl.Number.ToString(), FontFamily = BpMono, FontSize = 12, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ctl.RowBadge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0), Child = ctl.RowBadgeText,
        };

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = ctl.Name.ToUpperInvariant(), FontFamily = BpMono, FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(BpLine(0xBB)),
        });
        ctl.RowName = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        ctl.RowName.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        text.Children.Add(ctl.RowName);
        ctl.RowChips = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        text.Children.Add(ctl.RowChips);

        var dock = new DockPanel();
        DockPanel.SetDock(ctl.RowBadge, Dock.Left);
        dock.Children.Add(ctl.RowBadge);
        dock.Children.Add(text);

        ctl.Row = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 4),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = dock,
        };
        HookBlueprintControl(ctl.Row, ctl);
        return ctl.Row;
    }

    private static Border BpChip(string gesture, string action)
    {
        var tb = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 210 };
        var g = new System.Windows.Documents.Run(gesture + " · ");
        g.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextDimBrush");
        var a = new System.Windows.Documents.Run(action);
        a.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecBrush");
        tb.Inlines.Add(g);
        tb.Inlines.Add(a);
        var chip = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 5, 4), Child = tb, ToolTip = $"{gesture}: {action}" };
        chip.SetResourceReference(Border.BackgroundProperty, "CardInsetBrush");
        return chip;
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

    // ── Refresh ───────────────────────────────────────────────────────

    private void RefreshV2HardwareTiles()
    {
        if (_config == null) return;
        foreach (var ctl in _v2HwControls)
        {
            var press = _config.N3.Buttons.FirstOrDefault(b => b.Idx == ctl.ButtonIdx);
            ctl.RowChips?.Children.Clear();
            if (ctl.EncoderIdx >= 0)
            {
                var rotation = GetEffectiveEditorEncoderBinding(ctl.EncoderIdx);
                string rotateLabel = FormatN3EncoderTarget(rotation?.Target);
                var kind = BlueprintTargetIcon(rotation?.Target);
                ctl.Color = IsSpotifyish(rotation?.Target) ? BpSpotifyGreen : ThemeManager.Accent;
                SetBlueprintIcon(ctl.Icon, kind, ctl.Color);
                if (ctl.RowName != null)
                {
                    ctl.RowName.Text = rotation == null || kind == null ? "Not set" : rotateLabel;
                    ctl.RowChips.Children.Add(BpChip("Twist", kind == null ? "Nothing" : rotateLabel));
                    AddGestureChips(ctl.RowChips, press, "Press");
                }
                ctl.LastLevel = -1; // repaint arc in the new colour
                UpdateBlueprintLevel(ctl);
            }
            else
            {
                var (kind, dest) = BlueprintButtonLook(press);
                ctl.Color = IsSpotifyish(dest) || IsSpotifyish(press?.Action) ? BpSpotifyGreen : ThemeManager.Accent;
                SetBlueprintIcon(ctl.Icon, kind, ctl.Color);
                if (ctl.RowName != null)
                {
                    ctl.RowName.Text = dest;
                    AddGestureChips(ctl.RowChips, press, "Tap");
                }
            }
            ApplyBlueprintState(ctl);
        }
    }

    /// <summary>Tap/Press, Double and Hold chips for whichever gestures are set.</summary>
    private void AddGestureChips(WrapPanel chips, ButtonConfig? btn, string firstGesture)
    {
        string tap = GetStreamActionDisplay(btn);
        chips.Children.Add(BpChip(firstGesture, tap == "None" ? "Nothing" : tap));
        if (btn == null) return;
        if (!string.IsNullOrWhiteSpace(btn.DoublePressAction) && btn.DoublePressAction != "none")
            chips.Children.Add(BpChip("Double", GetStreamActionDisplay(btn.DoublePressAction)));
        if (!string.IsNullOrWhiteSpace(btn.HoldAction) && btn.HoldAction != "none")
            chips.Children.Add(BpChip("Hold", GetStreamActionDisplay(btn.HoldAction)));
    }

    /// <summary>What a side button shows: where it goes for Spaces, else the action's icon.</summary>
    private (MaterialIconKind? Kind, string Dest) BlueprintButtonLook(ButtonConfig? btn)
    {
        if (btn == null || string.IsNullOrWhiteSpace(btn.Action) || btn.Action == "none") return (null, "Not set");
        if (btn.Action == "open_folder")
        {
            var space = string.IsNullOrWhiteSpace(btn.FolderName) ? "Home" : btn.FolderName;
            var s = space.ToLowerInvariant();
            MaterialIconKind kind =
                s == "home" ? MaterialIconKind.HomeOutline
                : s.Contains("spotify") ? MaterialIconKind.Spotify
                : s.Contains("light") ? MaterialIconKind.LightbulbOutline
                : s.Contains("effect") || s.Contains("fx") ? MaterialIconKind.CreationOutline
                : s.Contains("music") || s.Contains("podcast") || s.Contains("pocket") ? MaterialIconKind.PlaylistMusic
                : MaterialIconKind.FolderOutline;
            return (kind, space);
        }
        return (BlueprintActionIcon(btn.Action), GetStreamActionDisplay(btn));
    }

    private static bool IsSpotifyish(string? s) => !string.IsNullOrEmpty(s) && s.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    private void BpLevelTick(object? sender, EventArgs e)
    {
        foreach (var ctl in _v2HwControls)
            if (ctl.EncoderIdx >= 0) UpdateBlueprintLevel(ctl);
    }

    /// <summary>Arc + pointer follow the encoder's live position (0-1).</summary>
    private static void UpdateBlueprintLevel(V2HwControl ctl)
    {
        if (ctl.ValueArc == null || ctl.EncoderIdx < 0) return;
        var positions = App.StreamControllerKnobPositions;
        double v = ctl.EncoderIdx < positions.Length ? Math.Clamp(positions[ctl.EncoderIdx], 0f, 1f) : 0;
        if (Math.Abs(v - ctl.LastLevel) < 0.002) return;
        ctl.LastLevel = v;
        double t = ctl.ValueArc.StrokeThickness;
        ctl.ValueArc.Data = v < 0.004 ? null : BpArcGeometry(ctl.Diameter, t, 270 * v);
        ctl.ValueArc.Stroke = new SolidColorBrush(ctl.Color);
        if (ctl.ValueArc.Effect is DropShadowEffect fx) fx.Color = ctl.Color;
        if (ctl.Pointer != null) ctl.Pointer.Angle = -135 + 270 * v;
    }

    /// <summary>Arc on a circle of diameter d (inset by half the stroke), starting at -135° (bottom-left).</summary>
    private static Geometry BpArcGeometry(double d, double thickness, double sweepDeg)
    {
        double r = d / 2 - thickness / 2 - 1, c = d / 2;
        Point P(double deg) { double a = deg * Math.PI / 180; return new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)); }
        sweepDeg = Math.Clamp(sweepDeg, 0.5, 270);
        var fig = new PathFigure { StartPoint = P(-135), IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment(P(-135 + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private void ApplyBlueprintState(V2HwControl ctl)
    {
        bool selected = _scSelectedButtonIdx == ctl.ButtonIdx;
        var a = ThemeManager.Accent;
        ctl.Outline.Stroke = new SolidColorBrush(selected ? a : BpLine(ctl.Hover ? (byte)0xDD : (byte)0x99));
        ctl.Outline.StrokeThickness = selected ? 2.6 : 1.6;
        ctl.Glow.Color = a;
        ctl.Glow.Opacity = selected ? 0.95 : ctl.Hover ? 0.5 : 0;

        foreach (var (badge, txt) in new[] { (ctl.Badge, ctl.BadgeText), (ctl.RowBadge, ctl.RowBadgeText) })
        {
            if (badge == null) continue;
            badge.BorderBrush = new SolidColorBrush(a);
            badge.Background = new SolidColorBrush(selected ? a : Color.FromRgb(0x0A, 0x13, 0x1E));
            txt.Foreground = new SolidColorBrush(selected ? Colors.White : ThemeManager.WithAlpha(a, 0xDD));
        }
        if (ctl.Row != null)
        {
            if (selected) ctl.Row.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x26));
            else if (ctl.Hover) ctl.Row.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x12));
            else ctl.Row.Background = Brushes.Transparent;
        }
    }

    private static void SetBlueprintIcon(MaterialIcon icon, MaterialIconKind? kind, Color color)
    {
        icon.Kind = kind ?? MaterialIconKind.Plus;
        icon.Foreground = new SolidColorBrush(kind != null ? color : BpLine(0x55));
        icon.Effect = kind != null
            ? new DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.6 }
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
