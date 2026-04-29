using System;
using System.Windows;
using System.Windows.Media;

namespace AmpUp.Controls
{
    public partial class EffectPreviewControl
    {
        private void RenderCollision(Ctx c)
        {
            double phase = Saw(c.T * 0.5);
            double pos1 = phase * c.W * 0.5;
            double pos2 = c.W - phase * c.W * 0.5;
            double r = Math.Min(c.W * 0.06, c.H * 0.28);
            double flash = Math.Pow(Math.Max(0, phase - 0.85) / 0.15, 2);

            Dot(c.Dc, pos1, c.Cy, r, c.Color, 0.95);
            Dot(c.Dc, pos1 - r * 2, c.Cy, r * 0.5, c.Color, 0.4);
            Dot(c.Dc, pos2, c.Cy, r, c.Color2, 0.95);
            Dot(c.Dc, pos2 + r * 2, c.Cy, r * 0.5, c.Color2, 0.4);

            if (flash > 0.01)
            {
                double fr = r * (1 + flash * 3);
                Dot(c.Dc, c.Cx, c.Cy, fr, Colors.White, flash * 0.9);
                Dot(c.Dc, c.Cx, c.Cy, fr * 0.5, Colors.White, flash);
            }
        }

        private void RenderDNA(Ctx c)
        {
            int N = 10;
            double sp = c.W / (N + 1);
            double r = Math.Min(sp * 0.35, c.H * 0.18);
            var sg1 = new StreamGeometry();
            var sg2 = new StreamGeometry();

            using (var ctx1 = sg1.Open())
            {
                for (int i = 0; i <= N; i++)
                {
                    double x = sp * (i + 0.5);
                    double off = Math.Sin(c.T * 2.5 + i * 0.6) * c.H * 0.3;
                    if (i == 0) ctx1.BeginFigure(new Point(x, c.Cy + off), false, false);
                    else ctx1.LineTo(new Point(x, c.Cy + off), true, true);
                }
            }

            using (var ctx2 = sg2.Open())
            {
                for (int i = 0; i <= N; i++)
                {
                    double x = sp * (i + 0.5);
                    double off = Math.Sin(c.T * 2.5 + i * 0.6) * c.H * 0.3;
                    if (i == 0) ctx2.BeginFigure(new Point(x, c.Cy - off), false, false);
                    else ctx2.LineTo(new Point(x, c.Cy - off), true, true);
                }
            }

            c.Dc.DrawGeometry(null, Pen(c.Color, 1.5, 0.6), sg1);
            c.Dc.DrawGeometry(null, Pen(c.Color2, 1.5, 0.6), sg2);

            for (int i = 0; i < N; i += 2)
            {
                double x = sp * (i + 0.5);
                double off = Math.Sin(c.T * 2.5 + i * 0.6) * c.H * 0.3;
                Dot(c.Dc, x, c.Cy + off, r, c.Color, 0.95);
                Dot(c.Dc, x, c.Cy - off, r, c.Color2, 0.95);
            }
        }

        private void RenderRainfall(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.05, 3);

            double[] xPcts = { 0.15, 0.38, 0.62, 0.85 };
            double[] speeds = { 1.0, 0.8, 1.1, 0.9 };
            double[] delays = { 0.0, 0.3, 0.6, 0.15 };

            for (int i = 0; i < 4; i++)
            {
                double x = c.W * xPcts[i];
                double phase = Saw(c.T * speeds[i] + delays[i]);
                double y = phase * (c.H + 8) - 4;
                double dropW = 2;
                double dropH = 6;
                double a = 1.0 - phase * 0.5;

                Rect(c.Dc, x - dropW / 2, y, dropW, dropH, c.Color, a, 1);

                if (phase > 0.85)
                {
                    double splash = (phase - 0.85) / 0.15;
                    Dot(c.Dc, x, c.H - 2, 3 * splash, c.Color, (1 - splash) * 0.7);
                }
            }
        }

        private void RenderPoliceLights(Ctx c)
        {
            bool leftOn = ((int)(c.T * 6)) % 2 == 0;
            Rect(c.Dc, 0, 0, c.W / 2, c.H, c.Color, leftOn ? 0.9 : 0.12, 3);
            Rect(c.Dc, c.W / 2, 0, c.W / 2, c.H, c.Color2, leftOn ? 0.12 : 0.9, 3);
        }

        private void RenderAurora(Ctx c)
        {
            double drift = c.T * 0.15;
            double s0 = (Math.Sin(drift) * 0.5 + 0.5) * 0.3;
            double s1 = (Math.Sin(drift + 1.5) * 0.5 + 0.5) * 0.3 + 0.35;
            double s2 = (Math.Sin(drift + 3.0) * 0.5 + 0.5) * 0.3 + 0.7;

            var stops = new GradientStopCollection
            {
                new GradientStop(Hsv(0.45, 0.7, 0.6), Math.Clamp(s0, 0, 1)),
                new GradientStop(Hsv(0.38, 0.8, 0.85), Math.Clamp(s1, 0, 1)),
                new GradientStop(Hsv(0.3, 0.75, 0.7), Math.Clamp(s2, 0, 1)),
                new GradientStop(Hsv(0.52, 0.6, 0.5), 0.0),
                new GradientStop(Hsv(0.35, 0.65, 0.6), 1.0),
            };
            var gb = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderMatrix(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Color.FromRgb(0, 15, 0), 0.7, 3);

            var green = Color.FromRgb(0, 0xE6, 0x76);
            double[] xPcts = { 0.1, 0.3, 0.55, 0.78 };
            double[] speeds = { 0.7, 0.5, 0.6, 0.45 };
            double[] delays = { 0, 0.5, 0.2, 0.8 };
            int[] lengths = { 3, 3, 2, 3 };

            for (int col = 0; col < 4; col++)
            {
                double x = c.W * xPcts[col];
                double headY = Saw(c.T * speeds[col] + delays[col]) * (c.H + 20) - 5;

                for (int seg = 0; seg < lengths[col]; seg++)
                {
                    double y = headY - seg * 5;
                    if (y < -3 || y > c.H + 3) continue;
                    double a = seg == 0 ? 1.0 : Math.Max(0.15, 1.0 - seg * 0.35);
                    var col2 = seg == 0 ? Lerp(green, Colors.White, 0.4) : green;
                    double r = seg == 0 ? 2.0 : 1.5;
                    Rect(c.Dc, x - r, y, r * 2, r * 2, col2, a, 1);
                }
            }
        }

        private void RenderStarfield(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, Color.FromRgb(8, 6, 15), 0.5, 3);

            double[] xs = { 0.12, 0.45, 0.7, 0.3, 0.85, 0.55, 0.2, 0.65 };
            double[] ys = { 0.2, 0.65, 0.25, 0.78, 0.5, 0.12, 0.55, 0.82 };
            double[] rates = { 1.5, 2.1, 1.8, 2.5, 1.3, 2.0, 1.7, 2.3 };
            bool[] warm = { false, true, false, false, false, true, false, false };

            var lavender = Color.FromRgb(0xB3, 0x9D, 0xDB);
            var gold = Color.FromRgb(0xFF, 0xCA, 0x28);

            for (int i = 0; i < 8; i++)
            {
                double k = Math.Pow(Sin01(c.T * rates[i] + Rand(i + 50) * 10), 3);
                var col = warm[i] ? gold : Lerp(lavender, Colors.White, 0.2);
                double r = 1.0 + k * 1.2;
                Dot(c.Dc, c.W * xs[i], c.H * ys[i], r, col, 0.1 + k * 0.9);
            }
        }

        private void RenderEqualizer(Ctx c)
        {
            int bars = 5;
            double bw = c.W / (bars + 1);
            double gap = 2;
            for (int i = 0; i < bars; i++)
            {
                double e = Math.Pow(Sin01(c.T * (2 + i * 0.5) + i), 2);
                double bh = c.H * (0.15 + e * 0.8);
                double x = (c.W - bars * bw) / 2 + i * bw + gap / 2;
                double bwi = bw - gap;

                Rect(c.Dc, x, c.H - bh, bwi, bh, c.Color, 0.95, 1.5);

                double tipH = Math.Min(bh, c.H * 0.2);
                Rect(c.Dc, x, c.H - bh, bwi, tipH, Lerp(c.Color, c.Color2, 0.7), 0.85, 1.5);
            }
        }

        private void RenderWaterfall(Ctx c)
        {
            double scroll = c.T * 0.3;
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 6; i++)
            {
                double t = i / 6.0;
                double hue = ((scroll + t * 0.5) % 1.0 + 1.0) % 1.0;
                stops.Add(new GradientStop(Hsv(hue, 0.85, 0.9), t));
            }
            var gb = new LinearGradientBrush(stops, 90);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderLava(Ctx c)
        {
            var deep = Color.FromRgb(0xCC, 0x33, 0x00);
            var amber = Color.FromRgb(0xFF, 0xAA, 0x00);
            var stops = new GradientStopCollection
            {
                new GradientStop(deep, 0),
                new GradientStop(Lerp(deep, amber, 0.3), 0.35),
                new GradientStop(amber, 0.7),
                new GradientStop(deep, 1.0),
            };
            var bg = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, c.W, c.H), 3, 3);

            for (int k = 0; k < 3; k++)
            {
                double bx = c.W * (0.5 + 0.35 * Math.Sin(c.T * 0.4 + k * 2.1));
                double by = c.H * (0.5 + 0.25 * Math.Sin(c.T * 0.3 + k * 1.7));
                double br = 4 + 3 * Sin01(c.T * 0.5 + k);
                var bright = Color.FromRgb(0xFF, 0xDD, 0x44);
                Dot(c.Dc, bx, by, br, bright, 0.35 + 0.3 * Sin01(c.T * 0.6 + k * 0.8));
            }
        }

        private void RenderVuWave(Ctx c)
        {
            int bars = 5;
            double bw = c.W / (bars + 1);
            double gap = 2;

            for (int i = 0; i < bars; i++)
            {
                double dist = Math.Abs(i - (bars - 1) / 2.0) / ((bars - 1) / 2.0);
                double e = Math.Pow(Sin01(c.T * 2 - dist * 1.5), 2);
                double bh = c.H * (0.1 + e * 0.85);
                double x = (c.W - bars * bw) / 2 + i * bw + gap / 2;
                double bwi = bw - gap;
                double halfH = bh / 2;

                Rect(c.Dc, x, c.Cy - halfH, bwi, bh, c.Color, 0.9, 1.5);
            }
        }

        private void RenderNebulaDrift(Ctx c)
        {
            double drift = c.T * 0.1;
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 8; i++)
            {
                double t = i / 8.0;
                double hue = ((drift + t) % 1.0 + 1.0) % 1.0;
                stops.Add(new GradientStop(Hsv(hue, 0.8, 0.9), t));
            }
            var gb = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        // ── New room sweep effects ──

        private void RenderVortex(Ctx c)
        {
            // Swirling spiral lines converging to center
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.08, 3);
            int arms = 3;
            for (int a = 0; a < arms; a++)
            {
                double baseAngle = c.T * 1.5 + a * (Math.PI * 2 / arms);
                var sg = new StreamGeometry();
                using (var ctx = sg.Open())
                {
                    for (int s = 0; s <= 20; s++)
                    {
                        double t = s / 20.0;
                        double radius = (1.0 - t) * Math.Min(c.W, c.H) * 0.45;
                        double angle = baseAngle + t * Math.PI * 3; // 1.5 full rotations
                        double x = c.Cx + Math.Cos(angle) * radius;
                        double y = c.Cy + Math.Sin(angle) * radius;
                        if (s == 0) ctx.BeginFigure(new Point(x, y), false, false);
                        else ctx.LineTo(new Point(x, y), true, true);
                    }
                }
                double alpha = 0.5 + 0.3 * Math.Sin(c.T * 2 + a);
                c.Dc.DrawGeometry(null, Pen(Lerp(c.Color, c.Color2, (double)a / arms), 1.8, alpha), sg);
            }
            // Bright center
            Dot(c.Dc, c.Cx, c.Cy, 3, Colors.White, 0.5 + 0.3 * Sin01(c.T * 2));
        }

        private void RenderShockwave(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.06, 3);
            double phase = Saw(c.T * 0.4);
            double maxR = Math.Sqrt(c.Cx * c.Cx + c.Cy * c.Cy);
            double ringR = phase * maxR;
            double ringWidth = 3.0;

            // Ring
            if (phase < 0.85)
            {
                double alpha = 1.0 - phase * 0.8;
                c.Dc.DrawEllipse(null, Pen(c.Color, ringWidth, alpha),
                    new Point(c.Cx, c.Cy), ringR, ringR);
                // Inner glow trail
                if (ringR > ringWidth * 2)
                {
                    c.Dc.DrawEllipse(null, Pen(Lerp(c.Color, c.Color2, 0.5), 1.5, alpha * 0.4),
                        new Point(c.Cx, c.Cy), ringR * 0.7, ringR * 0.7);
                }
            }
            // Center flash at start of pulse
            double flash = Math.Max(0, 1.0 - phase * 5);
            if (flash > 0.01)
                Dot(c.Dc, c.Cx, c.Cy, 4 * flash, Colors.White, flash * 0.9);
        }

        private void RenderTidal(Ctx c)
        {
            // Rising/falling water fill with foam crest
            double tide = Sin01(c.T * 0.35);
            double waterY = c.H * (1.0 - tide * 0.85);

            // Water body
            var waterStops = new GradientStopCollection
            {
                new GradientStop(Lerp(c.Color, Colors.Black, 0.3), 0),
                new GradientStop(c.Color, 0.6),
                new GradientStop(Lerp(c.Color, c.Color2, 0.3), 1.0),
            };
            var waterBrush = new LinearGradientBrush(waterStops, 90);
            c.Dc.DrawRoundedRectangle(waterBrush, null,
                new Rect(0, waterY, c.W, c.H - waterY), 0, 0);

            // Foam crest — bright line at water surface
            double foamAlpha = 0.6 + 0.3 * Math.Sin(c.T * 8);
            var foam = Lerp(c.Color2, Colors.White, 0.5);
            c.Dc.DrawLine(Pen(foam, 2.0, foamAlpha),
                new Point(0, waterY), new Point(c.W, waterY));

            // Ripple lines
            double ripple = Math.Sin(c.T * 4) * 2;
            c.Dc.DrawLine(Pen(foam, 1.0, 0.2),
                new Point(0, waterY + 4 + ripple), new Point(c.W, waterY + 4 + ripple));
        }

        private void RenderPrism(Ctx c)
        {
            // Rainbow bands spreading apart then merging to white
            double spread = Sin01(c.T * 0.3);
            double offset = c.T * 0.2;
            int bands = 7;
            double bandW = c.W / bands;

            for (int i = 0; i < bands; i++)
            {
                double hue = ((double)i / bands + offset) % 1.0;
                var col = Hsv(hue, 1.0, 1.0);
                // Blend toward white when merged
                var final_ = Lerp(Colors.White, col, spread);
                double alpha = 0.6 + 0.35 * spread;
                Rect(c.Dc, i * bandW, 0, bandW + 1, c.H, final_, alpha, 0);
            }

            // White center flash when fully merged
            double mergeFlash = Math.Pow(1.0 - spread, 3);
            if (mergeFlash > 0.05)
                Rect(c.Dc, 0, 0, c.W, c.H, Colors.White, mergeFlash * 0.7, 3);
        }

        private void RenderEmberDrift(Ctx c)
        {
            // Dark background with floating warm dots
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.08, 3);

            double[] xs = { 0.15, 0.4, 0.65, 0.85, 0.3, 0.7 };
            double[] ys = { 0.3, 0.7, 0.4, 0.6, 0.5, 0.25 };
            double[] speeds = { 0.4, 0.3, 0.5, 0.35, 0.45, 0.55 };

            for (int i = 0; i < 6; i++)
            {
                double x = c.W * (xs[i] + 0.15 * Math.Sin(c.T * speeds[i] + i * 1.5));
                double y = c.H * (ys[i] + 0.12 * Math.Sin(c.T * speeds[i] * 0.7 + i * 2.3));
                double glow = 0.3 + 0.7 * Math.Pow(Sin01(c.T * speeds[i] * 1.5 + i), 2);
                double r = 2.0 + glow * 2.5;
                var col = Lerp(c.Color, c.Color2, (double)i / 6);
                Dot(c.Dc, x, y, r, col, glow);
                // Soft outer glow
                Dot(c.Dc, x, y, r * 2, col, glow * 0.15);
            }
        }

        private void RenderGlitch(Ctx c)
        {
            // Dark base with random bright bursts
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.12, 3);

            // Pseudo-random flicker segments
            int segments = 6;
            double segW = c.W / segments;
            for (int i = 0; i < segments; i++)
            {
                // Deterministic "random" per segment per time window
                double seed = Rand(i + (int)(c.T * 3) * 7);
                double alpha = seed > 0.6 ? (seed - 0.6) / 0.4 : 0;
                alpha *= 0.9;
                if (alpha < 0.05) continue;
                var col = seed > 0.85 ? Colors.White : Lerp(c.Color, c.Color2, seed);
                Rect(c.Dc, i * segW, 0, segW, c.H, col, alpha, 1);
            }

            // Scan line
            double scanPhase = Saw(c.T * 1.5);
            if (scanPhase < 0.1)
            {
                double scanY = scanPhase / 0.1 * c.H;
                c.Dc.DrawLine(Pen(Colors.White, 1.5, 0.5),
                    new Point(0, scanY), new Point(c.W, scanY));
            }
        }

        private void RenderOpalWave(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 6; i++)
            {
                double p = i / 6.0;
                double drift = p
                    + 0.18 * Math.Sin(c.T * 1.2 + p * 5.5)
                    + 0.08 * Math.Sin(c.T * 2.4 - p * 11.0);
                drift = ((drift % 1.0) + 1.0) % 1.0;
                var col = Lerp(Hsv(drift, 0.45, 1.0), Colors.White, 0.18);
                stops.Add(new GradientStop(col, p));
            }
            var gb = new LinearGradientBrush(stops, 0);
            c.Dc.DrawRoundedRectangle(gb, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderBloom(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.06, 3);
            double center = c.Cx + Math.Sin(c.T * 0.9) * c.W * 0.18;
            double phase = 0.5 + 0.5 * Math.Sin(c.T * 1.8);
            double radius = 4 + phase * Math.Min(c.W, c.H) * 0.42;
            for (int ring = 0; ring < 3; ring++)
            {
                double rr = radius * (0.48 + ring * 0.28);
                double a = Math.Max(0.08, 0.42 - ring * 0.1);
                var col = ring switch
                {
                    0 => c.Color2,
                    1 => Lerp(c.Color, c.Color2, 0.5),
                    _ => c.Color,
                };
                c.Dc.DrawEllipse(null, Pen(col, 2.0 - ring * 0.35, a),
                    new Point(center, c.Cy), rr, rr * 0.68);
            }
            Dot(c.Dc, center, c.Cy, 4.5, Colors.White, 0.35 + phase * 0.25);
        }

        private void RenderColorTwinkle(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.05, 3);
            int stars = 7;
            for (int i = 0; i < stars; i++)
            {
                double seed = i * 1.371;
                double phase = ((c.T * (0.45 + i * 0.06) + seed) % 1.0 + 1.0) % 1.0;
                double glow = Math.Sin(phase * Math.PI);
                glow *= glow;
                double x = c.W * (0.1 + (i / (double)(stars - 1)) * 0.8);
                double y = c.H * (0.25 + 0.5 * Math.Sin(seed * 2.7));
                var col = Lerp(c.Color, c.Color2, (Math.Sin(c.T * 0.6 + seed) * 0.5 + 0.5));
                Dot(c.Dc, x, y, 1.4 + glow * 2.4, col, 0.15 + glow * 0.85);
                Dot(c.Dc, x, y, 3.8 + glow * 2.0, col, glow * 0.12);
            }
        }

        private void RenderAuroraVeil(Ctx c)
        {
            RenderAurora(c);
            var veil = new LinearGradientBrush(Lerp(c.Color, Colors.White, 0.18), c.Color2, 0);
            veil.Opacity = 0.24;
            c.Dc.DrawRoundedRectangle(veil, null, new Rect(0, 0, c.W, c.H), 3, 3);
        }

        private void RenderSolarStorm(Ctx c)
        {
            RenderAurora(c);
            double arc = Saw(c.T * 0.55) * c.W;
            for (int i = 0; i < 5; i++)
            {
                double x = arc - i * c.W * 0.08;
                double d = Math.Abs(x - c.Cx) / c.W;
                double a = Math.Max(0, 0.85 - d * 1.8) * (1.0 - i * 0.13);
                Dot(c.Dc, x, c.Cy + Math.Sin(c.T * 2 + i) * c.H * 0.18, 3.8 - i * 0.35, c.Color, a);
            }
        }

        private void RenderStarlightCanopy(Ctx c)
        {
            RenderNebulaDrift(c);
            for (int i = 0; i < 9; i++)
            {
                double seed = Rand(i + 911);
                double tw = Math.Pow(Sin01(c.T * (0.8 + seed) + seed * 9.0), 8);
                double x = c.W * (0.08 + 0.84 * Rand(i + 31));
                double y = c.H * (0.18 + 0.64 * Rand(i + 73));
                Dot(c.Dc, x, y, 1.0 + tw * 2.0, Colors.White, 0.12 + tw * 0.88);
            }
        }

        private void RenderPlasmaBloom(Ctx c)
        {
            int bands = 8;
            double w = c.W / bands;
            for (int i = 0; i < bands; i++)
            {
                double x = i / (double)Math.Max(1, bands - 1);
                double plasma = (Sin01(x * 17.5 + c.T * 1.4) + Sin01(x * 43.0 - c.T * 0.9)) * 0.5;
                var col = Lerp(c.Color, c.Color2, plasma);
                Rect(c.Dc, i * w, 0, w + 1, c.H, col, 0.35 + plasma * 0.6, 0);
            }
            Dot(c.Dc, c.Cx + Math.Sin(c.T) * c.W * 0.2, c.Cy, c.H * 0.32, Colors.White, 0.14);
        }

        private void RenderRippleRoom(Ctx c)
        {
            Rect(c.Dc, 0, 0, c.W, c.H, c.Color, 0.06, 3);
            double center = c.Cx + Math.Sin(c.T * 0.7) * c.W * 0.18;
            for (int r = 0; r < 4; r++)
            {
                double phase = Saw(c.T * 0.45 + r * 0.22);
                double rr = phase * c.W * 0.58;
                double a = (1 - phase) * 0.45;
                c.Dc.DrawEllipse(null, Pen(Lerp(c.Color, c.Color2, r / 3.0), 1.5, a),
                    new Point(center, c.Cy), rr, rr * 0.44);
            }
        }

        private void RenderPrismDrift(Ctx c)
        {
            RenderPrism(c);
            double shimmerX = Saw(c.T * 0.28) * c.W;
            Rect(c.Dc, shimmerX - c.W * 0.08, 0, c.W * 0.16, c.H, Colors.White, 0.20, 2);
        }

        private void RenderNebulaRain(Ctx c)
        {
            RenderNebulaDrift(c);
            int drops = 6;
            for (int i = 0; i < drops; i++)
            {
                double phase = Saw(c.T * (0.55 + i * 0.05) + i * 0.17);
                double x = c.W * (0.08 + i / (double)(drops - 1) * 0.84);
                double y = phase * (c.H + 10) - 5;
                Rect(c.Dc, x - 1, y - 4, 2, 8, Lerp(c.Color, c.Color2, i / (double)drops), 0.72 * (1 - phase * 0.35), 1);
            }
        }

        private void RenderReactiveAurora(Ctx c)
        {
            RenderAurora(c);
            int bars = 5;
            double bw = c.W / (bars * 1.8);
            for (int i = 0; i < bars; i++)
            {
                double e = Math.Pow(Sin01(c.T * (2.0 + i * 0.35) + i * 0.9), 2.4);
                double h = c.H * (0.14 + e * 0.62);
                double x = c.W * (0.12 + i * 0.19);
                Rect(c.Dc, x, c.H - h, bw, h, Lerp(c.Color, Colors.White, e * 0.35), 0.55, 1.5);
            }
        }

        private void RenderLiquidGlass(Ctx c)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i <= 7; i++)
            {
                double p = i / 7.0;
                double caustic = Math.Pow(Sin01(p * 21.0 - c.T * 1.1) * 0.65 + Sin01(p * 61.0 + c.T * 0.7) * 0.35, 2.0);
                stops.Add(new GradientStop(Lerp(Lerp(c.Color, c.Color2, caustic), Colors.White, caustic * 0.22), p));
            }
            c.Dc.DrawRoundedRectangle(new LinearGradientBrush(stops, 0), null, new Rect(0, 0, c.W, c.H), 3, 3);
            c.Dc.DrawLine(Pen(Colors.White, 1.2, 0.25), new Point(0, c.H * 0.32 + Math.Sin(c.T) * 3), new Point(c.W, c.H * 0.62 + Math.Cos(c.T * 0.7) * 3));
        }

        private void RenderChromaLayerStack(Ctx c)
        {
            RenderColorWave(c);
            double scanner = Saw(c.T * 0.45);
            scanner = scanner < 0.5 ? scanner * 2 : 2 - scanner * 2;
            double x0 = scanner * c.W;
            for (int i = 0; i < 5; i++)
            {
                double d = Math.Abs((i / 4.0) * c.W - x0) / c.W;
                double a = Math.Exp(-d * d * 55) * 0.85;
                Rect(c.Dc, i * c.W / 5.0, 0, c.W / 5.0 + 1, c.H, Hsv(i / 5.0 + c.T * 0.08), a, 0);
            }
        }
    }
}
