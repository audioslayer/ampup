using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private const int StaticDotCount = 3;

        private static void StaticDotLayout(Ctx c, out double r, out double y, out double x0, out double step)
        {
            r = Math.Min(c.W, c.H) * 0.22;
            y = c.Cy;
            step = c.W / (StaticDotCount + 1.0);
            x0 = step;
        }

        private void RenderSingleColor(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            for (int i = 0; i < StaticDotCount; i++)
            {
                Dot(c.Dc, x0 + step * i, y, r, c.Color);
            }
        }

        private void RenderColorBlend(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double t = i / (double)(StaticDotCount - 1);
                Dot(c.Dc, x0 + step * i, y, r, Lerp(c.Color, c.Color2, t));
            }
        }

        private void RenderPositionFill(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            double pos = Sin01(c.T * 1.2);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double threshold = (i + 1) / (double)StaticDotCount;
                double fade = Math.Clamp((pos - (threshold - 1.0 / StaticDotCount)) * StaticDotCount, 0.0, 1.0);
                double alpha = 0.18 + 0.82 * fade;
                Dot(c.Dc, x0 + step * i, y, r, c.Color, alpha);
            }
        }

        private void RenderPositionBlend(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            double pos = Sin01(c.T * 1.2);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double t = i / (double)(StaticDotCount - 1);
                Color blended = Lerp(c.Color, c.Color2, t);
                double threshold = (i + 1) / (double)StaticDotCount;
                double fade = Math.Clamp((pos - (threshold - 1.0 / StaticDotCount)) * StaticDotCount, 0.0, 1.0);
                double alpha = 0.18 + 0.82 * fade;
                Dot(c.Dc, x0 + step * i, y, r, blended, alpha);
            }
        }

        private void RenderPositionBlendMute(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            double cycle = Saw(c.T / 4.0);
            bool muted = cycle > 0.5;
            double mutedFade = Math.Clamp(Math.Abs(cycle - 0.5) * 6.0, 0.0, 1.0);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double t = i / (double)(StaticDotCount - 1);
                if (muted)
                {
                    Dot(c.Dc, x0 + step * i, y, r, c.Color2, 0.25 + 0.15 * mutedFade);
                }
                else
                {
                    Color blended = Lerp(c.Color, c.Color2, t);
                    Dot(c.Dc, x0 + step * i, y, r, blended);
                }
            }
        }

        private void RenderCycleFill(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            double pos = Sin01(c.T * 1.2);
            double tint = Sin01(c.T * 0.6);
            Color tinted = Lerp(c.Color, c.Color2, tint);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double threshold = (i + 1) / (double)StaticDotCount;
                double fade = Math.Clamp((pos - (threshold - 1.0 / StaticDotCount)) * StaticDotCount, 0.0, 1.0);
                double alpha = 0.18 + 0.82 * fade;
                Dot(c.Dc, x0 + step * i, y, r, tinted, alpha);
            }
        }

        private void RenderRainbowFill(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            double pos = Sin01(c.T * 1.2);
            double hueBase = Saw(c.T * 0.25);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double hue = hueBase + i / (double)StaticDotCount;
                Color rainbow = Hsv(hue);
                double threshold = (i + 1) / (double)StaticDotCount;
                double fade = Math.Clamp((pos - (threshold - 1.0 / StaticDotCount)) * StaticDotCount, 0.0, 1.0);
                double alpha = 0.18 + 0.82 * fade;
                Dot(c.Dc, x0 + step * i, y, r, rainbow, alpha);
            }
        }

        private void RenderGradientFill(Ctx c)
        {
            StaticDotLayout(c, out double r, out double y, out double x0, out double step);
            for (int i = 0; i < StaticDotCount; i++)
            {
                double t = i / (double)(StaticDotCount - 1);
                Dot(c.Dc, x0 + step * i, y, r, Lerp(c.Color, c.Color2, t));
            }
        }
    }
}
