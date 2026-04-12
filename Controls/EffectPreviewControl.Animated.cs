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
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col);
        }

        private void RenderPulse(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double k = Sin01(c.T * 2.0 + i * 0.7);
                var col = Lerp(c.Color2, c.Color, k);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.4 + 0.6 * k);
            }
        }

        private void RenderBreathing(Ctx c)
        {
            double k = Math.Pow(Sin01(c.T * 1.4), 2);
            var col = Lerp(c.Color2, c.Color, k);
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.35 + 0.65 * k);
        }

        private void RenderFire(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double flick = Rand(c.Frame / 3 + i * 11);
                double heat = 0.35 + i * 0.22 + flick * 0.35;
                heat = Math.Clamp(heat, 0.0, 1.0);
                var col = Lerp(c.Color, c.Color2, heat * 0.7);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.5 + 0.5 * heat);
            }
        }

        private void RenderComet(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double pos = Saw(c.T * 0.6) * 3.0;
            for (int i = 0; i < 3; i++)
            {
                double d = pos - i;
                double a;
                if (d < 0) a = 0.0;
                else if (d > 2.0) a = 0.0;
                else a = Math.Max(0.0, 1.0 - d * 0.5);
                var col = Lerp(c.Color2, c.Color, a);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.25 + 0.75 * a);
            }
        }

        private void RenderSparkle(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double v = Rand(c.Frame / 6 + i * 13);
                bool flash = v > 0.85;
                double phase = (c.Frame % 6) / 6.0;
                if (flash)
                {
                    var white = Color.FromRgb(255, 255, 255);
                    var col = Lerp(c.Color, white, 1.0 - phase);
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 1.0);
                }
                else
                {
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 0.3);
                }
            }
        }

        private void RenderPingPong(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double tri = Math.Abs(Saw(c.T * 0.5) * 2.0 - 1.0);
            double margin = r + 2;
            double x = margin + (c.W - 2 * margin) * tri;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r * 0.55, c.Color2, 0.25);
            Dot(c.Dc, x, c.Cy, r, c.Color, 1.0);
        }

        private void RenderStack(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            int count = (int)((c.T * 1.2) % 4);
            for (int i = 0; i < 3; i++)
            {
                bool lit = i < count;
                if (lit)
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 1.0);
                else
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color2, 0.25);
            }
        }

        private void RenderWave(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double k = Sin01(c.T * 2.5 + i * (Math.PI * 2.0 / 3.0));
                var col = Lerp(c.Color2, c.Color, k);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.3 + 0.7 * k);
            }
        }

        private void RenderCandle(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double a = Rand(c.Frame / 8 + i * 7);
                double b = Rand(c.Frame / 8 + 1 + i * 7);
                double phase = (c.Frame % 8) / 8.0;
                double flick = a * (1 - phase) + b * phase;
                double k = 0.55 + flick * 0.45;
                var col = Lerp(c.Color, c.Color2, flick * 0.4);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);
            }
        }

        private void RenderRainbowWave(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.18;
            int n = 5;
            double step = c.W / (n + 1.0);
            for (int i = 0; i < n; i++)
            {
                double h = (c.T * 0.3 + i / (double)n) % 1.0;
                Dot(c.Dc, step * (i + 1), c.Cy, r, Hsv(h));
            }
        }

        private void RenderRainbowCycle(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double h = (c.T * 0.3 + i / 3.0) % 1.0;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, Hsv(h));
            }
        }

        private void RenderWheel(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double pos = Saw(c.T * 0.5) * 3.0;
            int head = (int)pos % 3;
            int trail = (head + 2) % 3;
            for (int i = 0; i < 3; i++)
            {
                double a;
                if (i == head) a = 1.0;
                else if (i == trail) a = 0.35;
                else a = 0.15;
                var col = i == head ? c.Color : Lerp(c.Color2, c.Color, a);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, a);
            }
        }

        private void RenderRainbowWheel(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double baseHue = c.T * 0.3;
            for (int i = 0; i < 3; i++)
            {
                double h = (baseHue + i * 0.11) % 1.0;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, Hsv(h));
            }
        }

        private void RenderHeartbeat(Ctx c)
        {
            double t = c.T * 1.4;
            double a = Math.Pow(Sin01(t * 4), 8);
            double b = Math.Pow(Sin01(t * 4 - 0.6), 8);
            double k = Math.Max(a, b);
            double r = Math.Min(c.W, c.H) * (0.18 + 0.10 * k);
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 0.35 + 0.65 * k);
        }

        private void RenderPlasma(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
            {
                double s1 = Sin01(c.T * 1.3 + i * 0.9);
                double s2 = Sin01(c.T * 0.7 - i * 1.4);
                double s3 = Sin01(c.T * 2.1 + i * 0.4);
                double h = (s1 * 0.4 + s2 * 0.35 + s3 * 0.25) % 1.0;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, Hsv(h, 0.85, 1.0));
            }
        }

        private void RenderDrip(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double prog = Saw(c.T * 0.7);
            int led = (int)Math.Min(2, prog * 3);
            bool splash = prog > 0.9;
            for (int i = 0; i < 3; i++)
            {
                if (splash)
                {
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 0.9);
                }
                else if (i == led)
                {
                    double sub = (prog * 3) - led;
                    double a = 0.5 + 0.5 * (1.0 - Math.Abs(sub - 0.5) * 2);
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, a);
                }
                else
                {
                    Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color2, 0.2);
                }
            }
        }
    }
}
