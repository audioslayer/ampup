using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AmpUp.Core.Models;

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

    // Pixels darker than this (R+G+B sum) are ignored to prevent dark UI from washing out colors
    private const int DarkThreshold = 30; // ~10 per channel

    // Black bar detection: a column/row is "black" if fewer than this % of pixels are non-dark
    private const float BlackBarContentThreshold = 0.02f; // 2% — a mostly-black column

    // Cache detected content bounds (recalculate every N frames since aspect ratio rarely changes)
    private Rectangle _cachedContentBounds;
    private int _contentBoundsFrameCounter;
    private const int ContentBoundsRecalcInterval = 30; // ~1s at 30fps

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sample all zones from the specified monitor. Returns one averaged (R,G,B) per zone.
    /// ZoneCount must be 4, 8, or 16. Returns null on capture failure.
    /// </summary>
    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount)
        => CaptureZones(monitorIndex, zoneCount, false);

    /// <summary>
    /// Sample all zones from the specified monitor, optionally cropping black bars.
    /// When cropBlackBars is true, detects pillarbox/letterbox and only samples the content area.
    /// </summary>
    public (byte R, byte G, byte B)[]? CaptureZones(int monitorIndex, int zoneCount, bool cropBlackBars)
    {
        if (_disposed) return null;

        var bounds = GetMonitorBounds(monitorIndex);
        if (bounds.Width == 0 || bounds.Height == 0) return null;

        try
        {
            using var bmp = CaptureScreen(bounds);
            if (bmp == null) return null;

            if (cropBlackBars)
            {
                // Recalculate content bounds periodically (aspect ratio doesn't change often)
                _contentBoundsFrameCounter++;
                if (_contentBoundsFrameCounter >= ContentBoundsRecalcInterval ||
                    _cachedContentBounds.Width == 0 || _cachedContentBounds.Height == 0)
                {
                    _cachedContentBounds = DetectContentBounds(bmp);
                    _contentBoundsFrameCounter = 0;
                }

                // Only use crop if it's meaningfully smaller than the full frame
                // (at least 3% cropped from one side to avoid false positives on dark scenes)
                int minCropPixels = bmp.Width / 30; // ~3% of width
                bool hasMeaningfulCrop =
                    _cachedContentBounds.Left > minCropPixels ||
                    (bmp.Width - _cachedContentBounds.Right) > minCropPixels ||
                    _cachedContentBounds.Top > minCropPixels ||
                    (bmp.Height - _cachedContentBounds.Bottom) > minCropPixels;

                if (hasMeaningfulCrop)
                    return SampleZones(bmp, zoneCount, _cachedContentBounds);
            }

            return SampleZones(bmp, zoneCount);
        }
        catch (Exception ex)
        {
            Logger.Log($"ScreenCapture.CaptureZones failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Capture a 2D zone grid [row, col] from the screen.
    /// Rows = vertical slices (e.g. 3 = top/mid/bottom), cols = horizontal zones (4/8/16).
    /// ContentBounds defines the crop area; null = full screen.
    /// </summary>
    public (byte R, byte G, byte B)[,]? CaptureZoneGrid(int monitorIndex, int cols, int rows, ContentBounds? crop)
    {
        if (_disposed) return null;

        var bounds = GetMonitorBounds(monitorIndex);
        if (bounds.Width == 0 || bounds.Height == 0) return null;

        try
        {
            using var bmp = CaptureScreen(bounds);
            if (bmp == null) return null;

            // Determine crop rectangle
            Rectangle cropRect;
            if (crop != null && crop.AutoDetect)
            {
                // Auto-detect content bounds
                _contentBoundsFrameCounter++;
                if (_contentBoundsFrameCounter >= ContentBoundsRecalcInterval ||
                    _cachedContentBounds.Width == 0)
                {
                    _cachedContentBounds = DetectContentBounds(bmp);
                    _contentBoundsFrameCounter = 0;

                    // Update the ContentBounds percentages from detected pixels
                    if (_cachedContentBounds.Width > 0 && _cachedContentBounds.Height > 0)
                    {
                        crop.LeftPct = (double)_cachedContentBounds.Left / bmp.Width;
                        crop.RightPct = 1.0 - (double)_cachedContentBounds.Right / bmp.Width;
                        crop.TopPct = (double)_cachedContentBounds.Top / bmp.Height;
                        crop.BottomPct = 1.0 - (double)_cachedContentBounds.Bottom / bmp.Height;
                    }
                }
                cropRect = _cachedContentBounds.Width > 0 ? _cachedContentBounds
                    : new Rectangle(0, 0, bmp.Width, bmp.Height);
            }
            else if (crop != null && (crop.LeftPct > 0 || crop.RightPct > 0 || crop.TopPct > 0 || crop.BottomPct > 0))
            {
                // Manual crop from percentages
                int left = (int)(crop.LeftPct * bmp.Width);
                int right = (int)((1.0 - crop.RightPct) * bmp.Width);
                int top = (int)(crop.TopPct * bmp.Height);
                int bottom = (int)((1.0 - crop.BottomPct) * bmp.Height);
                cropRect = new Rectangle(left, top, Math.Max(right - left, 1), Math.Max(bottom - top, 1));
            }
            else
            {
                cropRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            }

            return SampleZoneGrid(bmp, cols, rows, cropRect);
        }
        catch (Exception ex)
        {
            Logger.Log($"ScreenCapture.CaptureZoneGrid failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Compute an expanded content bounds for Ambient crop mode (+5% each side, clamped).
    /// </summary>
    public static ContentBounds ExpandForAmbient(ContentBounds source)
    {
        return new ContentBounds
        {
            LeftPct = Math.Max(0, source.LeftPct - 0.05),
            RightPct = Math.Max(0, source.RightPct - 0.05),
            TopPct = Math.Max(0, source.TopPct - 0.05),
            BottomPct = Math.Max(0, source.BottomPct - 0.05),
            AutoDetect = false,
        };
    }

    /// <summary>
    /// Flatten a 2D zone grid to a 1D horizontal array by averaging rows.
    /// Used for backward compatibility with OnZoneColors event.
    /// </summary>
    public static (byte R, byte G, byte B)[] FlattenToHorizontal((byte R, byte G, byte B)[,] grid, int cols, int rows)
    {
        var result = new (byte R, byte G, byte B)[cols];
        for (int c = 0; c < cols; c++)
        {
            int r = 0, g = 0, b = 0;
            for (int row = 0; row < rows; row++)
            {
                r += grid[row, c].R;
                g += grid[row, c].G;
                b += grid[row, c].B;
            }
            result[c] = ((byte)(r / rows), (byte)(g / rows), (byte)(b / rows));
        }
        return result;
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

    // ── 2D Zone Grid sampling ─────────────────────────────────────────────

    /// <summary>
    /// Sample a 2D grid of zones [row, col] within the given crop rectangle.
    /// Uses gamma-correct averaging and dark pixel filtering.
    /// </summary>
    private static (byte R, byte G, byte B)[,] SampleZoneGrid(Bitmap bmp, int cols, int rows, Rectangle crop)
    {
        crop.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (crop.Width == 0 || crop.Height == 0)
            return new (byte R, byte G, byte B)[rows, cols];

        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        var results = new (byte R, byte G, byte B)[rows, cols];

        try
        {
            int stride = data.Stride;
            int colWidth = Math.Max(1, crop.Width / cols);
            int rowHeight = Math.Max(1, crop.Height / rows);

            unsafe
            {
                byte* ptr = (byte*)data.Scan0;

                for (int row = 0; row < rows; row++)
                {
                    int yStart = crop.Top + row * rowHeight;
                    int yEnd = (row == rows - 1) ? crop.Bottom : yStart + rowHeight;

                    for (int col = 0; col < cols; col++)
                    {
                        int xStart = crop.Left + col * colWidth;
                        int xEnd = (col == cols - 1) ? crop.Right : xStart + colWidth;

                        double rLin = 0, gLin = 0, bLin = 0;
                        long count = 0;

                        for (int y = yStart; y < yEnd; y += SampleStride)
                        {
                            byte* rowPtr = ptr + y * stride;
                            for (int x = xStart; x < xEnd; x += SampleStride)
                            {
                                int offset = x * 4;
                                byte pb = rowPtr[offset];
                                byte pg = rowPtr[offset + 1];
                                byte pr = rowPtr[offset + 2];

                                if (pr + pg + pb < DarkThreshold)
                                    continue;

                                double rl = pr / 255.0; rl *= rl;
                                double gl = pg / 255.0; gl *= gl;
                                double bl = pb / 255.0; bl *= bl;

                                rLin += rl;
                                gLin += gl;
                                bLin += bl;
                                count++;
                            }
                        }

                        if (count > 0)
                        {
                            results[row, col] = (
                                (byte)Math.Clamp(Math.Sqrt(rLin / count) * 255, 0, 255),
                                (byte)Math.Clamp(Math.Sqrt(gLin / count) * 255, 0, 255),
                                (byte)Math.Clamp(Math.Sqrt(bLin / count) * 255, 0, 255));
                        }
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

    // ── Content bounds detection (black bar cropping) ──────────────────────

    /// <summary>
    /// Detect the bounding rectangle of actual content within the frame,
    /// excluding pillarbox (side black bars) and letterbox (top/bottom black bars).
    /// Scans columns from edges inward to find where non-dark content starts.
    /// Uses coarse sampling (every SampleStride rows/cols) for speed.
    /// </summary>
    private static Rectangle DetectContentBounds(Bitmap bmp)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        int width = bmp.Width;
        int height = bmp.Height;
        int stride = data.Stride;
        int left = 0, right = width, top = 0, bottom = height;

        // Minimum non-dark pixels for a column/row to count as "content"
        int colSamples = height / SampleStride;
        int rowSamples = width / SampleStride;
        int colThreshold = Math.Max(1, (int)(colSamples * BlackBarContentThreshold));
        int rowThreshold = Math.Max(1, (int)(rowSamples * BlackBarContentThreshold));

        try
        {
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;

                // Scan columns from left
                for (int x = 0; x < width / 2; x += SampleStride)
                {
                    int nonDark = 0;
                    for (int y = 0; y < height; y += SampleStride)
                    {
                        int offset = y * stride + x * 4;
                        if (ptr[offset] + ptr[offset + 1] + ptr[offset + 2] >= DarkThreshold)
                            nonDark++;
                    }
                    if (nonDark >= colThreshold) { left = x; break; }
                }

                // Scan columns from right
                for (int x = width - 1; x >= width / 2; x -= SampleStride)
                {
                    int nonDark = 0;
                    for (int y = 0; y < height; y += SampleStride)
                    {
                        int offset = y * stride + x * 4;
                        if (ptr[offset] + ptr[offset + 1] + ptr[offset + 2] >= DarkThreshold)
                            nonDark++;
                    }
                    if (nonDark >= colThreshold) { right = x + 1; break; }
                }

                // Scan rows from top
                for (int y = 0; y < height / 2; y += SampleStride)
                {
                    int nonDark = 0;
                    byte* row = ptr + y * stride;
                    for (int x = left; x < right; x += SampleStride)
                    {
                        int offset = x * 4;
                        if (row[offset] + row[offset + 1] + row[offset + 2] >= DarkThreshold)
                            nonDark++;
                    }
                    if (nonDark >= rowThreshold) { top = y; break; }
                }

                // Scan rows from bottom
                for (int y = height - 1; y >= height / 2; y -= SampleStride)
                {
                    int nonDark = 0;
                    byte* row = ptr + y * stride;
                    for (int x = left; x < right; x += SampleStride)
                    {
                        int offset = x * 4;
                        if (row[offset] + row[offset + 1] + row[offset + 2] >= DarkThreshold)
                            nonDark++;
                    }
                    if (nonDark >= rowThreshold) { bottom = y + 1; break; }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        // Sanity: ensure we have a reasonable content area (at least 25% of frame)
        int contentW = right - left;
        int contentH = bottom - top;
        if (contentW < width / 4 || contentH < height / 4)
            return new Rectangle(0, 0, width, height); // fall back to full frame

        return new Rectangle(left, top, contentW, contentH);
    }

    // ── Zone sampling ────────────────────────────────────────────────────────

    /// <summary>
    /// Divide the bitmap into `zoneCount` horizontal slices within the given content bounds.
    /// </summary>
    private static (byte R, byte G, byte B)[] SampleZones(Bitmap bmp, int zoneCount, Rectangle contentBounds)
    {
        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        var results = new (byte R, byte G, byte B)[zoneCount];

        try
        {
            int stride = data.Stride;
            int zoneWidth = Math.Max(1, contentBounds.Width / zoneCount);

            unsafe
            {
                byte* ptr = (byte*)data.Scan0;

                for (int z = 0; z < zoneCount; z++)
                {
                    int xStart = contentBounds.Left + z * zoneWidth;
                    int xEnd = (z == zoneCount - 1) ? contentBounds.Right : xStart + zoneWidth;

                    double rLin = 0, gLin = 0, bLin = 0;
                    long count = 0;

                    for (int y = contentBounds.Top; y < contentBounds.Bottom; y += SampleStride)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = xStart; x < xEnd; x += SampleStride)
                        {
                            int offset = x * 4;
                            byte pb = row[offset];
                            byte pg = row[offset + 1];
                            byte pr = row[offset + 2];

                            if (pr + pg + pb < DarkThreshold)
                                continue;

                            double rl = pr / 255.0; rl *= rl;
                            double gl = pg / 255.0; gl *= gl;
                            double bl = pb / 255.0; bl *= bl;

                            rLin += rl;
                            gLin += gl;
                            bLin += bl;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        results[z] = (
                            (byte)Math.Clamp(Math.Sqrt(rLin / count) * 255, 0, 255),
                            (byte)Math.Clamp(Math.Sqrt(gLin / count) * 255, 0, 255),
                            (byte)Math.Clamp(Math.Sqrt(bLin / count) * 255, 0, 255));
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
    /// Divide the bitmap into `zoneCount` horizontal slices and average each zone's pixels.
    /// Uses gamma-correct (linear space) averaging and filters dark pixels for accurate colors.
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

                    // Accumulate in linear space for gamma-correct averaging
                    double rLin = 0, gLin = 0, bLin = 0;
                    long count = 0;

                    for (int y = 0; y < height; y += SampleStride)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = xStart; x < xEnd; x += SampleStride)
                        {
                            int offset = x * bytesPerPixel;
                            byte pb = row[offset];
                            byte pg = row[offset + 1];
                            byte pr = row[offset + 2];

                            // Skip very dark pixels — prevents dark UI from washing out colors
                            if (pr + pg + pb < DarkThreshold)
                                continue;

                            // sRGB → linear (approximate gamma 2.2)
                            double rl = pr / 255.0; rl *= rl;
                            double gl = pg / 255.0; gl *= gl;
                            double bl = pb / 255.0; bl *= bl;

                            rLin += rl;
                            gLin += gl;
                            bLin += bl;
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        // Linear → sRGB (sqrt for gamma 2.2 approx)
                        results[z] = (
                            (byte)Math.Clamp(Math.Sqrt(rLin / count) * 255, 0, 255),
                            (byte)Math.Clamp(Math.Sqrt(gLin / count) * 255, 0, 255),
                            (byte)Math.Clamp(Math.Sqrt(bLin / count) * 255, 0, 255));
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
    /// Uses gamma-correct averaging and dark pixel filtering.
    /// </summary>
    private static (byte R, byte G, byte B) SampleRegion(Bitmap bmp, Rectangle region)
    {
        region.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (region.Width == 0 || region.Height == 0) return (0, 0, 0);

        var data = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppRgb);

        double rLin = 0, gLin = 0, bLin = 0;
        long count = 0;

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
                        byte pb = row[offset];
                        byte pg = row[offset + 1];
                        byte pr = row[offset + 2];

                        if (pr + pg + pb < DarkThreshold)
                            continue;

                        double rl = pr / 255.0; rl *= rl;
                        double gl = pg / 255.0; gl *= gl;
                        double bl = pb / 255.0; bl *= bl;
                        rLin += rl; gLin += gl; bLin += bl;
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
        return (
            (byte)Math.Clamp(Math.Sqrt(rLin / count) * 255, 0, 255),
            (byte)Math.Clamp(Math.Sqrt(gLin / count) * 255, 0, 255),
            (byte)Math.Clamp(Math.Sqrt(bLin / count) * 255, 0, 255));
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
