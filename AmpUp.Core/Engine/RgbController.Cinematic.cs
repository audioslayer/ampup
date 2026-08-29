using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

/// <summary>
/// Palette-driven spatial scenes curated from effect concepts common to the
/// public WLED and OpenRGB ecosystems. These are original 15-zone renderers;
/// none of them force a rainbow palette.
/// </summary>
public partial class RgbController
{
    private void GlobalBlackHole(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.020f, 0.072f);
        float center = 0.5f + MathF.Sin(t * 0.37f) * 0.13f;
        float ringRadius = 0.19f + MathF.Sin(t * 0.23f) * 0.035f;

        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float d = MathF.Abs(x - center);
            float ring = MathF.Exp(-MathF.Pow(d - ringRadius, 2f) * 520f);
            float outer = MathF.Exp(-MathF.Pow(d - ringRadius * 1.65f, 2f) * 75f) * 0.34f;
            float lens = MathF.Exp(-d * d * 42f) * Math.Clamp(d / 0.085f, 0f, 1f);
            float palette = PingPong(x * 0.86f + t * 0.055f + ring * 0.18f);
            SetCinematicLed(gl, i, palette, 0.055f + outer + ring * 0.88f + lens * 0.2f, ring * 0.24f);
        }
    }

    private void GlobalLavaLamp(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.012f, 0.042f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float field = 0f;
            float weightedPalette = 0f;
            for (int blob = 0; blob < 4; blob++)
            {
                float seed = PseudoRandom01(blob * 61 + 1709);
                float center = 0.5f + 0.43f * MathF.Sin(t * (0.19f + seed * 0.13f) + seed * 9f);
                float width = 0.055f + seed * 0.075f;
                float amount = MathF.Exp(-(x - center) * (x - center) / (2f * width * width));
                field += amount;
                weightedPalette += amount * (0.1f + blob * 0.27f);
            }

            float blobLevel = Math.Clamp(field, 0f, 1.2f);
            float palette = field > 0.01f ? Frac(weightedPalette / field) : PingPong(x + t * 0.02f);
            float glow = Math.Clamp(0.12f + blobLevel * 0.82f, 0f, 1f);
            SetCinematicLed(gl, i, palette, glow, MathF.Pow(blobLevel, 3f) * 0.12f);
        }
    }

    private void GlobalBubbles(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.018f, 0.060f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float rr = 0f, gg = 0f, bb = 0f;
            var (baseR, baseG, baseB) = GetGradientColor(gl, PingPong(x * 0.7f + t * 0.018f));
            rr = baseR * 0.13f; gg = baseG * 0.13f; bb = baseB * 0.13f;

            for (int bubble = 0; bubble < 5; bubble++)
            {
                float seed = PseudoRandom01(bubble * 97 + 2203);
                float center = Frac(seed + t * (0.055f + seed * 0.035f));
                float radius = 0.045f + seed * 0.055f;
                float d = WrapDist(x, center);
                float rim = MathF.Exp(-MathF.Pow(d - radius, 2f) * 1500f);
                float fill = MathF.Exp(-d * d * 115f) * 0.22f;
                var (cr, cg, cb) = GetGradientColor(gl, Frac(seed + bubble * 0.19f));
                rr += cr * (rim * 0.78f + fill);
                gg += cg * (rim * 0.78f + fill);
                bb += cb * (rim * 0.78f + fill);
                float white = MathF.Pow(rim, 5f) * 105f;
                rr += white; gg += white; bb += white;
            }

            SetGlobalLed(i, Math.Clamp((int)rr, 0, 255), Math.Clamp((int)gg, 0, 255), Math.Clamp((int)bb, 0, 255));
        }
    }

    private void GlobalFractalMotion(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.016f, 0.058f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float a = MathF.Sin((x * 2.1f + t * 0.13f) * MathF.PI * 2f);
            float b = MathF.Sin((x * 5.2f - t * 0.21f + a * 0.19f) * MathF.PI * 2f);
            float c = MathF.Sin((x * 11.3f + t * 0.08f + b * 0.13f) * MathF.PI * 2f);
            float ridge = MathF.Pow(MathF.Abs(a * b * c), 0.58f);
            float palette = Math.Clamp(0.5f + a * 0.2f + b * 0.17f + c * 0.11f, 0f, 1f);
            SetCinematicLed(gl, i, palette, 0.18f + ridge * 0.82f, MathF.Pow(ridge, 6f) * 0.17f);
        }
    }

    private void GlobalNoiseMap(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.012f, 0.046f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float coarse = CinematicNoise(x * 4.2f + t * 0.10f, 3109);
            float detail = CinematicNoise(x * 10.7f - t * 0.16f, 3917);
            float field = Smooth(coarse * 0.68f + detail * 0.32f);
            float palette = Math.Clamp(coarse * 0.72f + detail * 0.28f, 0f, 1f);
            SetCinematicLed(gl, i, palette, 0.2f + field * 0.76f, MathF.Pow(field, 7f) * 0.1f);
        }
    }

    private void GlobalMovingPanes(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.020f, 0.070f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float moving = Frac(x * 3.25f - t * 0.16f);
            int pane = (int)MathF.Floor(x * 3.25f - t * 0.16f);
            float edge = MathF.Exp(-MathF.Min(moving, 1f - moving) * 22f);
            float shade = 0.44f + 0.28f * MathF.Sin((pane * 1.73f + t * 0.11f) * MathF.PI * 2f);
            float palette = Frac(pane * 0.237f + t * 0.025f);
            SetCinematicLed(gl, i, palette, Math.Clamp(shade + edge * 0.46f, 0f, 1f), edge * 0.16f);
        }
    }

    private void GlobalSunrise(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.006f, 0.018f);
        float cycle = PingPong(t * 0.045f);
        float horizon = 0.02f + cycle * 1.08f;
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float daylight = 1f / (1f + MathF.Exp((x - horizon) * 24f));
            float rim = MathF.Exp(-(x - horizon) * (x - horizon) * 380f);
            float palette = Math.Clamp(0.05f + daylight * 0.78f + x * 0.14f, 0f, 1f);
            float brightness = 0.10f + daylight * 0.76f + rim * 0.28f;
            SetCinematicLed(gl, i, palette, brightness, rim * 0.32f);
        }
    }

    private void GlobalShimmer(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.025f, 0.090f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float grain = CinematicNoise(i * 1.73f + t * 1.8f, 4517);
            float wave = Wave(x * 2.8f - t * 0.18f);
            float flash = MathF.Pow(Math.Clamp(grain * 0.68f + wave * 0.32f, 0f, 1f), 7f);
            SetCinematicLed(gl, i, PingPong(x + t * 0.025f), 0.34f + wave * 0.38f + flash * 0.28f, flash * 0.48f);
        }
    }

    private void GlobalSpotsFade(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.018f, 0.065f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float field = 0f;
            float palette = 0f;
            for (int spot = 0; spot < 4; spot++)
            {
                float phase = Frac(t * (0.08f + spot * 0.011f) + spot * 0.27f);
                float center = Frac(spot * 0.29f + MathF.Sin(t * 0.12f + spot * 2.1f) * 0.08f);
                float radius = 0.025f + Wave(phase) * 0.13f;
                float d = WrapDist(x, center);
                float spotValue = MathF.Exp(-d * d / MathF.Max(0.001f, radius * radius)) * Wave(phase);
                if (spotValue > field) { field = spotValue; palette = spot / 3f; }
            }
            SetCinematicLed(gl, i, palette, 0.12f + field * 0.88f, MathF.Pow(field, 5f) * 0.18f);
        }
    }

    private void GlobalStreamDual(GlobalLightConfig gl)
    {
        float t = CinematicTime(gl, 0.022f, 0.078f);
        for (int i = 0; i < 15; i++)
        {
            float x = i / 14f;
            float left = MathF.Pow(Wave(x * 2.4f - t * 0.24f), 4f);
            float right = MathF.Pow(Wave(x * 2.4f + t * 0.19f + 0.5f), 4f);
            var (r1, g1, b1) = GetGradientColor(gl, PingPong(x + t * 0.03f));
            var (r2, g2, b2) = GetGradientColor(gl, PingPong(1f - x + t * 0.025f));
            float floor = 0.12f;
            float white = MathF.Pow(MathF.Min(left, right), 3f) * 0.18f;
            SetGlobalLed(i,
                Math.Clamp((int)(r1 * (floor + left * 0.76f) + r2 * right * 0.64f + 255f * white), 0, 255),
                Math.Clamp((int)(g1 * (floor + left * 0.76f) + g2 * right * 0.64f + 255f * white), 0, 255),
                Math.Clamp((int)(b1 * (floor + left * 0.76f) + b2 * right * 0.64f + 255f * white), 0, 255));
        }
    }

    private float CinematicTime(GlobalLightConfig gl, float slow, float fast)
    {
        float speed = Math.Clamp(gl.EffectSpeed, 1, 100) / 100f;
        return _animTick * (slow + speed * (fast - slow));
    }

    private void SetCinematicLed(GlobalLightConfig gl, int index, float palette, float brightness, float white = 0f)
    {
        var (r, g, b) = GetGradientColor(gl, Math.Clamp(palette, 0f, 1f));
        brightness = Math.Clamp(brightness, 0f, 1f);
        white = Math.Clamp(white, 0f, 1f);
        SetGlobalLed(index,
            Math.Clamp((int)(r * brightness + (255 - r * brightness) * white), 0, 255),
            Math.Clamp((int)(g * brightness + (255 - g * brightness) * white), 0, 255),
            Math.Clamp((int)(b * brightness + (255 - b * brightness) * white), 0, 255));
    }

    private static float CinematicNoise(float position, int salt)
    {
        int cell = (int)MathF.Floor(position);
        float f = position - cell;
        float eased = f * f * (3f - 2f * f);
        float a = PseudoRandom01(cell * 131 + salt);
        float b = PseudoRandom01((cell + 1) * 131 + salt);
        return a + (b - a) * eased;
    }
}
