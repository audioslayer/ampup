using AmpUp.Core.Interfaces;
using AmpUp.Core.Models;

namespace AmpUp;

/// <summary>
/// Windows-specific IScreenCapture implementation that delegates to the
/// GDI-based ScreenCapture class.
/// </summary>
public class WindowsScreenCapture : IScreenCapture
{
    private readonly ScreenCapture _capture = new();

    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount)
        => _capture.CaptureZones(monitorIndex, zoneCount);

    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount, bool cropBlackBars)
        => _capture.CaptureZones(monitorIndex, zoneCount, cropBlackBars);

    public (byte R, byte G, byte B)[,]? CaptureZoneGrid(int monitorIndex, int cols, int rows, ContentBounds? crop)
        => _capture.CaptureZoneGrid(monitorIndex, cols, rows, crop);

    public int MonitorCount => ScreenCapture.MonitorCount;

    public void Dispose()
    {
        _capture.Dispose();
    }
}
