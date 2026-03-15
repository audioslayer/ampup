using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
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

        // Pre-allocated frozen brushes — standard green/orange/red VU meter
        private static readonly SolidColorBrush GreenBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xDD, 0x44)));
        private static readonly SolidColorBrush OrangeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)));
        private static readonly SolidColorBrush RedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x33)));
        private static readonly SolidColorBrush UnlitBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)));
        private static readonly SolidColorBrush PeakBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));

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
                new PropertyMetadata(ThemeManager.Accent));

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
            // 0-9: green, 10-12: orange, 13-15: red
            if (index <= 9) return GreenBrush;
            if (index <= 12) return OrangeBrush;
            return RedBrush;
        }

        #endregion

        #region Helpers

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        #endregion
    }
}
