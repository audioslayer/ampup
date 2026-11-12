using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    /// <summary>
    /// WolfEQ-style live spectrum drawn behind a mixer knob card: log-spaced,
    /// bottom-anchored soft bars (body gradient + bright cap + faint glow) tinted
    /// with the card's LED color.
    ///
    /// Honest limitation: Windows loopback capture only exposes the system-wide
    /// mix, so every card shows the same system spectrum SHAPE, scaled by that
    /// knob's own live peak level (app/device meter). It is not a true per-app
    /// spectrum. When the target is silent the bars settle to a faint baseline.
    ///
    /// The parent owns the timer: call <see cref="SetSpectrum"/> + <see cref="Tick"/>
    /// (~30 fps) and <see cref="SetLevel"/> whenever the knob's peak is sampled.
    /// </summary>
    public class SpectrumBackdropControl : FrameworkElement
    {
        private const int MaxBars = 40;
        private const double BarPitch = 7.0;      // px per bar slot
        private const double MaxHeightFraction = 0.62;
        private const double BaselineHeight = 2.0;
        private const float AttackRate = 0.55f;
        private const float DecayRate = 0.86f;     // per tick, smooth WolfEQ-like fall
        private const float LevelAttack = 0.5f;
        private const float LevelDecay = 0.9f;

        private readonly VisualCollection _visuals;
        private readonly DrawingVisual _drawingVisual;

        private float[] _bars = Array.Empty<float>();
        private float[]? _spectrum;
        private float _targetLevel;
        private float _level;
        private bool _idleRendered;

        private Color _barColor = ThemeManager.Accent;
        private Brush? _bodyBrush, _capBrush, _glowBrush, _baselineBrush;

        public SpectrumBackdropControl()
        {
            _drawingVisual = new DrawingVisual();
            _visuals = new VisualCollection(this) { _drawingVisual };
            IsHitTestVisible = false; // never block the knob/pickers above
            SizeChanged += (_, _) => { _idleRendered = false; Render(); };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 100 : availableSize.Height);
        }

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _visuals.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _visuals[index];
        }

        public Color BarColor
        {
            get => _barColor;
            set
            {
                if (_barColor == value) return;
                _barColor = value;
                _bodyBrush = null;
                _idleRendered = false;
                Render();
            }
        }

        /// <summary>This knob's live level, 0..1 (same value the old VU bar showed).</summary>
        public void SetLevel(float level) => _targetLevel = Math.Clamp(level, 0f, 1f);

        /// <summary>Shared system spectrum frame (0..1 per band); may be null when capture is off.</summary>
        public void SetSpectrum(float[]? spectrum) => _spectrum = spectrum;

        /// <summary>Drops all motion back to the idle baseline (page hidden / reduce effects).</summary>
        public void Reset()
        {
            _targetLevel = 0f;
            _level = 0f;
            Array.Clear(_bars);
            _idleRendered = false;
            Render();
        }

        public void Tick()
        {
            if (_targetLevel > _level) _level += (_targetLevel - _level) * LevelAttack;
            else _level *= LevelDecay;
            if (_level < 0.003f) _level = 0f;

            int count = BarCount();
            if (_bars.Length != count) _bars = new float[count];

            bool active = false;
            var spectrum = _spectrum;
            for (int i = 0; i < count; i++)
            {
                float target = 0f;
                if (spectrum is { Length: > 0 } && _level > 0f)
                {
                    int first = i * spectrum.Length / count;
                    int last = Math.Max(first + 1, (i + 1) * spectrum.Length / count);
                    for (int j = first; j < last && j < spectrum.Length; j++)
                        target = Math.Max(target, spectrum[j]);
                    // Gentle lift so a moderately loud app still fills the card.
                    target = Math.Clamp(target * (0.35f + 0.9f * _level), 0f, 1f);
                }
                float cur = _bars[i];
                cur = target > cur ? cur + (target - cur) * AttackRate : cur * DecayRate;
                if (cur < 0.004f) cur = 0f;
                _bars[i] = cur;
                if (cur > 0f) active = true;
            }

            // Silent: render the flat baseline once, then skip re-recording entirely.
            if (!active)
            {
                if (_idleRendered) return;
                _idleRendered = true;
            }
            else
            {
                _idleRendered = false;
            }
            Render();
        }

        private int BarCount()
        {
            double w = ActualWidth;
            if (w <= 0) return 24;
            return Math.Clamp((int)(w / BarPitch), 12, MaxBars);
        }

        private void EnsureBrushes()
        {
            if (_bodyBrush != null) return;
            var c = _barColor;
            var body = new LinearGradientBrush(Color.FromArgb(70, c.R, c.G, c.B),
                Color.FromArgb(6, c.R, c.G, c.B), 90);
            var cap = new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B));
            var glow = new SolidColorBrush(Color.FromArgb(22, c.R, c.G, c.B));
            var baseline = new SolidColorBrush(Color.FromArgb(28, c.R, c.G, c.B));
            body.Freeze(); cap.Freeze(); glow.Freeze(); baseline.Freeze();
            _bodyBrush = body; _capBrush = cap; _glowBrush = glow; _baselineBrush = baseline;
        }

        private void Render()
        {
            double w = ActualWidth, h = ActualHeight;
            using var dc = _drawingVisual.RenderOpen();
            if (w <= 0 || h <= 0) return;
            EnsureBrushes();

            int count = _bars.Length > 0 ? _bars.Length : BarCount();
            double pitch = w / count;
            double width = Math.Max(1, pitch - Math.Clamp(pitch * 0.3, 1.5, 4));
            double maxH = h * MaxHeightFraction;
            for (int i = 0; i < count; i++)
            {
                float level = i < _bars.Length ? _bars[i] : 0f;
                double x = i * pitch + (pitch - width) / 2;
                double barH = level * maxH;
                if (barH < BaselineHeight)
                {
                    dc.DrawRectangle(_baselineBrush, null, new Rect(x, h - BaselineHeight, width, BaselineHeight));
                    continue;
                }
                var rect = new Rect(x, h - barH, width, barH);
                dc.DrawRoundedRectangle(_bodyBrush, null, rect, 2, 2);
                dc.DrawRoundedRectangle(_glowBrush, null, new Rect(rect.Left - 1, rect.Top - 2, width + 2, 6), 3, 3);
                dc.DrawRoundedRectangle(_capBrush, null, new Rect(rect.Left, rect.Top, width, Math.Min(2, barH)), 1, 1);
            }
        }
    }
}
