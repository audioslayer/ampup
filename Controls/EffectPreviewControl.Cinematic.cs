using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderBlackHole(Ctx c)
        {
            var bg = new LinearGradientBrush(Scale(c.Color2, 0.2), Scale(c.Color, 0.45), 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);
            double x = c.Cx + Math.Sin(c.T * 0.45) * c.W * 0.08;
            double radius = c.H * (0.28 + Math.Sin(c.T * 0.31) * 0.025);
            c.Dc.DrawEllipse(null, Pen(c.Color, Math.Max(2, c.H * 0.09), 0.82), new Point(x, c.Cy), radius * 1.65, radius);
            c.Dc.DrawEllipse(null, Pen(Colors.White, Math.Max(0.8, c.H * 0.025), 0.38), new Point(x, c.Cy), radius * 1.72, radius * 1.06);
            Dot(c.Dc, x, c.Cy, radius * 0.78, Colors.Black, 0.98);
        }

        private void RenderLavaLamp(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color2, 0.16), 1, 3);
            for (int i = 0; i < 5; i++)
            {
                double seed = Rand(i + 7801);
                double x = c.W * (0.5 + 0.43 * Math.Sin(c.T * (0.34 + seed * 0.22) + seed * 8));
                double y = c.H * (0.2 + i * 0.15);
                double radius = c.H * (0.17 + seed * 0.11);
                var color = Lerp(c.Color, c.Color2, i / 4.0);
                Dot(c.Dc, x, y, radius * 1.7, color, 0.15);
                Dot(c.Dc, x, y, radius, color, 0.74);
                Dot(c.Dc, x - radius * 0.18, y - radius * 0.18, radius * 0.18, Colors.White, 0.25);
            }
        }

        private void RenderBubbles(Ctx c)
        {
            var bg = new LinearGradientBrush(Scale(c.Color, 0.25), Scale(c.Color2, 0.32), 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);
            for (int i = 0; i < 7; i++)
            {
                double seed = Rand(i + 8101);
                double x = c.W * Saw(seed + c.T * (0.035 + seed * 0.025));
                double y = c.H * (0.16 + seed * 0.68);
                double radius = c.H * (0.08 + seed * 0.11);
                var color = Lerp(c.Color, c.Color2, seed);
                c.Dc.DrawEllipse(Brush(color, 0.08), Pen(Lerp(color, Colors.White, 0.35), 1.2, 0.8), new Point(x, y), radius, radius);
                Dot(c.Dc, x - radius * 0.28, y - radius * 0.28, Math.Max(0.7, radius * 0.12), Colors.White, 0.78);
            }
        }

        private void RenderFractalMotion(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 18; i++)
            {
                double x = i / 18.0;
                double a = Math.Sin((x * 2.1 + c.T * 0.17) * Math.PI * 2);
                double b = Math.Sin((x * 5.2 - c.T * 0.29 + a * 0.19) * Math.PI * 2);
                double d = Math.Sin((x * 11.3 + c.T * 0.11 + b * 0.13) * Math.PI * 2);
                double ridge = Math.Pow(Math.Abs(a * b * d), 0.58);
                var color = Lerp(c.Color, c.Color2, Math.Clamp(0.5 + a * 0.2 + b * 0.17 + d * 0.11, 0, 1));
                color = Lerp(Scale(color, 0.25 + ridge * 0.75), Colors.White, Math.Pow(ridge, 7) * 0.12);
                stops.Add(new GradientStop(color, x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderNoiseMap(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 16; i++)
            {
                double x = i / 16.0;
                double n1 = PreviewNoise(x * 4.2 + c.T * 0.18, 9101);
                double n2 = PreviewNoise(x * 10.7 - c.T * 0.27, 9901);
                double field = Math.Clamp(n1 * 0.68 + n2 * 0.32, 0, 1);
                var color = Lerp(c.Color, c.Color2, field);
                stops.Add(new GradientStop(Scale(color, 0.22 + field * 0.78), x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderMovingPanes(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color, 0.15), 1, 3);
            double paneW = c.W * 0.34;
            double offset = -paneW + Saw(c.T * 0.16) * paneW;
            for (int pane = -1; pane < 5; pane++)
            {
                double x = offset + pane * paneW;
                var color = Lerp(c.Color, c.Color2, Saw(pane * 0.37 + c.T * 0.025));
                Rect(c.Dc, x + 1, 1, paneW - 2, c.H - 2, color, 0.52 + 0.25 * Sin01(pane + c.T * 0.35), 1.5);
                Rect(c.Dc, x, 0, Math.Max(1.2, c.W * 0.018), c.H, Colors.White, 0.26, 0.7);
            }
        }

        private void RenderSunrise(Ctx c)
        {
            double cycle = Sin01(c.T * 0.22);
            var dark = Scale(c.Color2, 0.12 + cycle * 0.18);
            var sky = Lerp(c.Color, c.Color2, 0.35 + cycle * 0.25);
            var stops = new GradientStopCollection
            {
                new(dark, 0),
                new(Scale(sky, 0.55), 0.42),
                new(Lerp(c.Color, Colors.White, 0.22), 1),
            };
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
            double sunX = c.W * (0.08 + cycle * 0.84);
            Dot(c.Dc, sunX, c.Cy, c.H * 0.34, c.Color, 0.18);
            Dot(c.Dc, sunX, c.Cy, c.H * 0.14, Lerp(c.Color, Colors.White, 0.48), 0.96);
        }

        private void RenderShimmer(Ctx c)
        {
            var bg = new LinearGradientBrush(Scale(c.Color, 0.55), Scale(c.Color2, 0.55), 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);
            for (int i = 0; i < 12; i++)
            {
                double seed = Rand(i + 10211);
                double x = c.W * seed;
                double y = c.H * Rand(i + 10501);
                double flash = Math.Pow(Sin01(c.T * (1.2 + seed * 2.1) + seed * 17), 5);
                Dot(c.Dc, x, y, 1.0 + flash * 3.8, Lerp(c.Color, Colors.White, 0.7), 0.12 + flash * 0.84);
            }
        }

        private void RenderSpotsFade(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color2, 0.12), 1, 3);
            for (int i = 0; i < 5; i++)
            {
                double phase = Saw(c.T * (0.18 + i * 0.016) + i * 0.23);
                double pulse = Math.Sin(phase * Math.PI);
                double x = c.W * (0.1 + i * 0.2 + Math.Sin(c.T * 0.25 + i) * 0.04);
                double radius = c.H * (0.05 + pulse * 0.24);
                var color = Lerp(c.Color, c.Color2, i / 4.0);
                Dot(c.Dc, x, c.Cy, radius * 1.6, color, pulse * 0.12);
                Dot(c.Dc, x, c.Cy, radius, color, pulse * 0.82);
            }
        }

        private void RenderStreamDual(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 18; i++)
            {
                double x = i / 18.0;
                double left = Math.Pow(Sin01((x * 2.4 - c.T * 0.55) * Math.PI * 2), 4);
                double right = Math.Pow(Sin01((x * 2.4 + c.T * 0.43 + 0.5) * Math.PI * 2), 4);
                var a = Scale(c.Color, 0.12 + left * 0.78);
                var b = Scale(c.Color2, right * 0.72);
                var color = Lerp(a, b, right / Math.Max(0.001, left + right));
                if (left * right > 0.4) color = Lerp(color, Colors.White, left * right * 0.24);
                stops.Add(new GradientStop(color, x));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private static double PreviewNoise(double position, int salt)
        {
            int cell = (int)Math.Floor(position);
            double f = position - cell;
            double eased = f * f * (3 - 2 * f);
            double a = Rand(cell * 131 + salt);
            double b = Rand((cell + 1) * 131 + salt);
            return a + (b - a) * eased;
        }
    }
}
