using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AmpUp.Core.Models;

namespace AmpUp.Mac.Controls;

/// <summary>
/// Compact status bar at the bottom of the main window showing real-time
/// state for all 5 knobs: LED colors, knob position arc, audio level bar, label.
/// Polls App.Rgb and App.KnobPositions at 50ms (20fps).
/// </summary>
public partial class HardwarePreviewStrip : UserControl
{
    // ── Layout ────────────────────────────────────────────────────────────────
    private const double ColW = 120;        // nominal, actual width from bounds
    private const double LedDotR = 4.5;     // LED dot radius
    private const double ArcRadius = 16.0;
    private const double ArcStroke = 3.0;
    private const double ArcStartDeg = 135.0;  // matches KnobControl
    private const double ArcSweepDeg = 270.0;
    private const double LevelBarH = 4.0;
    private const double LevelBarMaxW = 52.0;

    // ── State ─────────────────────────────────────────────────────────────────
    private AppConfig? _config;
    private readonly DispatcherTimer _timer;

    // Per-channel elements (built once in Loaded)
    private readonly KnobPreviewColumn[] _cols = new KnobPreviewColumn[5];

    // Cached values for dirty-checking
    private readonly float[] _lastPos = new float[5];
    private readonly byte[] _lastR = new byte[5], _lastG = new byte[5], _lastB = new byte[5];
    private readonly float[] _lastLevel = new float[5];
    private readonly string[] _lastLabel = new string[5];

    // ── Canvas references (from XAML) ─────────────────────────────────────────
    private Canvas[] _canvases = Array.Empty<Canvas>();

    public HardwarePreviewStrip()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => { if (IsVisible) Refresh(); };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void LoadConfig(AppConfig config)
    {
        _config = config;

        // Force refresh of labels/colors on next tick
        for (int i = 0; i < 5; i++)
        {
            _lastLabel[i] = string.Empty;
            _lastR[i] = 0; _lastG[i] = 0; _lastB[i] = 0;
            _lastPos[i] = -1f;
        }

        // Apply labels immediately if already built
        if (_cols[0] != null)
            ApplyLabels();
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _canvases = new[]
        {
            Strip0, Strip1, Strip2, Strip3, Strip4
        };

        for (int i = 0; i < 5; i++)
        {
            _cols[i] = new KnobPreviewColumn();
            _canvases[i].Children.Add(_cols[i]);
        }

        // Respond to canvas size changes
        for (int ci = 0; ci < _canvases.Length; ci++)
        {
            var canvas = _canvases[ci];
            var col = _cols[ci];
            canvas.SizeChanged += (_, e) =>
            {
                col.Width = e.NewSize.Width;
                col.Height = e.NewSize.Height;
                col.InvalidateVisual();
            };
        }

        ApplyLabels();
        _timer.Start();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _timer.Stop();
    }

    private void ApplyLabels()
    {
        if (_config == null) return;
        for (int i = 0; i < 5; i++)
        {
            var knob = _config.Knobs.Count > i ? _config.Knobs[i] : null;
            string lbl = !string.IsNullOrEmpty(knob?.Label) ? knob!.Label : $"Knob {i + 1}";
            _cols[i].Label = lbl;
        }
    }

    // ── Live update ───────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (_cols[0] == null) return;  // not yet built
        var rgb = App.Rgb;

        for (int i = 0; i < 5; i++)
        {
            // ── LED color ──────────────────────────────────────────────────
            Color ledColor = Color.Parse("#00E676");
            if (rgb != null)
            {
                var (r, g, b) = rgb.GetCurrentColor(i);
                ledColor = (r > 5 || g > 5 || b > 5)
                    ? Color.FromRgb(r, g, b)
                    : Color.Parse("#222222");

                if (r != _lastR[i] || g != _lastG[i] || b != _lastB[i])
                {
                    _lastR[i] = r; _lastG[i] = g; _lastB[i] = b;
                    _cols[i].LedColor = ledColor;
                }
            }

            // ── Knob position ──────────────────────────────────────────────
            float pos = App.KnobPositions[i];
            if (Math.Abs(pos - _lastPos[i]) > 0.005f)
            {
                _lastPos[i] = pos;
                _cols[i].KnobPos = pos;
            }

            // ── Label ──────────────────────────────────────────────────────
            var knob = _config?.Knobs.Count > i ? _config!.Knobs[i] : null;
            string lbl = !string.IsNullOrEmpty(knob?.Label) ? knob!.Label : $"Knob {i + 1}";
            if (lbl != _lastLabel[i])
            {
                _lastLabel[i] = lbl;
                _cols[i].Label = lbl;
            }

            _cols[i].InvalidateVisual();
        }
    }

    // ── KnobPreviewColumn ─────────────────────────────────────────────────────

    /// <summary>
    /// Single-knob column drawn entirely via Render().
    /// Layout (top→bottom, centered):
    ///   · 3 LED dots (row)
    ///   · mini arc (knob position)
    ///   · thin level bar
    ///   · label text
    /// </summary>
    private sealed class KnobPreviewColumn : Control
    {
        public Color LedColor { get; set; } = Color.Parse("#00E676");
        public float KnobPos { get; set; }   // 0–1
        public float Level { get; set; }     // 0–1 (audio peak, future)
        public string Label { get; set; } = string.Empty;

        public override void Render(DrawingContext dc)
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w < 4 || h < 4) return;

            double cx = w / 2.0;

            // ── Vertical layout ────────────────────────────────────────────
            // Measure components first
            double ledRowH = LedDotR * 2 + 2;
            double arcSize = ArcRadius * 2 + ArcStroke * 2;
            double levelBarH2 = LevelBarH;
            double labelH = 11.0;
            double gapLedArc = 3.0;
            double gapArcLevel = 3.0;
            double gapLevelLabel = 2.0;

            double totalH = ledRowH + gapLedArc + arcSize + gapArcLevel + levelBarH2 + gapLevelLabel + labelH;
            double topPad = Math.Max(0, (h - totalH) / 2.0);

            double yLed = topPad;
            double yArc = yLed + ledRowH + gapLedArc;
            double yLevel = yArc + arcSize + gapArcLevel;
            double yLabel = yLevel + levelBarH2 + gapLevelLabel;

            // ── 3 LED dots ─────────────────────────────────────────────────
            double dotSpacing = LedDotR * 2 + 4.0;
            double dotsStartX = cx - dotSpacing;  // center of 3 dots
            for (int d = 0; d < 3; d++)
            {
                double dx = dotsStartX + d * dotSpacing;
                double dy = yLed + LedDotR;
                var dotBrush = new SolidColorBrush(LedColor);
                dc.DrawEllipse(dotBrush, null, new Point(dx, dy), LedDotR, LedDotR);
            }

            // ── Mini arc (knob position) ───────────────────────────────────
            double arcCx = cx;
            double arcCy = yArc + ArcRadius + ArcStroke;

            // Track arc
            var trackPen = new Pen(new SolidColorBrush(Color.Parse("#2A2A2A")), ArcStroke, lineCap: PenLineCap.Round);
            DrawArc(dc, trackPen, arcCx, arcCy, ArcRadius, ArcStartDeg, ArcSweepDeg);

            // Value arc
            if (KnobPos > 0.005f)
            {
                double sweep = ArcSweepDeg * KnobPos;
                var valuePen = new Pen(new SolidColorBrush(LedColor), ArcStroke, lineCap: PenLineCap.Round);
                DrawArc(dc, valuePen, arcCx, arcCy, ArcRadius, ArcStartDeg, sweep);

                // Tip dot
                double tipRad = (ArcStartDeg + sweep) * Math.PI / 180.0;
                dc.DrawEllipse(new SolidColorBrush(LedColor), null,
                    new Point(arcCx + ArcRadius * Math.Cos(tipRad), arcCy + ArcRadius * Math.Sin(tipRad)),
                    2.0, 2.0);
            }

            // ── Level bar ─────────────────────────────────────────────────
            double barMaxW = Math.Min(LevelBarMaxW, w - 16);
            double barX = cx - barMaxW / 2.0;

            // Background track
            var trackBg = new SolidColorBrush(Color.Parse("#2A2A2A"));
            dc.DrawRectangle(trackBg, null, new Rect(barX, yLevel, barMaxW, LevelBarH), 2, 2);

            // Filled level
            double levelW = barMaxW * Math.Clamp(Level, 0, 1);
            if (levelW > 0.5)
            {
                var levelColor = Color.FromArgb(0xCC, LedColor.R, LedColor.G, LedColor.B);
                dc.DrawRectangle(new SolidColorBrush(levelColor), null,
                    new Rect(barX, yLevel, levelW, LevelBarH), 2, 2);
            }

            // ── Label ──────────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(Label))
            {
                var ft = new FormattedText(
                    Label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("system-ui"),
                    9.5,
                    new SolidColorBrush(Color.Parse("#777777")));
                ft.MaxTextWidth = w - 4;
                ft.MaxTextHeight = labelH + 2;
                double tx = cx - ft.Width / 2.0;
                dc.DrawText(ft, new Point(Math.Max(2, tx), yLabel));
            }
        }

        private static void DrawArc(DrawingContext dc, Pen pen, double cx, double cy, double r,
            double startDeg, double sweepDeg)
        {
            if (sweepDeg <= 0) return;
            sweepDeg = Math.Clamp(sweepDeg, 0, 359.9);

            double startRad = startDeg * Math.PI / 180.0;
            double endRad = (startDeg + sweepDeg) * Math.PI / 180.0;

            var startPt = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            var endPt = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(startPt, false);
                ctx.ArcTo(endPt, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
            }

            dc.DrawGeometry(null, pen, geo);
        }
    }
}
