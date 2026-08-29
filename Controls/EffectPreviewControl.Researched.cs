using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderColorClouds(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 10; i++)
            {
                double x = i / 10.0;
                double field = Math.Sin(x * 5.3 + c.T * 0.52) * 0.25
                    + Math.Sin(x * 13.7 - c.T * 0.31) * 0.16
                    + Math.Sin(x * 29.0 + c.T * 0.17) * 0.08;
                double k = Math.Clamp(0.5 + field, 0, 1);
                var color = Lerp(c.Color, c.Color2, k);
                if (k > 0.72) color = Lerp(color, Colors.White, (k - 0.72) * 0.42);
                stops.Add(new GradientStop(color, x));
            }
            var brush = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderFireflyGarden(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color, 0.2), 0.58, 3);
            for (int i = 0; i < 8; i++)
            {
                double seed = Rand(i + 331);
                double x = c.W * (((seed + c.T * (0.018 + seed * 0.025)) % 1.0 + 1.0) % 1.0);
                double y = c.H * (0.18 + 0.64 * Sin01(c.T * (0.11 + seed * 0.08) + seed * 7));
                double flicker = Math.Pow(Sin01(c.T * (1.1 + seed) + seed * 13), 3);
                var color = Lerp(c.Color, c.Color2, seed);
                Dot(c.Dc, x, y, 5.0 + flicker * 2.0, color, 0.07 + flicker * 0.11);
                Dot(c.Dc, x, y, 1.2 + flicker * 1.5, Lerp(color, Colors.White, 0.42), 0.3 + flicker * 0.7);
            }
        }

        private void RenderSparkler(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color2, 0.12), 0.5, 3);
            double center = c.Cx + Math.Sin(c.T * 0.5) * c.W * 0.1;
            for (int i = 0; i < 13; i++)
            {
                double seed = Rand(i + 701);
                double age = ((c.T * (0.55 + seed * 0.45) + seed * 8.0) % 1.0 + 1.0) % 1.0;
                double side = i % 2 == 0 ? -1 : 1;
                double x = center + side * age * c.W * (0.15 + seed * 0.42);
                double y = c.Cy - Math.Sin(age * Math.PI) * c.H * (0.12 + seed * 0.34);
                double alpha = Math.Pow(1.0 - age, 1.4);
                var color = Lerp(Colors.White, Lerp(c.Color, c.Color2, seed), age * 0.82);
                Dot(c.Dc, x, y, 0.8 + alpha * 1.8, color, alpha);
            }
            Dot(c.Dc, center, c.Cy, 3.2, Colors.White, 0.82);
            Dot(c.Dc, center, c.Cy, 7.5, c.Color, 0.16);
        }

        private void RenderDancingShadows(Ctx c)
        {
            var bg = new LinearGradientBrush(c.Color, c.Color2, 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);
            double[] rates = { 0.23, -0.17, 0.11 };
            double[] phases = { 0.0, 0.31, 0.68 };
            for (int i = 0; i < rates.Length; i++)
            {
                double x = c.W * (0.1 + 0.8 * (Math.Sin(c.T * rates[i] * Math.PI * 2 + phases[i] * 6) * 0.5 + 0.5));
                double radius = c.W * (0.10 + i * 0.018);
                Dot(c.Dc, x, c.Cy, radius * 1.25, Lerp(c.Color, c.Color2, i / 2.0), 0.20);
                Dot(c.Dc, x, c.Cy, radius, Colors.Black, 0.62);
            }
        }

        private void RenderNovaBurst(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color2, 0.12), 0.65, 3);
            double phase = Saw(c.T * 0.38);
            int cycle = (int)(c.T * 0.38);
            double center = c.W * (0.22 + Rand(cycle + 1201) * 0.56);
            double radius = phase * c.W * 0.58;
            double alpha = Math.Pow(1.0 - phase, 0.55);

            c.Dc.DrawEllipse(null, Pen(c.Color, 2.2, alpha), new Point(center, c.Cy), radius, radius * 0.34);
            if (phase > 0.16)
            {
                double r2 = (phase - 0.16) * c.W * 0.42;
                c.Dc.DrawEllipse(null, Pen(c.Color2, 1.4, alpha * 0.68), new Point(center, c.Cy), r2, r2 * 0.34);
            }
            double flash = Math.Clamp(1.0 - phase * 5.0, 0, 1);
            Dot(c.Dc, center, c.Cy, 3.0 + flash * 4.0, Colors.White, 0.25 + flash * 0.75);
        }

        private void RenderChromaticSpring(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Scale(c.Color, 0.15), 0.55, 3);
            double compression = 0.5 + 0.5 * Math.Sin(c.T * 0.9);
            int points = 30;
            var path = new StreamGeometry();
            using (var geometry = path.Open())
            {
                for (int i = 0; i <= points; i++)
                {
                    double x = c.W * i / points;
                    double coils = 2.5 + compression * 4.0;
                    double y = c.Cy + Math.Sin(i / (double)points * coils * Math.PI * 2 - c.T * 2.0)
                        * c.H * (0.12 + compression * 0.22);
                    if (i == 0) geometry.BeginFigure(new Point(x, y), false, false);
                    else geometry.LineTo(new Point(x, y), true, true);
                }
            }
            c.Dc.DrawGeometry(null, Pen(c.Color, 4.5, 0.18), path);
            c.Dc.DrawGeometry(null, Pen(Lerp(c.Color, c.Color2, compression), 1.8, 0.95), path);
            for (int i = 0; i < 6; i++)
            {
                double x = c.W * (i + 0.5) / 6.0;
                double y = c.Cy + Math.Sin((i + 0.5) / 6.0 * (2.5 + compression * 4.0) * Math.PI * 2 - c.T * 2.0)
                    * c.H * (0.12 + compression * 0.22);
                Dot(c.Dc, x, y, 1.6, Lerp(c.Color, c.Color2, i / 5.0), 0.9);
            }
        }
    }
}
