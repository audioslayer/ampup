using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private const int StripN = 12;

        private void RenderScanner(Ctx c)
        {
            double pos = Math.Abs(Saw(c.T * 0.6) * 2 - 1);
            double x = 4 + pos * (c.W - 8);
            Dot(c.Dc, x, c.Cy, 8, c.Color, 0.15);
            Dot(c.Dc, x, c.Cy, 5, c.Color, 0.35);
            Dot(c.Dc, x, c.Cy, 3, c.Color, 1.0);
            for (int t = 1; t <= 4; t++)
            {
                double trail = pos - (Saw(c.T * 0.6) > 0.5 ? -1 : 1) * t * 0.04;
                trail = Math.Clamp(trail, 0, 1);
                double tx = 4 + trail * (c.W - 8);
                double a = Math.Max(0, 0.5 - t * 0.12);
                Dot(c.Dc, tx, c.Cy, 2.5 - t * 0.3, c.Color, a);
            }
        }

        private void RenderMeteorRain(Ctx c)
        {
            double head = Saw(c.T * 0.5);
            double hx = 4 + head * (c.W - 8);
            double hy = c.Cy;
            Dot(c.Dc, hx, hy, 4, c.Color, 1.0);
            Dot(c.Dc, hx, hy, 6, c.Color, 0.3);
            for (int t = 1; t <= 5; t++)
            {
                double trail = head - t * 0.06;
                if (trail < 0) trail += 1.0;
                double tx = 4 + trail * (c.W - 8);
                double a = Math.Max(0.05, 1.0 - t * 0.2);
                double r = Math.Max(1.5, 3.5 - t * 0.5);
                Dot(c.Dc, tx, hy, r, c.Color, a);
            }
        }

        private void RenderColorWave(Ctx c)
        {
            double offset = c.T * 0.3;
            var stops = new GradientStopCollection
            {
                new GradientStop(c.Color, (0.0 + offset) % 1.0),
                new GradientStop(c.Color2, (0.33 + offset) % 1.0),
                new GradientStop(c.Color, (0.66 + offset) % 1.0),
                new GradientStop(c.Color2, (1.0 + offset) % 1.0),
            };
            var gb = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5))
            {
                SpreadMethod = GradientSpreadMethod.Repeat,
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            var transform = new TranslateTransform(-(c.T * 40 % c.W), 0);
            gb.Transform = transform;
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderSegments(Ctx c)
        {
            int bands = 4;
            double bandW = c.W / bands;
            double shift = (c.T * 30) % (bandW * 2);
            for (int i = -1; i <= bands + 1; i++)
            {
                double x = i * bandW - shift;
                Color col = i % 2 == 0 ? c.Color : c.Color2;
                if (x + bandW > 0 && x < c.W)
                    Rect(c.Dc, Math.Max(0, x), 0, Math.Min(bandW, c.W - Math.Max(0, x)), c.H, col, 1.0, 0);
            }
        }

        private void RenderTheaterChase(Ctx c)
        {
            int count = 5;
            double spacing = c.W / (count + 1);
            int shift = (int)(c.T * 4);
            for (int i = 0; i < count; i++)
            {
                bool on = (i + shift) % 3 == 0;
                double x = spacing * (i + 1);
                if (on)
                {
                    Dot(c.Dc, x, c.Cy, 5, c.Color, 0.25);
                    Dot(c.Dc, x, c.Cy, 3, c.Color, 1.0);
                }
                else
                {
                    Dot(c.Dc, x, c.Cy, 2, c.Color, 0.12);
                }
            }
        }

        private void RenderRainbowScanner(Ctx c)
        {
            double pos = Math.Abs(Saw(c.T * 0.6) * 2 - 1);
            double x = 4 + pos * (c.W - 8);
            Color head = Hsv(c.T * 0.3);
            Dot(c.Dc, x, c.Cy, 8, head, 0.15);
            Dot(c.Dc, x, c.Cy, 5, head, 0.35);
            Dot(c.Dc, x, c.Cy, 3, head, 1.0);
            for (int t = 1; t <= 4; t++)
            {
                double trail = pos - (Saw(c.T * 0.6) > 0.5 ? -1 : 1) * t * 0.04;
                trail = Math.Clamp(trail, 0, 1);
                double tx = 4 + trail * (c.W - 8);
                Color tc = Hsv(c.T * 0.3 - t * 0.08);
                double a = Math.Max(0, 0.5 - t * 0.12);
                Dot(c.Dc, tx, c.Cy, 2.5 - t * 0.3, tc, a);
            }
        }

        private void RenderSparkleRain(Ctx c)
        {
            int count = 8;
            for (int i = 0; i < count; i++)
            {
                double sx = Rand(i * 31 + 7) * c.W;
                double sy = Rand(i * 53 + 13) * c.H;
                int phase = (c.Frame / 4 + (int)(Rand(i * 17) * 20)) % 12;
                double brightness = phase < 3 ? (phase / 2.0) : Math.Max(0, 1.0 - (phase - 3) / 5.0);
                double r = 1.5 + brightness * 1.5;
                Color col = brightness > 0.5 ? Colors.White : c.Color;
                Dot(c.Dc, sx, sy, r, col, 0.1 + 0.9 * brightness);
            }
        }

        private void RenderBreathingSync(Ctx c)
        {
            double k = Math.Pow(Sin01(c.T * 1.5), 2);
            byte a = (byte)(k * 255);
            var radial = new RadialGradientBrush(
                Color.FromArgb(a, c.Color.R, c.Color.G, c.Color.B),
                Color.FromArgb(0, c.Color.R, c.Color.G, c.Color.B))
            {
                Center = new Point(0.5, 0.5),
                RadiusX = 0.6,
                RadiusY = 0.8,
            };
            c.Dc.DrawRoundedRectangle(radial, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderFireWall(Ctx c)
        {
            double flicker = 0.7 + 0.3 * Sin01(c.T * 6);
            Color red = Color.FromRgb((byte)(255 * flicker), (byte)(34 * flicker), 0);
            Color orange = Color.FromRgb((byte)(255 * flicker), (byte)(138 * flicker), (byte)(61 * flicker));
            Color yellow = Color.FromRgb((byte)(255 * flicker), (byte)(202 * flicker), (byte)(40 * flicker));
            var stops = new GradientStopCollection
            {
                new GradientStop(red, 1.0),
                new GradientStop(orange, 0.5),
                new GradientStop(yellow, 0.15),
                new GradientStop(Color.FromArgb(0, 255, 213, 79), 0.0),
            };
            var gb = new LinearGradientBrush(stops, new Point(0.5, 1), new Point(0.5, 0));
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderDualRacer(Ctx c)
        {
            double s = Saw(c.T * 0.6);
            double x1 = 4 + s * (c.W - 8);
            double x2 = 4 + (1 - s) * (c.W - 8);
            double y1 = c.Cy - 3;
            double y2 = c.Cy + 3;
            Dot(c.Dc, x1, y1, 6, c.Color, 0.2);
            Dot(c.Dc, x1, y1, 3.5, c.Color, 1.0);
            Dot(c.Dc, x2, y2, 6, c.Color2, 0.2);
            Dot(c.Dc, x2, y2, 3.5, c.Color2, 1.0);
            double dist = Math.Abs(x1 - x2);
            if (dist < 12)
            {
                double flash = Math.Max(0, 1 - dist / 12.0);
                double mx = (x1 + x2) / 2;
                double my = c.Cy;
                Dot(c.Dc, mx, my, 6 * flash, Colors.White, flash * 0.7);
            }
        }

        private void RenderLightning(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Color.FromRgb(0x11, 0x11, 0x11), 1.0, 3);
            double cycle = Saw(c.T * 0.7);
            double flash = 0;
            if (cycle > 0.90 && cycle < 0.93) flash = 1.0;
            else if (cycle > 0.95 && cycle < 0.97) flash = 0.7;
            if (flash > 0)
            {
                Color bolt = Color.FromRgb(0xFF, 0xF1, 0x76);
                double bx = c.Cx;
                double bw = 2;
                double bh = c.H * 0.7;
                double by = c.H * 0.15;
                Rect(c.Dc, bx - bw / 2, by, bw, bh, bolt, flash, 1);
                Dot(c.Dc, bx, c.Cy, 10, bolt, flash * 0.25);
                Dot(c.Dc, bx, c.Cy, 5, bolt, flash * 0.5);
            }
        }

        private void RenderFillup(Ctx c)
        {
            double tri = Math.Abs(Saw(c.T * 0.3) * 2 - 1);
            double fillW = tri * c.W;
            if (fillW > 0.5)
            {
                var stops = new GradientStopCollection
                {
                    new GradientStop(c.Color, 0),
                    new GradientStop(Color.FromArgb(128, c.Color.R, c.Color.G, c.Color.B), 1),
                };
                var gb = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
                c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, fillW, c.H), 0, 0);
            }
        }

        private void RenderOcean(Ctx c)
        {
            double shift = c.T * 0.15;
            var stops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0x00, 0x97, 0xA7), 0),
                new GradientStop(Color.FromRgb(0x29, 0xB6, 0xF6), 0.25),
                new GradientStop(Color.FromRgb(0x4D, 0xD0, 0xE1), 0.5),
                new GradientStop(Color.FromRgb(0x29, 0xB6, 0xF6), 0.75),
                new GradientStop(Color.FromRgb(0x00, 0x97, 0xA7), 1.0),
            };
            var gb = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5))
            {
                SpreadMethod = GradientSpreadMethod.Repeat,
            };
            gb.Transform = new TranslateTransform(-(c.T * 25 % c.W), 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
            double wave = Sin01(c.T * 2.0) * 0.15;
            Dot(c.Dc, c.W * (0.3 + wave), c.H * 0.3, 3, Colors.White, 0.15 + 0.1 * Sin01(c.T * 3));
            Dot(c.Dc, c.W * (0.7 - wave), c.H * 0.6, 2, Colors.White, 0.1 + 0.08 * Sin01(c.T * 2.5 + 1));
        }
    }
}
