using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    /// <summary>
    /// Ambient radial glow behind each mixer channel knob.
    /// Tinted with the knob's LED color, pulses with audio level.
    /// Parent drives animation by calling Tick() on a 50ms interval.
    /// </summary>
    public class ChannelGlowControl : FrameworkElement
    {
        private const float BaseOpacity = 0.06f;   // idle glow
        private const float PeakOpacity = 0.22f;   // max audio glow
        private const float AttackRate = 0.4f;      // fast rise
        private const float DecayRate = 0.92f;      // slow fade

        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _drawingVisual;

        private float _targetLevel;
        private float _smoothedLevel;
        private Color _glowColor = ThemeManager.Accent;
        private byte _lastAlpha;
        private Color _lastColor;

        public ChannelGlowControl()
        {
            _drawingVisual = new DrawingVisual();
            _visuals = new VisualCollection(this) { _drawingVisual };
            IsHitTestVisible = false; // don't block mouse events to controls above
        }

        #region Layout

        protected override Size MeasureOverride(Size availableSize)
        {
            // Take whatever space the parent gives us
            return new Size(
                double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 100 : availableSize.Height);
        }

        #endregion

        #region Visual tree plumbing

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _visuals.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _visuals[index];
        }

        #endregion

        #region Public API

        public Color GlowColor
        {
            get => _glowColor;
            set
            {
                if (_glowColor != value)
                {
                    _glowColor = value;
                    Render();
                }
            }
        }

        public void SetLevel(float level)
        {
            _targetLevel = Math.Clamp(level, 0f, 1f);
        }

        /// <summary>
        /// Called by parent's 50ms timer. Smooths level and re-renders.
        /// </summary>
        public void Tick()
        {
            // Smooth: fast attack, slow decay
            if (_targetLevel > _smoothedLevel)
                _smoothedLevel = _smoothedLevel + (_targetLevel - _smoothedLevel) * AttackRate;
            else
                _smoothedLevel *= DecayRate;

            if (_smoothedLevel < 0.005f)
                _smoothedLevel = 0f;

            Render();
        }

        #endregion

        #region Rendering

        private void Render()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            float opacity = BaseOpacity + _smoothedLevel * (PeakOpacity - BaseOpacity);
            byte alpha = (byte)(opacity * 255);

            // Skip re-render if nothing changed
            if (alpha == _lastAlpha && _glowColor == _lastColor) return;
            _lastAlpha = alpha;
            _lastColor = _glowColor;

            using (DrawingContext dc = _drawingVisual.RenderOpen())
            {
                var gradient = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.45),
                    GradientOrigin = new Point(0.5, 0.4),
                    RadiusX = 0.7,
                    RadiusY = 0.65,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(alpha, _glowColor.R, _glowColor.G, _glowColor.B), 0.0),
                        new GradientStop(Color.FromArgb((byte)(alpha * 0.4), _glowColor.R, _glowColor.G, _glowColor.B), 0.5),
                        new GradientStop(Color.FromArgb(0, _glowColor.R, _glowColor.G, _glowColor.B), 1.0),
                    }
                };
                gradient.Freeze();

                dc.DrawRectangle(gradient, null, new Rect(0, 0, w, h));
            }
        }

        #endregion
    }
}
