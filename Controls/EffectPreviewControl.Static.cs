using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderSingleColor(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.28;
            var glow = new RadialGradientBrush(
                Color.FromArgb(255, c.Color.R, c.Color.G, c.Color.B),
                Color.FromArgb(0, c.Color.R, c.Color.G, c.Color.B));
            c.Dc.DrawEllipse(glow, null, new Point(c.Cx, c.Cy), r * 1.8, r * 1.8);
            c.Dc.DrawEllipse(Brush(c.Color), null, new Point(c.Cx, c.Cy), r, r);
        }

        private void RenderColorBlend(Ctx c)
        {
            var gb = new LinearGradientBrush(c.Color, c.Color2, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderPositionFill(Ctx c)
        {
            double fillPct = Sin01(c.T * 1.2);
            double fillW = fillPct * c.W;
            if (fillW < 1) fillW = 1;
            Rect(c.Dc, 0, 0, c.W, c.H, Colors.Black, 0.3, 3);
            var bar = new LinearGradientBrush(
                c.Color,
                Color.FromArgb(128, c.Color.R, c.Color.G, c.Color.B), 0);
            c.Dc.DrawRoundedRectangle(bar, null, new Rect(0, 0, fillW, c.H), 2, 2);
        }

        private void RenderPositionBlend(Ctx c)
        {
            double fillPct = Sin01(c.T * 1.2);
            double fillW = fillPct * c.W;
            if (fillW < 1) fillW = 1;
            Rect(c.Dc, 0, 0, c.W, c.H, Colors.Black, 0.3, 3);
            var bar = new LinearGradientBrush(c.Color, c.Color2, 0);
            c.Dc.DrawRoundedRectangle(bar, null, new Rect(0, 0, fillW, c.H), 2, 2);
        }

        private void RenderPositionBlendMute(Ctx c)
        {
            double cycle = Saw(c.T / 4.0);
            bool muted = cycle > 0.5;
            if (muted)
            {
                double dimAlpha = 0.15 + 0.1 * Sin01(c.T * 2.0);
                Rect(c.Dc, 0, 0, c.W, c.H, c.Color2, dimAlpha, 3);
                double iconR = Math.Min(c.W, c.H) * 0.22;
                c.Dc.DrawEllipse(null, Pen(c.Color2, 1.5, 0.6), new Point(c.Cx, c.Cy), iconR, iconR);
            }
            else
            {
                double fillPct = Sin01(c.T * 1.2);
                double fillW = Math.Max(1, fillPct * c.W);
                Rect(c.Dc, 0, 0, c.W, c.H, Colors.Black, 0.3, 3);
                var bar = new LinearGradientBrush(c.Color, c.Color2, 0);
                c.Dc.DrawRoundedRectangle(bar, null, new Rect(0, 0, fillW, c.H), 2, 2);
            }
        }

        private void RenderCycleFill(Ctx c)
        {
            double fillPct = Sin01(c.T * 1.2);
            double fillW = Math.Max(1, fillPct * c.W);
            double tint = Sin01(c.T * 0.6);
            Color barColor = Lerp(c.Color, c.Color2, tint);
            Color barColor2 = Lerp(c.Color2, c.Color, tint);
            Rect(c.Dc, 0, 0, c.W, c.H, Colors.Black, 0.3, 3);
            var bar = new LinearGradientBrush(barColor, barColor2, 0);
            c.Dc.DrawRoundedRectangle(bar, null, new Rect(0, 0, fillW, c.H), 2, 2);
        }

        private void RenderRainbowFill(Ctx c)
        {
            double fillPct = Sin01(c.T * 1.2);
            double fillW = Math.Max(1, fillPct * c.W);
            double hueShift = Saw(c.T * 0.25);
            Rect(c.Dc, 0, 0, c.W, c.H, Colors.Black, 0.3, 3);
            var stops = new GradientStopCollection
            {
                new GradientStop(Hsv(hueShift), 0.0),
                new GradientStop(Hsv(hueShift + 0.2), 0.25),
                new GradientStop(Hsv(hueShift + 0.4), 0.5),
                new GradientStop(Hsv(hueShift + 0.6), 0.75),
                new GradientStop(Hsv(hueShift + 0.8), 1.0),
            };
            var rainbow = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(rainbow, null, new Rect(0, 0, fillW, c.H), 2, 2);
        }

        private void RenderGradientFill(Ctx c)
        {
            var gb = new LinearGradientBrush(c.Color, c.Color2, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }
    }
}
