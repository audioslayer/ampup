using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderBlink(Ctx c)
        {
            bool flip = ((int)(c.T * 3)) % 2 == 0;
            var col = flip ? c.Color : c.Color2;
            c.Dc.DrawRoundedRectangle(Brush(col, flip ? 1.0 : 0.7), null,
                new Rect(1, 1, c.W - 2, c.H - 2), 3, 3);
        }

        private void RenderPulse(Ctx c)
        {
            double k = Sin01(c.T * 2.5);
            double maxR = Math.Min(c.W, c.H) * 0.45;
            double r = maxR * (0.3 + 0.7 * k);
            var col = Lerp(c.Color2, c.Color, k);
            Dot(c.Dc, c.Cx, c.Cy, r, col, 0.4 + 0.6 * k);
            Dot(c.Dc, c.Cx, c.Cy, r * 1.4, col, 0.15 * k);
        }

        private void RenderBreathing(Ctx c)
        {
            double k = Math.Pow(Sin01(c.T * 1.4), 2);
            var col = Lerp(c.Color2, c.Color, k);
            c.Dc.DrawRoundedRectangle(Brush(col, 0.15 + 0.85 * k), null,
                new Rect(1, 1, c.W - 2, c.H - 2), 3, 3);
        }

        private void RenderFire(Ctx c)
        {
            int cols = 5;
            double colW = c.W / cols;
            for (int i = 0; i < cols; i++)
            {
                double flick1 = Rand(c.Frame / 3 + i * 7);
                double flick2 = Rand(c.Frame / 3 + 1 + i * 7);
                double phase = (c.Frame % 3) / 3.0;
                double flick = flick1 * (1.0 - phase) + flick2 * phase;
                double h = c.H * (0.3 + 0.6 * flick);
                double y = c.H - h;
                var top = Hsv(0.12 + flick * 0.05, 0.9, 1.0);
                var bot = Hsv(0.02, 1.0, 0.9);
                var gb = new LinearGradientBrush(top, bot, 90);
                gb.Opacity = 0.6 + 0.4 * flick;
                c.Dc.DrawRoundedRectangle(gb, null,
                    new Rect(i * colW + 1, y, colW - 2, h), 1, 1);
            }
        }

        private void RenderComet(Ctx c)
        {
            double pos = Saw(c.T * 0.5) * (c.W + 20) - 10;
            int count = 5;
            for (int i = count - 1; i >= 0; i--)
            {
                double x = pos - i * 6;
                double a = Math.Max(0, 1.0 - i * 0.25);
                double r = (i == 0) ? 4 : 3 - i * 0.3;
                var col = (i == 0) ? Colors.White : c.Color;
                Dot(c.Dc, x, c.Cy, Math.Max(1.5, r), col, a);
            }
        }

        private void RenderSparkle(Ctx c)
        {
            c.Dc.DrawRoundedRectangle(Brush(c.Color, 0.1), null,
                new Rect(0, 0, c.W, c.H), 3, 3);
            for (int i = 0; i < 6; i++)
            {
                double px = Rand(i * 31 + 5) * c.W;
                double py = Rand(i * 31 + 17) * c.H;
                double cycle = Saw(c.T * 0.8 + Rand(i * 31 + 29));
                double flash = cycle < 0.3 ? cycle / 0.3 : cycle < 0.5 ? 1.0 - (cycle - 0.3) / 0.2 : 0.0;
                int offset = (int)(Rand(i * 31 + 41) * 30);
                double timeFlash = Rand(c.Frame / 6 + i * 13 + offset);
                double alpha = timeFlash > 0.7 ? flash : 0.0;
                if (alpha > 0.05)
                    Dot(c.Dc, px, py, 2.0 + alpha, Colors.White, alpha);
            }
        }

        private void RenderPingPong(Ctx c)
        {
            double tri = Math.Abs(Saw(c.T * 0.6) * 2.0 - 1.0);
            double margin = 5;
            double x = margin + (c.W - 2 * margin) * tri;
            double r = 4;
            Dot(c.Dc, x, c.Cy, r, c.Color, 1.0);
            Dot(c.Dc, x, c.Cy, r + 3, c.Color, 0.2);
        }

        private void RenderStack(Ctx c)
        {
            int count = (int)((c.T * 1.0) % 4);
            double barW = (c.W - 8) / 3.0;
            for (int i = 0; i < 3; i++)
            {
                bool lit = i < count;
                double x = 3 + i * (barW + 1);
                double barH = c.H * 0.6;
                double y = c.H - 3 - barH;
                var col = lit ? c.Color : c.Color2;
                double alpha = lit ? 1.0 : 0.15;
                Rect(c.Dc, x, y, barW - 1, barH, col, alpha, 2);
            }
        }

        private void RenderWave(Ctx c)
        {
            var pen = Pen(c.Color, 1.8);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                int pts = 12;
                double startY = c.Cy + Math.Sin(c.T * 3.0) * c.H * 0.3;
                ctx.BeginFigure(new Point(0, startY), false, false);
                for (int i = 1; i <= pts; i++)
                {
                    double x = c.W * i / (double)pts;
                    double y = c.Cy + Math.Sin(c.T * 3.0 + i * 0.8) * c.H * 0.3;
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }
            geo.Freeze();
            c.Dc.DrawGeometry(null, pen, geo);
        }

        private void RenderCandle(Ctx c)
        {
            double f1 = Rand(c.Frame / 5);
            double f2 = Rand(c.Frame / 5 + 1);
            double phase = (c.Frame % 5) / 5.0;
            double flick = f1 * (1.0 - phase) + f2 * phase;
            double r = Math.Min(c.W, c.H) * (0.25 + 0.15 * flick);
            var warm = Hsv(0.08 + flick * 0.04, 0.85, 0.7 + 0.3 * flick);
            Dot(c.Dc, c.Cx, c.Cy, r * 1.8, warm, 0.15 + 0.15 * flick);
            Dot(c.Dc, c.Cx, c.Cy, r, warm, 0.6 + 0.4 * flick);
        }

        private void RenderRainbowWave(Ctx c)
        {
            int stops = 7;
            var gsc = new GradientStopCollection();
            for (int i = 0; i < stops; i++)
            {
                double h = (c.T * 0.2 + (double)i / stops) % 1.0;
                gsc.Add(new GradientStop(Hsv(h), (double)i / (stops - 1)));
            }
            var gb = new LinearGradientBrush(gsc, 0);
            gb.MappingMode = BrushMappingMode.RelativeToBoundingBox;
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderRainbowCycle(Ctx c)
        {
            double bandW = c.W / 3.0;
            for (int i = 0; i < 3; i++)
            {
                double h = (c.T * 0.3 + i / 3.0) % 1.0;
                var col = Hsv(h);
                c.Dc.DrawRectangle(Brush(col), null,
                    new Rect(i * bandW, 0, bandW, c.H));
            }
        }

        private void RenderWheel(Ctx c)
        {
            double angle = c.T * 3.0;
            double orbitR = Math.Min(c.W, c.H) * 0.28;
            int trail = 4;
            for (int i = trail - 1; i >= 0; i--)
            {
                double a = angle - i * 0.5;
                double x = c.Cx + Math.Cos(a) * orbitR;
                double y = c.Cy + Math.Sin(a) * orbitR * 0.6;
                double alpha = 1.0 - i * 0.25;
                double r = (i == 0) ? 3.5 : 2.5;
                var col = (i == 0) ? c.Color : Scale(c.Color, 0.5);
                Dot(c.Dc, x, y, r, col, Math.Max(0.1, alpha));
            }
        }

        private void RenderRainbowWheel(Ctx c)
        {
            double angle = c.T * 3.0;
            double orbitR = Math.Min(c.W, c.H) * 0.28;
            int trail = 5;
            for (int i = trail - 1; i >= 0; i--)
            {
                double a = angle - i * 0.4;
                double x = c.Cx + Math.Cos(a) * orbitR;
                double y = c.Cy + Math.Sin(a) * orbitR * 0.6;
                double h = (c.T * 0.3 + i * 0.08) % 1.0;
                double alpha = 1.0 - i * 0.2;
                double r = (i == 0) ? 3.5 : 2.5;
                Dot(c.Dc, x, y, r, Hsv(h), Math.Max(0.1, alpha));
            }
        }

        private void RenderHeartbeat(Ctx c)
        {
            var pen = Pen(c.Color, 1.8);
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                double scrollX = Saw(c.T * 0.4) * c.W;
                ctx.BeginFigure(new Point(0, c.Cy), false, false);
                int pts = 20;
                for (int i = 0; i <= pts; i++)
                {
                    double x = c.W * i / (double)pts;
                    double norm = ((x + scrollX) % c.W) / c.W;
                    double y;
                    if (norm > 0.35 && norm < 0.42)
                        y = c.Cy - c.H * 0.35;
                    else if (norm >= 0.42 && norm < 0.48)
                        y = c.Cy + c.H * 0.25;
                    else if (norm >= 0.48 && norm < 0.55)
                        y = c.Cy - c.H * 0.22;
                    else
                        y = c.Cy;
                    ctx.LineTo(new Point(x, y), true, true);
                }
            }
            geo.Freeze();
            c.Dc.DrawGeometry(null, pen, geo);
        }

        private void RenderPlasma(Ctx c)
        {
            int cols = 8;
            double colW = c.W / cols;
            for (int i = 0; i < cols; i++)
            {
                double s1 = Sin01(c.T * 1.3 + i * 0.6);
                double s2 = Sin01(c.T * 0.7 - i * 0.9);
                double h = (s1 * 0.5 + s2 * 0.5 + 0.7) % 1.0;
                double v = 0.6 + 0.4 * Sin01(c.T * 2.0 + i * 0.4);
                c.Dc.DrawRectangle(Brush(Hsv(h, 0.85, v)), null,
                    new Rect(i * colW, 0, colW + 1, c.H));
            }
        }

        private void RenderDrip(Ctx c)
        {
            double prog = Saw(c.T * 0.7);
            double y = prog * (c.H + 6) - 3;
            bool splash = prog > 0.85;
            if (splash)
            {
                double splashK = (prog - 0.85) / 0.15;
                double r = 3 + splashK * 8;
                Dot(c.Dc, c.Cx, c.H - 3, r, c.Color, 1.0 - splashK * 0.7);
                Dot(c.Dc, c.Cx - 6 * splashK, c.H - 3 - 4 * splashK, 2, c.Color, 0.8 - splashK);
                Dot(c.Dc, c.Cx + 5 * splashK, c.H - 3 - 3 * splashK, 1.5, c.Color, 0.8 - splashK);
            }
            else
            {
                double dropY = Math.Min(y, c.H - 3);
                c.Dc.DrawRoundedRectangle(Brush(c.Color), null,
                    new Rect(c.Cx - 1.5, dropY - 3, 3, 6), 1.5, 1.5);
            }
        }
    }
}
