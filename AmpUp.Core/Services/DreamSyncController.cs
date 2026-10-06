using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using AmpUp.Core;
using AmpUp.Core.Models;
using AmpUp.Core.Interfaces;
using AmpUp.Core.Engine;

namespace AmpUp.Core.Services;

/// <summary>
/// DreamView / Screen Sync orchestrator.
/// Captures screen zones via an injected IScreenCapture, averages colors per zone, and pushes them
/// to configured Govee LAN devices via UDP. Capture runs at user-selected fps
/// (15/30/60) for smooth color averaging, but UDP sends are capped at 30fps
/// (Govee LED hardware can't transition faster — sending more causes flicker).
///
/// Architecture:
///   - One background task captures frames; a bounded worker owns UDP sends
///   - IScreenCapture handles platform-specific screen capture + zone sampling
///   - A persistent UdpClient per device avoids per-frame socket allocation
///   - Delta threshold suppresses sends when color hasn't changed enough
/// </summary>
public class DreamSyncController : IDisposable
{
    private ScreenSyncConfig _config;
    private AmbienceConfig _ambience;
    private readonly IScreenCapture _capture;
    private readonly object _lock = new();
    private bool _disposed;
    private ScreenSpatialMapper? _spatialMapper;

    // Running state
    private CancellationTokenSource? _cts;
    private Thread? _loopThread;
    private volatile bool _resetSendStateRequested; // set by SetSuspended/Stop, consumed by capture thread

    // Grid rows sampled per frame. 6 gives vertical bars / spatial side regions real vertical
    // resolution (3 rows meant a 10-segment side bar showed only 3 distinct colors). Top/Bottom
    // edges use the outer third (EdgeRows) to match the old 3-row behaviour.
    // 12 rows: enough vertical resolution for tall wall lights (e.g. 12 segments per side).
    private const int GridRows = 12;
    private const int EdgeRows = 2; // top/bottom bars read the outer ~17% of the screen

    // UI grid preview is throttled — the RoomView handler allocates a brush per cell on the
    // dispatcher, which is wasted work at 60 fps while a game is running.
    private const int PreviewIntervalMs = 66;
    private long _lastGridPreviewTick;

    // Latest-wins color frame per device. The channel only carries a small flush token for
    // these, so at most ONE color frame per device is ever pending — a slow network drops stale
    // frames instead of replaying a backlog. Control packets (enable/brightness) stay in order
    // in the channel.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, UdpSendRequest> _latestColorFrame = new();
    private volatile bool _running;
    private volatile bool _suspended;

    // Per-device persistent UDP sockets (avoids allocation per frame)
    private readonly Dictionary<string, UdpClient> _udpClients = new();
    private readonly Channel<UdpSendRequest> _udpSendQueue = Channel.CreateBounded<UdpSendRequest>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly CancellationTokenSource _udpSendCts = new();
    private readonly Task _udpSendTask;
    private readonly Dictionary<string, long> _udpErrorLogTick = new();
    private long _udpSendGeneration;
    private const int LanControlPort = 4003;
    private const long UdpErrorLogIntervalMs = 30_000;

    // Per-device last-sent color for delta suppression (single-color fallback)
    private readonly Dictionary<string, (byte R, byte G, byte B)> _lastSent = new();

    // Per-segment protocol state
    private readonly HashSet<string> _segmentEnabled = new();        // devices with segment mode active
    private readonly Dictionary<string, long> _segmentEnableTick = new(); // keepalive timer
    private readonly Dictionary<string, long> _segmentBrightnessResetTick = new();
    private readonly Dictionary<string, (byte R, byte G, byte B)[]> _lastSegmentColors = new();
    private const long SegmentKeepaliveMs = 30_000; // re-send enable every 30s (device times out ~60s)
    private const long SegmentBrightnessResetMs = 30_000;

    // Cap UDP send rate at 30fps (hardware can't transition faster — sending more causes flicker)
    private const int MaxSendFps = 30;
    private readonly System.Diagnostics.Stopwatch _sendThrottle = System.Diagnostics.Stopwatch.StartNew();

    // Cached IP → device lookup for the send loop. Avoids a FirstOrDefault closure
    // allocation per mapping per frame. Rebuilt when the device list instance or
    // its count changes (only the capture-loop thread touches these fields).
    private Dictionary<string, GoveeDeviceConfig>? _devicesByIp;
    private List<GoveeDeviceConfig>? _devicesByIpSource;
    private int _devicesByIpCount;

    private Dictionary<string, GoveeDeviceConfig> GetDevicesByIp(List<GoveeDeviceConfig> devices)
    {
        if (_devicesByIp == null
            || !ReferenceEquals(_devicesByIpSource, devices)
            || _devicesByIpCount != devices.Count)
        {
            var map = new Dictionary<string, GoveeDeviceConfig>(devices.Count);
            foreach (var d in devices)
            {
                // TryAdd keeps the FIRST device per IP — matches FirstOrDefault semantics.
                if (!string.IsNullOrEmpty(d.Ip))
                    map.TryAdd(d.Ip, d);
            }
            _devicesByIp = map;
            _devicesByIpSource = devices;
            _devicesByIpCount = devices.Count;
        }
        return _devicesByIp;
    }

    // Raised each frame with current zone colors — used by the live preview in RoomView
    public event Action<(byte R, byte G, byte B)[]>? OnZoneColors;

    // Raised each frame with the 2D zone grid — used by ScreenEdgeControl for spatial preview
    public event Action<(byte R, byte G, byte B)[,], int, int>? OnZoneGrid;

    // Status text for UI display (e.g. "Syncing at 30fps" / "Stopped")
    public string Status { get; private set; } = "Stopped";

    public bool IsRunning => _running;

    public void SetSuspended(bool suspended)
    {
        if (suspended)
        {
            // The send-state dictionaries belong to the capture thread; clearing them here
            // (UI / power-event thread) raced the loop. Ask the loop to do it instead.
            _resetSendStateRequested = true;
            Interlocked.Increment(ref _udpSendGeneration); // drop anything still queued
            Status = "Suspended";
        }
        _suspended = suspended;
    }

    private void ResetSendState()
    {
        _lastSent.Clear();
        _segmentEnabled.Clear();
        _segmentEnableTick.Clear();
        _segmentBrightnessResetTick.Clear();
        _lastSegmentColors.Clear();
        _smoothState.Clear();
    }

    public DreamSyncController(ScreenSyncConfig config, AmbienceConfig ambience, IScreenCapture capture)
    {
        _config = config;
        _ambience = ambience;
        _capture = capture;
        _udpSendTask = Task.Run(() => RunUdpSendLoopAsync(_udpSendCts.Token));
    }

    public void UpdateConfig(ScreenSyncConfig config, AmbienceConfig ambience)
    {
        lock (_lock)
        {
            _config = config;
            _ambience = ambience;
        }

        // If enabled state changed, start/stop accordingly
        if (config.Enabled && !_running)
            Start();
        else if (!config.Enabled && _running)
            Stop();
    }

    public void SetSpatialMapper(ScreenSpatialMapper? mapper)
    {
        lock (_lock) { _spatialMapper = mapper; }
    }

    /// <summary>
    /// One-shot screen capture for preview only — does not send to any devices.
    /// Returns the 2D zone grid, or null if capture fails.
    /// </summary>
    public (byte R, byte G, byte B)[,]? CapturePreviewGrid()
    {
        ScreenSyncConfig cfg;
        lock (_lock) { cfg = _config; }

        ContentBounds? crop = cfg.CropBlackBars ? cfg.ContentBounds : null;
        if (crop == null && (cfg.ContentBounds.LeftPct > 0 || cfg.ContentBounds.RightPct > 0
            || cfg.ContentBounds.TopPct > 0 || cfg.ContentBounds.BottomPct > 0))
            crop = cfg.ContentBounds;

        int cols = cfg.ZoneCount;
        int rows = GridRows;
        return _capture.CaptureZoneGrid(cfg.MonitorIndex, cols, rows, crop);
    }

    // ── Start / Stop ─────────────────────────────────────────────────────────

    public void Start()
    {
        if (_disposed || _running) return;
        Interlocked.Increment(ref _udpSendGeneration);
        _running = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        // Dedicated BelowNormal thread: the loop is a steady 30-60 Hz wakeup, so keep it off
        // the thread pool and let the game win any CPU contention.
        _loopThread = new Thread(() => RunLoop(token))
        {
            IsBackground = true,
            Name = "AmpUp ScreenSync",
            Priority = ThreadPriority.BelowNormal,
        };
        _loopThread.Start();
        Logger.Log("DreamSync: started");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        // Invalidate queued frames from this run. The UDP worker may still be
        // draining while a later Start begins, but stale screen colors must not
        // leak into the stopped/new session.
        Interlocked.Increment(ref _udpSendGeneration);
        _cts?.Cancel();
        bool exited = true;
        try
        {
            if (_loopThread != null && _loopThread != Thread.CurrentThread)
                exited = _loopThread.Join(2000);
        }
        catch { }
        _loopThread = null;
        // Don't dispose the CTS if the loop is still alive — it may still touch WaitHandle.
        if (exited) _cts?.Dispose();
        _cts = null;
        // Clear tracking without sending disable commands — Game Mode cycles start/stop
        // frequently and sending segment-disable kills room effects using segments.
        // Segments auto-timeout on the device after ~60s without keepalive frames.
        if (exited) ResetSendState();
        else _resetSendStateRequested = true;
        Status = "Stopped";
        Logger.Log("DreamSync: stopped");
    }

    // ── Capture loop ─────────────────────────────────────────────────────────

    private void RunLoop(CancellationToken ct)
    {
        WaitHandle wake;
        try { wake = ct.WaitHandle; } catch (ObjectDisposedException) { return; }

        while (!ct.IsCancellationRequested)
        {
            if (_resetSendStateRequested)
            {
                _resetSendStateRequested = false;
                ResetSendState();
            }

            if (_suspended)
            {
                Status = "Suspended";
                if (wake.WaitOne(250)) break;
                continue;
            }

            ScreenSyncConfig cfg;
            AmbienceConfig amb;
            lock (_lock) { cfg = _config; amb = _ambience; }

            int fps = cfg.TargetFps switch { 60 => 60, 15 => 15, _ => 30 };
            int delayMs = 1000 / fps;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Determine content crop bounds
                ContentBounds? contentCrop = cfg.CropBlackBars ? cfg.ContentBounds : null;

                // Try 2D grid capture first (preferred — enables spatial mapping)
                int gridCols = cfg.ZoneCount;
                int gridRows = GridRows;
                var grid = _capture.CaptureZoneGrid(cfg.MonitorIndex, gridCols, gridRows, contentCrop);

                // Flat zone array (for legacy path + UI preview)
                (byte R, byte G, byte B)[]? zones = null;

                if (grid != null)
                {
                    // Flatten 2D grid to 1D for backward compat (average rows per column)
                    zones = FlattenGridToHorizontal(grid, gridCols, gridRows);
                }
                else
                {
                    // Fallback: use legacy 1D capture
                    zones = _capture.CaptureZones(cfg.MonitorIndex, cfg.ZoneCount, cfg.CropBlackBars);
                }

                if (zones != null)
                {
                    // Check if any device needs FullScreen (uncropped) colors. Resolve this
                    // BEFORE the saturation boost: the original FullScreen path sampled raw
                    // (unboosted) colors via a second capture, so when this frame's capture
                    // had no effective crop we can snapshot the pre-boost zones instead of
                    // running a second StretchBlt + LockBits + sampling pass.
                    bool needFullScreen = false;
                    foreach (var mapping in cfg.DeviceMappings)
                    {
                        if (mapping.CropMode == DeviceCropMode.FullScreen && !mapping.UseAutoSpatial)
                        {
                            needFullScreen = true;
                            break;
                        }
                    }
                    (byte R, byte G, byte B)[]? fullScreenZones = null;
                    if (needFullScreen)
                    {
                        // grid path: crop is effective only if contentCrop has meaningful
                        // percentages (auto-detect writes them back during CaptureZoneGrid).
                        // fallback path: CaptureZones cropped only when CropBlackBars is on.
                        bool zonesAreFullScreen = grid != null
                            ? !HasEffectiveCrop(contentCrop)
                            : !cfg.CropBlackBars;

                        if (zonesAreFullScreen)
                            fullScreenZones = ((byte R, byte G, byte B)[])zones.Clone();
                        else
                            // An actual crop was applied to this frame's sampling — the
                            // IScreenCapture interface has no resample-last-frame API, so a
                            // second (uncropped) capture is still required in this case.
                            fullScreenZones = _capture.CaptureZones(cfg.MonitorIndex, cfg.ZoneCount, false);
                    }

                    // Boost saturation on flat zones (used for UI preview + legacy path)
                    for (int i = 0; i < zones.Length; i++)
                    {
                        var (r, g, b) = zones[i];
                        zones[i] = BoostSaturation(r, g, b, cfg.Saturation);
                    }

                    // Also boost saturation on the 2D grid if available
                    if (grid != null)
                    {
                        for (int row = 0; row < gridRows; row++)
                            for (int col = 0; col < gridCols; col++)
                            {
                                var (r, g, b) = grid[row, col];
                                grid[row, col] = BoostSaturation(r, g, b, cfg.Saturation);
                            }
                    }

                    // Notify UI for live preview (fire and forget — UI marshal on receipt)
                    // OnZoneColors also feeds Turn Up / Corsair, so it fires every frame.
                    OnZoneColors?.Invoke(zones);
                    if (grid != null)
                    {
                        long nowTick = Environment.TickCount64;
                        if (nowTick - _lastGridPreviewTick >= PreviewIntervalMs)
                        {
                            _lastGridPreviewTick = nowTick;
                            OnZoneGrid?.Invoke(grid, gridCols, gridRows);
                        }
                    }

                    // Snapshot spatial mapper under lock
                    ScreenSpatialMapper? spatialMapper;
                    lock (_lock) { spatialMapper = _spatialMapper; }

                    // Push to each configured Govee device (capped at 30fps — hardware limit)
                    bool sendThisFrame = _sendThrottle.ElapsedMilliseconds >= (1000 / MaxSendFps);
                    if (sendThisFrame && amb.GoveeEnabled && amb.GoveeDevices.Count > 0)
                    {
                        var devicesByIp = GetDevicesByIp(amb.GoveeDevices);
                        foreach (var mapping in cfg.DeviceMappings)
                        {
                            if (string.IsNullOrWhiteSpace(mapping.DeviceIp)) continue;

                            if (!devicesByIp.TryGetValue(mapping.DeviceIp, out var dev)) continue;
                            if (!dev.PoweredOn || !dev.SyncWithAmpUp) continue;

                            int segCount = AmbienceSync.GetSegmentCount(dev);
                            bool useSegments = segCount > 0 && dev.UseSegmentProtocol;

                            if (useSegments)
                            {
                                // ── Per-segment path ──
                                // Enable segment mode if not yet active or keepalive expired
                                long nowMs = Environment.TickCount64;
                                EnsureSegmentHardwareBrightness(mapping.DeviceIp, nowMs);
                                if (!_segmentEnabled.Contains(mapping.DeviceIp) ||
                                    nowMs - (_segmentEnableTick.GetValueOrDefault(mapping.DeviceIp)) > SegmentKeepaliveMs)
                                {
                                    SendSegmentEnable(mapping.DeviceIp, true);
                                    _segmentEnabled.Add(mapping.DeviceIp);
                                    _segmentEnableTick[mapping.DeviceIp] = nowMs;
                                }

                                // Map screen zones to device segments (side-aware for edge glow)
                                (byte R, byte G, byte B)[] segColors;

                                if (mapping.UseAutoSpatial && spatialMapper != null && grid != null)
                                {
                                    // ── Spatial mapping path (2D grid) ──
                                    string deviceKey = dev.Ip ?? mapping.DeviceIp;
                                    var region = spatialMapper.GetRegion(deviceKey);
                                    var splitRight = spatialMapper.GetRegion(deviceKey + ":R");
                                    if (region.HasValue && splitRight.HasValue && segCount >= 2)
                                    {
                                        // SplitLR layout (paired lights): the mapper emits two regions
                                        // but this path used to ignore the ":R" one. Same wiring as the
                                        // non-spatial paired path: first half = right unit (reversed),
                                        // second half = left unit.
                                        int half = segCount / 2;
                                        var rightCols = MapZonesToSegmentsSpatial(grid, gridCols, gridRows, splitRight.Value, half);
                                        var leftCols = MapZonesToSegmentsSpatial(grid, gridCols, gridRows, region.Value, segCount - half);
                                        segColors = new (byte R, byte G, byte B)[segCount];
                                        for (int si = 0; si < half; si++)
                                            segColors[si] = rightCols[half - 1 - si];
                                        Array.Copy(leftCols, 0, segColors, half, segCount - half);
                                    }
                                    else if (region.HasValue)
                                    {
                                        segColors = MapZonesToSegmentsSpatial(grid, gridCols, gridRows, region.Value, segCount);
                                        // Honor the layout's "reverse segment order" flag.
                                        if (region.Value.Reversed) Array.Reverse(segColors);
                                    }
                                    else
                                    {
                                        // Fallback if device not in spatial layout
                                        segColors = MapZonesToSegments(zones, segCount, mapping.Side);
                                    }
                                }
                                else if (AmbienceSync.IsPairedDevice(dev.Sku))
                                {
                                    int half = segCount / 2;
                                    if ((mapping.Side == ZoneSide.LeftVertical || mapping.Side == ZoneSide.RightVertical) && grid != null)
                                    {
                                        // Paired vertical: left panel (first half) = left screen edge top→bottom
                                        //                  right panel (second half) = right screen edge top→bottom
                                        var leftPanelColors = MapGridToSegmentsVertical(grid, gridCols, gridRows, half, ZoneSide.LeftVertical);
                                        var rightPanelColors = MapGridToSegmentsVertical(grid, gridCols, gridRows, segCount - half, ZoneSide.RightVertical);
                                        segColors = new (byte R, byte G, byte B)[segCount];
                                        Array.Copy(leftPanelColors, 0, segColors, 0, half);
                                        Array.Copy(rightPanelColors, 0, segColors, half, segCount - half);
                                    }
                                    else
                                    {
                                        // Paired horizontal (default): first half = right screen edge, second half = left
                                        // (H610A wiring: segments 0-5 = right panel, 6-11 = left panel)
                                        var effectiveZones = (mapping.CropMode == DeviceCropMode.FullScreen && fullScreenZones != null)
                                            ? fullScreenZones : zones;
                                        var leftColors = MapZonesToSegments(effectiveZones, half, ZoneSide.Right);
                                        var rightColors = MapZonesToSegments(effectiveZones, segCount - half, ZoneSide.Left);
                                        segColors = new (byte R, byte G, byte B)[segCount];
                                        // Reverse first panel to match physical orientation
                                        for (int si = 0; si < half; si++)
                                            segColors[si] = leftColors[half - 1 - si];
                                        Array.Copy(rightColors, 0, segColors, half, segCount - half);
                                    }
                                }
                                else if ((mapping.Side == ZoneSide.LeftVertical || mapping.Side == ZoneSide.RightVertical) && grid != null)
                                {
                                    // Vertical light bars — sample top-to-bottom from left/right columns of 2D grid
                                    segColors = MapGridToSegmentsVertical(grid, gridCols, gridRows, segCount, mapping.Side);
                                }
                                else if ((mapping.Side == ZoneSide.Top || mapping.Side == ZoneSide.Bottom) && grid != null)
                                {
                                    // Bar along the top/bottom edge: full width, left→right, sampled from
                                    // the outer third of the screen. (The 1D zone path treated Top/Bottom
                                    // as the left/right QUARTER of the screen.)
                                    segColors = MapZonesToSegmentsSpatial(grid, gridCols, gridRows,
                                        EdgeRegion(mapping.Side, gridRows), segCount);
                                }
                                else
                                {
                                    var effectiveZones = (mapping.CropMode == DeviceCropMode.FullScreen && fullScreenZones != null)
                                        ? fullScreenZones : zones;
                                    segColors = MapZonesToSegments(effectiveZones, segCount, mapping.Side);
                                }

                                segColors = SmoothColors(mapping.DeviceIp, segColors);

                                int brightnessScale = CombinedBrightnessScale(amb.BrightnessScale, dev.BrightnessScale);
                                for (int s = 0; s < segColors.Length; s++)
                                    segColors[s] = ApplyWhiteBalance(ApplyBrightness(LedChromaGamma(segColors[s]), brightnessScale), dev);

                                // Delta check across all segments
                                if (SegmentColorsChanged(mapping.DeviceIp, segColors, ChangeDeadband(cfg.Sensitivity)))
                                {
                                    _lastSegmentColors[mapping.DeviceIp] = segColors;
                                    SendSegmentColors(mapping.DeviceIp, segColors);
                                }
                            }
                            else
                            {
                                // ── Single-color fallback (colorwc) ──
                                var effectiveZones = (mapping.CropMode == DeviceCropMode.FullScreen && fullScreenZones != null)
                                    ? fullScreenZones : zones;
                                var color = (mapping.Side == ZoneSide.Top || mapping.Side == ZoneSide.Bottom) && grid != null
                                    ? AverageRegion(grid, gridCols, gridRows, EdgeRegion(mapping.Side, gridRows))
                                    : SampleColorForSide(effectiveZones, effectiveZones.Length, mapping.Side);
                                color = SmoothColors(mapping.DeviceIp, new[] { color })[0];
                                color = ApplyWhiteBalance(ApplyBrightness(LedChromaGamma(color), CombinedBrightnessScale(amb.BrightnessScale, dev.BrightnessScale)), dev);

                                int sensitivity = ChangeDeadband(cfg.Sensitivity);
                                if (_lastSent.TryGetValue(mapping.DeviceIp, out var prev))
                                {
                                    int dr = Math.Abs(color.R - prev.R);
                                    int dg = Math.Abs(color.G - prev.G);
                                    int db = Math.Abs(color.B - prev.B);
                                    if (dr <= sensitivity && dg <= sensitivity && db <= sensitivity)
                                        continue;
                                }

                                _lastSent[mapping.DeviceIp] = color;
                                SendColorFast(mapping.DeviceIp, color.R, color.G, color.B);
                            }
                        }
                    }

                    if (sendThisFrame)
                        _sendThrottle.Restart();
                    Status = $"Syncing at {fps}fps (send ≤{MaxSendFps}fps)";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"DreamSync loop error: {ex.Message}");
            }

            // Sleep for the remainder of the frame interval
            int elapsed = (int)sw.ElapsedMilliseconds;
            // Cap the loop's duty cycle at ~25% of one core: if a frame took longer than budgeted
            // (big monitors, GDI contention with a game), idle at least 3x the work time so the
            // effective FPS drops instead of the game stuttering.
            int remaining = Math.Max(delayMs - elapsed, elapsed * 3);
            if (remaining > 1 && wake.WaitOne(remaining)) break;
        }

        Status = "Stopped";
    }

    /// <summary>
    /// True when the content bounds describe a meaningful crop (&gt; 0.5% on any side).
    /// Auto-detect writes detected percentages back into the ContentBounds during
    /// CaptureZoneGrid, so this reflects the crop actually applied this frame.
    /// </summary>
    private static bool HasEffectiveCrop(ContentBounds? crop)
        => crop != null && (crop.LeftPct > 0.005 || crop.RightPct > 0.005
            || crop.TopPct > 0.005 || crop.BottomPct > 0.005);

    // ── Grid helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Flatten a 2D zone grid to a 1D horizontal array by averaging rows per column.
    /// Used for backward compatibility with OnZoneColors event and legacy mapping path.
    /// </summary>
    private static (byte R, byte G, byte B)[] FlattenGridToHorizontal(
        (byte R, byte G, byte B)[,] grid, int cols, int rows)
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
    /// Map a 2D zone grid to device segments using a spatial screen region.
    /// Vertical edge devices (Left/Right): segments map top→bottom across rows.
    /// Horizontal edge devices (Top/Bottom): segments map left→right across cols.
    /// Full: segments spread left→right, averaging all rows in range.
    /// </summary>
    private static (byte R, byte G, byte B)[] MapZonesToSegmentsSpatial(
        (byte R, byte G, byte B)[,] grid, int cols, int rows,
        ScreenSpatialMapper.ScreenRegion region, int segmentCount)
    {
        var result = new (byte R, byte G, byte B)[segmentCount];

        // Convert normalized region bounds to grid indices (clamped)
        // Small epsilon so float round-off (e.g. 2f/6*6 = 2.0000002) doesn't pull in an extra row/col.
        const float eps = 1e-3f;
        int colStart = Math.Clamp((int)(region.XStart * cols + eps), 0, cols - 1);
        int colEnd = Math.Clamp((int)Math.Ceiling(region.XEnd * cols - eps), colStart + 1, cols);
        int rowStart = Math.Clamp((int)(region.YStart * rows + eps), 0, rows - 1);
        int rowEnd = Math.Clamp((int)Math.Ceiling(region.YEnd * rows - eps), rowStart + 1, rows);

        int colRange = Math.Max(colEnd - colStart, 1);
        int rowRange = Math.Max(rowEnd - rowStart, 1);

        bool vertical = region.PrimaryEdge == ZoneSide.Left || region.PrimaryEdge == ZoneSide.Right;

        if (vertical)
        {
            // Side lights follow the outermost screen column only — inner columns would mix in
            // content from further toward the middle of the screen.
            if (region.PrimaryEdge == ZoneSide.Left) { colStart = 0; colEnd = 1; }
            else { colStart = cols - 1; colEnd = cols; }

            // Segments map top→bottom across rows, averaging cols in range
            for (int seg = 0; seg < segmentCount; seg++)
            {
                float segRowStart = rowStart + (float)seg / segmentCount * rowRange;
                float segRowEnd = rowStart + (float)(seg + 1) / segmentCount * rowRange;

                int r = 0, g = 0, b = 0, count = 0;
                for (int row = (int)segRowStart; row < (int)Math.Ceiling(segRowEnd) && row < rowEnd; row++)
                {
                    for (int col = colStart; col < colEnd; col++)
                    {
                        r += grid[row, col].R;
                        g += grid[row, col].G;
                        b += grid[row, col].B;
                        count++;
                    }
                }
                if (count > 0)
                    result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
        }
        else
        {
            // Horizontal (Top/Bottom/Full): segments map left→right across cols, averaging rows in range
            for (int seg = 0; seg < segmentCount; seg++)
            {
                float segColStart = colStart + (float)seg / segmentCount * colRange;
                float segColEnd = colStart + (float)(seg + 1) / segmentCount * colRange;

                int r = 0, g = 0, b = 0, count = 0;
                for (int col = (int)segColStart; col < (int)Math.Ceiling(segColEnd) && col < colEnd; col++)
                {
                    for (int row = rowStart; row < rowEnd; row++)
                    {
                        r += grid[row, col].R;
                        g += grid[row, col].G;
                        b += grid[row, col].B;
                        count++;
                    }
                }
                if (count > 0)
                    result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
        }

        return result;
    }

    private static ScreenSpatialMapper.ScreenRegion EdgeRegion(ZoneSide side, int rows)
    {
        float edge = (float)EdgeRows / rows;
        return new ScreenSpatialMapper.ScreenRegion
        {
            XStart = 0, XEnd = 1,
            YStart = side == ZoneSide.Top ? 0f : 1f - edge,
            YEnd = side == ZoneSide.Top ? edge : 1f,
            PrimaryEdge = side,
        };
    }

    private static (byte R, byte G, byte B) AverageRegion(
        (byte R, byte G, byte B)[,] grid, int cols, int rows, ScreenSpatialMapper.ScreenRegion region)
        => MapZonesToSegmentsSpatial(grid, cols, rows, region, 1)[0];

    // ── Color helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Map a ZoneSide to an averaged color from the zone array.
    /// Left = average of left half of zones, Right = right half, Full = all zones.
    /// </summary>
    private static (byte R, byte G, byte B) SampleColorForSide(
        (byte R, byte G, byte B)[] zones, int zoneCount, ZoneSide side)
    {
        int start = 0, end = zoneCount;
        switch (side)
        {
            case ZoneSide.Left:
                end = zoneCount / 2;
                break;
            case ZoneSide.Right:
                start = zoneCount / 2;
                break;
            case ZoneSide.Top:
                end = zoneCount / 2;
                break;
            case ZoneSide.Bottom:
                start = zoneCount / 2;
                break;
            case ZoneSide.LeftVertical:
                end = Math.Max(zoneCount / 4, 1); // treat as left for single-color fallback
                break;
            case ZoneSide.RightVertical:
                start = zoneCount - Math.Max(zoneCount / 4, 1);
                break;
            // Full: use all zones
        }

        end = Math.Max(end, start + 1); // guard against 0-length
        int r = 0, g = 0, b = 0, count = 0;
        for (int i = start; i < end && i < zones.Length; i++)
        {
            r += zones[i].R;
            g += zones[i].G;
            b += zones[i].B;
            count++;
        }
        if (count == 0) return (0, 0, 0);
        return ((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static (byte R, byte G, byte B) ApplyBrightness((byte R, byte G, byte B) c, int scale)
    {
        scale = Math.Clamp(scale, 0, 100);
        return (
            (byte)(c.R * scale / 100),
            (byte)(c.G * scale / 100),
            (byte)(c.B * scale / 100)
        );
    }

    /// <summary>
    /// LEDs are linear emitters, so small secondary channels that barely show on a monitor glow
    /// strongly on a light (screen red ≈ 220,40,50 turns pink). Apply gamma to each channel
    /// RELATIVE to the strongest one: the dominant channel keeps its level (no dimming) while the
    /// minor channels drop the way they would on a display. Greys/whites are unaffected.
    /// </summary>
    private const float LedChromaGammaExp = 2.2f;
    private static (byte R, byte G, byte B) LedChromaGamma((byte R, byte G, byte B) c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        if (max == 0) return c;
        float m = max;
        byte F(byte v) => (byte)Math.Clamp(MathF.Pow(v / m, LedChromaGammaExp) * m + 0.5f, 0f, 255f);
        return (F(c.R), F(c.G), F(c.B));
    }

    private static (byte R, byte G, byte B) ApplyWhiteBalance((byte R, byte G, byte B) c, GoveeDeviceConfig dev)
    {
        int wr = Math.Clamp(dev.WhiteBalanceR, 0, 100), wg = Math.Clamp(dev.WhiteBalanceG, 0, 100),
            wb = Math.Clamp(dev.WhiteBalanceB, 0, 100);
        if (wr == 100 && wg == 100 && wb == 100) return c;
        return ((byte)(c.R * wr / 100), (byte)(c.G * wg / 100), (byte)(c.B * wb / 100));
    }

    private static int CombinedBrightnessScale(int globalScale, int deviceScale)
    {
        return Math.Clamp(globalScale, 0, 100) * Math.Clamp(deviceScale, 0, 100) / 100;
    }

    /// <summary>
    /// Apply HSV saturation multiplier to an RGB color. Clamps to 0-255.
    /// </summary>
    private static (byte R, byte G, byte B) BoostSaturation(byte r, byte g, byte b, float saturation)
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

        // Near-black: go dark instead of glowing. Dark game scenes carry a faint blue/grey
        // tint that the saturation boost used to turn into a solid blue room.
        if (v < 0.05f) return (0, 0, 0);

        // Fade the boost in with brightness so dim, nearly-neutral colours aren't pushed
        // into a strong hue; full boost from ~35% value up.
        float boostWeight = Math.Clamp((v - 0.05f) / 0.30f, 0f, 1f);
        float effSat = 1f + (saturation - 1f) * boostWeight;
        // Low-chroma pixels (greys) stay grey-ish: scale the boost down when s is tiny.
        if (s < 0.15f) effSat = 1f + (effSat - 1f) * (s / 0.15f);
        s = Math.Clamp(s * effSat, 0f, 1f);

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

    // ── UDP send (persistent socket, bounded single-writer loop) ──────────────

    private readonly record struct UdpSendRequest(string Ip, byte[] Data, long Generation, bool IsFlushToken = false);

    /// <summary>Queue a color frame; replaces any not-yet-sent color frame for the same device.</summary>
    private void QueueLatestColorFrame(string ip, byte[] data)
    {
        if (_disposed || !_running || string.IsNullOrWhiteSpace(ip) || data.Length == 0)
            return;

        var req = new UdpSendRequest(ip, data, Volatile.Read(ref _udpSendGeneration));
        if (_latestColorFrame.TryAdd(ip, req))
        {
            // First pending frame for this device — wake the sender with a flush token.
            _udpSendQueue.Writer.TryWrite(new UdpSendRequest(ip, Array.Empty<byte>(), req.Generation, IsFlushToken: true));
        }
        else
        {
            // A flush token is already queued; just replace the payload it will pick up.
            // (If the sender TryRemove'd in between, AddOrUpdate re-adds without a token, so
            // re-check and enqueue one in that case.)
            bool added = false;
            _latestColorFrame.AddOrUpdate(ip, _ => { added = true; return req; }, (_, _) => req);
            if (added)
                _udpSendQueue.Writer.TryWrite(new UdpSendRequest(ip, Array.Empty<byte>(), req.Generation, IsFlushToken: true));
        }
    }

    private void QueueUdpSend(string ip, byte[] data, long? generation = null)
    {
        if (_disposed || !_running || string.IsNullOrWhiteSpace(ip) || data.Length == 0)
            return;

        // Screen sync is latest-state data. A bounded queue prevents a slow or
        // unavailable network from retaining an unbounded number of old frames.
        _udpSendQueue.Writer.TryWrite(new UdpSendRequest(
            ip, data, generation ?? Volatile.Read(ref _udpSendGeneration)));
    }

    private async Task RunUdpSendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var queued in _udpSendQueue.Reader.ReadAllAsync(ct))
            {
                var request = queued;
                if (request.IsFlushToken && !_latestColorFrame.TryRemove(request.Ip, out request))
                    continue;

                if (request.Generation != Volatile.Read(ref _udpSendGeneration))
                    continue;

                try
                {
                    if (!_udpClients.TryGetValue(request.Ip, out var udp))
                    {
                        udp = new UdpClient();
                        _udpClients[request.Ip] = udp;
                    }

                    await udp.SendAsync(request.Data.AsMemory(), request.Ip, LanControlPort, ct);
                    _udpErrorLogTick.Remove(request.Ip);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    long nowMs = Environment.TickCount64;
                    if (!_udpErrorLogTick.TryGetValue(request.Ip, out long lastLog)
                        || nowMs - lastLog >= UdpErrorLogIntervalMs)
                    {
                        _udpErrorLogTick[request.Ip] = nowMs;
                        Logger.Log($"DreamSync UDP send to {request.Ip} failed: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void SendColorFast(string ip, byte r, byte g, byte b)
    {
        // Build JSON payload
        string json = $"{{\"msg\":{{\"cmd\":\"colorwc\",\"data\":{{\"color\":{{\"r\":{r},\"g\":{g},\"b\":{b}}},\"colorTemInKelvin\":0}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        QueueLatestColorFrame(ip, data);
    }

    private void SendBrightnessFast(string ip, int brightness, long? generation = null)
    {
        brightness = Math.Clamp(brightness, 0, 100);
        string json = $"{{\"msg\":{{\"cmd\":\"brightness\",\"data\":{{\"value\":{brightness}}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        QueueUdpSend(ip, data, generation);
    }

    private void EnsureSegmentHardwareBrightness(string ip, long nowMs)
    {
        if (_segmentBrightnessResetTick.TryGetValue(ip, out long lastReset)
            && nowMs - lastReset < SegmentBrightnessResetMs)
            return;

        _segmentBrightnessResetTick[ip] = nowMs;
        long generation = Volatile.Read(ref _udpSendGeneration);
        SendBrightnessFast(ip, 100, generation);
        _ = QueueSegmentEnableAfterDelayAsync(ip, generation);
    }

    private async Task QueueSegmentEnableAfterDelayAsync(string ip, long generation)
    {
        try
        {
            await Task.Delay(35, _udpSendCts.Token).ConfigureAwait(false);
            SendSegmentEnable(ip, true, generation);
        }
        catch (OperationCanceledException) when (_udpSendCts.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    // ── Per-segment protocol (Govee "razer" command) ─────────────────────────

    private static byte XorChecksum(byte[] data, int length)
    {
        byte xor = 0;
        for (int i = 0; i < length; i++) xor ^= data[i];
        return xor;
    }

    private void SendSegmentEnable(string ip, bool enable, long? generation = null)
    {
        // Binary: BB 00 01 B1 [01=enable/00=disable] [xor]
        var pkt = new byte[] { 0xBB, 0x00, 0x01, 0xB1, (byte)(enable ? 1 : 0), 0 };
        pkt[5] = XorChecksum(pkt, 5);

        string b64 = Convert.ToBase64String(pkt);
        string json = $"{{\"msg\":{{\"cmd\":\"razer\",\"data\":{{\"pt\":\"{b64}\"}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        QueueUdpSend(ip, data, generation);
    }

    private void SendSegmentColors(string ip, (byte R, byte G, byte B)[] colors)
    {
        // Binary: BB [len_hi] [len_lo] B0 00 [count] [R G B]... [xor]
        int count = colors.Length;
        int payloadLen = 2 + 1 + count * 3; // B0 00 + count + RGB data
        int totalLen = 3 + payloadLen + 1;   // header(3) + payload + checksum
        var pkt = new byte[totalLen];

        pkt[0] = 0xBB;
        pkt[1] = (byte)(payloadLen >> 8);
        pkt[2] = (byte)(payloadLen & 0xFF);
        pkt[3] = 0xB0;
        pkt[4] = 0x00;
        pkt[5] = (byte)count;

        for (int i = 0; i < count; i++)
        {
            pkt[6 + i * 3] = colors[i].R;
            pkt[7 + i * 3] = colors[i].G;
            pkt[8 + i * 3] = colors[i].B;
        }
        pkt[totalLen - 1] = XorChecksum(pkt, totalLen - 1);

        string b64 = Convert.ToBase64String(pkt);
        string json = $"{{\"msg\":{{\"cmd\":\"razer\",\"data\":{{\"pt\":\"{b64}\"}}}}}}";
        byte[] data = Encoding.UTF8.GetBytes(json);

        QueueLatestColorFrame(ip, data);
    }

    /// <summary>
    /// Map the 2D zone grid vertically to device segments — samples a left or right
    /// column slice from top to bottom. Used for vertical light bars on screen edges.
    /// </summary>
    private static (byte R, byte G, byte B)[] MapGridToSegmentsVertical(
        (byte R, byte G, byte B)[,] grid, int cols, int rows, int segmentCount, ZoneSide side)
    {
        var result = new (byte R, byte G, byte B)[segmentCount];
        if (rows == 0 || cols == 0) return result;

        // Which columns to sample (leftmost 25% or rightmost 25%)
        int colStart, colEnd;
        if (side == ZoneSide.LeftVertical)
        {
            colStart = 0;
            colEnd = Math.Max(cols / 4, 1);
        }
        else // RightVertical
        {
            colStart = cols - Math.Max(cols / 4, 1);
            colEnd = cols;
        }

        // Map N segments to the rows (top-to-bottom), averaging the selected columns
        for (int seg = 0; seg < segmentCount; seg++)
        {
            float rowStart = (float)seg / segmentCount * rows;
            float rowEnd = (float)(seg + 1) / segmentCount * rows;

            int r = 0, g = 0, b = 0, count = 0;
            for (int row = (int)rowStart; row < (int)Math.Ceiling(rowEnd) && row < rows; row++)
            {
                for (int col = colStart; col < colEnd; col++)
                {
                    r += grid[row, col].R;
                    g += grid[row, col].G;
                    b += grid[row, col].B;
                    count++;
                }
            }
            if (count > 0)
                result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
        return result;
    }

    /// <summary>
    /// Map N screen zones to M device segments by proportional grouping.
    /// </summary>
    private static (byte R, byte G, byte B)[] MapZonesToSegments(
        (byte R, byte G, byte B)[] zones, int segmentCount, ZoneSide side = ZoneSide.Full)
    {
        var result = new (byte R, byte G, byte B)[segmentCount];
        int zoneCount = zones.Length;

        // Side-aware: only sample from the relevant portion of screen zones
        int zoneStart = 0, zoneEnd = zoneCount;
        switch (side)
        {
            case ZoneSide.Left:
                zoneEnd = Math.Max(zoneCount / 4, 1); // leftmost 25% of screen
                break;
            case ZoneSide.Right:
                zoneStart = zoneCount - Math.Max(zoneCount / 4, 1); // rightmost 25%
                break;
            case ZoneSide.Top:
                zoneEnd = Math.Max(zoneCount / 4, 1);
                break;
            case ZoneSide.Bottom:
                zoneStart = zoneCount - Math.Max(zoneCount / 4, 1);
                break;
        }
        int sideZoneCount = zoneEnd - zoneStart;

        for (int seg = 0; seg < segmentCount; seg++)
        {
            // Proportional mapping within the side's zone range
            float start = zoneStart + (float)seg / segmentCount * sideZoneCount;
            float end = zoneStart + (float)(seg + 1) / segmentCount * sideZoneCount;

            int r = 0, g = 0, b = 0, count = 0;
            for (int z = (int)start; z < (int)Math.Ceiling(end) && z < zoneEnd; z++)
            {
                r += zones[z].R;
                g += zones[z].G;
                b += zones[z].B;
                count++;
            }
            if (count > 0)
                result[seg] = ((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
        return result;
    }

    /// <summary>Sensitivity slider (1-20, higher = more responsive) → per-channel change needed
    /// before a new colour is sent. It used to be the raw deadband, so a higher "sensitivity"
    /// made the lights react LESS. 20 → 0 (every change), 13 → 1, 1 → 4.</summary>
    private static int ChangeDeadband(int sensitivity)
        => Math.Clamp((20 - Math.Clamp(sensitivity, 1, 20)) / 5, 0, 4);

    // Per-device smoothed colour state (linear light, 0-1). Only touched from the capture thread.
    private readonly Dictionary<string, float[]> _smoothState = new();

    /// <summary>
    /// Adaptive temporal smoothing, applied per send (~30/s). Small frame-to-frame changes
    /// (noise, film grain, slow camera pans) ease in so the room doesn't shimmer; big changes
    /// (scene cuts, explosions, menus) pass almost instantly so the lights still feel live.
    /// Done in linear light so fades don't dip through muddy midtones.
    /// </summary>
    private (byte R, byte G, byte B)[] SmoothColors(string ip, (byte R, byte G, byte B)[] colors)
    {
        int n = colors.Length * 3;
        if (!_smoothState.TryGetValue(ip, out var st) || st.Length != n)
        {
            st = new float[n];
            for (int i = 0; i < colors.Length; i++)
            {
                st[i * 3] = ToLin(colors[i].R); st[i * 3 + 1] = ToLin(colors[i].G); st[i * 3 + 2] = ToLin(colors[i].B);
            }
            _smoothState[ip] = st;
            return colors;
        }

        var result = new (byte R, byte G, byte B)[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            float tr = ToLin(colors[i].R), tg = ToLin(colors[i].G), tb = ToLin(colors[i].B);
            float sr = st[i * 3], sg = st[i * 3 + 1], sb = st[i * 3 + 2];
            // Difference in perceptual (sRGB-ish) space drives how fast we follow.
            float d = MathF.Max(MathF.Abs(MathF.Sqrt(tr) - MathF.Sqrt(sr)),
                      MathF.Max(MathF.Abs(MathF.Sqrt(tg) - MathF.Sqrt(sg)), MathF.Abs(MathF.Sqrt(tb) - MathF.Sqrt(sb))));
            float alpha = Math.Clamp(0.30f + d * 2.5f, 0.30f, 1f); // d≥0.28 (~70/255) → instant
            sr += (tr - sr) * alpha; sg += (tg - sg) * alpha; sb += (tb - sb) * alpha;
            st[i * 3] = sr; st[i * 3 + 1] = sg; st[i * 3 + 2] = sb;
            result[i] = (FromLin(sr), FromLin(sg), FromLin(sb));
        }
        return result;
    }

    private static float ToLin(byte v) { float f = v / 255f; return f * f; }
    private static byte FromLin(float v) => (byte)Math.Clamp(MathF.Sqrt(MathF.Max(v, 0f)) * 255f + 0.5f, 0f, 255f);

    private bool SegmentColorsChanged(string ip, (byte R, byte G, byte B)[] colors, int sensitivity)
    {
        if (!_lastSegmentColors.TryGetValue(ip, out var prev) || prev.Length != colors.Length)
            return true;
        for (int i = 0; i < colors.Length; i++)
        {
            if (Math.Abs(colors[i].R - prev[i].R) > sensitivity ||
                Math.Abs(colors[i].G - prev[i].G) > sensitivity ||
                Math.Abs(colors[i].B - prev[i].B) > sensitivity)
                return true;
        }
        return false;
    }

    private void DisableAllSegments()
    {
        foreach (var ip in _segmentEnabled)
        {
            try { SendSegmentEnable(ip, false); } catch { }
        }
        _segmentEnabled.Clear();
        _segmentEnableTick.Clear();
        _lastSegmentColors.Clear();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _capture.Dispose();
        _udpSendQueue.Writer.TryComplete();
        _udpSendCts.Cancel();
        try { _udpSendTask.Wait(2000); } catch { }
        foreach (var udp in _udpClients.Values)
        {
            try { udp.Dispose(); } catch { }
        }
        _udpClients.Clear();
        _udpSendCts.Dispose();
    }
}
