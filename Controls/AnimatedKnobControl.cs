using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WolfMixer.Controls
{
    public class AnimatedKnobControl : FrameworkElement
    {
        // ── Constants ──────────────────────────────────────────────────
        private const double StartAngleDeg = 225.0;
        private const double TotalSweepDeg = 270.0;
        private const double DefaultSize = 100.0;
        private const double ArcStroke = 5.0;
        private const double GlowStroke = 10.0;
        private const double OuterRingStroke = 2.0;
        private const double NeedleDotRadius = 4.0;
        private const double NeedleGlowRadius = 7.0;
        private const double CenterCircleRatio = 0.52;
        private const double ArcInset = 10.0;
        private const double DirtyThreshold = 0.001;

        // ── Static frozen resources (color-independent) ────────────────
        private static readonly Pen s_trackPen;
        private static readonly Pen s_outerRingPen;
        private static readonly Brush s_centerFill;
        private static readonly Pen s_centerBorderPen;
        private static readonly Brush s_textBrush;
        private static readonly Typeface s_typeface;

        static AnimatedKnobControl()
        {
            // Track arc: #363636, 5px round caps
            var trackBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
            trackBrush.Freeze();
            s_trackPen = new Pen(trackBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            s_trackPen.Freeze();

            // Outer ring: #2A2A2A, 2px
            var outerRingBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            outerRingBrush.Freeze();
            s_outerRingPen = new Pen(outerRingBrush, OuterRingStroke);
            s_outerRingPen.Freeze();

            // Center circle fill: #1A1A1A
            s_centerFill = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            s_centerFill.Freeze();

            // Center circle border: #2A2A2A
            var centerBorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            centerBorderBrush.Freeze();
            s_centerBorderPen = new Pen(centerBorderBrush, 1.5);
            s_centerBorderPen.Freeze();

            // Text: #E8E8E8
            s_textBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            s_textBrush.Freeze();

            // Segoe UI Bold
            s_typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
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
        private Pen _valuePen;
        private Pen _glowPen;
        private Brush _needleFill;
        private Brush _needleGlowBrush;
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

            // Value arc pen: ArcColor, 5px, round caps
            var valueBrush = new SolidColorBrush(color);
            valueBrush.Freeze();
            _valuePen = new Pen(valueBrush, ArcStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _valuePen.Freeze();

            // Glow pen: ArcColor at 35% opacity, 10px
            var glowColor = Color.FromArgb((byte)(255 * 0.35), color.R, color.G, color.B);
            var glowBrush = new SolidColorBrush(glowColor);
            glowBrush.Freeze();
            _glowPen = new Pen(glowBrush, GlowStroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _glowPen.Freeze();

            // Needle dot fill: ArcColor
            _needleFill = valueBrush; // already frozen

            // Needle glow: ArcColor at 50% opacity
            var needleGlowColor = Color.FromArgb(128, color.R, color.G, color.B);
            _needleGlowBrush = new SolidColorBrush(needleGlowColor);
            _needleGlowBrush.Freeze();
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

            // 1. Outer ring
            dc.DrawEllipse(null, s_outerRingPen, new Point(cx, cy), radius + 4, radius + 4);

            // 2. Track arc (full sweep)
            var trackGeometry = CreateArcGeometry(cx, cy, radius, StartAngleDeg, TotalSweepDeg);
            dc.DrawGeometry(null, s_trackPen, trackGeometry);

            if (value > DirtyThreshold)
            {
                double valueSweep = value * TotalSweepDeg;

                // 3. Glow arc (behind value arc)
                var glowGeometry = CreateArcGeometry(cx, cy, radius, StartAngleDeg, valueSweep);
                dc.DrawGeometry(null, _glowPen, glowGeometry);

                // 4. Value arc
                dc.DrawGeometry(null, _valuePen, glowGeometry);
            }

            // 5. Needle dot at arc tip
            double tipAngleDeg = StartAngleDeg + value * TotalSweepDeg;
            double tipAngleRad = tipAngleDeg * Math.PI / 180.0;
            double tipX = cx + radius * Math.Cos(tipAngleRad);
            double tipY = cy - radius * Math.Sin(tipAngleRad);
            var tipPoint = new Point(tipX, tipY);

            // Needle glow (14px diameter = 7px radius)
            dc.DrawEllipse(_needleGlowBrush, null, tipPoint, NeedleGlowRadius, NeedleGlowRadius);
            // Needle fill (8px diameter = 4px radius)
            dc.DrawEllipse(_needleFill, null, tipPoint, NeedleDotRadius, NeedleDotRadius);

            // 6. Center circle
            double centerRadius = radius * CenterCircleRatio;
            dc.DrawEllipse(s_centerFill, s_centerBorderPen, new Point(cx, cy), centerRadius, centerRadius);

            // 7. Center percentage text
            string text = PercentText ?? "0%";
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                s_typeface,
                12.0, // 9pt ≈ 12 device-independent pixels
                s_textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formattedText, new Point(cx - formattedText.Width / 2.0, cy - formattedText.Height / 2.0));
        }

        // ── Arc geometry helper ────────────────────────────────────────

        /// <summary>
        /// Creates a StreamGeometry arc from <paramref name="startAngleDeg"/> sweeping
        /// <paramref name="sweepDeg"/> degrees clockwise. Angles measured from positive
        /// X-axis, counter-clockwise in math convention but we negate Y for screen coords
        /// (Y increases downward in WPF). StartAngle 225° places the start at lower-left.
        /// </summary>
        private static StreamGeometry CreateArcGeometry(double cx, double cy, double radius, double startAngleDeg, double sweepDeg)
        {
            // Convert angles: our convention has 0° at right, increasing counter-clockwise
            // in math terms. For WPF screen coords (Y down), we negate the Y component.
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;

            // Start point
            double x0 = cx + radius * Math.Cos(startRad);
            double y0 = cy - radius * Math.Sin(startRad);

            // End point
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
