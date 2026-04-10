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

    /// <summary>Send a single static color to all device LEDs.</summary>
    public async Task SetStaticColorAllAsync(byte r, byte g, byte b)
    {
        if (_disposed || !_connected || !_dllLoaded) return;
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
