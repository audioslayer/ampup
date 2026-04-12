using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderCollision(Ctx c)
        {
            double phase = Saw(c.T * 0.5);
            double pos1 = phase * c.W * 0.5;
            double pos2 = c.W - phase * c.W * 0.5;
            double r = Math.Min(c.W * 0.06, c.H * 0.28);
            double flash = Math.Pow(Math.Max(0, phase - 0.85) / 0.15, 2);

            Dot(c.Dc, pos1, c.Cy, r, c.Color, 0.95);
            Dot(c.Dc, pos1 - r * 2, c.Cy, r * 0.5, c.Color, 0.4);
            Dot(c.Dc, pos2, c.Cy, r, c.Color2, 0.95);
            Dot(c.Dc, pos2 + r * 2, c.Cy, r * 0.5, c.Color2, 0.4);

            if (flash > 0.01)
            {
                double fr = r * (1 + flash * 3);
                Dot(c.Dc, c.Cx, c.Cy, fr, Colors.White, flash * 0.9);
                Dot(c.Dc, c.Cx, c.Cy, fr * 0.5, Colors.White, flash);
            }
        }

        private void RenderDNA(Ctx c)
        {
            int N = 10;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.35, c.H * 0.18);
            var sg1 = new StreamGeometry();
            var sg2 = new StreamGeometry();

            using (var ctx1 = sg1.Open())
            {
                for (int i = 0; i <= N; i++)
                {
                    double x = sp * (i + 0.5);
                    double off = Math.Sin(c.T * 2.5 + i * 0.6) * c.H * 0.3;
                    if (i == 0) ctx1.BeginFigure(new Point(x, c.Cy + off), false, false);
                    else ctx1.LineTo(new Point(x, c.Cy + off), true, true);
                }
            }

            using (var ctx2 = sg2.Open())
            {
                for (int i = 0; i <= N; i++)
                {
                    double x = sp * (i + 0.5);
                    double off = Math.Sin(c.T * 2.5 + i * 0.6) * c.H * 0.3;
                    if (i == 0) ctx2.BeginFigure(new Point(x, c.Cy - off), false, false);
                    else ctx2.LineTo(new Point(x, c.Cy - off), true, true);
                }
            }

            c.Dc.DrawGeometry(null, Pen(c.Color, 1.5, 0.6), sg1);
            c.Dc.DrawGeometry(null, Pen(c.Color2, 1.5, 0.6), sg2);

            for (int i = 0; i < N; i += 2)
            {
                double x = sp * (i + 0.5);
                double off = Math.Sin(c.T * 2.5 + i * 0.6) * c.H * 0.3;
                Dot(c.Dc, x, c.Cy + off, r, c.Color, 0.95);
                Dot(c.Dc, x, c.Cy - off, r, c.Color2, 0.95);
            }
        }

        private void RenderRainfall(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.05, 3);

            double[] xPcts = { 0.15, 0.38, 0.62, 0.85 };
            double[] speeds = { 1.0, 0.8, 1.1, 0.9 };
            double[] delays = { 0.0, 0.3, 0.6, 0.15 };

            for (int i = 0; i < 4; i++)
            {
                double x = c.W * xPcts[i];
                double phase = Saw(c.T * speeds[i] + delays[i]);
                double y = phase * (c.H + 8) - 4;
                double dropW = 2;
                double dropH = 6;
                double a = 1.0 - phase * 0.5;

                Rect(c.Dc, x - dropW / 2, y, dropW, dropH, c.Color, a, 1);

                if (phase > 0.85)
                {
                    double splash = (phase - 0.85) / 0.15;
                    Dot(c.Dc, x, c.H - 2, 3 * splash, c.Color, (1 - splash) * 0.7);
                }
            }
        }

        private void RenderPoliceLights(Ctx c)
        {
            bool leftOn = ((int)(c.T * 6)) % 2 == 0;
            Rect(c.Dc, 0, 0, c.W / 2, c.H, c.Color, leftOn ? 0.9 : 0.12, 3);
            Rect(c.Dc, c.W / 2, 0, c.W / 2, c.H, c.Color2, leftOn ? 0.12 : 0.9, 3);
        }

        private void RenderAurora(Ctx c)
        {
            double drift = c.T * 0.15;
            double s0 = (Math.Sin(drift) * 0.5 + 0.5) * 0.3;
            double s1 = (Math.Sin(drift + 1.5) * 0.5 + 0.5) * 0.3 + 0.35;
            double s2 = (Math.Sin(drift + 3.0) * 0.5 + 0.5) * 0.3 + 0.7;

            var stops = new GradientStopCollection
            {
                new GradientStop(Hsv(0.45, 0.7, 0.6), Math.Clamp(s0, 0, 1)),
                new GradientStop(Hsv(0.38, 0.8, 0.85), Math.Clamp(s1, 0, 1)),
                new GradientStop(Hsv(0.3, 0.75, 0.7), Math.Clamp(s2, 0, 1)),
                new GradientStop(Hsv(0.52, 0.6, 0.5), 0.0),
                new GradientStop(Hsv(0.35, 0.65, 0.6), 1.0),
            };
            var gb = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderMatrix(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Color.FromRgb(0, 15, 0), 0.7, 3);

            var green = Color.FromRgb(0, 0xE6, 0x76);
            double[] xPcts = { 0.1, 0.3, 0.55, 0.78 };
            double[] speeds = { 0.7, 0.5, 0.6, 0.45 };
            double[] delays = { 0, 0.5, 0.2, 0.8 };
            int[] lengths = { 3, 3, 2, 3 };

            for (int col = 0; col < 4; col++)
            {
                double x = c.W * xPcts[col];
                double headY = Saw(c.T * speeds[col] + delays[col]) * (c.H + 20) - 5;

                for (int seg = 0; seg < lengths[col]; seg++)
                {
                    double y = headY - seg * 5;
                    if (y < -3 || y > c.H + 3) continue;
                    double a = seg == 0 ? 1.0 : Math.Max(0.15, 1.0 - seg * 0.35);
                    var col2 = seg == 0 ? Lerp(green, Colors.White, 0.4) : green;
                    double r = seg == 0 ? 2.0 : 1.5;
                    Rect(c.Dc, x - r, y, r * 2, r * 2, col2, a, 1);
                }
            }
        }

        private void RenderStarfield(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Color.FromRgb(8, 6, 15), 0.5, 3);

            double[] xs = { 0.12, 0.45, 0.7, 0.3, 0.85, 0.55, 0.2, 0.65 };
            double[] ys = { 0.2, 0.65, 0.25, 0.78, 0.5, 0.12, 0.55, 0.82 };
            double[] rates = { 1.5, 2.1, 1.8, 2.5, 1.3, 2.0, 1.7, 2.3 };
            bool[] warm = { false, true, false, false, false, true, false, false };

            var lavender = Color.FromRgb(0xB3, 0x9D, 0xDB);
            var gold = Color.FromRgb(0xFF, 0xCA, 0x28);

            for (int i = 0; i < 8; i++)
            {
                double k = Math.Pow(Sin01(c.T * rates[i] + Rand(i + 50) * 10), 3);
                var col = warm[i] ? gold : Lerp(lavender, Colors.White, 0.2);
                double r = 1.0 + k * 1.2;
                Dot(c.Dc, c.W * xs[i], c.H * ys[i], r, col, 0.1 + k * 0.9);
            }
        }

        private void RenderEqualizer(Ctx c)
        {
            int bars = 5;
            double bw = c.W / (bars + 1);
            double gap = 2;
            for (int i = 0; i < bars; i++)
            {
                double e = Math.Pow(Sin01(c.T * (2 + i * 0.5) + i), 2);
                double bh = c.H * (0.15 + e * 0.8);
                double x = (c.W - bars * bw) / 2 + i * bw + gap / 2;
                double bwi = bw - gap;

                Rect(c.Dc, x, c.H - bh, bwi, bh, c.Color, 0.95, 1.5);

                double tipH = Math.Min(bh, c.H * 0.2);
                Rect(c.Dc, x, c.H - bh, bwi, tipH, Lerp(c.Color, c.Color2, 0.7), 0.85, 1.5);
            }
        }

        private void RenderWaterfall(Ctx c)
        {
            double scroll = c.T * 0.3;
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 6; i++)
            {
                double t = i / 6.0;
                double hue = ((scroll + t * 0.5) % 1.0 + 1.0) % 1.0;
                stops.Add(new GradientStop(Hsv(hue, 0.85, 0.9), t));
            }
            var gb = new LinearGradientBrush(stops, 90);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderLava(Ctx c)
        {
            var deep = Color.FromRgb(0xCC, 0x33, 0x00);
            var amber = Color.FromRgb(0xFF, 0xAA, 0x00);
            var stops = new GradientStopCollection
            {
                new GradientStop(deep, 0),
                new GradientStop(Lerp(deep, amber, 0.3), 0.35),
                new GradientStop(amber, 0.7),
                new GradientStop(deep, 1.0),
            };
            var bg = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);

            for (int k = 0; k < 3; k++)
            {
                double bx = c.W * (0.5 + 0.35 * Math.Sin(c.T * 0.4 + k * 2.1));
                double by = c.H * (0.5 + 0.25 * Math.Sin(c.T * 0.3 + k * 1.7));
                double br = 4 + 3 * Sin01(c.T * 0.5 + k);
                var bright = Color.FromRgb(0xFF, 0xDD, 0x44);
                Dot(c.Dc, bx, by, br, bright, 0.35 + 0.3 * Sin01(c.T * 0.6 + k * 0.8));
            }
        }

        private void RenderVuWave(Ctx c)
        {
            int bars = 5;
            double bw = c.W / (bars + 1);
            double gap = 2;

            for (int i = 0; i < bars; i++)
            {
                double dist = Math.Abs(i - (bars - 1) / 2.0) / ((bars - 1) / 2.0);
                double e = Math.Pow(Sin01(c.T * 2 - dist * 1.5), 2);
                double bh = c.H * (0.1 + e * 0.85);
                double x = (c.W - bars * bw) / 2 + i * bw + gap / 2;
                double bwi = bw - gap;
                double halfH = bh / 2;

                Rect(c.Dc, x, c.Cy - halfH, bwi, bh, c.Color, 0.9, 1.5);
            }
        }

        private void RenderNebulaDrift(Ctx c)
        {
            double drift = c.T * 0.1;
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 8; i++)
            {
                double t = i / 8.0;
                double hue = ((drift + t) % 1.0 + 1.0) % 1.0;
                stops.Add(new GradientStop(Hsv(hue, 0.8, 0.9), t));
            }
            var gb = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }
    }
}
