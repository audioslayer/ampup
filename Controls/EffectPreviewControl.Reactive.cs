using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderMicStatus(Ctx c)
        {
            bool unmuted = ((int)(c.T * 0.5)) % 2 == 0;
            var col = unmuted ? c.Color : c.Color2;
            double k = unmuted ? 0.45 + 0.55 * Sin01(c.T * 3) : 0.35;
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);
        }

        private void RenderDeviceMute(Ctx c)
        {
            bool unmuted = ((int)(c.T * 0.5)) % 2 == 0;
            var col = unmuted ? c.Color : c.Color2;
            double k = unmuted ? 0.45 + 0.55 * Sin01(c.T * 3) : 0.35;
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);

            if (!unmuted)
            {
                double mx = sp * 2;
                var pen = Pen(c.Color2, 1.0, 0.9);
                c.Dc.DrawLine(pen, new Point(mx - r, c.Cy), new Point(mx + r, c.Cy));
            }
        }

        private void RenderAudioReactive(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double[] freqs = { 2.0, 3.5, 5.0 };
            double[] phases = { 0.0, 1.3, 2.7 };
            for (int i = 0; i < 3; i++)
            {
                double k = Math.Pow(Sin01(c.T * freqs[i] + phases[i]), 2);
                k = 0.2 + 0.8 * k;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, c.Color, k);
            }
        }

        private void RenderAudioPositionBlend(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double beatPhase = Saw(c.T / 1.2);
            double spike = Math.Pow(1.0 - beatPhase, 6);
            for (int i = 0; i < 3; i++)
            {
                double t = i / 2.0;
                var baseCol = Lerp(c.Color, c.Color2, t);
                var col = Lerp(baseCol, c.Color, spike);
                double k = 0.45 + 0.55 * spike;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);
            }
        }

        private void RenderProgramMute(Ctx c)
        {
            bool unmuted = ((int)(c.T * 0.5)) % 2 == 0;
            var col = unmuted ? c.Color : c.Color2;
            double k = unmuted ? 0.45 + 0.55 * Sin01(c.T * 3) : 0.35;
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            for (int i = 0; i < 3; i++)
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);

            if (!unmuted)
            {
                double mx = sp * 2;
                var pen = Pen(c.Color2, 1.0, 0.9);
                c.Dc.DrawLine(pen, new Point(mx - r, c.Cy), new Point(mx + r, c.Cy));
            }
        }

        private void RenderAppGroupMute(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            double cyc = Saw(c.T / 1.5);
            bool flickerA = cyc < 0.5;
            bool flickerB = Sin01(c.T * 4.0) > 0.5;
            for (int i = 0; i < 3; i++)
            {
                bool muted;
                if (i == 0) muted = false;
                else if (i == 1) muted = flickerA;
                else muted = flickerB;
                var col = muted ? c.Color2 : c.Color;
                double k = muted ? 0.35 : 0.45 + 0.45 * Sin01(c.T * 3 + i);
                Dot(c.Dc, sp * (i + 1), c.Cy, r, col, k);
            }
        }

        private void RenderDeviceSelect(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.22;
            double sp = c.W / 4.0;
            var mid = Lerp(c.Color, c.Color2, 0.5);
            Color[] cols = { c.Color, mid, c.Color2 };
            int sel = ((int)(c.T * 0.5)) % 3;
            for (int i = 0; i < 3; i++)
            {
                double k = (i == sel) ? 0.55 + 0.45 * Sin01(c.T * 4) : 0.3;
                Dot(c.Dc, sp * (i + 1), c.Cy, r, cols[i], k);
            }
        }
    }
}
