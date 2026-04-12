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
            double r = Math.Min(c.W, c.H) * 0.32;

            if (unmuted)
            {
                double ringPulse = Sin01(c.T * 3);
                double ringR = r + 4 + ringPulse * 4;
                Dot(c.Dc, c.Cx, c.Cy, ringR, c.Color, 0.08 + 0.12 * ringPulse);
            }

            double k = unmuted ? 0.7 + 0.3 * Sin01(c.T * 3) : 0.4;
            Dot(c.Dc, c.Cx, c.Cy, r, col, k);
        }

        private void RenderDeviceMute(Ctx c)
        {
            bool unmuted = ((int)(c.T * 0.5)) % 2 == 0;
            var col = unmuted ? c.Color : c.Color2;
            double r = Math.Min(c.W, c.H) * 0.32;
            double k = unmuted ? 0.7 + 0.3 * Sin01(c.T * 3) : 0.4;
            Dot(c.Dc, c.Cx, c.Cy, r, col, k);

            if (!unmuted)
            {
                double d = r * 0.85;
                var pen = Pen(c.Color2, 2.0, 0.85);
                c.Dc.DrawLine(pen, new Point(c.Cx - d, c.Cy - d), new Point(c.Cx + d, c.Cy + d));
            }
        }

        private void RenderAudioReactive(Ctx c)
        {
            int bars = 5;
            double barW = c.W / (bars + 1);
            double gap = barW * 0.2;
            for (int i = 0; i < bars; i++)
            {
                double energy = Math.Pow(Sin01(c.T * (2 + i * 0.7) + i), 2);
                double barH = c.H * (0.2 + energy * 0.75);
                double x = (c.W - bars * barW) / 2 + i * barW + gap / 2;
                Rect(c.Dc, x, c.H - barH, barW - gap, barH, c.Color, 0.9, 1.5);
            }
        }

        private void RenderAudioPositionBlend(Ctx c)
        {
            double beatPhase = Saw(c.T / 1.2);
            double spike = Math.Pow(1.0 - beatPhase, 6);
            double bright = 0.4 + 0.6 * spike;

            int cols = 12;
            double sliceW = c.W / cols;
            for (int i = 0; i < cols; i++)
            {
                double t = i / (double)(cols - 1);
                var col = Lerp(c.Color, c.Color2, t);
                col = Scale(col, bright);
                Rect(c.Dc, i * sliceW, 0, sliceW + 1, c.H, col, 0.85, 0);
            }
        }

        private void RenderProgramMute(Ctx c)
        {
            bool unmuted = ((int)(c.T * 0.5)) % 2 == 0;
            var col = unmuted ? c.Color : c.Color2;
            double r = Math.Min(c.W, c.H) * 0.3;
            double k = unmuted ? 0.7 + 0.3 * Sin01(c.T * 2.5) : 0.3;

            Dot(c.Dc, c.Cx, c.Cy, r + 2, col, k * 0.3);
            Dot(c.Dc, c.Cx, c.Cy, r, col, k);

            if (!unmuted)
            {
                double d = r * 0.85;
                var pen = Pen(c.Color2, 2.0, 0.8);
                c.Dc.DrawLine(pen, new Point(c.Cx - d, c.Cy - d), new Point(c.Cx + d, c.Cy + d));
            }
        }

        private void RenderAppGroupMute(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.18;
            double sp = c.W / 4.0;
            double cyc = Saw(c.T / 2.0);
            int pattern = (int)(cyc * 4);
            bool[] muted = pattern switch
            {
                0 => new[] { false, false, true },
                1 => new[] { false, true, true },
                2 => new[] { true, false, false },
                _ => new[] { false, true, false },
            };
            for (int i = 0; i < 3; i++)
            {
                var col = muted[i] ? c.Color2 : c.Color;
                double k = muted[i] ? 0.25 : 0.6 + 0.4 * Sin01(c.T * 3 + i * 1.2);
                double dotR = muted[i] ? r * 0.8 : r;
                Dot(c.Dc, sp * (i + 1), c.Cy, dotR, col, k);
            }
        }

        private void RenderDeviceSelect(Ctx c)
        {
            double r = Math.Min(c.W, c.H) * 0.2;
            double sp = c.W / 4.0;
            var mid = Lerp(c.Color, c.Color2, 0.5);
            Color[] cols = { c.Color, mid, c.Color2 };
            int sel = ((int)(c.T * 0.4)) % 3;
            for (int i = 0; i < 3; i++)
            {
                bool active = i == sel;
                double pulse = active ? 0.7 + 0.3 * Sin01(c.T * 5) : 0.25;
                double dotR = active ? r * 1.3 : r;
                if (active)
                    Dot(c.Dc, sp * (i + 1), c.Cy, dotR + 3, cols[i], 0.15);
                Dot(c.Dc, sp * (i + 1), c.Cy, dotR, cols[i], pulse);
            }
        }
    }
}
