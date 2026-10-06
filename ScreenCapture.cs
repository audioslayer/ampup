using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AmpUp.Core.Models;

namespace AmpUp;

/// <summary>
/// Screen capture with zone-based color sampling.
/// Primary path: DXGI Desktop Duplication with GPU mip downsampling (DxgiDesktopDuplicator),
/// read back at 1/8 res into a cached bitmap. Fallback: GDI StretchBlt (HALFTONE) into the
/// same cached bitmap when DXGI is unavailable. Samplers then pick dominant colors per zone.
/// </summary>
public class ScreenCapture : IDisposable
{
    private bool _disposed;

    // Pixel stride for downsampled sampling (every Nth pixel — good balance of speed vs accuracy)
    // The bitmap is already a 1/8 box-filtered downsample (GPU mip / HALFTONE), so a coarse
    // stride here would throw away most of it: at 3440x1440 -> 430x180 a stride of 4 left only
    // ~15 sample rows per zone row. Stride 2 is ~19k reads per pass — negligible.
    private const int SampleStride = 2;

    // StretchBlt downsample factor — capture at 1/Nth of source resolution via GPU HALFTONE filter.
    // HALFTONE box-filters on the way down, so sampling the result is more accurate than
    // stride-sampling the full-res source. 8x (e.g. 3440x1440 -> 430x180) is still far more
    // detail than a 16x3 zone grid needs. GDI is only the fallback when DXGI is unavailable.
    private const int CaptureDownsample = 8; // matches DxgiDesktopDuplicator.MipLevel (1/8) so both paths yield the same bitmap size

    // Cached downsampled bitmap reused across frames to eliminate per-frame allocations.
    // Single-threaded instance (DreamSyncController owns a single capture instance).
    private Bitmap? _cachedBmp;
    private int _cachedSrcW, _cachedSrcH;

    // Precomputed sRGB → linear (approximate gamma 2.2) table: LinSq[v] = (v/255.0)*(v/255.0).
    // Replaces the per-pixel divide+square done for each channel in the sampling loops.
    private static readonly double[] LinSq = BuildLinSqTable();

    private static double[] BuildLinSqTable()
    {
        var t = new double[256];
        for (int v = 0; v < 256; v++)
        {
            double l = v / 255.0;
            t[v] = l * l;
        }
        return t;
    }

    // Pixels darker than this (R+G+B sum) are ignored to prevent dark UI from washing out colors.
    // Set high enough to filter gray/near-black pixels that dilute saturated colors (e.g. red → pink).
    private const int HueBinCount = 12;
    private const int DarkThreshold = 80; // ~27 per channel

    // Black bar detection: a column/row is "black" if fewer than this % of pixels are non-dark
    private const float BlackBarContentThreshold = 0.02f; // 2% — a mostly-black column

    // Cache detected content bounds (recalculate every N frames since aspect ratio rarely changes)
    // NOTE: bounds are in DOWNSAMPLED bitmap pixels; reset whenever the bitmap size changes
    // (DXGI <-> GDI switch, resolution change, monitor change) — see EnsureBoundsCacheFor.
    private Rectangle _cachedContentBounds;
    private Rectangle _pendingContentBounds;   // hysteresis: a new crop must be seen twice in a row
    private int _boundsBmpW, _boundsBmpH;
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

        lock (_lock) // Dispose may run on another thread
        try
        {
            var bmp = CaptureScreen(bounds, monitorIndex);
            if (bmp == null) return null;

            if (cropBlackBars)
            {
                // Recalculate content bounds periodically (aspect ratio doesn't change often)
                UpdateContentBounds(bmp);

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
            LogFailureThrottled($"ScreenCapture.CaptureZones failed: {ex.Message}");
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

        lock (_lock) // Dispose may run on another thread
        try
        {
            var bmp = CaptureScreen(bounds, monitorIndex);
            if (bmp == null) return null;

            // Determine crop rectangle
            Rectangle cropRect;
            if (crop != null && crop.AutoDetect)
            {
                // Auto-detect content bounds
                if (UpdateContentBounds(bmp))
                {
                    // Update the ContentBounds percentages from detected pixels (percentages
                    // are resolution-independent, so downsampled coords convert exactly)
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
            LogFailureThrottled($"ScreenCapture.CaptureZoneGrid failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Periodically re-detect letterbox/pillarbox bounds. Returns true when the detection ran.
    /// A changed crop is only committed after two consecutive detections agree, so a single
    /// dark frame (night scene, loading screen fade) can't snap the crop inward for a second.
    /// </summary>
    private bool UpdateContentBounds(Bitmap bmp)
    {
        if (bmp.Width != _boundsBmpW || bmp.Height != _boundsBmpH)
        {
            // Cached rectangle was measured on a different-sized bitmap — invalid now.
            _boundsBmpW = bmp.Width;
            _boundsBmpH = bmp.Height;
            _cachedContentBounds = Rectangle.Empty;
            _pendingContentBounds = Rectangle.Empty;
            _contentBoundsFrameCounter = 0;
        }

        _contentBoundsFrameCounter++;
        bool empty = _cachedContentBounds.Width == 0 || _cachedContentBounds.Height == 0;
        if (!empty && _contentBoundsFrameCounter < ContentBoundsRecalcInterval)
            return false;
        _contentBoundsFrameCounter = 0;

        var detected = DetectContentBounds(bmp);
        if (empty || BoundsClose(detected, _cachedContentBounds) || BoundsClose(detected, _pendingContentBounds))
        {
            _cachedContentBounds = detected;
            _pendingContentBounds = Rectangle.Empty;
        }
        else
        {
            _pendingContentBounds = detected;
        }
        return true;
    }

    private static bool BoundsClose(Rectangle a, Rectangle b)
    {
        if (b.Width == 0 || b.Height == 0) return false;
        const int tol = 3; // downsampled px (~24 source px)
        return Math.Abs(a.Left - b.Left) <= tol && Math.Abs(a.Top - b.Top) <= tol
            && Math.Abs(a.Right - b.Right) <= tol && Math.Abs(a.Bottom - b.Bottom) <= tol;
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

        lock (_lock) // Dispose may run on another thread
        try
        {
            var bmp = CaptureScreen(bounds, monitorIndex);
            if (bmp == null) return null;

            var region = GetSideRegion(bmp.Width, bmp.Height, side);
            return SampleRegion(bmp, region);
        }
        catch (Exception ex)
        {
            LogFailureThrottled($"ScreenCapture.CaptureSide failed: {ex.Message}");
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

    public static Bitmap? CapturePreviewFrame(int monitorIndex, int targetWidth, int targetHeight)
    {
        var bounds = GetMonitorBounds(monitorIndex);
        if (bounds.Width <= 0 || bounds.Height <= 0 || targetWidth <= 0 || targetHeight <= 0)
            return null;

        var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppRgb);
        try
        {
            using var g = Graphics.FromImage(bitmap);
            var destHdc = g.GetHdc();
            IntPtr srcHdc = IntPtr.Zero;
            try
            {
                srcHdc = GetDC(IntPtr.Zero);
                if (srcHdc == IntPtr.Zero)
                {
                    bitmap.Dispose();
                    return null;
                }

                SetStretchBltMode(destHdc, HALFTONE);
                SetBrushOrgEx(destHdc, 0, 0, IntPtr.Zero);

                bool ok = StretchBlt(
                    destHdc, 0, 0, targetWidth, targetHeight,
                    srcHdc, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    SRCCOPY);

                if (!ok)
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                if (srcHdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, srcHdc);
                g.ReleaseHdc(destHdc);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }
    }

    // ── GDI screen capture ───────────────────────────────────────────────────

    // StretchBlt interop — GPU-accelerated downsample via HALFTONE filter.
    // Captures ~4x faster at 4K than full-res BitBlt and produces a smaller
    // bitmap for the CPU sampling loops to read.
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc,
        uint rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr pt);

    private const int HALFTONE = 4;
    private const uint SRCCOPY = 0x00CC0020;

    // ── DXGI Desktop Duplication (primary path) ─────────────────────────────

    private readonly object _lock = new();
    private DxgiDesktopDuplicator? _dxgi;
    private long _dxgiRetryAfterTick;      // Environment.TickCount64 before which we stay on GDI
    private bool _dxgiFailureLogged;       // log once per failure streak
    private string? _dxgiLoggedDevice;
    private const int DxgiRetryMs = 3000;
    private const int DxgiHoldLastFrameMs = 5000;
    private long _lastDxgiOkTick;
    private long _lastLostLogTick;
    private static long _lastFailureLogTick;

    // Capture runs up to 60x/s — a persistent failure must not flood the log.
    private static void LogFailureThrottled(string msg)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastFailureLogTick) < 60_000) return;
        Interlocked.Exchange(ref _lastFailureLogTick, now);
        Logger.Log(msg + " (further failures suppressed for 60s)");
    }
    private const int DxgiLostRetryMs = 250; // after ACCESS_LOST: brief GDI gap, then rebuild

    private Bitmap? CaptureScreen(Rectangle bounds, int monitorIndex)
    {
        if (_disposed) return null; // caller holds _lock; don't resurrect resources after Dispose
        var bmp = TryCaptureDxgi(monitorIndex);
        if (bmp != null)
        {
            _lastDxgiOkTick = Environment.TickCount64;
            return bmp;
        }
        // DXGI hiccup (fullscreen game mode switch, access lost, rebuild backoff). Mixing in GDI
        // frames here makes the lights flash — GDI colours differ a lot (esp. on HDR desktops) —
        // so hold the last good DXGI frame for a while and only fall back if the outage persists.
        if (_cachedBmp != null && _lastDxgiOkTick != 0
            && Environment.TickCount64 - _lastDxgiOkTick < DxgiHoldLastFrameMs)
            return _cachedBmp;
        return CaptureScreenGdi(bounds);
    }

    /// <summary>
    /// Returns the cached bitmap filled from DXGI, or null to fall back to GDI for this frame.
    /// When no new desktop frame is available the previous image is reused (no spin, no GDI).
    /// </summary>
    private Bitmap? TryCaptureDxgi(int monitorIndex)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (monitorIndex < 0 || monitorIndex >= screens.Length) monitorIndex = 0;
        var screen = screens[monitorIndex];

        if (_dxgi != null && !string.Equals(_dxgi.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase))
        {
            _dxgi.Dispose();
            _dxgi = null;
        }

        if (_dxgi == null)
        {
            if (Environment.TickCount64 < _dxgiRetryAfterTick) return null;
            try
            {
                _dxgi = DxgiDesktopDuplicator.Create(screen.DeviceName);
                if (!string.Equals(_dxgiLoggedDevice, screen.DeviceName, StringComparison.Ordinal))
                {
                    Logger.Log($"ScreenCapture: DXGI desktop duplication active on {screen.DeviceName} ({_dxgi.Width}x{_dxgi.Height} readback)");
                    _dxgiLoggedDevice = screen.DeviceName;
                }
            }
            catch (Exception ex)
            {
                _dxgi = null;
                _dxgiRetryAfterTick = Environment.TickCount64 + DxgiRetryMs;
                if (!_dxgiFailureLogged)
                {
                    Logger.Log($"ScreenCapture: DXGI init failed on {screen.DeviceName}, using GDI fallback (retry every {DxgiRetryMs}ms): {ex.Message}");
                    _dxgiFailureLogged = true;
                }
                return null;
            }
        }

        // Cached bitmap must match the DXGI readback size.
        if (_cachedBmp == null || _cachedBmp.Width != _dxgi.Width || _cachedBmp.Height != _dxgi.Height)
        {
            _cachedBmp?.Dispose();
            _cachedBmp = new Bitmap(_dxgi.Width, _dxgi.Height, PixelFormat.Format32bppRgb);
            _cachedSrcW = _cachedSrcH = -1; // force GDI path to re-validate size if it runs
            _dxgi.ResetFrameState();
        }

        DxgiDesktopDuplicator.CaptureResult result;
        try
        {
            result = _dxgi.TryCapture(_cachedBmp);
        }
        catch (Exception ex)
        {
            // Unexpected failure (unsupported surface format, device removed): back off the
            // full retry interval so we don't rebuild the D3D device every frame.
            if (!_dxgiFailureLogged)
            {
                Logger.Log($"ScreenCapture: DXGI capture error, GDI fallback (retry every {DxgiRetryMs}ms): {ex.Message}");
                _dxgiFailureLogged = true;
            }
            _dxgiRetryAfterTick = Environment.TickCount64 + DxgiRetryMs;
            _dxgi.Dispose();
            _dxgi = null;
            return null;
        }

        if (result == DxgiDesktopDuplicator.CaptureResult.Lost)
        {
            _dxgiRetryAfterTick = Environment.TickCount64 + DxgiLostRetryMs;
            if (Environment.TickCount64 - _lastLostLogTick > 60_000)
            {
                Logger.Log($"ScreenCapture: DXGI lost ({_dxgi.LastLostReason}) — holding last frame while rebuilding");
                _lastLostLogTick = Environment.TickCount64;
            }
            // Mode change / fullscreen-exclusive switch / UAC or lock screen. Recreate on next
            // call; DuplicateOutput fails while the secure desktop is up, which the retry
            // backoff above absorbs (GDI covers the gap).
            _dxgi.Dispose();
            _dxgi = null;
            return null;
        }

        if (result == DxgiDesktopDuplicator.CaptureResult.NewFrame)
            _dxgiFailureLogged = false; // healthy again — log the next failure streak

        // NoChange before the first real frame → nothing valid in the bitmap yet.
        return _dxgi.HasFrame ? _cachedBmp : null;
    }

    private Bitmap? CaptureScreenGdi(Rectangle bounds)
    {
        // Downsampled target size — floored, minimum 1px per dim.
        int dw = Math.Max(1, bounds.Width / CaptureDownsample);
        int dh = Math.Max(1, bounds.Height / CaptureDownsample);

        // Allocate (or reallocate) the cached bitmap only when source resolution changes.
        if (_cachedBmp == null || _cachedSrcW != bounds.Width || _cachedSrcH != bounds.Height)
        {
            _cachedBmp?.Dispose();
            _cachedBmp = new Bitmap(dw, dh, PixelFormat.Format32bppRgb);
            _cachedSrcW = bounds.Width;
            _cachedSrcH = bounds.Height;
        }

        try
        {
            using var g = Graphics.FromImage(_cachedBmp);
            var destHdc = g.GetHdc();
            IntPtr srcHdc = IntPtr.Zero;
            try
            {
                srcHdc = GetDC(IntPtr.Zero);
                if (srcHdc == IntPtr.Zero) return null;

                // HALFTONE requires SetBrushOrgEx after SetStretchBltMode per MSDN.
                SetStretchBltMode(destHdc, HALFTONE);
                SetBrushOrgEx(destHdc, 0, 0, IntPtr.Zero);

                bool ok = StretchBlt(
                    destHdc, 0, 0, dw, dh,
                    srcHdc, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    SRCCOPY);

                if (!ok) return null;
            }
            finally
            {
                if (srcHdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, srcHdc);
                g.ReleaseHdc(destHdc);
            }
            return _cachedBmp;
        }
        catch
        {
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
        var hueBins = new double[HueBinCount * 4]; // per bin: linR, linG, linB (chroma-weighted), weight

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

                        // Edge lighting only cares about what's near the screen border. Interior
                        // cells (not in the first/last row or column) sample this column's top +
                        // bottom bands instead of the middle of the screen, so every consumer of
                        // the grid (side columns, top/bottom rows, flattened zones, spatial
                        // regions) sees edge content only.
                        bool interior = rows > 2 && cols > 2
                            && row > 0 && row < rows - 1 && col > 0 && col < cols - 1;
                        int bandH = rowHeight;
                        int y0a, y1a, y0b, y1b;
                        if (interior)
                        {
                            y0a = crop.Top; y1a = crop.Top + bandH;
                            y0b = crop.Bottom - bandH; y1b = crop.Bottom;
                        }
                        else
                        {
                            y0a = yStart; y1a = yEnd; y0b = y1b = 0;
                        }

                        // Colour (chroma) comes from non-dark pixels so dark UI doesn't grey it
                        // out; brightness comes from ALL pixels so a mostly-black edge with a few
                        // bright spots is dim instead of full brightness.
                        double rLin = 0, gLin = 0, bLin = 0, lumAll = 0;
                        long count = 0, total = 0;
                        Array.Clear(hueBins);

                        for (int pass = 0; pass < 2; pass++)
                        {
                            int ys = pass == 0 ? y0a : y0b, ye = pass == 0 ? y1a : y1b;
                            for (int y = ys; y < ye; y += SampleStride)
                            {
                                byte* rowPtr = ptr + y * stride;
                                for (int x = xStart; x < xEnd; x += SampleStride)
                                {
                                    int offset = x * 4;
                                    byte pb = rowPtr[offset];
                                    byte pg = rowPtr[offset + 1];
                                    byte pr = rowPtr[offset + 2];

                                    double lr = LinSq[pr], lg = LinSq[pg], lb = LinSq[pb];
                                    lumAll += 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
                                    total++;

                                    if (pr + pg + pb < DarkThreshold)
                                        continue;

                                    rLin += lr;
                                    gLin += lg;
                                    bLin += lb;
                                    count++;

                                    // Hue histogram, weighted by chroma, for picking the dominant
                                    // colour (a red sign on blue sky shouldn't average to purple).
                                    int mx = Math.Max(pr, Math.Max(pg, pb)), mn = Math.Min(pr, Math.Min(pg, pb));
                                    int chroma = mx - mn;
                                    if (chroma >= 32 && chroma * 4 >= mx) // reasonably saturated
                                    {
                                        int h6; // hue * 6 in [0, 6*chroma)
                                        if (mx == pr) h6 = (pg - pb + 6 * chroma) % (6 * chroma);
                                        else if (mx == pg) h6 = pb - pr + 2 * chroma;
                                        else h6 = pr - pg + 4 * chroma;
                                        int bin = Math.Clamp(h6 * HueBinCount / (6 * chroma), 0, HueBinCount - 1);
                                        int o = bin * 4;
                                        hueBins[o] += lr * chroma;
                                        hueBins[o + 1] += lg * chroma;
                                        hueBins[o + 2] += lb * chroma;
                                        hueBins[o + 3] += chroma;
                                    }
                                }
                            }
                        }

                        if (count > 0)
                        {
                            rLin /= count; gLin /= count; bLin /= count;

                            // Dominant hue: strongest group of 3 neighbouring bins. If it owns most
                            // of the colourful content, lean toward its average instead of the mean.
                            double hueTotal = 0, best = 0; int bestBin = -1;
                            for (int hb = 0; hb < HueBinCount; hb++) hueTotal += hueBins[hb * 4 + 3];
                            for (int hb = 0; hb < HueBinCount; hb++)
                            {
                                double w3 = hueBins[((hb + HueBinCount - 1) % HueBinCount) * 4 + 3]
                                    + hueBins[hb * 4 + 3] + hueBins[((hb + 1) % HueBinCount) * 4 + 3];
                                if (w3 > best) { best = w3; bestBin = hb; }
                            }
                            // Ignore when colourful pixels are a tiny fraction (mostly grey edge).
                            if (bestBin >= 0 && hueTotal > count * 8)
                            {
                                double dr = 0, dg = 0, db = 0;
                                for (int k = -1; k <= 1; k++)
                                {
                                    int o = ((bestBin + k + HueBinCount) % HueBinCount) * 4;
                                    dr += hueBins[o]; dg += hueBins[o + 1]; db += hueBins[o + 2];
                                }
                                dr /= best; dg /= best; db /= best;
                                double dominance = best / hueTotal;          // 0.25 (even spread) .. 1
                                // Capped at 0.6 so neighbouring cells don't snap to different hues (patchy strips).
                                double t = 0.6 * Math.Clamp((dominance - 0.45) / 0.4, 0, 1);
                                // Keep the cell's overall brightness; take the dominant hue.
                                double lm = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
                                double ld = 0.2126 * dr + 0.7152 * dg + 0.0722 * db;
                                if (ld > 1e-6) { double k2 = lm / ld; dr *= k2; dg *= k2; db *= k2; }
                                rLin += (dr - rLin) * t; gLin += (dg - gLin) * t; bLin += (db - bLin) * t;
                            }
                            double lumColor = 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
                            double lumMean = lumAll / total;
                            if (lumColor > 1e-6 && lumMean < lumColor)
                            {
                                // Slight lift (^0.8) keeps dim-but-coloured scenes visible on the
                                // LEDs while still tracking real screen brightness.
                                double scale = Math.Pow(lumMean / lumColor, 0.8);
                                rLin *= scale; gLin *= scale; bLin *= scale;
                            }
                            results[row, col] = (
                                (byte)Math.Clamp(Math.Sqrt(rLin) * 255, 0, 255),
                                (byte)Math.Clamp(Math.Sqrt(gLin) * 255, 0, 255),
                                (byte)Math.Clamp(Math.Sqrt(bLin) * 255, 0, 255));
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
        contentBounds.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        if (contentBounds.Width == 0 || contentBounds.Height == 0)
            return new (byte R, byte G, byte B)[zoneCount];
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

                            rLin += LinSq[pr];
                            gLin += LinSq[pg];
                            bLin += LinSq[pb];
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

                            // sRGB → linear (approximate gamma 2.2) via precomputed table
                            rLin += LinSq[pr];
                            gLin += LinSq[pg];
                            bLin += LinSq[pb];
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

                        rLin += LinSq[pr];
                        gLin += LinSq[pg];
                        bLin += LinSq[pb];
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
        lock (_lock)
        {
            _disposed = true;
            _dxgi?.Dispose();
            _dxgi = null;
            _cachedBmp?.Dispose();
            _cachedBmp = null;
        }
    }
}
