using AmpUp.Core.Models;

namespace AmpUp.Core.Engine;

/// <summary>
/// Maps room device positions to screen regions for DreamView/Screen Sync.
/// Pre-computes which screen edge and region each device should sample from
/// based on its physical position relative to the monitor in the room layout.
/// Recalculated only when the layout changes, not per-frame.
/// </summary>
public class ScreenSpatialMapper
{
    /// <summary>
    /// A normalized screen region that a device should sample from.
    /// Coordinates are 0.0-1.0 where (0,0) is top-left of the screen.
    /// </summary>
    public struct ScreenRegion
    {
        public float XStart, XEnd;   // horizontal range on screen
        public float YStart, YEnd;   // vertical range on screen
        public ZoneSide PrimaryEdge; // which screen edge the device is closest to

        public override string ToString()
        {
            int xs = (int)(XStart * 100), xe = (int)(XEnd * 100);
            int ys = (int)(YStart * 100), ye = (int)(YEnd * 100);
            return PrimaryEdge switch
            {
                ZoneSide.Left => $"Left Edge, {ys}-{ye}%",
                ZoneSide.Right => $"Right Edge, {ys}-{ye}%",
                ZoneSide.Top => $"Top Edge, {xs}-{xe}%",
                ZoneSide.Bottom => $"Bottom Edge, {xs}-{xe}%",
                _ => $"Full, X:{xs}-{xe}% Y:{ys}-{ye}%",
            };
        }
    }

    private readonly Dictionary<string, ScreenRegion> _deviceRegions = new();

    public bool HasLayout => _deviceRegions.Count > 0;

    /// <summary>
    /// Get the computed screen region for a device (by its DeviceId/IP).
    /// Returns null if the device isn't in the layout or no monitor is placed.
    /// </summary>
    public ScreenRegion? GetRegion(string deviceId)
    {
        return _deviceRegions.TryGetValue(deviceId, out var region) ? region : null;
    }

    /// <summary>
    /// Recalculate all device→screen region mappings from the room layout.
    /// Only call when the layout changes (monitor moved, device moved, etc).
    /// </summary>
    public void Recalculate(RoomLayout layout)
    {
        _deviceRegions.Clear();
        if (layout.Monitor == null || layout.Devices.Count == 0) return;

        var mon = layout.Monitor;
        float monHalfW = (float)(mon.WidthFt / 2);
        float monHalfH = (float)(mon.HeightFt / 2);

        foreach (var dev in layout.Devices)
        {
            // Offset from monitor center
            float dx = (float)(dev.X - mon.X);  // positive = right of monitor
            float dz = (float)(dev.Z - mon.Z);  // positive = above monitor
            float dy = (float)(dev.Y - mon.Y);  // positive = behind/further from monitor

            // Determine primary edge based on which side of the monitor the device is on
            ZoneSide edge;
            float absX = Math.Abs(dx);
            float absZ = Math.Abs(dz);

            // How far past the monitor edge is the device?
            float beyondX = absX - monHalfW;
            float beyondZ = absZ - monHalfH;

            if (beyondX > 0 && beyondX >= beyondZ)
            {
                // Device extends past left or right edge
                edge = dx < 0 ? ZoneSide.Left : ZoneSide.Right;
            }
            else if (beyondZ > 0 && beyondZ > beyondX)
            {
                // Device extends past top or bottom edge
                edge = dz > 0 ? ZoneSide.Top : ZoneSide.Bottom;
            }
            else
            {
                // Device is within the monitor's footprint (behind it)
                edge = ZoneSide.Full;
            }

            ScreenRegion region;

            if (dev.SplitLR && dev.SegmentCount > 1)
            {
                // Split device: create two separate regions (left half + right half)
                float gapHalf = (float)(dev.SplitGapFt / 2);
                float halfLen = (float)(dev.LengthFt * 0.4); // each half ~40% of total length

                // Left half region
                float leftDx = dx - gapHalf;
                var leftRegion = ComputeRegion(leftDx, dz, halfLen, dev, mon, ZoneSide.Left);
                _deviceRegions[dev.DeviceId] = leftRegion;

                // Right half region
                float rightDx = dx + gapHalf;
                var rightRegion = ComputeRegion(rightDx, dz, halfLen, dev, mon, ZoneSide.Right);
                _deviceRegions[dev.DeviceId + ":R"] = rightRegion;
                continue;
            }

            region = ComputeRegion(dx, dz, (float)dev.LengthFt, dev, mon, edge);
            _deviceRegions[dev.DeviceId] = region;
        }
    }

    private static ScreenRegion ComputeRegion(float dx, float dz, float deviceLen,
        RoomDevicePlacement dev, MonitorPlacement mon, ZoneSide edge)
    {
        float monHalfW = (float)(mon.WidthFt / 2);
        float monHalfH = (float)(mon.HeightFt / 2);
        float monW = (float)mon.WidthFt;
        float monH = (float)mon.HeightFt;

        ScreenRegion region;

        switch (edge)
        {
            case ZoneSide.Left:
            {
                // Device is to the left — sample left portion of screen
                // X range: 0 to ~25%, wider if device is close, narrower if far
                float dist = Math.Max(Math.Abs(dx) - monHalfW, 0.1f);
                float xWidth = Math.Clamp(0.4f / (1f + dist), 0.1f, 0.35f);
                // Y range: map device vertical position to screen Y
                float yCenter = monH > 0 ? Math.Clamp(0.5f - dz / monH, 0, 1) : 0.5f;
                float yExtent = monH > 0 ? Math.Clamp(deviceLen / monH / 2, 0.1f, 0.5f) : 0.4f;
                region = new ScreenRegion
                {
                    XStart = 0, XEnd = xWidth,
                    YStart = Math.Clamp(yCenter - yExtent, 0, 1),
                    YEnd = Math.Clamp(yCenter + yExtent, 0, 1),
                    PrimaryEdge = ZoneSide.Left,
                };
                break;
            }
            case ZoneSide.Right:
            {
                float dist = Math.Max(Math.Abs(dx) - monHalfW, 0.1f);
                float xWidth = Math.Clamp(0.4f / (1f + dist), 0.1f, 0.35f);
                float yCenter = monH > 0 ? Math.Clamp(0.5f - dz / monH, 0, 1) : 0.5f;
                float yExtent = monH > 0 ? Math.Clamp(deviceLen / monH / 2, 0.1f, 0.5f) : 0.4f;
                region = new ScreenRegion
                {
                    XStart = 1f - xWidth, XEnd = 1f,
                    YStart = Math.Clamp(yCenter - yExtent, 0, 1),
                    YEnd = Math.Clamp(yCenter + yExtent, 0, 1),
                    PrimaryEdge = ZoneSide.Right,
                };
                break;
            }
            case ZoneSide.Top:
            {
                float dist = Math.Max(dz - monHalfH, 0.1f);
                float yHeight = Math.Clamp(0.4f / (1f + dist), 0.1f, 0.35f);
                float xCenter = monW > 0 ? Math.Clamp(0.5f + dx / monW, 0, 1) : 0.5f;
                float xExtent = monW > 0 ? Math.Clamp(deviceLen / monW / 2, 0.1f, 0.5f) : 0.4f;
                region = new ScreenRegion
                {
                    XStart = Math.Clamp(xCenter - xExtent, 0, 1),
                    XEnd = Math.Clamp(xCenter + xExtent, 0, 1),
                    YStart = 0, YEnd = yHeight,
                    PrimaryEdge = ZoneSide.Top,
                };
                break;
            }
            case ZoneSide.Bottom:
            {
                float dist = Math.Max(-dz - monHalfH, 0.1f);
                float yHeight = Math.Clamp(0.4f / (1f + dist), 0.1f, 0.35f);
                float xCenter = monW > 0 ? Math.Clamp(0.5f + dx / monW, 0, 1) : 0.5f;
                float xExtent = monW > 0 ? Math.Clamp(deviceLen / monW / 2, 0.1f, 0.5f) : 0.4f;
                region = new ScreenRegion
                {
                    XStart = Math.Clamp(xCenter - xExtent, 0, 1),
                    XEnd = Math.Clamp(xCenter + xExtent, 0, 1),
                    YStart = 1f - yHeight, YEnd = 1f,
                    PrimaryEdge = ZoneSide.Bottom,
                };
                break;
            }
            default: // Full — device is behind the monitor
            {
                // Broad sampling centered on device's position relative to monitor
                float xCenter = monW > 0 ? Math.Clamp(0.5f + dx / monW, 0, 1) : 0.5f;
                float yCenter = monH > 0 ? Math.Clamp(0.5f - dz / monH, 0, 1) : 0.5f;
                region = new ScreenRegion
                {
                    XStart = Math.Clamp(xCenter - 0.4f, 0, 1),
                    XEnd = Math.Clamp(xCenter + 0.4f, 0, 1),
                    YStart = Math.Clamp(yCenter - 0.4f, 0, 1),
                    YEnd = Math.Clamp(yCenter + 0.4f, 0, 1),
                    PrimaryEdge = ZoneSide.Full,
                };
                break;
            }
        }

        return region;
    }

    /// <summary>
    /// Get all computed regions (for UI visualization — drawing edge affinity lines).
    /// </summary>
    public IReadOnlyDictionary<string, ScreenRegion> AllRegions => _deviceRegions;
}
