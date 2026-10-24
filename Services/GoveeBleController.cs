using AmpUp.Core.Models;

namespace AmpUp.Services;

/// <summary>
/// Drives Govee lights that have no LAN API over Bluetooth LE instead of the rate-limited cloud.
///
/// A light qualifies when it has no IP, its SKU has a <see cref="GoveeBleProtocol.Profile"/>, and
/// its device id carries a Bluetooth address. For each one:
///   • Screen Sync on  → the light is switched to its own camera mode (it syncs itself, per segment).
///   • otherwise       → room effects / Music Reactive / VU Fill frames become zone colors.
///   • power and the Room Lights knob go over Bluetooth too.
/// </summary>
internal sealed class GoveeBleController : IDisposable
{
    private sealed class Entry
    {
        public required GoveeDeviceConfig Device;
        public required GoveeBleLink Link;
        public required GoveeBleProtocol.Profile Profile;
        public bool InCamera;
        public (byte R, byte G, byte B)?[] LastSent = Array.Empty<(byte, byte, byte)?>();
        public long[] LastSentTick = Array.Empty<long>();
    }

    // A zone is resent when any channel moved this much, or after this long anyway.
    private const int ChangeThreshold = 6;
    private const int RefreshMs = 1500;

    private readonly Func<AppConfig?> _config;
    private readonly Action _stateChanged;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly Timer _reconcile;

    public GoveeBleController(Func<AppConfig?> config, Action stateChanged)
    {
        _config = config;
        _stateChanged = stateChanged;
        _reconcile = new Timer(_ => Reconcile(), null, 1000, 500);
    }

    public static bool Qualifies(GoveeDeviceConfig dev) =>
        string.IsNullOrWhiteSpace(dev.Ip)
        && dev.UseBluetooth
        && GoveeBleProtocol.GetProfile(dev.Sku) != null
        && GoveeBleProtocol.TryGetBleAddress(dev.DeviceId, out _);

    public bool Handles(GoveeDeviceConfig dev)
    {
        lock (_gate) return _entries.ContainsKey(dev.DeviceId);
    }

    /// <summary>Starts links for qualifying devices and stops links for ones that no longer qualify.</summary>
    public void Sync(AppConfig config)
    {
        lock (_gate)
        {
            var wanted = config.Ambience.GoveeEnabled || config.Ambience.GoveeCloudEnabled
                ? config.Ambience.GoveeDevices.Where(Qualifies).ToList()
                : new List<GoveeDeviceConfig>();

            foreach (var id in _entries.Keys.Where(id => wanted.All(d => !string.Equals(d.DeviceId, id, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _entries[id].Link.Dispose();
                _entries.Remove(id);
            }

            foreach (var dev in wanted)
            {
                if (_entries.TryGetValue(dev.DeviceId, out var existing))
                {
                    existing.Device = dev; // config objects are replaced on reload
                    continue;
                }
                GoveeBleProtocol.TryGetBleAddress(dev.DeviceId, out ulong addr);
                var profile = GoveeBleProtocol.GetProfile(dev.Sku)!;
                var entry = new Entry
                {
                    Device = dev,
                    Profile = profile,
                    Link = new GoveeBleLink(addr, string.IsNullOrWhiteSpace(dev.Name) ? dev.Sku : dev.Name),
                    LastSent = new (byte, byte, byte)?[profile.Zones.Length],
                    LastSentTick = new long[profile.Zones.Length],
                };
                entry.Link.Connected += () => OnConnected(entry);
                entry.Link.Notification += bytes => OnNotification(entry, bytes);
                _entries[dev.DeviceId] = entry;
            }
        }
    }

    public void SetPower(GoveeDeviceConfig dev, bool on)
    {
        var e = Get(dev);
        if (e == null) return;
        // Under the frame lock: a color sent after "off" switches a Govee light straight back
        // on, so drop queued zone colors first and keep OnRoomFrame from queueing more.
        lock (_gate)
        {
            if (!on) e.Link.DropPending(k => k.StartsWith("zone", StringComparison.Ordinal) || k == "mode");
            e.Link.Send("power", GoveeBleProtocol.Power(on), priority: true);
            if (on)
            {
                // Back on: re-apply camera mode if Screen Sync owns the light, and resend every zone.
                e.InCamera = false;
                ForgetZones(e);
            }
        }
        _stateChanged();
    }

    public void SetBrightness(GoveeDeviceConfig dev, int percent)
    {
        Get(dev)?.Link.Send("brightness", GoveeBleProtocol.Brightness(percent), priority: true);
    }

    /// <summary>
    /// A room effect / Music Reactive / VU Fill frame (15 RGB LEDs, left to right). Ignored while a
    /// light is in camera mode (Screen Sync) or switched off. Only zones that changed are sent.
    /// </summary>
    public void OnRoomFrame(byte[] frame45, AmbienceConfig ambience)
    {
        if (ambience.ScreenSync.Enabled) return;
        long now = Environment.TickCount64;
        lock (_gate)
        {
            foreach (var e in _entries.Values)
            {
                if (!e.Device.PoweredOn || !e.Device.SyncWithAmpUp || e.InCamera) continue;
                var zones = GoveeBleProtocol.SampleZones(e.Profile, frame45);
                for (int z = 0; z < zones.Length; z++)
                {
                    var c = zones[z];
                    var last = e.LastSent[z];
                    bool changed = last is not { } l
                        || Math.Abs(l.R - c.R) > ChangeThreshold
                        || Math.Abs(l.G - c.G) > ChangeThreshold
                        || Math.Abs(l.B - c.B) > ChangeThreshold
                        || now - e.LastSentTick[z] > RefreshMs;
                    if (!changed) continue;
                    e.LastSent[z] = c;
                    e.LastSentTick[z] = now;
                    e.Link.Send("zone" + z, GoveeBleProtocol.SegmentColor(c.R, c.G, c.B, e.Profile.Zones[z].Mask));
                }
            }
        }
    }

    // Screen Sync (manual or Game Mode) decides camera mode; checked twice a second.
    private void Reconcile()
    {
        var cfg = _config();
        if (cfg == null) return;
        bool screenSync = cfg.Ambience.ScreenSync.Enabled;
        lock (_gate)
        {
            foreach (var e in _entries.Values)
            {
                bool wantCamera = screenSync && e.Device.PoweredOn && e.Device.SyncWithAmpUp;
                if (wantCamera && !e.InCamera && e.Link.IsConnected)
                {
                    e.Link.DropPending(k => k.StartsWith("zone", StringComparison.Ordinal));
                    e.Link.Send("mode", GoveeBleProtocol.CameraMode(GoveeBleProtocol.FromHex(e.Device.BleCameraMode)), priority: true);
                    e.InCamera = true;
                    Logger.Log($"Govee BLE: {e.Link.Name} -> camera mode (Screen Sync)");
                }
                else if (!wantCamera && e.InCamera)
                {
                    // Leaving Screen Sync: room frames take over; resend every zone right away.
                    e.InCamera = false;
                    ForgetZones(e);
                    Logger.Log($"Govee BLE: {e.Link.Name} -> room effects");
                }
            }
        }
    }

    private void OnConnected(Entry e)
    {
        lock (_gate)
        {
            e.InCamera = false; // re-sent by Reconcile if Screen Sync is on
            ForgetZones(e);
        }
        // Learn the user's own camera-mode settings (from the Govee app) before AmpUp changes modes.
        e.Link.Send("query-mode", GoveeBleProtocol.QueryMode(), priority: true);
        _stateChanged();
    }

    private void OnNotification(Entry e, byte[] bytes)
    {
        if (!GoveeBleProtocol.TryParseCameraMode(bytes, out var mode)) return;
        string hex = GoveeBleProtocol.ToHex(mode);
        if (e.Device.BleCameraMode == hex) return;
        e.Device.BleCameraMode = hex;
        Logger.Log($"Govee BLE: {e.Link.Name} camera settings {hex}");
    }

    private static void ForgetZones(Entry e)
    {
        Array.Clear(e.LastSent);
        Array.Clear(e.LastSentTick);
    }

    private Entry? Get(GoveeDeviceConfig dev)
    {
        lock (_gate) return _entries.TryGetValue(dev.DeviceId, out var e) ? e : null;
    }

    public void Dispose()
    {
        _reconcile.Dispose();
        lock (_gate)
        {
            foreach (var e in _entries.Values) e.Link.Dispose();
            _entries.Clear();
        }
    }
}
