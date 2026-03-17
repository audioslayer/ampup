using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AmpUp.Controls
{
    /// <summary>
    /// Persistent hardware status strip docked at the bottom of the main window.
    /// Shows all 5 knobs with live LED colors, knob position arcs, VU level bars,
    /// and target labels. Updates at 20 FPS via Tick() called from MainWindow.
    /// </summary>
    public class HardwarePreview : FrameworkElement
    {
        private const int KnobCount = 5;
        private const double StripHeight = 52.0;

        // Layout constants
        private const double LedDotRadius = 4.0;
        private const double LedDotSpacing = 11.0;    // center-to-center
        private const double ArcRadius = 14.0;
        private const double ArcStroke = 2.5;
        private const double VuHeight = 3.0;
        private const double TopPad = 6.0;
        private const double LabelFontSize = 9.0;

        // ── Static frozen resources ──────────────────────────────────────
        private static readonly SolidColorBrush BgBrush;
        private static readonly Pen TopBorderPen;
        private static readonly SolidColorBrush DividerBrush;
        private static readonly Pen ArcTrackPen;
        private static readonly SolidColorBrush LabelBrush;
        private static readonly SolidColorBrush DimBrush;
        private static readonly SolidColorBrush VuBgBrush;
        private static readonly Typeface LabelTypeface;

        static HardwarePreview()
        {
            BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)));
            TopBorderPen = FreezeP(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), 1.0));
            DividerBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)));
            VuBgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)));
            LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
            DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));

            var trackBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            trackBrush.Freeze();
            ArcTrackPen = new Pen(trackBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            ArcTrackPen.Freeze();

            LabelTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        }

        // ── State ─────────────────────────────────────────────────────────

        // LED colors: [knobIdx, ledIdx] → (R, G, B)
        private readonly (byte R, byte G, byte B)[,] _ledColors = new (byte, byte, byte)[KnobCount, 3];
        // Knob positions 0-1
        private readonly float[] _positions = new float[KnobCount];
        // Smoothed VU levels 0-1
        private readonly float[] _vuSmoothed = new float[KnobCount];
        private readonly float[] _vuTarget = new float[KnobCount];
        // Labels per knob
        private readonly string[] _labels = { "1", "2", "3", "4", "5" };
        // Connected status
        private bool _connected;
        // Click callback: knobIdx → navigate to Mixer tab
        public Action<int>? OnKnobClicked;

        // ── DrawingVisual ─────────────────────────────────────────────────

        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _dv;

        public HardwarePreview()
        {
            _dv = new DrawingVisual();
            _visuals = new VisualCollection(this) { _dv };
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += OnMouseDown;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (OnKnobClicked == null) return;
            double w = ActualWidth;
            if (w <= 0) return;
            double slotW = w / KnobCount;
            double x = e.GetPosition(this).X;
            int idx = (int)(x / slotW);
            idx = Math.Clamp(idx, 0, KnobCount - 1);
            OnKnobClicked(idx);
        }

        #region Visual tree plumbing

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _visuals.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _visuals[index];
        }

        #endregion

        #region Layout

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width, StripHeight);

        protected override Size ArrangeOverride(Size finalSize)
        {
            Render(finalSize);
            return new Size(finalSize.Width, StripHeight);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set current LED frame data from RgbController._linearColors.
        /// byte[45]: knob0LED0_R/G/B, knob0LED1_R/G/B, ... knob4LED2_R/G/B
        /// </summary>
        public void SetLedFrame(byte[] frame)
        {
            if (frame == null || frame.Length < 45) return;
            for (int k = 0; k < KnobCount; k++)
            {
                for (int l = 0; l < 3; l++)
                {
                    int off = k * 9 + l * 3;
                    _ledColors[k, l] = (frame[off], frame[off + 1], frame[off + 2]);
                }
            }
        }

        /// <summary>
        /// Update knob positions (0-1) and VU target levels. Called per tick.
        /// </summary>
        public void SetPositions(float[] positions)
        {
            for (int i = 0; i < KnobCount && i < positions.Length; i++)
                _positions[i] = Math.Clamp(positions[i], 0f, 1f);
        }

        public void SetVuLevel(int idx, float level)
        {
            if (idx >= 0 && idx < KnobCount)
                _vuTarget[idx] = Math.Clamp(level, 0f, 1f);
        }

        public void SetLabel(int idx, string label)
        {
            if (idx >= 0 && idx < KnobCount)
                _labels[idx] = string.IsNullOrWhiteSpace(label) ? (idx + 1).ToString() : label;
        }

        public void SetConnected(bool connected)
        {
            _connected = connected;
        }

        /// <summary>
        /// Called by MainWindow's 50ms DispatcherTimer to smooth VU and redraw.
        /// </summary>
        public void Tick()
        {
            const float attack = 0.5f;
            const float decay = 0.88f;

            for (int i = 0; i < KnobCount; i++)
            {
                if (_vuTarget[i] > _vuSmoothed[i])
                    _vuSmoothed[i] += (_vuTarget[i] - _vuSmoothed[i]) * attack;
                else
                    _vuSmoothed[i] *= decay;

                if (_vuSmoothed[i] < 0.001f) _vuSmoothed[i] = 0f;
            }

            Render(new Size(ActualWidth, ActualHeight));
        }

        #endregion

        #region Rendering

        private void Render(Size size)
        {
            double w = size.Width;
            double h = StripHeight;
            if (w <= 0) return;

            double slotW = w / KnobCount;

            using var dc = _dv.RenderOpen();

            // Background
            dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));

            // Top border line
            dc.DrawLine(TopBorderPen, new Point(0, 0.5), new Point(w, 0.5));

            for (int k = 0; k < KnobCount; k++)
            {
                double slotLeft = k * slotW;
                double cx = slotLeft + slotW / 2.0;

                // Vertical divider (not before first slot)
                if (k > 0)
                    dc.DrawRectangle(DividerBrush, null, new Rect(slotLeft, 4, 1, h - 8));

                DrawKnobSlot(dc, k, cx, slotW, h);
            }

            // Connection status dot — bottom-right corner of strip
            double dotX = w - 7;
            double dotY = h - 7;
            var dotBrush = _connected
                ? Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0x77)))
                : Freeze(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)));
            dc.DrawEllipse(dotBrush, null, new Point(dotX, dotY), 2.5, 2.5);
        }

        private void DrawKnobSlot(DrawingContext dc, int k, double cx, double slotW, double h)
        {
            // ── Layout ──────────────────────────────────────────────────
            // From top:  pad | label | gap | arc | gap | LED dots | gap | VU bar | pad
            double labelY = TopPad;
            double arcCy = labelY + 10 + ArcRadius + 2;
            double ledRowY = arcCy + ArcRadius + 5;
            double vuY = h - VuHeight - 4;

            // ── Label ────────────────────────────────────────────────────
            string label = _labels[k];
            if (label.Length > 7) label = label.Substring(0, 7);
            var ft = MakeText(label, LabelBrush, LabelFontSize, slotW - 8);
            dc.DrawText(ft, new Point(cx - ft.Width / 2.0, labelY));

            // ── Arc (knob position) ───────────────────────────────────────
            // Get the "dominant" color for this knob (average of 3 LEDs, using LED 1 as representative)
            var (r1, g1, b1) = _ledColors[k, 1];
            bool hasBrightLed = r1 > 10 || g1 > 10 || b1 > 10;

            Color arcColor = hasBrightLed
                ? Color.FromRgb(r1, g1, b1)
                : Color.FromRgb(0x44, 0x44, 0x44);

            // Draw track arc
            const double StartDeg = 225.0;
            const double SweepDeg = 270.0;
            var trackArc = CreateArc(cx, arcCy, ArcRadius, StartDeg, SweepDeg);
            dc.DrawGeometry(null, ArcTrackPen, trackArc);

            float pos = _positions[k];
            if (pos > 0.005f)
            {
                double valueSweep = pos * SweepDeg;
                var arcBrush = new SolidColorBrush(arcColor);
                arcBrush.Freeze();
                var arcPen = new Pen(arcBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                arcPen.Freeze();

                var valueArc = CreateArc(cx, arcCy, ArcRadius, StartDeg, valueSweep);
                dc.DrawGeometry(null, arcPen, valueArc);

                // Tip dot
                double tipRad = (StartDeg - 90.0 + valueSweep) * Math.PI / 180.0;
                double tipX = cx + ArcRadius * Math.Cos(tipRad);
                double tipY = arcCy + ArcRadius * Math.Sin(tipRad);
                dc.DrawEllipse(arcBrush, null, new Point(tipX, tipY), 2.0, 2.0);
            }

            // Percentage text inside arc
            int pct = (int)Math.Round(pos * 100);
            var pctBrush = hasBrightLed
                ? Freeze(new SolidColorBrush(Color.FromArgb(0xAA, r1, g1, b1)))
                : DimBrush;
            var pctFt = MakeText($"{pct}", pctBrush, 7.5, ArcRadius * 1.8);
            dc.DrawText(pctFt, new Point(cx - pctFt.Width / 2.0, arcCy - pctFt.Height / 2.0));

            // ── LED color dots (3 per knob) ──────────────────────────────
            double dotsWidth = 2 * LedDotSpacing; // span of outer two dots
            double dot0X = cx - dotsWidth / 2.0;

            for (int l = 0; l < 3; l++)
            {
                var (r, g, b) = _ledColors[k, l];
                double dotX = dot0X + l * LedDotSpacing;
                bool lit = r > 8 || g > 8 || b > 8;

                Color dotColor = lit ? Color.FromRgb(r, g, b) : Color.FromRgb(0x22, 0x22, 0x22);
                var dotBrush = new SolidColorBrush(dotColor);
                dotBrush.Freeze();
                dc.DrawEllipse(dotBrush, null, new Point(dotX, ledRowY), LedDotRadius, LedDotRadius);

                // Glow ring when lit
                if (lit)
                {
                    byte glowAlpha = (byte)Math.Min(255, (r + g + b) / 3 * 60 / 128);
                    if (glowAlpha > 8)
                    {
                        var glowColor = Color.FromArgb(glowAlpha, r, g, b);
                        var glowBrush = new SolidColorBrush(glowColor);
                        glowBrush.Freeze();
                        dc.DrawEllipse(glowBrush, null, new Point(dotX, ledRowY), LedDotRadius + 2.5, LedDotRadius + 2.5);
                    }
                }
            }

            // ── VU level bar ──────────────────────────────────────────────
            double vuWidth = slotW - 16;
            double vuLeft = cx - vuWidth / 2.0;

            // Background track
            dc.DrawRectangle(VuBgBrush, null, new Rect(vuLeft, vuY, vuWidth, VuHeight));

            float vu = _vuSmoothed[k];
            if (vu > 0.005f)
            {
                double litWidth = vuWidth * vu;
                // Color: use knob LED color when lit, else accent green fallback
                Color vuColor = hasBrightLed && vu > 0.05f
                    ? Color.FromRgb(r1, g1, b1)
                    : Color.FromRgb(0x00, 0xDD, 0x77);
                var vuBrush = new SolidColorBrush(vuColor);
                vuBrush.Freeze();
                dc.DrawRectangle(vuBrush, null, new Rect(vuLeft, vuY, litWidth, VuHeight));
            }
        }

        #endregion

        #region Helpers

        private static StreamGeometry CreateArc(double cx, double cy, double radius, double startDeg, double sweepDeg)
        {
            double startRad = (startDeg - 90.0) * Math.PI / 180.0;
            double endRad = (startDeg - 90.0 + sweepDeg) * Math.PI / 180.0;

            double x0 = cx + radius * Math.Cos(startRad);
            double y0 = cy + radius * Math.Sin(startRad);
            double x1 = cx + radius * Math.Cos(endRad);
            double y1 = cy + radius * Math.Sin(endRad);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(x0, y0), false, false);
                ctx.ArcTo(new Point(x1, y1), new Size(radius, radius), 0, sweepDeg > 180.0, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            return geo;
        }

        private static double _dpi = 1.0;

        private static FormattedText MakeText(string text, Brush brush, double size, double maxWidth)
        {
            var ft = new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                size,
                brush,
                _dpi);
            ft.MaxTextWidth = Math.Max(maxWidth, 1);
            ft.Trimming = TextTrimming.CharacterEllipsis;
            return ft;
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _dpi = newDpi.PixelsPerDip;
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
        private static Pen FreezeP(Pen p) { p.Freeze(); return p; }

        #endregion
    }
}
