using System.Runtime.InteropServices;
using AmpUp.Core.Interfaces;
using AmpUp.Core.Models;
using AmpUp.Core;

namespace AmpUp.Mac;

/// <summary>
/// macOS screen capture implementation using CoreGraphics CGWindowListCreateImage.
/// Captures the full display, samples N horizontal zones across the bottom edge,
/// applies gamma-correct linearization + re-encoding, and filters near-black pixels
/// (same behavior as Windows ScreenCapture.cs).
/// </summary>
public class MacScreenCapture : IScreenCapture, IDisposable
{
    // ── CoreGraphics P/Invoke ─────────────────────────────────────────────────

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGDisplayCreateImage(uint display);

    [DllImport(CoreGraphics)]
    private static extern uint CGMainDisplayID();

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] activeDisplays, out uint displayCount);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetWidth(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetHeight(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGImageGetDataProvider(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGDataProviderCopyData(IntPtr provider);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGImageRelease(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetBytesPerRow(IntPtr image);

    [DllImport(CoreGraphics)]
    private static extern int CGImageGetBitsPerPixel(IntPtr image);

    // CFData
    [DllImport(CoreFoundation)]
    private static extern long CFDataGetLength(IntPtr cfData);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr cfData);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    // ── IScreenCapture ────────────────────────────────────────────────────────

    private bool _disposed;

    public int MonitorCount
    {
        get
        {
            try
            {
                var displays = new uint[8];
                CGGetActiveDisplayList(8, displays, out uint count);
                return (int)count;
            }
            catch
            {
                return 1;
            }
        }
    }

    /// <summary>
    /// Capture zoneCount horizontal zones sampled from the bottom quarter of the screen.
    /// Uses gamma-correct sRGB linearization + re-encoding, dark pixel filtering.
    /// </summary>
    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount)
    {
        if (_disposed) return null;

        uint displayId = GetDisplayId(monitorIndex);
        IntPtr image = IntPtr.Zero;
        IntPtr cfData = IntPtr.Zero;

        try
        {
            image = CGDisplayCreateImage(displayId);
            if (image == IntPtr.Zero) return null;

            int width = CGImageGetWidth(image);
            int height = CGImageGetHeight(image);
            int bytesPerRow = CGImageGetBytesPerRow(image);
            int bitsPerPixel = CGImageGetBitsPerPixel(image);
            int bytesPerPixel = bitsPerPixel / 8;

            if (width <= 0 || height <= 0 || bytesPerPixel < 3) return null;

            var provider = CGImageGetDataProvider(image);
            if (provider == IntPtr.Zero) return null;

            cfData = CGDataProviderCopyData(provider);
            if (cfData == IntPtr.Zero) return null;

            long dataLen = CFDataGetLength(cfData);
            IntPtr dataPtr = CFDataGetBytePtr(cfData);
            if (dataPtr == IntPtr.Zero || dataLen == 0) return null;

            // Sample from the bottom 25% of the screen (like Windows version)
            int sampleTop = (int)(height * 0.75);
            int sampleHeight = height - sampleTop;
            int sampleRows = Math.Max(1, sampleHeight);

            int zoneWidth = Math.Max(1, width / zoneCount);
            var zones = new (byte R, byte G, byte B)[zoneCount];

            for (int z = 0; z < zoneCount; z++)
            {
                int xStart = z * zoneWidth;
                int xEnd = Math.Min(width, xStart + zoneWidth);

                double rSum = 0, gSum = 0, bSum = 0;
                int count = 0;

                for (int row = sampleTop; row < height; row += 4) // stride sample every 4 rows
                {
                    for (int x = xStart; x < xEnd; x += 4) // stride sample every 4 px
                    {
                        long offset = (long)row * bytesPerRow + (long)x * bytesPerPixel;
                        if (offset + 2 >= dataLen) continue;

                        // CoreGraphics BGRx or RGBA depending on format — try BGRA first
                        byte b = Marshal.ReadByte(dataPtr, (int)offset);
                        byte g = Marshal.ReadByte(dataPtr, (int)offset + 1);
                        byte r = Marshal.ReadByte(dataPtr, (int)offset + 2);

                        // Dark pixel filter: skip near-black (saves from dark desktop lowering colors)
                        if (r < 10 && g < 10 && b < 10) continue;

                        // sRGB → linear for gamma-correct averaging
                        rSum += SrgbToLinear(r);
                        gSum += SrgbToLinear(g);
                        bSum += SrgbToLinear(b);
                        count++;
                    }
                }

                if (count == 0)
                {
                    zones[z] = (0, 0, 0);
                }
                else
                {
                    // Linear average → back to sRGB
                    zones[z] = (
                        LinearToSrgb(rSum / count),
                        LinearToSrgb(gSum / count),
                        LinearToSrgb(bSum / count)
                    );
                }
            }

            return zones;
        }
        catch (Exception ex)
        {
            Logger.Log($"MacScreenCapture error: {ex.Message}");
            return null;
        }
        finally
        {
            if (cfData != IntPtr.Zero) CFRelease(cfData);
            if (image != IntPtr.Zero) CGImageRelease(image);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static uint GetDisplayId(int monitorIndex)
    {
        try
        {
            var displays = new uint[8];
            CGGetActiveDisplayList(8, displays, out uint count);
            if (monitorIndex < count && monitorIndex >= 0)
                return displays[monitorIndex];
        }
        catch { }
        return CGMainDisplayID();
    }

    /// <summary>Convert sRGB byte (0-255) to linear float.</summary>
    private static double SrgbToLinear(byte v)
    {
        double c = v / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>Convert linear float to sRGB byte (0-255).</summary>
    private static byte LinearToSrgb(double c)
    {
        double srgb = c <= 0.0031308
            ? c * 12.92
            : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)(srgb * 255.0 + 0.5), 0, 255);
    }

    // Crop not implemented on Mac yet — pass through to regular capture
    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount, bool cropBlackBars)
        => CaptureZones(monitorIndex, zoneCount);

    // Zone grid not implemented on Mac yet — stub
    public (byte R, byte G, byte B)[,]? CaptureZoneGrid(int monitorIndex, int cols, int rows, ContentBounds? crop)
        => null;

    public void Dispose()
    {
        _disposed = true;
    }
}
