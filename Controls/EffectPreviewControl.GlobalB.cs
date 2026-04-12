using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderCollision(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            double phase = Saw(c.T * 0.5);
            double pos1 = phase * (N - 1) / 2.0;
            double pos2 = (N - 1) - phase * (N - 1) / 2.0;
            double flash = Math.Pow(Math.Max(0, phase - 0.85) / 0.15, 2);
            for (int i = 0; i < N; i++)
            {
                double d1 = Math.Abs(i - pos1);
                double d2 = Math.Abs(i - pos2);
                double k1 = Math.Max(0, 1.0 - d1 * 0.7);
                double k2 = Math.Max(0, 1.0 - d2 * 0.7);
                var col = k1 > k2 ? c.Color : c.Color2;
                double k = Math.Max(k1, k2);
                if (flash > 0 && Math.Abs(i - (N - 1) / 2.0) < 1.8)
                {
                    col = Lerp(col, Colors.White, flash);
                    k = Math.Max(k, flash);
                }
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.15 + k * 0.85);
            }
        }

        private void RenderDNA(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.32, c.H * 0.22);
            for (int i = 0; i < N; i++)
            {
                double off = Math.Sin(c.T * 2 + i * 0.5) * c.H * 0.25;
                double x = sp * (i + 1);
                Dot(c.Dc, x, c.Cy + off, r, c.Color, 0.95);
                Dot(c.Dc, x, c.Cy - off, r, c.Color2, 0.95);
            }
        }

        private void RenderRainfall(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r * 0.5, c.Color, 0.12);
            for (int k = 0; k < 4; k++)
            {
                double phase = Saw(c.T * 0.8 + k * 0.25);
                int col = ((int)(c.T * 0.8 + k * 0.25)) % N;
                col = (col + k * 3) % N;
                double y = phase * c.H;
                double x = sp * (col + 1);
                bool impact = phase > 0.9;
                double rr = impact ? r * 1.4 : r * 0.8;
                Dot(c.Dc, x, y, rr, c.Color, impact ? 1.0 : 0.85);
            }
        }

        private void RenderPoliceLights(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            bool leftSide = ((int)(c.T * 6)) % 2 == 0;
            double env = Math.Pow(Sin01(c.T * 12), 4);
            for (int i = 0; i < N; i++)
            {
                bool inLeft = i < N / 2;
                bool on = (inLeft && leftSide) || (!inLeft && !leftSide);
                var col = inLeft ? c.Color : c.Color2;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, on ? env : 0.1);
            }
        }

        private void RenderAurora(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double hue = 0.3 + 0.25 * Math.Sin(c.T * 0.4 + i * 0.2);
                double v = Math.Pow(Sin01(c.T * 0.8 + i * 0.15), 1.5);
                var col = Hsv(hue, 0.85, 0.9);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.2 + v * 0.8);
            }
        }

        private void RenderMatrix(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.32, c.H * 0.18);
            var green = Color.FromRgb(0, 0xE6, 0x76);
            for (int i = 0; i < N; i++)
            {
                double headY = Saw(c.T * 0.6 + i * 0.07) * c.H;
                double x = sp * (i + 1);
                for (int t = 0; t < 3; t++)
                {
                    double y = headY - t * r * 1.6;
                    if (y < 0) y += c.H;
                    double a = (1.0 - t / 3.0) * 0.95;
                    Dot(c.Dc, x, y, r, t == 0 ? Lerp(green, Colors.White, 0.4) : green, a);
                }
            }
        }

        private void RenderStarfield(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double k = Math.Pow(Sin01(c.T * (1.5 + Rand(i) * 2) + Rand(i + 100) * 10), 3);
                var col = Lerp(c.Color, Colors.White, 0.3);
                Dot(c.Dc, sp * (i + 1), c.Cy, r * (0.5 + k * 0.6), col, 0.1 + k * 0.9);
            }
        }

        private void RenderEqualizer(Ctx c)
        {
            int bands = 5;
            double bw = c.W / (bands + 0.5);
            double pad = bw * 0.15;
            for (int k = 0; k < bands; k++)
            {
                double e = Math.Pow(Sin01(c.T * (2 + k * 0.5) + k), 2);
                double bh = c.H * (0.15 + e * 0.8);
                double bx = bw * 0.4 + k * bw + pad;
                double by = c.H - bh;
                double bwi = bw - pad * 2;
                Rect(c.Dc, bx, by, bwi, bh, c.Color, 0.95, 1.5);
                double tipH = Math.Min(bh, c.H * 0.18);
                Rect(c.Dc, bx, by, bwi, tipH, Lerp(c.Color, c.Color2, 0.7), 0.95, 1.5);
            }
        }

        private void RenderWaterfall(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.5, c.H * 0.16);
            for (int i = 0; i < N; i++)
            {
                double x = sp * (i + 1);
                for (int row = 0; row < 3; row++)
                {
                    double yt = (row + 0.5) / 3.0;
                    double hue = ((c.T * 0.3 + i / (double)N + yt * 0.4) % 1.0 + 1.0) % 1.0;
                    double v = 0.5 + 0.5 * Math.Sin(c.T * 1.5 - row * 1.2 + i * 0.3);
                    var col = Hsv(hue, 0.85, 1.0);
                    Dot(c.Dc, x, yt * c.H, r, col, 0.3 + v * 0.7);
                }
            }
        }

        private void RenderLava(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    double bx = (Math.Sin(c.T * 0.3 + k * 1.5) + 1) * 0.5 * (N - 1);
                    sum += Math.Exp(-Math.Pow((i - bx) / 1.5, 2));
                }
                double k01 = Math.Min(1.0, sum);
                var hot = Lerp(c.Color, c.Color2, 0.7);
                var col = Lerp(c.Color, hot, k01);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.2 + k01 * 0.8);
            }
        }

        private void RenderVuWave(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double k = Math.Pow(Sin01(c.T * 2 - Math.Abs(i - N / 2.0) * 0.4), 2);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 0.15 + k * 0.85);
            }
        }

        private void RenderNebulaDrift(Ctx c)
        {
            int N = 12;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double hue = ((c.T * 0.15 + i / (double)N + 0.1 * Math.Sin(c.T * 0.5 + i * 0.3)) % 1.0 + 1.0) % 1.0;
                double v = Sin01(c.T * 0.3 + i * 0.2) * 0.7 + 0.3;
                var col = Hsv(hue, 0.9, 1.0);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, v);
            }
        }
    }
}
