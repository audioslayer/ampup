using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

/// <summary>
/// Pure math for the knob-to-volume pipeline:
/// raw ADC value (0-1023) → normalize → response curve → volume range → final 0-1 volume.
/// </summary>
public static class VolumePipeline
{
    /// <summary>
    /// Apply response curve to a 0-1 normalized value.
    /// </summary>
    public static float ApplyCurve(float raw, ResponseCurve curve)
    {
        return curve switch
        {
            ResponseCurve.Logarithmic => (float)(Math.Log10(1.0 + raw * 9.0) / Math.Log10(10.0)),
            ResponseCurve.Exponential => raw * raw,
            ResponseCurve.Exponential2 => raw * raw * raw, // x³ — steeper curve for fine low-volume control
            _ => raw // Linear
        };
    }

    /// <summary>
    /// Remap a 0-1 curved value into the MinVolume..MaxVolume range (both 0-100), returning 0-1.
    /// </summary>
    public static float ApplyVolumeRange(float curved, int minVolume, int maxVolume)
    {
        float min = Math.Clamp(minVolume, 0, 100) / 100f;
        float max = Math.Clamp(maxVolume, 0, 100) / 100f;
        if (max <= min) max = min + 0.01f; // safety
        return min + curved * (max - min);
    }

    /// <summary>
    /// Full pipeline: raw 0-1023 → 0-1 → curve → range clamp → final 0-1 volume.
    /// </summary>
    public static float ComputeVolume(int rawValue, int minVolume, int maxVolume, ResponseCurve curve)
    {
        float raw = Math.Clamp(rawValue / 1023f, 0f, 1f);
        float curved = ApplyCurve(raw, curve);
        float vol = ApplyVolumeRange(curved, minVolume, maxVolume);
        return Math.Clamp(vol, 0f, 1f);
    }

    /// <summary>
    /// Convenience overload that reads knob config properties directly.
    /// </summary>
    public static float ComputeVolume(int rawValue, KnobConfig knob)
    {
        return ComputeVolume(rawValue, knob.MinVolume, knob.MaxVolume, knob.Curve);
    }
}
