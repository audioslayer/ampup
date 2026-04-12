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
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            double pos = Math.Abs(Saw(c.T * 0.6) * 2 - 1) * (N - 1);
            for (int i = 0; i < N; i++)
            {
                double dist = Math.Abs(pos - i);
                double k = Math.Max(0, 1 - dist / 3.0);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 0.12 + 0.88 * k);
            }
        }

        private void RenderMeteorRain(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            double head = Saw(c.T * 0.5) * (N + 4) - 2;
            for (int i = 0; i < N; i++)
            {
                double behind = head - i;
                double k;
                if (behind < 0) k = 0.05;
                else if (behind > 5) k = 0.05;
                else k = Math.Max(0.05, 1 - behind / 5.0);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, k);
            }
        }

        private void RenderColorWave(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double t = Sin01(c.T * 1.5 + i * 0.4);
                Color col = Lerp(c.Color, c.Color2, t);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 1.0);
            }
        }

        private void RenderSegments(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            int shift = (int)(c.T * 3);
            for (int i = 0; i < N; i++)
            {
                int band = (i + shift) / 3;
                Color col = band % 2 == 0 ? c.Color : c.Color2;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 1.0);
            }
        }

        private void RenderTheaterChase(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            int shift = (int)(c.T * 4);
            for (int i = 0; i < N; i++)
            {
                bool on = (i + shift) % 3 == 0;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, on ? 1.0 : 0.12);
            }
        }

        private void RenderRainbowScanner(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            double pos = Math.Abs(Saw(c.T * 0.6) * 2 - 1) * (N - 1);
            for (int i = 0; i < N; i++)
            {
                double dist = Math.Abs(pos - i);
                double k = Math.Max(0, 1 - dist / 3.0);
                Color col = Hsv((c.T * 0.2 + i / (double)N) % 1.0);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.12 + 0.88 * k);
            }
        }

        private void RenderSparkleRain(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double n = Rand(c.Frame / 4 + i * 7);
                bool bright = n > 0.85;
                Color col = bright ? c.Color : Scale(c.Color, 0.2);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, bright ? 1.0 : 0.6);
            }
        }

        private void RenderBreathingSync(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double k = Math.Pow(Sin01(c.T * 1.5 + i * 0.2), 2);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, 0.15 + 0.85 * k);
            }
        }

        private void RenderFireWall(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double hue = 0.02 + 0.10 * Sin01(c.T * 0.5 + i * 0.3);
                double v = 0.4 + 0.6 * Sin01(c.T * 3 + i * 0.7);
                Color col = Hsv(hue, 1.0, v);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 1.0);
            }
        }

        private void RenderDualRacer(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            double s = Saw(c.T * 0.6);
            double p1 = s * (N - 1);
            double p2 = (1 - s) * (N - 1);
            for (int i = 0; i < N; i++)
            {
                double k1 = Math.Max(0, 1 - Math.Abs(p1 - i) / 1.6);
                double k2 = Math.Max(0, 1 - Math.Abs(p2 - i) / 1.6);
                double overlap = Math.Min(k1, k2);
                Color baseCol = k1 > k2 ? c.Color : c.Color2;
                double k = Math.Max(k1, k2);
                Color col = overlap > 0.4
                    ? Lerp(baseCol, Color.FromRgb(255, 255, 255), 0.5)
                    : baseCol;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 0.12 + 0.88 * k);
            }
        }

        private void RenderLightning(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            int strikeId = (int)(c.T * 1.4);
            int strikeIdx = (int)(Rand(strikeId) * N);
            double frac = Saw(c.T * 1.4);
            double fade = Math.Max(0, 1 - frac * 2.0);
            Color white = Color.FromRgb(255, 255, 255);
            for (int i = 0; i < N; i++)
            {
                double k = 0.12;
                Color col = c.Color;
                int d = Math.Abs(i - strikeIdx);
                if (d <= 2)
                {
                    double strikeK = (1 - d / 3.0) * fade;
                    if (strikeK > 0)
                    {
                        col = Lerp(c.Color, white, strikeK);
                        k = Math.Max(k, strikeK);
                    }
                }
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);
            }
        }

        private void RenderFillup(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            double tri = Math.Abs(Saw(c.T * 0.3) * 2 - 1);
            for (int i = 0; i < N; i++)
            {
                bool on = (i / (double)N) < tri;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, on ? 1.0 : 0.12);
            }
        }

        private void RenderOcean(Ctx c)
        {
            int N = StripN;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.42, c.H * 0.28);
            for (int i = 0; i < N; i++)
            {
                double hue = 0.5 + 0.05 * Math.Sin(c.T * 1.2 + i * 0.4);
                double v = Sin01(c.T * 2 + i * 0.3) * 0.8 + 0.2;
                Color col = Hsv(hue, 1.0, v);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, 1.0);
            }
        }
    }
}
