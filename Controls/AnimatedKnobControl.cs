using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AmpUp.Controls
{
    public class AnimatedKnobControl : FrameworkElement
    {
        // ── Constants ──────────────────────────────────────────────────
        // Clock convention: 0° = 12 o'clock, 90° = 3 o'clock, sweeps clockwise
        private const double StartAngleDeg = 225.0;  // 7:30 position
        private const double TotalSweepDeg = 270.0;  // sweeps CW to 4:30
        private const double DefaultSize = 100.0;
        private const double ArcStroke = 5.0;        // thicker value + track for a solid dial feel
        private const double ArcInset = 4.0;         // ring sits closer to the control edge
        private const double TipDotRadius = 5.0;     // bright endpoint marker
        private const double TipDotRingRadius = 7.0; // subtle accent ring around the dot
        private const double DirtyThreshold = 0.003;
        private const float LerpSpeed = 0.5f; // smoothing factor per tick (0=no move, 1=instant snap)
        private const double KnobImageRatio = 0.82; // close the gap between the image and the ring

        // ── Static frozen resources (color-independent) ────────────────
        private static readonly Pen s_trackPen;
        private static readonly Typeface s_typeface;
        private static readonly Brush s_textBrush;
        private static readonly BitmapImage s_knobImage;

        static AnimatedKnobControl()
        {
            // Track arc: #363636 — visible against the #1C card bg so the
            // unfilled sweep reads as a proper dial, not as empty space.
            var trackBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
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
                ThemeManager.Accent,
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

        private float _targetValue;
        private float _displayedValue;
        private bool _initialized;

        /// <summary>
        /// Set the target position. The arc will smoothly lerp toward it on each Tick().
        /// </summary>
        public void SetTarget(float target)
        {
            _targetValue = Math.Clamp(target, 0f, 1f);
            // Snap immediately on first set (startup) to avoid sweep-up from 0
            if (!_initialized)
            {
                _initialized = true;
                _displayedValue = _targetValue;
                Value = _displayedValue;
            }
        }

        /// <summary>
        /// Called by parent's 50ms timer. Lerps displayed value toward target for smooth animation.
        /// </summary>
        public void Tick()
        {
            float diff = _targetValue - _displayedValue;
            if (Math.Abs(diff) < 0.001f)
            {
                if (_displayedValue != _targetValue)
                {
                    _displayedValue = _targetValue;
                    Value = _displayedValue;
                }
                return;
            }
            _displayedValue += diff * LerpSpeed;
            Value = _displayedValue;
        }

        // ── Cached ArcColor-dependent resources ────────────────────────
        private Pen _valuePen = null!;
        private Brush _endDotBrush = null!;
        private Brush _endDotRingBrush = null!;
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

            // End dot core (full color) + outer ring (40% alpha) for a small depth halo
            _endDotBrush = valueBrush;
            var ringBrush = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B));
            ringBrush.Freeze();
            _endDotRingBrush = ringBrush;
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

                // 2. Value arc (clean, solid — no glow ring)
                var arcGeometry = CreateArcGeometry(cx, cy, radius, StartAngleDeg, valueSweep);
                dc.DrawGeometry(null, _valuePen, arcGeometry);

                // 3. Bright dot at the arc tip with a soft accent ring for depth
                double tipClockDeg = StartAngleDeg + valueSweep;
                double tipScreenRad = (tipClockDeg - 90.0) * Math.PI / 180.0;
                double tipX = cx + radius * Math.Cos(tipScreenRad);
                double tipY = cy + radius * Math.Sin(tipScreenRad);
                var tipPoint = new Point(tipX, tipY);
                dc.DrawEllipse(_endDotRingBrush, null, tipPoint, TipDotRingRadius, TipDotRingRadius);
                dc.DrawEllipse(_endDotBrush, null, tipPoint, TipDotRadius, TipDotRadius);
            }

            // 5. Knob image (rotated by value)
            // The knob-face.png needle points down (6 o'clock = 180° CW from 12).
            // WPF RotateTransform: positive = clockwise from current orientation.
            // At value=0: needle should be at 7:30 (225° CW from 12).
            //   Need to rotate 225° - 180° = 45° CW from image's resting position.
            // At value=1: needle at 4:30 (135° CW from 12).
            //   45° + 270° = 315° CW → 180° + 315° = 495° = 135° CW. ✓
            double rotationDeg = 45.0 + (value * 270.0);

            double knobSize = (radius * 2.0) * KnobImageRatio;
            double knobLeft = cx - knobSize / 2.0;
            double knobTop = cy - knobSize / 2.0;

            dc.PushTransform(new RotateTransform(rotationDeg, cx, cy));
            dc.DrawImage(s_knobImage, new Rect(knobLeft, knobTop, knobSize, knobSize));
            dc.Pop();

            // Percentage text removed — shown separately below the knob control
        }

        // ── Arc geometry helper ────────────────────────────────────────

        /// <summary>
        /// Creates an arc from a clock-convention start angle, sweeping clockwise on screen.
        /// startAngleDeg: 0=12 o'clock, 90=3 o'clock, 180=6 o'clock, 270=9 o'clock.
        /// </summary>
        private static StreamGeometry CreateArcGeometry(double cx, double cy, double radius, double startAngleDeg, double sweepDeg)
        {
            // Convert clock angles (0=top, CW) to screen radians (0=right, CW on screen)
            double startScreenRad = (startAngleDeg - 90.0) * Math.PI / 180.0;
            double endScreenRad = (startAngleDeg - 90.0 + sweepDeg) * Math.PI / 180.0;

            double x0 = cx + radius * Math.Cos(startScreenRad);
            double y0 = cy + radius * Math.Sin(startScreenRad);

            double x1 = cx + radius * Math.Cos(endScreenRad);
            double y1 = cy + radius * Math.Sin(endScreenRad);

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
                    SweepDirection.Clockwise,
                    true,
                    false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
