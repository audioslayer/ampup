using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

/// <summary>
/// Maps 15 Turn Up LEDs to room device positions based on 3D spatial layout.
/// Projects device positions onto an effect direction axis, then samples
/// the LED buffer at each device's projected position with interpolation.
/// </summary>
public class SpatialMapper
{
    // Per-device: normalized position range on the 0–14 LED strip
    // For segment devices, Start..End spans the device's physical extent
    // For single-color devices, Start == End (point sample)
    private readonly Dictionary<string, (float Start, float End)> _deviceLedPositions = new();
    private RoomLayout _layout = new();

    public bool HasLayout => _layout.Devices.Count > 0;

    /// <summary>
    /// Recalculate LED position mappings when room layout or direction changes.
    /// </summary>
    public void Recalculate(RoomLayout layout)
    {
        _layout = layout;
        _deviceLedPositions.Clear();
        if (layout.Devices.Count == 0) return;

        var direction = layout.Direction;

        // Project each device onto the effect direction axis
        var projections = new List<(string Id, float Center, float HalfExtent, int Segments)>();

        foreach (var dev in layout.Devices)
        {
            float center = ProjectPosition(dev.X, dev.Y, dev.Z, direction,
                layout.WidthFt, layout.DepthFt, layout.HeightFt);

            // Physical extent along the direction axis (for segment devices)
            float halfExtent = 0;
            if (dev.SegmentCount > 1 && dev.LengthFt > 0)
            {
                // Project the device's length onto the direction axis based on rotation
                double rotRad = dev.Rotation * Math.PI / 180.0;
                float dirComponent = direction switch
                {
                    EffectDirection.LeftToRight => (float)Math.Abs(Math.Cos(rotRad)),
                    EffectDirection.FrontToBack => (float)Math.Abs(Math.Sin(rotRad)),
                    EffectDirection.Diagonal => (float)(Math.Abs(Math.Cos(rotRad - Math.PI / 4)) * 0.707),
                    _ => 0.5f // Radial/BottomToTop: spread evenly
                };
                float extentFt = (float)(dev.LengthFt * dirComponent);
                float roomExtent = direction switch
                {
                    EffectDirection.LeftToRight => (float)layout.WidthFt,
                    EffectDirection.FrontToBack => (float)layout.DepthFt,
                    EffectDirection.BottomToTop => (float)layout.HeightFt,
                    EffectDirection.Diagonal => (float)Math.Sqrt(layout.WidthFt * layout.WidthFt + layout.DepthFt * layout.DepthFt),
                    _ => (float)Math.Max(layout.WidthFt, layout.DepthFt) / 2
                };
                halfExtent = roomExtent > 0 ? extentFt / roomExtent / 2f : 0;
            }

            projections.Add((dev.DeviceId, center, halfExtent, dev.SegmentCount));
        }

        // Map normalized positions (0–1) to LED strip positions (0–14)
        foreach (var (id, center, halfExtent, segments) in projections)
        {
            float start = Math.Clamp((center - halfExtent) * 14f, 0, 14);
            float end = Math.Clamp((center + halfExtent) * 14f, 0, 14);
            if (segments <= 1) end = start; // point sample for bulbs
            _deviceLedPositions[id] = (start, end);
        }
    }

    /// <summary>
    /// Project a 3D position onto a 0–1 axis based on effect direction.
    /// </summary>
    private static float ProjectPosition(double x, double y, double z,
        EffectDirection direction, double roomW, double roomD, double roomH)
    {
        return direction switch
        {
            EffectDirection.LeftToRight => roomW > 0 ? (float)(x / roomW) : 0.5f,
            EffectDirection.FrontToBack => roomD > 0 ? (float)(y / roomD) : 0.5f,
            EffectDirection.BottomToTop => roomH > 0 ? (float)(z / roomH) : 0.5f,
            EffectDirection.Radial => ProjectRadial(x, y, roomW, roomD),
            EffectDirection.Diagonal => ProjectDiagonal(x, y, roomW, roomD),
            _ => 0.5f
        };
    }

    private static float ProjectRadial(double x, double y, double roomW, double roomD)
    {
        double cx = roomW / 2, cy = roomD / 2;
        double dx = x - cx, dy = y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double maxDist = Math.Sqrt(cx * cx + cy * cy);
        return maxDist > 0 ? Math.Clamp((float)(dist / maxDist), 0, 1) : 0;
    }

    private static float ProjectDiagonal(double x, double y, double roomW, double roomD)
    {
        double diagLen = Math.Sqrt(roomW * roomW + roomD * roomD);
        double proj = (x / roomW + y / roomD) / 2.0;
        return Math.Clamp((float)proj, 0, 1);
    }

    /// <summary>
    /// Sample colors for a specific device from the 15-LED buffer.
    /// Returns one color per segment (or 1 for single-color devices).
    /// </summary>
    public (int R, int G, int B)[] SampleForDevice(string deviceId, byte[] linear45, int segmentCount)
    {
        if (!_deviceLedPositions.TryGetValue(deviceId, out var pos))
        {
            // Fallback: sample from center
            return SampleAtPosition(linear45, 7f, segmentCount);
        }

        if (segmentCount <= 1)
        {
            // Single-color device: point sample at its position
            return SampleAtPosition(linear45, pos.Start, 1);
        }

        // Segment device: spread segments across the device's LED range
        var colors = new (int R, int G, int B)[segmentCount];
        float range = pos.End - pos.Start;
        for (int s = 0; s < segmentCount; s++)
        {
            float ledPos = range > 0
                ? pos.Start + s * range / Math.Max(segmentCount - 1, 1)
                : pos.Start;
            var sampled = SampleAtPosition(linear45, ledPos, 1);
            colors[s] = sampled[0];
        }
        return colors;
    }

    /// <summary>
    /// Sample from the 15-LED buffer at a fractional position with interpolation.
    /// </summary>
    private static (int R, int G, int B)[] SampleAtPosition(byte[] linear45, float ledPos, int count)
    {
        var result = new (int R, int G, int B)[count];
        for (int i = 0; i < count; i++)
        {
            float pos = count > 1
                ? ledPos + i * (14f - ledPos) / Math.Max(count - 1, 1)
                : ledPos;
            pos = Math.Clamp(pos, 0, 14);

            int lo = Math.Min((int)pos, 13);
            int hi = Math.Min(lo + 1, 14);
            float frac = pos - lo;

            int r = (int)(linear45[lo * 3] * (1 - frac) + linear45[hi * 3] * frac);
            int g = (int)(linear45[lo * 3 + 1] * (1 - frac) + linear45[hi * 3 + 1] * frac);
            int b = (int)(linear45[lo * 3 + 2] * (1 - frac) + linear45[hi * 3 + 2] * frac);
            result[i] = (r, g, b);
        }
        return result;
    }

    /// <summary>
    /// Get the normalized LED position for a device (for UI visualization).
    /// Returns (start, end) in 0–14 range.
    /// </summary>
    public (float Start, float End)? GetDevicePosition(string deviceId)
    {
        return _deviceLedPositions.TryGetValue(deviceId, out var pos) ? pos : null;
    }
}
