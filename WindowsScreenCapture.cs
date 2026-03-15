using AmpUp.Core.Interfaces;

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

    public int MonitorCount => ScreenCapture.MonitorCount;

    public void Dispose()
    {
        _capture.Dispose();
    }
}
