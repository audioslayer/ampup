using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WolfMixer.Controls
{
    public class AnimatedKnobControl : FrameworkElement
    {
        // ── Constants ──────────────────────────────────────────────────
        private const double StartAngleDeg = 225.0;
        private const double TotalSweepDeg = 270.0;
        private const double DefaultSize = 100.0;
        private const double ArcStroke = 4.0;
        private const double GlowStroke = 8.0;
        private const double ArcInset = 6.0;
        private const double DirtyThreshold = 0.001;
        private const double KnobImageRatio = 0.72; // knob image fills 72% of control size

        // ── Static frozen resources (color-independent) ────────────────
        private static readonly Pen s_trackPen;
        private static readonly Typeface s_typeface;
        private static readonly Brush s_textBrush;
        private static readonly BitmapImage s_knobImage;

        static AnimatedKnobControl()
        {
            // Track arc: #2A2A2A, round caps
            var trackBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            trackBrush.Freeze();
            s_trackPen = new Pen(trackBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            s_trackPen.Freeze();

            // Text: #E8E8E8
            s_textBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            s_textBrush.Freeze();

            // Segoe UI Bold for percentage label
            s_typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

            // Load knob face image from embedded resource
            s_knobImage = new BitmapImage(new Uri("pack://application:,,,/Assets/knob-face.png", UriKind.Absolute));
            s_knobImage.Freeze();
        }

        // ── Dependency Properties ──────────────────────────────────────

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(float),
            typeof(AnimatedKnobControl),
            new FrameworkPropertyMetadata(
                0f,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnValueChanged,
                CoerceValue));

        public float Value
        {
            get => (float)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var v = (float)baseValue;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return baseValue;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (AnimatedKnobControl)d;
            var oldVal = (float)e.OldValue;
            var newVal = (float)e.NewValue;
            if (Math.Abs(newVal - oldVal) < DirtyThreshold)
                return;
            ctrl.InvalidateVisual();
        }

        public static readonly DependencyProperty ArcColorProperty = DependencyProperty.Register(
            nameof(ArcColor),
            typeof(Color),
            typeof(AnimatedKnobControl),
            new FrameworkPropertyMetadata(
                Color.FromRgb(0x00, 0xB4, 0xD8),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnArcColorChanged));

        public Color ArcColor
        {
            get => (Color)GetValue(ArcColorProperty);
            set => SetValue(ArcColorProperty, value);
        }

        private static void OnArcColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (AnimatedKnobControl)d;
            ctrl.RebuildArcResources((Color)e.NewValue);
            ctrl.InvalidateVisual();
        }

        public static readonly DependencyProperty PercentTextProperty = DependencyProperty.Register(
            nameof(PercentText),
            typeof(string),
            typeof(AnimatedKnobControl),
            new FrameworkPropertyMetadata(
                "0%",
                FrameworkPropertyMetadataOptions.AffectsRender));

        public string PercentText
        {
            get => (string)GetValue(PercentTextProperty);
            set => SetValue(PercentTextProperty, value);
        }

        // ── Cached ArcColor-dependent resources ────────────────────────
        private Pen _valuePen = null!;
        private Pen _glowPen = null!;
        private Brush _endDotBrush = null!;
        private Color _cachedArcColor;

        public AnimatedKnobControl()
        {
            RebuildArcResources(ArcColor);
        }

        private void RebuildArcResources(Color color)
        {
            if (color == _cachedArcColor && _valuePen != null)
                return;

            _cachedArcColor = color;

            // Value arc pen
            var valueBrush = new SolidColorBrush(color);
            valueBrush.Freeze();
            _valuePen = new Pen(valueBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _valuePen.Freeze();

            // Glow pen: ArcColor at 30% opacity
            var glowColor = Color.FromArgb((byte)(255 * 0.30), color.R, color.G, color.B);
            var glowBrush = new SolidColorBrush(glowColor);
            glowBrush.Freeze();
            _glowPen = new Pen(glowBrush, GlowStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _glowPen.Freeze();

            // End dot brush
            _endDotBrush = valueBrush; // already frozen
        }

        // ── Layout ─────────────────────────────────────────────────────

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(DefaultSize, DefaultSize);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return new Size(DefaultSize, DefaultSize);
        }

        // ── Rendering ──────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth > 0 ? ActualWidth : DefaultSize;
            double h = ActualHeight > 0 ? ActualHeight : DefaultSize;
            double cx = w / 2.0;
            double cy = h / 2.0;
            double radius = Math.Min(w, h) / 2.0 - ArcInset;
            float value = Value;

            // 1. Track arc (full sweep background)
            var trackGeometry = CreateArcGeometry(cx, cy, radius, StartAngleDeg, TotalSweepDeg);
            dc.DrawGeometry(null, s_trackPen, trackGeometry);

            if (value > DirtyThreshold)
            {
                double valueSweep = value * TotalSweepDeg;

                // 2. Glow arc (behind value arc)
                var arcGeometry = CreateArcGeometry(cx, cy, radius, StartAngleDeg, valueSweep);
                dc.DrawGeometry(null, _glowPen, arcGeometry);

                // 3. Value arc
                dc.DrawGeometry(null, _valuePen, arcGeometry);

                // 4. Small bright dot at the arc tip
                double tipAngleDeg = StartAngleDeg + valueSweep;
                double tipAngleRad = tipAngleDeg * Math.PI / 180.0;
                double tipX = cx + radius * Math.Cos(tipAngleRad);
                double tipY = cy - radius * Math.Sin(tipAngleRad);
                dc.DrawEllipse(_endDotBrush, null, new Point(tipX, tipY), 3.0, 3.0);
            }

            // 5. Knob image (rotated by value)
            // The knob-face.png needle points up (12 o'clock / 0° in WPF rotation).
            // Real hardware: needle points down, sweep is 270° from lower-left to lower-right.
            // WPF RotateTransform is clockwise from 12 o'clock.
            // At value=0: needle at lower-left (7:30 position) = 225° CW from 12 o'clock,
            //   but image is already flipped (up), so add 180°: base = 225° - 180° = 45°
            //   Actually simpler: we want the indicator line at 7:30 (225° CW).
            //   Image line is at 0° (up). Rotate 225° CW → line at 7:30. ✓
            //   At value=1: 225 + 270 = 495 → 135° CW = 4:30 position. ✓
            double rotationDeg = 225.0 + (value * 270.0);

            double knobSize = (radius * 2.0) * KnobImageRatio;
            double knobLeft = cx - knobSize / 2.0;
            double knobTop = cy - knobSize / 2.0;

            dc.PushTransform(new RotateTransform(rotationDeg, cx, cy));
            dc.DrawImage(s_knobImage, new Rect(knobLeft, knobTop, knobSize, knobSize));
            dc.Pop();

            // 6. Percentage text below the knob
            string text = PercentText ?? "0%";
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                s_typeface,
                11.0,
                s_textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            double textY = cy + knobSize / 2.0 + 2.0;
            dc.DrawText(formattedText, new Point(cx - formattedText.Width / 2.0, textY));
        }

        // ── Arc geometry helper ────────────────────────────────────────

        private static StreamGeometry CreateArcGeometry(double cx, double cy, double radius, double startAngleDeg, double sweepDeg)
        {
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;

            double x0 = cx + radius * Math.Cos(startRad);
            double y0 = cy - radius * Math.Sin(startRad);

            double x1 = cx + radius * Math.Cos(endRad);
            double y1 = cy - radius * Math.Sin(endRad);

            bool isLargeArc = sweepDeg > 180.0;

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(x0, y0), false, false);
                ctx.ArcTo(
                    new Point(x1, y1),
                    new Size(radius, radius),
                    0,
                    isLargeArc,
                    SweepDirection.Counterclockwise,
                    true,
                    false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
