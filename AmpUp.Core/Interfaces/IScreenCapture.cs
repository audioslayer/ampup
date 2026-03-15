namespace AmpUp.Core.Interfaces;

public interface IScreenCapture : IDisposable
{
    (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount);
    int MonitorCount { get; }
}
