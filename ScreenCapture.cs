using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AmpUp;

/// <summary>
/// GDI-based screen capture with zone-based color sampling.
/// Captures a monitor region via BitBlt and samples dominant colors per zone.
/// Runs on a background thread at up to 30fps. DXGI can replace this layer later.
/// </summary>
public class ScreenCapture : IDisposable
{
    private bool _disposed;

    // Pixel stride for downsampled sampling (every Nth pixel — good balance of speed vs accuracy)
    private const int SampleStride = 4;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sample all zones from the specified monitor. Returns one averaged (R,G,B) per zone.
    /// ZoneCount must be 4, 8, or 16. Returns null on capture failure.
    /// </summary>
    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount)
    {
        if (_disposed) return null;

        var bounds = GetMonitorBounds(monitorIndex);
        if (bounds.Width == 0 || bounds.Height == 0) return null;

        try
        {
            using var bmp = CaptureScreen(bounds);
            if (bmp == null) return null;
            return SampleZones(bmp, zoneCount);
        }
        catch (Exception ex)
        {
            Logger.Log($"ScreenCapture.CaptureZones failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the averaged color of a named side/zone from the specified monitor.
    /// Used by DreamSyncController for per-device zone mapping.
    /// </summary>
    public (byte R, byte G, byte B)? CaptureSide(int monitorIndex, ZoneSide side, int zoneCount)
    {
        if (_disposed) return null;

        var bounds = GetMonitorBounds(monitorIndex);
        if (bounds.Width == 0 || bounds.Height == 0) return null;

        try
        {
            using var bmp = CaptureScreen(bounds);
            if (bmp == null) return null;

            var region = GetSideRegion(bmp.Width, bmp.Height, side);
            return SampleRegion(bmp, region);
        }
        catch (Exception ex)
        {
            Logger.Log($"ScreenCapture.CaptureSide failed: {ex.Message}");
            return null;
        }
    }

    // ── Monitor bounds ───────────────────────────────────────────────────────

    public static Rectangle GetMonitorBounds(int monitorIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length)
            monitorIndex = 0;
        return screens[monitorIndex].Bounds;
    }

    public static int MonitorCount => System.Windows.Forms.Screen.AllScreens.Length;

    // ── GDI screen capture ───────────────────────────────────────────────────

    private static Bitmap? CaptureScreen(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        try
        {
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            return null;
        }
    }

    // ── Zone sampling ────────────────────────────────────────────────────────

    /// <summary>
    /// Divide the bitmap into `zoneCount` horizontal slices and average each zone's pixels.
    /// Returns an array of (R,G,B) per zone, left→right.
    /// </summary>
    private static (byte R, byte G, byte B)[] SampleZones(Bitmap bmp, int zoneCount)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        var results = new (byte R, byte G, byte B)[zoneCount];

        try
        {
            int bytesPerPixel = 4; // Format32bppRgb is still 4 bytes per pixel (B,G,R,unused)
            int stride = data.Stride;
            int width = bmp.Width;
            int height = bmp.Height;
            int zoneWidth = Math.Max(1, width / zoneCount);

            unsafe
            {
                byte* ptr = (byte*)data.Scan0;

                for (int z = 0; z < zoneCount; z++)
                {
                    int xStart = z * zoneWidth;
                    int xEnd = (z == zoneCount - 1) ? width : xStart + zoneWidth;

                    long rSum = 0, gSum = 0, bSum = 0, count = 0;

                    for (int y = 0; y < height; y += SampleStride)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = xStart; x < xEnd; x += SampleStride)
                        {
                            int offset = x * bytesPerPixel;
                            bSum += row[offset];
                            gSum += row[offset + 1];
                            rSum += row[offset + 2];
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        results[z] = ((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count));
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return results;
    }

    /// <summary>
    /// Average pixels in a specific rectangle region of the bitmap.
    /// </summary>
    private static (byte R, byte G, byte B) SampleRegion(Bitmap bmp, Rectangle region)
    {
        // Clamp to bitmap bounds
        region.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (region.Width == 0 || region.Height == 0) return (0, 0, 0);

        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        long rSum = 0, gSum = 0, bSum = 0, count = 0;

        try
        {
            int stride = data.Stride;
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;
                for (int y = region.Top; y < region.Bottom; y += SampleStride)
                {
                    byte* row = ptr + y * stride;
                    for (int x = region.Left; x < region.Right; x += SampleStride)
                    {
                        int offset = x * 4;
                        bSum += row[offset];
                        gSum += row[offset + 1];
                        rSum += row[offset + 2];
                        count++;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        if (count == 0) return (0, 0, 0);
        return ((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count));
    }

    /// <summary>
    /// Map a ZoneSide to the pixel region it covers within the bitmap.
    /// </summary>
    private static Rectangle GetSideRegion(int width, int height, ZoneSide side)
    {
        return side switch
        {
            ZoneSide.Left   => new Rectangle(0, 0, width / 2, height),
            ZoneSide.Right  => new Rectangle(width / 2, 0, width / 2, height),
            ZoneSide.Top    => new Rectangle(0, 0, width, height / 2),
            ZoneSide.Bottom => new Rectangle(0, height / 2, width, height / 2),
            _               => new Rectangle(0, 0, width, height), // Full
        };
    }

    // ── Saturation boost ─────────────────────────────────────────────────────

    /// <summary>
    /// Apply HSV saturation multiplier to an RGB color. Clamps to 0-255.
    /// </summary>
    public static (byte R, byte G, byte B) BoostSaturation(byte r, byte g, byte b, float saturation)
    {
        if (Math.Abs(saturation - 1.0f) < 0.01f) return (r, g, b);

        // RGB → HSV
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;

        float h = 0, s = 0, v = max;
        if (max > 0) s = delta / max;
        if (delta > 0)
        {
            if (max == rf) h = (gf - bf) / delta % 6;
            else if (max == gf) h = (bf - rf) / delta + 2;
            else h = (rf - gf) / delta + 4;
            h /= 6;
            if (h < 0) h += 1;
        }

        // Boost saturation
        s = Math.Clamp(s * saturation, 0f, 1f);

        // HSV → RGB
        float c = v * s;
        float x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        float m = v - c;

        float ro, go, bo;
        int hi = (int)(h * 6);
        switch (hi % 6)
        {
            case 0: ro = c; go = x; bo = 0; break;
            case 1: ro = x; go = c; bo = 0; break;
            case 2: ro = 0; go = c; bo = x; break;
            case 3: ro = 0; go = x; bo = c; break;
            case 4: ro = x; go = 0; bo = c; break;
            default: ro = c; go = 0; bo = x; break;
        }

        return (
            (byte)Math.Clamp((ro + m) * 255, 0, 255),
            (byte)Math.Clamp((go + m) * 255, 0, 255),
            (byte)Math.Clamp((bo + m) * 255, 0, 255)
        );
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
