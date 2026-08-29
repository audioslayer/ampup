using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderRgbOverdrive(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 12; i++)
            {
                double x = i / 12.0;
                double hue = ((x * 0.84 + c.T * 0.10 + Math.Sin(x * 17 - c.T * 2.2) * 0.06) % 1.0 + 1.0) % 1.0;
                stops.Add(new GradientStop(Hsv(hue, 0.98, 1.0), x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
            double crest = c.W * Saw(c.T * 0.37);
            double crestWidth = Math.Max(3, c.W * 0.025);
            Rect(c.Dc, crest - crestWidth / 2, 0, crestWidth, c.H, Colors.White, 0.24, crestWidth * 0.35);
        }

        private void RenderLaserGrid(Ctx c)
        {
            var bg = new LinearGradientBrush(c.Color, c.Color2, 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);
            double[] rates = { 0.31, -0.23, 0.17 };
            double[] offsets = { 0.0, 0.35, 0.72 };
            for (int i = 0; i < 3; i++)
            {
                double pos = Sin01(c.T * rates[i] * Math.PI * 2 + offsets[i] * 7.0);
                double x = c.W * pos;
                var color = i == 1 ? c.Color2 : c.Color;
                double haloWidth = Math.Max(8, c.W * 0.08);
                double coreWidth = Math.Max(2, c.W * 0.012);
                Rect(c.Dc, x - haloWidth / 2, 0, haloWidth, c.H, color, 0.22, haloWidth * 0.25);
                Rect(c.Dc, x - coreWidth / 2, 0, coreWidth, c.H, Colors.White, 0.90, coreWidth * 0.5);
            }
        }

        private void RenderHyperdrive(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 8; i++)
            {
                double x = i / 8.0;
                stops.Add(new GradientStop(Hsv((x * 0.72 + c.T * 0.06) % 1.0, 0.98, 0.62), x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
            for (int i = 0; i < 5; i++)
            {
                double head = c.W * Saw(c.T * (0.44 + i * 0.035) + i * 0.21);
                var color = Hsv((i / 5.0 + c.T * 0.04) % 1.0, 0.96, 1.0);
                double lineHeight = Math.Max(2.6, c.H * 0.075);
                double y = c.H * (0.1 + i * 0.19);
                Rect(c.Dc, head - c.W * 0.17, y, c.W * 0.17, lineHeight, color, 0.70, lineHeight * 0.5);
                Dot(c.Dc, head, y + lineHeight / 2, Math.Max(2.3, c.H * 0.065), Colors.White, 0.96);
            }
        }

        private void RenderPrismPulse(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 7; i++)
            {
                double x = i / 7.0;
                stops.Add(new GradientStop(Hsv((x + c.T * 0.08) % 1.0, 0.98, 1.0), x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
            double pulse = Math.Pow(Sin01(c.T * 1.15), 6);
            Rect(c.Dc, 0, 0, c.W, c.H, Colors.White, pulse * 0.32, 3);
        }

        private void RenderColorJuggle(Ctx c)
        {
            var background = new LinearGradientBrush(Hsv((c.T * 0.04) % 1.0, 0.95, 0.68),
                Hsv((0.52 + c.T * 0.04) % 1.0, 0.95, 0.68), 0);
            c.Dc.DrawRoundedRectangle(background, null, new Rect(0, 0, c.W, c.H), 3, 3);
            for (int i = 0; i < 6; i++)
            {
                double x = c.W * Sin01(c.T * (0.75 + i * 0.08) + i * 1.17);
                double y = c.H * (0.18 + i * 0.128);
                var color = Hsv((i / 6.0 + c.T * 0.03) % 1.0, 0.98, 1.0);
                Dot(c.Dc, x, y, Math.Max(5.5, c.H * 0.19), color, 0.28);
                Dot(c.Dc, x, y, Math.Max(2.4, c.H * 0.075), Lerp(color, Colors.White, 0.38), 1.0);
            }
        }

        private void RenderSpectrumSurge(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 12; i++)
            {
                double x = i / 12.0;
                double radial = Math.Abs(x - 0.5) * 2.0;
                double surge = Sin01(radial * 8.0 - c.T * 2.3);
                var color = Lerp(c.Color, c.Color2, (radial + c.T * 0.08) % 1.0);
                color = Lerp(color, Colors.White, Math.Pow(surge, 7) * 0.32);
                stops.Add(new GradientStop(color, x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
        }
    }
}
