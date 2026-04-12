using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AmpUp.Controls
{
    /// <summary>
    /// Tiny animated visualization of a LightEffect. One shared 30 FPS timer drives
    /// every visible instance — hidden tiles unregister to avoid wasted frames.
    /// </summary>
    public partial class EffectPreviewControl : FrameworkElement
    {
        // ── Dependency properties ──────────────────────────────────────────
        // Named EffectKind (not Effect) to avoid hiding UIElement.Effect (bitmap effect).
        public static readonly DependencyProperty EffectKindProperty = DependencyProperty.Register(
            nameof(EffectKind), typeof(LightEffect), typeof(EffectPreviewControl),
            new FrameworkPropertyMetadata(LightEffect.SingleColor,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((EffectPreviewControl)d).Invalidate()));

        public LightEffect EffectKind
        {
            get => (LightEffect)GetValue(EffectKindProperty);
            set => SetValue(EffectKindProperty, value);
        }

        public static readonly DependencyProperty TileColorProperty = DependencyProperty.Register(
            nameof(TileColor), typeof(Color), typeof(EffectPreviewControl),
            new FrameworkPropertyMetadata(Colors.White,
                FrameworkPropertyMetadataOptions.AffectsRender,
                (d, _) => ((EffectPreviewControl)d).Invalidate()));

        public Color TileColor
        {
            get => (Color)GetValue(TileColorProperty);
            set => SetValue(TileColorProperty, value);
        }

        public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
            nameof(AccentColor), typeof(Color), typeof(EffectPreviewControl),
            new FrameworkPropertyMetadata(Color.FromRgb(0xE0, 0x6C, 0x9F),
                FrameworkPropertyMetadataOptions.AffectsRender));

        // Secondary / companion color (e.g. Blend color2). Defaults to a soft pink so
        // gradient effects still look intentional if the caller doesn't set it.
        public Color AccentColor
        {
            get => (Color)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }

        // ── Shared timer ────────────────────────────────────────────────────
        private static readonly HashSet<EffectPreviewControl> s_active = new();
        private static DispatcherTimer? s_timer;
        private static int s_frame;

        /// <summary>Monotonic frame counter — preview renders use this as their time source.</summary>
        protected static int Frame => s_frame;

        /// <summary>Seconds since app start (derived from frame counter at 30 FPS).</summary>
        protected static double Time => s_frame / 30.0;

        // ── Visual backing ──────────────────────────────────────────────────
        private readonly DrawingVisual _visual = new();

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;

        public EffectPreviewControl()
        {
            AddVisualChild(_visual);
            SnapsToDevicePixels = true;
            IsVisibleChanged += OnVisibilityChanged;
            Loaded += (_, _) => { if (IsVisible) Register(); Invalidate(); };
            Unloaded += (_, _) => Unregister();
        }

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) Register();
            else Unregister();
        }

        private void Register()
        {
            s_active.Add(this);
            if (s_timer == null)
            {
                s_timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(33)
                };
                s_timer.Tick += OnTick;
                s_timer.Start();
            }
        }

        private void Unregister()
        {
            s_active.Remove(this);
            if (s_active.Count == 0 && s_timer != null)
            {
                s_timer.Stop();
                s_timer = null;
            }
        }

        private static void OnTick(object? sender, EventArgs e)
        {
            s_frame++;
            // Copy to array — Invalidate() may trigger layout that mutates the set.
            var arr = new EffectPreviewControl[s_active.Count];
            s_active.CopyTo(arr);
            foreach (var c in arr)
            {
                if (!c.IsVisible) continue;
                c.Invalidate();
            }
        }

        private void Invalidate()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0)
            {
                // Use measured/explicit size as fallback before layout runs
                w = Width > 0 ? Width : 72;
                h = Height > 0 ? Height : 32;
            }

            using var dc = _visual.RenderOpen();
            var ctx = new Ctx
            {
                Dc = dc,
                W = w,
                H = h,
                Frame = s_frame,
                T = s_frame / 30.0,
                Color = TileColor,
                Color2 = AccentColor,
            };

            // Clip to bounds so renders don't bleed into padding/label
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

            try
            {
                Dispatch(ctx);
            }
            catch
            {
                // Never let a preview crash the whole picker — fall back to a dot.
                RenderFallback(ctx);
            }

            dc.Pop();
        }

        /// <summary>Shared drawing context for all render methods.</summary>
        protected struct Ctx
        {
            public DrawingContext Dc;
            public double W, H;
            public int Frame;
            public double T;
            public Color Color;
            public Color Color2;

            public readonly double Cx => W * 0.5;
            public readonly double Cy => H * 0.5;
        }

        // ── Dispatch ────────────────────────────────────────────────────────
        private void Dispatch(Ctx c)
        {
            switch (EffectKind)
            {
                // STATIC
                case LightEffect.SingleColor:     RenderSingleColor(c);     break;
                case LightEffect.ColorBlend:      RenderColorBlend(c);      break;
                case LightEffect.PositionFill:    RenderPositionFill(c);    break;
                case LightEffect.PositionBlend:   RenderPositionBlend(c);   break;
                case LightEffect.PositionBlendMute: RenderPositionBlendMute(c); break;
                case LightEffect.CycleFill:       RenderCycleFill(c);       break;
                case LightEffect.RainbowFill:     RenderRainbowFill(c);     break;
                case LightEffect.GradientFill:    RenderGradientFill(c);    break;

                // ANIMATED
                case LightEffect.Blink:           RenderBlink(c);           break;
                case LightEffect.Pulse:           RenderPulse(c);           break;
                case LightEffect.Breathing:       RenderBreathing(c);       break;
                case LightEffect.Fire:            RenderFire(c);            break;
                case LightEffect.Comet:           RenderComet(c);           break;
                case LightEffect.Sparkle:         RenderSparkle(c);         break;
                case LightEffect.PingPong:        RenderPingPong(c);        break;
                case LightEffect.Stack:           RenderStack(c);           break;
                case LightEffect.Wave:            RenderWave(c);            break;
                case LightEffect.Candle:          RenderCandle(c);          break;
                case LightEffect.RainbowWave:     RenderRainbowWave(c);     break;
                case LightEffect.RainbowCycle:    RenderRainbowCycle(c);    break;
                case LightEffect.Wheel:           RenderWheel(c);           break;
                case LightEffect.RainbowWheel:    RenderRainbowWheel(c);    break;
                case LightEffect.Heartbeat:       RenderHeartbeat(c);       break;
                case LightEffect.Plasma:          RenderPlasma(c);          break;
                case LightEffect.Drip:            RenderDrip(c);            break;

                // REACTIVE
                case LightEffect.MicStatus:       RenderMicStatus(c);       break;
                case LightEffect.DeviceMute:      RenderDeviceMute(c);      break;
                case LightEffect.AudioReactive:   RenderAudioReactive(c);   break;
                case LightEffect.AudioPositionBlend: RenderAudioPositionBlend(c); break;
                case LightEffect.ProgramMute:     RenderProgramMute(c);     break;
                case LightEffect.AppGroupMute:    RenderAppGroupMute(c);    break;
                case LightEffect.DeviceSelect:    RenderDeviceSelect(c);    break;

                // GLOBAL SPAN — group A
                case LightEffect.Scanner:         RenderScanner(c);         break;
                case LightEffect.MeteorRain:      RenderMeteorRain(c);      break;
                case LightEffect.ColorWave:       RenderColorWave(c);       break;
                case LightEffect.Segments:        RenderSegments(c);        break;
                case LightEffect.TheaterChase:    RenderTheaterChase(c);    break;
                case LightEffect.RainbowScanner:  RenderRainbowScanner(c);  break;
                case LightEffect.SparkleRain:     RenderSparkleRain(c);     break;
                case LightEffect.BreathingSync:   RenderBreathingSync(c);   break;
                case LightEffect.FireWall:        RenderFireWall(c);        break;
                case LightEffect.DualRacer:       RenderDualRacer(c);       break;
                case LightEffect.Lightning:       RenderLightning(c);       break;
                case LightEffect.Fillup:          RenderFillup(c);          break;
                case LightEffect.Ocean:           RenderOcean(c);           break;

                // GLOBAL SPAN — group B
                case LightEffect.Collision:       RenderCollision(c);       break;
                case LightEffect.DNA:             RenderDNA(c);             break;
                case LightEffect.Rainfall:        RenderRainfall(c);        break;
                case LightEffect.PoliceLights:    RenderPoliceLights(c);    break;
                case LightEffect.Aurora:          RenderAurora(c);          break;
                case LightEffect.Matrix:          RenderMatrix(c);          break;
                case LightEffect.Starfield:       RenderStarfield(c);       break;
                case LightEffect.Equalizer:       RenderEqualizer(c);       break;
                case LightEffect.Waterfall:       RenderWaterfall(c);       break;
                case LightEffect.Lava:            RenderLava(c);            break;
                case LightEffect.VuWave:          RenderVuWave(c);          break;
                case LightEffect.NebulaDrift:     RenderNebulaDrift(c);     break;

                default: RenderFallback(c); break;
            }
        }

        // ── Shared helpers (usable from any partial file) ───────────────────

        /// <summary>Fresh SolidColorBrush with the given alpha multiplier.</summary>
        protected static SolidColorBrush Brush(Color c, double alpha = 1.0)
        {
            byte a = (byte)Math.Clamp(alpha * 255.0, 0, 255);
            return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }

        /// <summary>Brush with alpha fixed as a byte.</summary>
        protected static SolidColorBrush BrushA(Color c, byte a)
            => new(Color.FromArgb(a, c.R, c.G, c.B));

        protected static Pen Pen(Color c, double thickness, double alpha = 1.0)
        {
            byte a = (byte)Math.Clamp(alpha * 255.0, 0, 255);
            var p = new Pen(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            return p;
        }

        /// <summary>Linearly scale a color's brightness (0..1+).</summary>
        protected static Color Scale(Color c, double k)
        {
            k = Math.Clamp(k, 0.0, 1.5);
            return Color.FromRgb(
                (byte)Math.Clamp(c.R * k, 0, 255),
                (byte)Math.Clamp(c.G * k, 0, 255),
                (byte)Math.Clamp(c.B * k, 0, 255));
        }

        /// <summary>Lerp two colors (t = 0..1).</summary>
        protected static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>HSV (0..1 each) to RGB. Saturation/value default to full.</summary>
        protected static Color Hsv(double h, double s = 1.0, double v = 1.0)
        {
            h = ((h % 1.0) + 1.0) % 1.0;
            double hh = h * 6.0;
            int i = (int)hh;
            double f = hh - i;
            double p = v * (1 - s);
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));
            double r, g, b;
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromRgb(
                (byte)Math.Clamp(r * 255, 0, 255),
                (byte)Math.Clamp(g * 255, 0, 255),
                (byte)Math.Clamp(b * 255, 0, 255));
        }

        /// <summary>Deterministic pseudo-random in [0,1) from an integer seed.</summary>
        protected static double Rand(int seed)
        {
            uint x = (uint)seed * 2654435761u;
            x ^= x >> 13;
            x *= 1597334677u;
            x ^= x >> 16;
            return (x & 0xFFFFFF) / (double)0x1000000;
        }

        protected static double Sin01(double t) => 0.5 + 0.5 * Math.Sin(t);
        protected static double Saw(double t) => t - Math.Floor(t);

        /// <summary>Draw a single LED dot — used as a building block by many effects.</summary>
        protected static void Dot(DrawingContext dc, double x, double y, double r, Color c, double alpha = 1.0)
        {
            dc.DrawEllipse(Brush(c, alpha), null, new Point(x, y), r, r);
        }

        /// <summary>Filled rounded rect.</summary>
        protected static void Rect(DrawingContext dc, double x, double y, double w, double h, Color c, double alpha = 1.0, double radius = 1.5)
        {
            dc.DrawRoundedRectangle(Brush(c, alpha), null, new Rect(x, y, w, h), radius, radius);
        }

        private void RenderFallback(Ctx c)
        {
            Dot(c.Dc, c.Cx, c.Cy, Math.Min(c.W, c.H) * 0.25, c.Color);
        }
    }
}
