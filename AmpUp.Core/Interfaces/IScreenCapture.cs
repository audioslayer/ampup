using AmpUp.Core.Models;

namespace AmpUp.Core.Interfaces;

public interface IScreenCapture : IDisposable
{
    (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount);
    (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount, bool cropBlackBars);
    /// <summary>
    /// Capture a 2D zone grid [row, col] from the screen. Rows = vertical slices (top/mid/bottom),
    /// cols = horizontal zones. ContentBounds defines crop area (null = full screen).
    /// </summary>
    (byte R, byte G, byte B)[,]? CaptureZoneGrid(int monitorIndex, int cols, int rows, ContentBounds? crop);
    int MonitorCount { get; }
}
