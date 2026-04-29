using System.Reflection;
using System.Runtime.InteropServices;
using AmpUp.Core.Models;

namespace AmpUp.Core.Services;

public class CorsairDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "fan", "cooler", "keyboard", "mouse", etc.
    public int LedCount { get; set; }
}

/// <summary>
/// Corsair iCUE integration via the official iCUE SDK v4 (native DLL P/Invoke).
/// Requires iCUE 5+ running with third-party control enabled in settings.
/// DLL: x64/iCUESDK.x64_2019.dll (shipped alongside exe).
/// </summary>
public class CorsairSync : IDisposable
{
    static CorsairSync()
    {
        // Register resolver so the runtime can find the DLL in the x64/ subfolder
        NativeLibrary.SetDllImportResolver(typeof(CorsairSync).Assembly, (name, assembly, path) =>
        {
            if (name == DllName)
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location)
                          ?? AppContext.BaseDirectory;
                var dllPath = Path.Combine(exeDir, "x64", $"{DllName}.dll");
                if (File.Exists(dllPath) && NativeLibrary.TryLoad(dllPath, out var handle))
                    return handle;
            }
            return IntPtr.Zero;
        });
    }

    private bool _connected;
    private bool _disposed;
    private bool _dllLoaded;
    private int _sessionState; // CorsairSessionState

    public bool IsAvailable => _connected && !_disposed && _dllLoaded && !_paused;
    public List<CorsairDevice> Devices { get; private set; } = new();

    // ── Native SDK P/Invoke ─────────────────────────────────────────

    private const string DllName = "iCUESDK.x64_2019";

    // Session states
    private const int CSS_Connected = 6;
    private const int CSS_Connecting = 2;

    // Device types (bitmask)
    private const int CDT_All = unchecked((int)0xFFFFFFFF);
    private const int CDT_FanLedController = 0x0020;
    private const int CDT_LedController = 0x0040;
    private const int CDT_Cooler = 0x0100;

    // Access levels
    private const int CAL_Shared = 0;
    private const int CAL_ExclusiveLightingControl = 1;

    // Max constants from SDK
    private const int CORSAIR_DEVICE_COUNT_MAX = 64;
    private const int CORSAIR_DEVICE_LEDCOUNT_MAX = 512;
    private const int CORSAIR_STRING_SIZE_M = 128;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct CorsairDeviceFilter
    {
        public int deviceTypeMask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct CorsairDeviceInfo
    {
        public int type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CORSAIR_STRING_SIZE_M)]
        public string id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CORSAIR_STRING_SIZE_M)]
        public string serial;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CORSAIR_STRING_SIZE_M)]
        public string model;
        public int ledCount;
        public int channelCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CorsairLedColor
    {
        public uint id;   // CorsairLedLuid
        public byte r;
        public byte g;
        public byte b;
        public byte a;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CorsairLedPosition
    {
        public uint id;
        public double cx;
        public double cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CorsairVersion
    {
        public int major;
        public int minor;
        public int patch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CorsairSessionDetails
    {
        public CorsairVersion clientVersion;
        public CorsairVersion serverVersion;
        public CorsairVersion serverHostVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CorsairSessionStateChanged
    {
        public int state;
        public CorsairSessionDetails details;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SessionStateChangedHandler(IntPtr context, ref CorsairSessionStateChanged eventData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairConnect(SessionStateChangedHandler onStateChanged, IntPtr context);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairDisconnect();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairGetDevices(ref CorsairDeviceFilter filter, int sizeMax,
        [Out] CorsairDeviceInfo[] devices, out int size);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairGetLedPositions(
        [MarshalAs(UnmanagedType.LPStr)] string deviceId,
        int sizeMax, [Out] CorsairLedPosition[] positions, out int size);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairSetLedColors(
        [MarshalAs(UnmanagedType.LPStr)] string deviceId,
        int size, [In] CorsairLedColor[] ledColors);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairRequestControl(
        [MarshalAs(UnmanagedType.LPStr)] string deviceId, int accessLevel);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairReleaseControl(
        [MarshalAs(UnmanagedType.LPStr)] string deviceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CorsairSetLayerPriority(uint priority);

    // ── Keep delegate alive to prevent GC ───────────────────────────
    private SessionStateChangedHandler? _sessionCallback;

    // ── Public API ──────────────────────────────────────────────────

    public CorsairSync()
    {
        // Test if the DLL can actually be loaded
        try
        {
            var exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location)
                      ?? AppContext.BaseDirectory;
            var dllPath = Path.Combine(exeDir, "x64", $"{DllName}.dll");
            _dllLoaded = File.Exists(dllPath);
            if (!_dllLoaded)
            {
                Logger.Log($"CorsairSync: DLL not found at {dllPath}");
            }
        }
        catch (Exception ex)
        {
            _dllLoaded = false;
            Logger.Log($"CorsairSync: failed to locate DLL — {ex.Message}");
        }
    }

    private bool _started;

    public void Start()
    {
        if (_disposed || !_dllLoaded) return;
        _paused = false;
        if (_started) return; // CorsairConnect can only be called once per session
        try
        {
            _sessionCallback = OnSessionStateChanged;
            int err = CorsairConnect(_sessionCallback, IntPtr.Zero);
            if (err != 0)
            {
                Logger.Log($"CorsairSync: CorsairConnect returned error {err}");
                return;
            }
            _started = true;
        }
        catch (Exception ex)
        {
            _dllLoaded = false;
            Logger.Log($"CorsairSync: Start failed — {ex.Message}");
        }
    }

    private void OnSessionStateChanged(IntPtr context, ref CorsairSessionStateChanged eventData)
    {
        _sessionState = eventData.state;
        var sv = eventData.details.serverVersion;
        var hv = eventData.details.serverHostVersion;

        if (eventData.state == CSS_Connected)
        {
            _connected = true;
            Logger.Log($"CorsairSync: connected to iCUE (SDK {sv.major}.{sv.minor}.{sv.patch}, iCUE {hv.major}.{hv.minor}.{hv.patch})");

            // Set higher priority so our colors show on top of iCUE's own effects
            CorsairSetLayerPriority(200);

            // Auto-discover devices
            _ = Task.Run(() =>
            {
                try { DiscoverDevices(); }
                catch (Exception ex) { Logger.Log($"CorsairSync: device discovery failed — {ex.Message}"); }
            });
        }
        else
        {
            _connected = false;
        }
    }

    private void DiscoverDevices()
    {
        var filter = new CorsairDeviceFilter { deviceTypeMask = CDT_All };
        var devices = new CorsairDeviceInfo[CORSAIR_DEVICE_COUNT_MAX];
        int err = CorsairGetDevices(ref filter, CORSAIR_DEVICE_COUNT_MAX, devices, out int count);
        if (err != 0)
        {
            Logger.Log($"CorsairSync: CorsairGetDevices error {err}");
            return;
        }

        var list = new List<CorsairDevice>();
        for (int i = 0; i < count; i++)
        {
            var d = devices[i];
            string typeName = d.type switch
            {
                0x0001 => "keyboard",
                0x0002 => "mouse",
                0x0004 => "mousemat",
                0x0008 => "headset",
                0x0010 => "headset_stand",
                0x0020 => "fan_controller",
                0x0040 => "led_controller",
                0x0080 => "memory",
                0x0100 => "cooler",
                0x0200 => "motherboard",
                0x0400 => "gpu",
                _ => "unknown"
            };
            list.Add(new CorsairDevice
            {
                Id = d.id ?? "",
                Name = d.model ?? "",
                Type = typeName,
                LedCount = d.ledCount
            });
        }
        Devices = list;
    }

    public Task<List<CorsairDevice>> GetDevicesAsync()
    {
        if (_disposed || !IsAvailable) return Task.FromResult(new List<CorsairDevice>());
        return Task.Run(() =>
        {
            try
            {
                DiscoverDevices();
                return Devices;
            }
            catch (Exception ex)
            {
                Logger.Log($"CorsairSync: GetDevicesAsync failed — {ex.Message}");
                return new List<CorsairDevice>();
            }
        });
    }

    /// <summary>
    /// Refresh the cached iCUE device list without requiring the caller to
    /// go through the UI-oriented IsAvailable gate. Toggle paths use this to
    /// make sure "all devices" really means the current live SDK device set.
    /// </summary>
    public void RefreshDevices()
    {
        if (_disposed || !_connected || !_dllLoaded) return;
        try { DiscoverDevices(); }
        catch (Exception ex)
        {
            Logger.Log($"CorsairSync: RefreshDevices failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Send 15 RGB values (5 knobs × 3 LEDs) to all connected iCUE devices.
    /// Maps Turn Up's 15 LEDs across all device LEDs proportionally.
    /// </summary>
    public void SyncColors(byte[] rgbColors)
    {
        if (!IsAvailable || rgbColors == null || rgbColors.Length < 45) return;
        if (Devices.Count == 0)
        {
            try { DiscoverDevices(); } catch { }
            if (Devices.Count == 0) return;
        }

        foreach (var device in Devices)
        {
            if (device.LedCount <= 0 || string.IsNullOrEmpty(device.Id)) continue;
            try
            {
                SetDeviceColors(device, rgbColors);
            }
            catch (Exception ex)
            {
                Logger.Log($"CorsairSync: SyncColors failed for {device.Name} — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Send already-sampled spatial colors to specific iCUE devices.
    /// Each device color array is spread across that device's physical LEDs.
    /// </summary>
    public void SyncDeviceColors(IReadOnlyDictionary<string, (int R, int G, int B)[]> colorsByDevice, float brightnessScale = 1f)
    {
        if (!IsAvailable || colorsByDevice.Count == 0) return;
        if (Devices.Count == 0)
        {
            try { DiscoverDevices(); } catch { }
            if (Devices.Count == 0) return;
        }

        brightnessScale = Math.Clamp(brightnessScale, 0f, 1f);
        foreach (var device in Devices)
        {
            if (device.LedCount <= 0 || string.IsNullOrEmpty(device.Id)) continue;
            if (!colorsByDevice.TryGetValue(device.Id, out var sampledColors) || sampledColors.Length == 0) continue;

            try
            {
                SetDeviceColors(device, sampledColors, brightnessScale);
            }
            catch (Exception ex)
            {
                Logger.Log($"CorsairSync: SyncDeviceColors failed for {device.Name} — {ex.Message}");
            }
        }
    }

    public bool SyncNativeRoomEffect(
        LightEffect effect,
        (int R, int G, int B) color1,
        (int R, int G, int B) color2,
        int speed,
        float brightnessScale = 1f)
    {
        if (!IsAvailable || !SupportsNativeRoomEffect(effect)) return false;
        if (Devices.Count == 0)
        {
            try { DiscoverDevices(); } catch { }
            if (Devices.Count == 0) return false;
        }

        brightnessScale = Math.Clamp(brightnessScale, 0f, 1f);
        float t = Environment.TickCount64 / 1000f * (0.35f + Math.Clamp(speed, 1, 100) / 55f);
        bool sentAny = false;

        foreach (var device in Devices)
        {
            if (device.LedCount <= 0 || string.IsNullOrEmpty(device.Id)) continue;
            try
            {
                sentAny |= SetNativeRoomEffect(device, effect, color1, color2, t, brightnessScale);
            }
            catch (Exception ex)
            {
                Logger.Log($"CorsairSync: SyncNativeRoomEffect failed for {device.Name} — {ex.Message}");
            }
        }

        return sentAny;
    }

    private void SetDeviceColors(CorsairDevice device, byte[] rgbColors)
    {
        // Get LED positions/IDs for this device
        var positions = new CorsairLedPosition[CORSAIR_DEVICE_LEDCOUNT_MAX];
        int err = CorsairGetLedPositions(device.Id, CORSAIR_DEVICE_LEDCOUNT_MAX, positions, out int ledCount);
        if (err != 0 || ledCount <= 0) return;

        var colors = new CorsairLedColor[ledCount];
        for (int i = 0; i < ledCount; i++)
        {
            // Map device LED index to one of the 15 Turn Up LEDs
            int srcIdx = (i * 15 / ledCount) % 15;
            int offset = srcIdx * 3;
            colors[i] = new CorsairLedColor
            {
                id = positions[i].id,
                r = rgbColors[offset],
                g = rgbColors[offset + 1],
                b = rgbColors[offset + 2],
                a = 255
            };
        }

        CorsairSetLedColors(device.Id, ledCount, colors);
    }

    private static bool SupportsNativeRoomEffect(LightEffect effect) => effect is
        LightEffect.Aurora or LightEffect.NebulaDrift or LightEffect.OpalWave
        or LightEffect.Ocean or LightEffect.Prism or LightEffect.Tidal
        or LightEffect.Vortex or LightEffect.Shockwave or LightEffect.Scanner
        or LightEffect.RainbowScanner or LightEffect.MeteorRain or LightEffect.ColorWave
        or LightEffect.FireWall or LightEffect.Lava or LightEffect.Waterfall
        or LightEffect.Matrix or LightEffect.Starfield or LightEffect.ColorTwinkle
        or LightEffect.Bloom or LightEffect.Glitch or LightEffect.DNA;

    private bool SetNativeRoomEffect(
        CorsairDevice device,
        LightEffect effect,
        (int R, int G, int B) color1,
        (int R, int G, int B) color2,
        float t,
        float brightnessScale)
    {
        var positions = new CorsairLedPosition[CORSAIR_DEVICE_LEDCOUNT_MAX];
        int err = CorsairGetLedPositions(device.Id, CORSAIR_DEVICE_LEDCOUNT_MAX, positions, out int ledCount);
        if (err != 0 || ledCount <= 0) return false;

        var activePositions = positions.Take(ledCount).ToArray();
        double minX = activePositions.Min(p => p.cx);
        double maxX = activePositions.Max(p => p.cx);
        double minY = activePositions.Min(p => p.cy);
        double maxY = activePositions.Max(p => p.cy);
        double width = Math.Max(maxX - minX, 0.001);
        double height = Math.Max(maxY - minY, 0.001);
        float phase = Math.Abs((device.Id ?? device.Name ?? "").GetHashCode() % 997) / 997f;
        var fanZones = InferFanZones(activePositions, device);

        var colors = new CorsairLedColor[ledCount];
        for (int i = 0; i < ledCount; i++)
        {
            float nx = (float)((positions[i].cx - minX) / width);
            float ny = (float)((positions[i].cy - minY) / height);
            float x = Math.Clamp(nx * 0.72f + ny * 0.28f, 0f, 1f);
            var raw = RenderNativeRoomEffect(effect, x, nx, ny, t, phase, color1, color2);
            raw = ApplyFanRingAccent(raw, effect, x, nx, ny, positions[i], fanZones, t, phase, color1, color2);
            raw = BoostEffectColor(raw);
            colors[i] = new CorsairLedColor
            {
                id = positions[i].id,
                r = (byte)Math.Clamp(raw.R * brightnessScale, 0, 255),
                g = (byte)Math.Clamp(raw.G * brightnessScale, 0, 255),
                b = (byte)Math.Clamp(raw.B * brightnessScale, 0, 255),
                a = 255
            };
        }

        CorsairSetLedColors(device.Id!, ledCount, colors);
        return true;
    }

    private readonly record struct FanZone(double X, double Y, double Radius);

    private static List<FanZone> InferFanZones(CorsairLedPosition[] positions, CorsairDevice device)
    {
        if (positions.Length < 24 || !LooksLikeFanDevice(device)) return new List<FanZone>();

        int targetCount = Math.Clamp((int)MathF.Round(positions.Length / 34f), 1, 12);
        double minX = positions.Min(p => p.cx);
        double maxX = positions.Max(p => p.cx);
        double minY = positions.Min(p => p.cy);
        double maxY = positions.Max(p => p.cy);
        double width = Math.Max(maxX - minX, 0.001);
        double height = Math.Max(maxY - minY, 0.001);

        var centers = new List<(double X, double Y)>();
        if (targetCount == 1)
        {
            centers.Add(((minX + maxX) / 2, (minY + maxY) / 2));
        }
        else
        {
            for (int i = 0; i < targetCount; i++)
            {
                double f = targetCount == 1 ? 0.5 : i / (double)(targetCount - 1);
                centers.Add(width >= height
                    ? (minX + width * f, (minY + maxY) / 2)
                    : ((minX + maxX) / 2, minY + height * f));
            }

            for (int pass = 0; pass < 8; pass++)
            {
                var sums = new (double X, double Y, int Count)[targetCount];
                foreach (var p in positions)
                {
                    int nearest = 0;
                    double best = double.MaxValue;
                    for (int c = 0; c < centers.Count; c++)
                    {
                        double d = DistSq(p.cx, p.cy, centers[c].X, centers[c].Y);
                        if (d < best) { best = d; nearest = c; }
                    }
                    sums[nearest].X += p.cx;
                    sums[nearest].Y += p.cy;
                    sums[nearest].Count++;
                }

                for (int c = 0; c < centers.Count; c++)
                    if (sums[c].Count > 0)
                        centers[c] = (sums[c].X / sums[c].Count, sums[c].Y / sums[c].Count);
            }
        }

        var zones = new List<FanZone>();
        for (int c = 0; c < centers.Count; c++)
        {
            double radius = 0;
            int count = 0;
            foreach (var p in positions)
            {
                int nearest = 0;
                double best = double.MaxValue;
                for (int i = 0; i < centers.Count; i++)
                {
                    double d = DistSq(p.cx, p.cy, centers[i].X, centers[i].Y);
                    if (d < best) { best = d; nearest = i; }
                }
                if (nearest != c) continue;
                radius = Math.Max(radius, Math.Sqrt(best));
                count++;
            }

            if (count >= 8 && radius > 0)
                zones.Add(new FanZone(centers[c].X, centers[c].Y, radius));
        }

        return zones;
    }

    private static bool LooksLikeFanDevice(CorsairDevice device)
    {
        string name = $"{device.Type} {device.Name}".ToLowerInvariant();
        return name.Contains("fan")
            || name.Contains("cooler")
            || name.Contains("pump")
            || name.Contains("commander")
            || name.Contains("link");
    }

    private static (int R, int G, int B) ApplyFanRingAccent(
        (int R, int G, int B) baseColor,
        LightEffect effect,
        float x,
        float nx,
        float ny,
        CorsairLedPosition position,
        List<FanZone> zones,
        float t,
        float phase,
        (int R, int G, int B) c1,
        (int R, int G, int B) c2)
    {
        if (zones.Count == 0) return baseColor;

        FanZone zone = zones[0];
        double best = double.MaxValue;
        foreach (var candidate in zones)
        {
            double d = DistSq(position.cx, position.cy, candidate.X, candidate.Y);
            if (d < best)
            {
                best = d;
                zone = candidate;
            }
        }

        float dist = zone.Radius > 0 ? (float)(Math.Sqrt(best) / zone.Radius) : 0f;
        float ringMix = Smooth((dist - 0.58f) / 0.28f);
        if (ringMix <= 0.02f) return baseColor;

        float angle = MathF.Atan2((float)(position.cy - zone.Y), (float)(position.cx - zone.X)) / MathF.Tau;
        angle = Frac(angle);
        var ringColor = RenderRingAccent(effect, angle, x, nx, ny, t, phase, c1, c2);
        ringColor = Scale(ringColor, 0.85f + 0.15f * Wave(angle * 3f + t * 0.5f));
        return Lerp(baseColor, ringColor, ringMix * 0.82f);
    }

    private static (int R, int G, int B) RenderRingAccent(
        LightEffect effect,
        float angle,
        float x,
        float nx,
        float ny,
        float t,
        float phase,
        (int R, int G, int B) c1,
        (int R, int G, int B) c2)
    {
        float ringX = Frac(angle + t * 0.08f + phase);
        return effect switch
        {
            LightEffect.Aurora => Lerp(NativeAurora(x, t, phase), Lerp(c1, c2, Wave(ringX * 2.0f)), 0.45f),
            LightEffect.Ocean => Lerp(NativeOcean(x, t, phase, c1, c2), c2, 0.38f + 0.25f * Wave(ringX * 2.0f)),
            LightEffect.FireWall or LightEffect.Lava => Lerp(c1, (255, 230, 80), 0.55f + 0.25f * Wave(ringX * 4f)),
            LightEffect.Waterfall => Lerp((0, 35, 95), (180, 245, 255), 0.65f + 0.25f * Wave(ringX * 5f)),
            LightEffect.Matrix => Lerp((0, 90, 36), (80, 255, 145), Wave(ringX * 12f - t * 1.2f)),
            LightEffect.Scanner => NativeScanner(ringX, t, c2, c1, false),
            LightEffect.RainbowScanner or LightEffect.Prism => Hsv(ringX, 0.92f, 1f),
            LightEffect.MeteorRain => NativeMeteor(ringX, t, phase, c2, c1),
            LightEffect.ColorWave or LightEffect.Tidal or LightEffect.Bloom => Lerp(c2, c1, Smooth(Wave(ringX * 3f - t * 0.5f))),
            LightEffect.DNA => NativeDna(ringX, t, phase + 0.18f, c2, c1),
            LightEffect.Glitch => NativeGlitch(ringX, t, phase + 0.23f),
            LightEffect.Starfield or LightEffect.ColorTwinkle => Lerp(c2, (255, 255, 255), MathF.Pow(Wave(ringX * 6f + t), 6f)),
            _ => Lerp(c2, c1, 0.25f + 0.5f * Wave(ringX * 2f)),
        };
    }

    private static double DistSq(double x1, double y1, double x2, double y2)
    {
        double dx = x1 - x2;
        double dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private static (int R, int G, int B) RenderNativeRoomEffect(
        LightEffect effect,
        float x,
        float nx,
        float ny,
        float t,
        float phase,
        (int R, int G, int B) c1,
        (int R, int G, int B) c2)
    {
        float radial = MathF.Sqrt((nx - 0.5f) * (nx - 0.5f) + (ny - 0.5f) * (ny - 0.5f));
        return effect switch
        {
            LightEffect.Aurora => NativeAurora(x, t, phase),
            LightEffect.NebulaDrift => NativeNebula(x, t, phase, c1, c2),
            LightEffect.OpalWave => NativeOpal(x, t, phase),
            LightEffect.Ocean => NativeOcean(x, t, phase, c1, c2),
            LightEffect.Prism => Hsv((x * 0.82f + t * 0.08f + phase) % 1f, 0.88f, 1f),
            LightEffect.Tidal => Lerp(Scale(c1, 0.28f), c2, MathF.Pow(Smooth(Wave(x * 2.2f - t * 0.55f + phase)), 3.2f)),
            LightEffect.Vortex => Hsv((0.72f + x * 0.25f + t * 0.04f) % 1f, 0.95f, 0.25f + MathF.Abs(MathF.Sin((x * 10.0f + t * 1.6f + phase) * MathF.PI)) * 0.9f),
            LightEffect.Shockwave => NativeShockwave(x, t, phase, c1, c2),
            LightEffect.Scanner => NativeScanner(nx, t, c1, c2, false),
            LightEffect.RainbowScanner => NativeScanner(nx, t, c1, c2, true),
            LightEffect.MeteorRain => NativeMeteor(x, t, phase, c1, c2),
            LightEffect.ColorWave => Lerp(c1, c2, Smooth(Wave(x * 3.8f - t * 0.62f + phase))),
            LightEffect.FireWall => NativeFire(x, t, phase),
            LightEffect.Lava => Lerp((80, 0, 0), (255, 105, 20), MathF.Pow(Smooth(Wave(x * 5.5f - t * 0.38f + phase)), 2.4f)),
            LightEffect.Waterfall => Lerp((0, 35, 95), (120, 235, 255), MathF.Pow(Smooth(Wave(x * 14f + t * 1.1f + phase)), 5f)),
            LightEffect.Matrix => Scale((0, 255, 105), Frac(x * 23f - t * 2.1f + phase) < 0.14f ? 1f : 0.08f),
            LightEffect.Starfield => NativeStarfield(x, t, phase),
            LightEffect.ColorTwinkle => NativeTwinkle(x, t, phase, c1, c2),
            LightEffect.Bloom => NativeBloom(x, t, phase, c1, c2),
            LightEffect.Glitch => NativeGlitch(x, t, phase),
            LightEffect.DNA => NativeDna(x, t, phase, c1, c2),
            _ => c1,
        };
    }

    private static (int R, int G, int B) NativeAurora(float x, float t, float phase)
    {
        float wave1 = MathF.Sin(t * 0.7f + x * 4f + phase * MathF.Tau) * 0.5f + 0.5f;
        float wave2 = MathF.Sin(t * 1.1f + x * 6f + 1.5f + phase * MathF.Tau) * 0.5f + 0.5f;
        float wave3 = MathF.Sin(t * 0.4f + x * 2f + 3f + phase * MathF.Tau) * 0.5f + 0.5f;
        float combined = (wave1 + wave2 * 0.6f + wave3 * 0.3f) / 1.9f;
        float hue = 120f + combined * 180f + MathF.Sin(t * 0.3f + x * 3f + phase * MathF.Tau) * 40f;
        return Hsv(hue / 360f, 0.86f, Math.Clamp(MathF.Pow(0.15f + combined * 0.85f, 2f) * 1.15f, 0f, 1f));
    }

    private static (int R, int G, int B) NativeNebula(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float cloud = Smooth(Wave(x * 3.0f + t * 0.17f + phase) * 0.65f + Wave(x * 8.0f - t * 0.11f) * 0.35f);
        return Scale(Lerp(Lerp((70, 20, 145), c1, 0.35f), c2, cloud), 0.45f + cloud * 0.75f);
    }

    private static (int R, int G, int B) NativeOpal(float x, float t, float phase)
    {
        float hue = 0.47f + 0.22f * Wave(x * 2.2f - t * 0.18f + phase);
        return Lerp((255, 210, 245), Hsv(hue, 0.32f, 1f), 0.65f + 0.25f * Wave(x * 6f + t));
    }

    private static (int R, int G, int B) NativeOcean(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float wave = Smooth(Wave(x * 4.5f - t * 0.5f + phase) * 0.7f + Wave(x * 11f + t * 0.2f) * 0.3f);
        return Lerp(Lerp((0, 55, 160), (0, 230, 210), wave), Lerp(c1, c2, wave), 0.25f);
    }

    private static (int R, int G, int B) NativeShockwave(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float center = (t * 0.35f + phase) % 1f;
        float d = MathF.Abs(x - center);
        d = MathF.Min(d, 1f - d);
        return Lerp(Scale(c1, 0.18f), c2, MathF.Exp(-d * d * 95f));
    }

    private static (int R, int G, int B) NativeScanner(float x, float t, (int R, int G, int B) c1, (int R, int G, int B) c2, bool rainbow)
    {
        float pos = PingPong(t * 0.32f);
        float tail = MathF.Exp(-MathF.Abs(x - pos) * MathF.Abs(x - pos) * 70f);
        return Lerp(Scale(c2, 0.08f), rainbow ? Hsv((x + t * 0.08f) % 1f, 1f, 1f) : c1, tail);
    }

    private static (int R, int G, int B) NativeMeteor(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float head = (t * 0.42f + phase) % 1f;
        float d = (x - head + 1f) % 1f;
        float tail = d < 0.36f ? MathF.Pow(1f - d / 0.36f, 2.2f) : 0f;
        return Lerp(Scale(c2, 0.05f), c1, tail);
    }

    private static (int R, int G, int B) NativeFire(float x, float t, float phase)
    {
        float heat = Smooth(Wave(x * 8f + t * 1.8f + phase) * 0.5f + Wave(x * 19f - t * 1.1f) * 0.5f);
        heat = MathF.Max(heat, 1f - x * 0.35f);
        return Lerp((120, 10, 0), (255, 230, 80), heat);
    }

    private static (int R, int G, int B) NativeStarfield(float x, float t, float phase)
    {
        float seed = Frac(MathF.Sin((x + phase) * 151.7f) * 43758.545f);
        float tw = MathF.Pow(Smooth(Wave(t * (0.6f + seed) + seed * 7f)), 8f);
        return Scale(Lerp((80, 90, 170), (255, 255, 255), tw), 0.18f + tw);
    }

    private static (int R, int G, int B) NativeTwinkle(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float seed = Frac(MathF.Sin((x + phase) * 331.3f) * 24634.634f);
        float sparkle = MathF.Pow(Smooth(Wave(t * (0.9f + seed * 1.4f) + seed * 9f)), 10f);
        return Lerp(Scale(c1, 0.18f), c2, sparkle);
    }

    private static (int R, int G, int B) NativeBloom(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float bloom = MathF.Pow(Smooth(Wave(x * 2.0f - t * 0.24f + phase)), 1.8f);
        return Scale(Lerp(c1, c2, bloom), 0.35f + bloom * 0.8f);
    }

    private static (int R, int G, int B) NativeGlitch(float x, float t, float phase)
    {
        float band = Frac(MathF.Floor(x * 18f) * 0.173f + MathF.Floor(t * 8f) * 0.071f + phase);
        if (band < 0.18f) return (255, 0, 120);
        if (band < 0.32f) return (0, 240, 255);
        return Scale((80, 0, 160), 0.35f + 0.25f * Wave(x * 6f + t));
    }

    private static (int R, int G, int B) NativeDna(float x, float t, float phase, (int R, int G, int B) c1, (int R, int G, int B) c2)
    {
        float a = MathF.Pow(Smooth(Wave(x * 5.0f + t * 0.72f + phase)), 6f);
        float b = MathF.Pow(Smooth(Wave(x * 5.0f + t * 0.72f + phase + 0.5f)), 6f);
        return Lerp(Scale(c1, a), Scale(c2, b), b / Math.Max(a + b, 0.001f));
    }

    private static (int R, int G, int B) BoostEffectColor((int R, int G, int B) c)
    {
        const float boost = 1.55f;
        return (
            Math.Clamp((int)MathF.Round(c.R * boost), 0, 255),
            Math.Clamp((int)MathF.Round(c.G * boost), 0, 255),
            Math.Clamp((int)MathF.Round(c.B * boost), 0, 255));
    }

    private static (int R, int G, int B) Lerp((int R, int G, int B) a, (int R, int G, int B) b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return (
            (int)MathF.Round(a.R + (b.R - a.R) * t),
            (int)MathF.Round(a.G + (b.G - a.G) * t),
            (int)MathF.Round(a.B + (b.B - a.B) * t));
    }

    private static (int R, int G, int B) Scale((int R, int G, int B) c, float scale)
        => (Math.Clamp((int)MathF.Round(c.R * scale), 0, 255),
            Math.Clamp((int)MathF.Round(c.G * scale), 0, 255),
            Math.Clamp((int)MathF.Round(c.B * scale), 0, 255));

    private static float Wave(float x) => (MathF.Sin(x * MathF.Tau) + 1f) * 0.5f;
    private static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
    private static float Frac(float x) => x - MathF.Floor(x);
    private static float PingPong(float x) { x = Frac(x); return x < 0.5f ? x * 2f : 2f - x * 2f; }

    private static (int R, int G, int B) Hsv(float h, float s, float v)
    {
        h = Frac(h);
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);
        float c = v * s;
        float x = c * (1f - MathF.Abs((h * 6f % 2f) - 1f));
        float m = v - c;
        float r, g, b;
        int sector = (int)(h * 6f);
        switch (sector)
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }

        return (
            Math.Clamp((int)MathF.Round((r + m) * 255f), 0, 255),
            Math.Clamp((int)MathF.Round((g + m) * 255f), 0, 255),
            Math.Clamp((int)MathF.Round((b + m) * 255f), 0, 255));
    }

    private void SetDeviceColors(CorsairDevice device, (int R, int G, int B)[] sampledColors, float brightnessScale)
    {
        var positions = new CorsairLedPosition[CORSAIR_DEVICE_LEDCOUNT_MAX];
        int err = CorsairGetLedPositions(device.Id, CORSAIR_DEVICE_LEDCOUNT_MAX, positions, out int ledCount);
        if (err != 0 || ledCount <= 0) return;

        var colors = new CorsairLedColor[ledCount];
        int sourceCount = sampledColors.Length;
        for (int i = 0; i < ledCount; i++)
        {
            int srcIdx = (i * sourceCount / ledCount) % sourceCount;
            var src = sampledColors[srcIdx];
            colors[i] = new CorsairLedColor
            {
                id = positions[i].id,
                r = (byte)Math.Clamp(src.R * brightnessScale, 0, 255),
                g = (byte)Math.Clamp(src.G * brightnessScale, 0, 255),
                b = (byte)Math.Clamp(src.B * brightnessScale, 0, 255),
                a = 255
            };
        }

        CorsairSetLedColors(device.Id, ledCount, colors);
    }

    /// <summary>Send a single static color to all device LEDs.</summary>
    public async Task SetStaticColorAllAsync(byte r, byte g, byte b)
    {
        if (_disposed || !_connected || !_dllLoaded) return;
        // Respect pause — otherwise background timers (music reactive, VU fill)
        // keep overwriting a "black + Stop()" shutoff via this path.
        if (_paused) return;
        // Auto-discover if device list is empty
        if (Devices.Count == 0)
        {
            try { DiscoverDevices(); }
            catch { }
        }
        if (Devices.Count == 0) return;
        await Task.Run(() =>
        {
            foreach (var device in Devices)
            {
                if (device.LedCount <= 0 || string.IsNullOrEmpty(device.Id)) continue;
                try
                {
                    var positions = new CorsairLedPosition[CORSAIR_DEVICE_LEDCOUNT_MAX];
                    int err = CorsairGetLedPositions(device.Id, CORSAIR_DEVICE_LEDCOUNT_MAX, positions, out int count);
                    if (err != 0 || count <= 0) continue;

                    var colors = new CorsairLedColor[count];
                    for (int i = 0; i < count; i++)
                    {
                        colors[i] = new CorsairLedColor { id = positions[i].id, r = r, g = g, b = b, a = 255 };
                    }
                    CorsairSetLedColors(device.Id, count, colors);
                }
                catch (Exception ex)
                {
                    Logger.Log($"CorsairSync: SetStaticColor failed for {device.Name} — {ex.Message}");
                }
            }
        });
    }

    // ── Effects (iCUE SDK doesn't have an effects API — these are no-ops) ───

    public Task<List<string>> GetEffectsAsync()
    {
        // The native SDK doesn't expose iCUE's built-in effects
        return Task.FromResult(new List<string>());
    }

    public Task ApplyEffectAsync(string effectId)
    {
        // No effect API in the native SDK — colors are set directly
        return Task.CompletedTask;
    }

    // ── Fan speed (not supported by iCUE SDK) ──────────────────────

    public Task SetFanSpeedAsync(string deviceId, int percent)
    {
        // iCUE SDK v4 does not expose fan speed control
        // Fan curves must be configured in iCUE itself
        return Task.CompletedTask;
    }

    public void SyncAudioReactive(float audioLevel, CorsairConfig cfg, string typeFilter = "")
    {
        // Fan speed not available — no-op
    }

    private bool _paused;

    public void Stop()
    {
        // Pause syncing without disconnecting from iCUE
        _paused = true;
    }

    public void Resume()
    {
        _paused = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_dllLoaded)
        {
            try { CorsairDisconnect(); }
            catch { }
        }
    }
}
