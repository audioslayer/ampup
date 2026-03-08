using System;
using System.Windows;
using System.Windows.Media;

namespace WolfMixer.Controls
{
    /// <summary>
    /// 16-segment vertical VU meter using DrawingVisual children.
    /// Parent drives animation by calling Tick() on a 50ms interval.
    /// </summary>
    public class VuMeterControl : FrameworkElement
    {
        private const int SegmentCount = 16;
        private const double DefaultWidth = 14;
        private const double DefaultHeight = 80;
        private const double SegmentGap = 1;

        // Pre-allocated frozen brushes
        private static readonly SolidColorBrush DefaultBarBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xB4, 0xD8)));
        private static readonly SolidColorBrush YellowBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00)));
        private static readonly SolidColorBrush RedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)));
        private static readonly SolidColorBrush UnlitBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)));
        private static readonly SolidColorBrush PeakBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));

        // Transitional brushes (blended between bar color and yellow) — rebuilt when BarColor changes
        private SolidColorBrush _trans1Brush;
        private SolidColorBrush _trans2Brush;

        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _drawingVisual;
        private Rect[] _segmentRects;

        private float _level;
        private float _peak;
        private int _peakHoldCount;
        private int _peakSegment = -1;

        private const int PeakHoldTicks = 30;   // 1.5s at 50ms
        private const int PeakDecayTicks = 8;   // ~400ms fade-out

        #region Dependency Properties

        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.Register(
                nameof(Level),
                typeof(float),
                typeof(VuMeterControl),
                new PropertyMetadata(0f, OnLevelChanged));

        public float Level
        {
            get => (float)GetValue(LevelProperty);
            set => SetValue(LevelProperty, value);
        }

        public static readonly DependencyProperty BarColorProperty =
            DependencyProperty.Register(
                nameof(BarColor),
                typeof(Color),
                typeof(VuMeterControl),
                new PropertyMetadata(Color.FromRgb(0x00, 0xB4, 0xD8), OnBarColorChanged));

        public Color BarColor
        {
            get => (Color)GetValue(BarColorProperty);
            set => SetValue(BarColorProperty, value);
        }

        #endregion

        public VuMeterControl()
        {
            _drawingVisual = new DrawingVisual();
            _visuals = new VisualCollection(this) { _drawingVisual };
            _segmentRects = Array.Empty<Rect>();
            BuildTransitionalBrushes(Color.FromRgb(0x00, 0xB4, 0xD8));
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

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(DefaultWidth, DefaultHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ComputeSegmentRects(finalSize);
            UpdateVisuals();
            return finalSize;
        }

        private void ComputeSegmentRects(Size size)
        {
            _segmentRects = new Rect[SegmentCount];
            double totalGaps = (SegmentCount - 1) * SegmentGap;
            double segHeight = (size.Height - totalGaps) / SegmentCount;
            double w = size.Width;

            for (int i = 0; i < SegmentCount; i++)
            {
                // Segment 0 = bottom, segment 15 = top
                // Y increases downward, so bottom segment has the largest Y
                double y = size.Height - (i + 1) * segHeight - i * SegmentGap;
                _segmentRects[i] = new Rect(0, y, w, segHeight);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Sets the current audio level and updates peak tracking.
        /// Called by the parent view when new audio data arrives.
        /// </summary>
        public void SetLevel(float value)
        {
            Level = Math.Clamp(value, 0f, 1f);
        }

        /// <summary>
        /// Called by the parent's 50ms DispatcherTimer for peak decay animation.
        /// </summary>
        public void Tick()
        {
            if (_peakSegment < 0)
                return;

            _peakHoldCount++;

            if (_peakHoldCount > PeakHoldTicks)
            {
                // Decay phase: drop peak by 1 segment per tick
                _peakSegment--;
                if (_peakSegment < 0)
                {
                    _peak = 0f;
                    _peakHoldCount = 0;
                }
                UpdateVisuals();
            }
        }

        #endregion

        #region Property change handlers

        private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var meter = (VuMeterControl)d;
            float newLevel = Math.Clamp((float)e.NewValue, 0f, 1f);
            meter._level = newLevel;
            meter.UpdatePeak(newLevel);
            meter.UpdateVisuals();
        }

        private static void OnBarColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var meter = (VuMeterControl)d;
            meter.BuildTransitionalBrushes((Color)e.NewValue);
            meter.UpdateVisuals();
        }

        #endregion

        #region Peak tracking

        private void UpdatePeak(float newLevel)
        {
            int litCount = (int)(newLevel * SegmentCount);
            int newPeakSeg = Math.Min(litCount, SegmentCount - 1);

            if (newPeakSeg >= _peakSegment)
            {
                _peakSegment = newPeakSeg;
                _peak = newLevel;
                _peakHoldCount = 0; // Reset hold timer
            }
        }

        #endregion

        #region Rendering

        private void UpdateVisuals()
        {
            if (_segmentRects.Length == 0)
                return;

            int litCount = (int)(_level * SegmentCount);
            litCount = Math.Clamp(litCount, 0, SegmentCount);

            using (DrawingContext dc = _drawingVisual.RenderOpen())
            {
                for (int i = 0; i < SegmentCount; i++)
                {
                    Brush brush;

                    if (i < litCount)
                    {
                        brush = GetSegmentBrush(i);
                    }
                    else if (i == _peakSegment && _peakSegment >= 0)
                    {
                        brush = PeakBrush;
                    }
                    else
                    {
                        brush = UnlitBrush;
                    }

                    dc.DrawRectangle(brush, null, _segmentRects[i]);
                }
            }
        }

        private Brush GetSegmentBrush(int index)
        {
            if (index <= 9)
                return GetCurrentBarBrush();
            if (index == 10)
                return _trans1Brush;
            if (index == 11)
                return _trans2Brush;
            if (index <= 14)
                return YellowBrush;
            return RedBrush; // index 15
        }

        private Brush GetCurrentBarBrush()
        {
            Color c = BarColor;
            if (c == Color.FromRgb(0x00, 0xB4, 0xD8))
                return DefaultBarBrush;

            // Custom bar color — create a frozen brush
            // This is called frequently, so cache if needed in the future
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        #endregion

        #region Helpers

        private void BuildTransitionalBrushes(Color barColor)
        {
            Color yellow = Color.FromRgb(0xFF, 0xB8, 0x00);

            // Segment 10: 1/3 toward yellow
            _trans1Brush = Freeze(new SolidColorBrush(LerpColor(barColor, yellow, 0.33f)));
            // Segment 11: 2/3 toward yellow
            _trans2Brush = Freeze(new SolidColorBrush(LerpColor(barColor, yellow, 0.67f)));
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        #endregion
    }
}
