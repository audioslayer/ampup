using HidSharp;

namespace AmpUp.Core.Services;

/// <summary>
/// Controls LG UltraGear monitor RGB LEDs via USB HID.
/// Protocol: 48 LEDs in a ring, controlled via vendor-specific HID interface.
/// Based on reverse engineering from OpenRGB, subraizada3/27gn950controller, and ryanw/lg-ultragear.
/// </summary>
public class LgMonitorSync : IDisposable
{
    private const int VendorId = 0x043E;   // LG Electronics
    private const int ProductId = 0x9A8A;  // UltraGear with Sphere Lighting
    private const int LedCount = 48;

    // Protocol constants
    private const byte StartCmd1 = 0x53; // 'S'
    private const byte StartCmd2 = 0x43; // 'C'
    private const byte EndCmd1 = 0x45;   // 'E'
    private const byte EndCmd2 = 0x44;   // 'D'
    private const byte CmdPower = 0xCF;
    private const byte CmdColor = 0xCD;
    private const byte CmdMode = 0xCA;
    private const byte CmdDirect = 0xC1;
    private const byte ModeVideoSync = 0x08;

    private HidStream? _stream;
    private HidDevice? _device;
    private bool _disposed;
    private bool _directModeActive;
    private readonly object _lock = new();

    public bool IsAvailable => _stream != null && !_disposed;
    public string DeviceName { get; private set; } = "";
    public int LedCountValue => LedCount;

    /// <summary>
    /// Try to find and connect to an LG UltraGear monitor with RGB LEDs.
    /// Returns true if a compatible monitor was found.
    /// </summary>
    public bool TryConnect()
    {
        try
        {
            // Enumerate ALL HID devices to find LG monitor
            var allDevices = DeviceList.Local.GetHidDevices();
            var lgDevices = allDevices.Where(d => d.VendorID == VendorId && d.ProductID == ProductId).ToList();

            if (lgDevices.Count == 0)
            {
                Logger.Log("LG Monitor: no compatible device found");
                return false;
            }

            // Prefer device with 65-byte output reports (Interface 1 = LED control)
            _device = lgDevices.FirstOrDefault(d =>
            {
                try { return d.GetMaxOutputReportLength() == 65; }
                catch { return false; }
            });

            // Fallback: try path matching for mi_01
            _device ??= lgDevices.FirstOrDefault(d =>
                d.DevicePath.Contains("mi_01", StringComparison.OrdinalIgnoreCase));

            // Last resort: any LG device
            _device ??= lgDevices.FirstOrDefault();

            if (_device == null)
            {
                Logger.Log("LG Monitor: no compatible device found");
                return false;
            }

            _stream = _device.Open();
            _stream.ReadTimeout = 500;
            _stream.WriteTimeout = 500;
            DeviceName = "LG UltraGear";
            try { DeviceName = _device.GetProductName() ?? DeviceName; } catch { }
            Logger.Log($"LG Monitor: connected to {DeviceName} (outReport={_device.GetMaxOutputReportLength()})");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"LG Monitor: connect failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enable direct (per-LED) control mode. Must be called before SendColors.
    /// </summary>
    public void EnableDirectMode()
    {
        if (!IsAvailable) return;
        try
        {
            // Set mode to Video Sync (0x08) which enables direct LED control
            var cmd = BuildSimpleCommand(CmdMode, new byte[] { 0x02, 0x02, 0x03, ModeVideoSync });
            WriteReport(cmd);
            _directModeActive = true;
        }
        catch (Exception ex)
        {
            Logger.Log($"LG Monitor: enable direct mode failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Send 48 RGB colors to the monitor LEDs. Direct mode must be enabled first.
    /// CRITICAL: All RGB values must be >= 1 (0x00 can crash the LED controller).
    /// </summary>
    public void SendColors(byte[] rgbData)
    {
        if (!IsAvailable || !_directModeActive) return;
        if (rgbData.Length < LedCount * 3) return;

        try
        {
            // Build 192-byte payload
            var payload = new byte[192];
            payload[0] = StartCmd1;
            payload[1] = StartCmd2;
            payload[2] = CmdDirect;
            payload[3] = 0x02;
            payload[4] = 0x91;
            payload[5] = 0x00;

            // Copy RGB data with minimum clamp to 1 (0x00 crashes LED MCU)
            for (int i = 0; i < LedCount * 3; i++)
                payload[6 + i] = Math.Max((byte)1, rgbData[i]);

            // CRC over RGB data
            payload[150] = XorChecksum(payload, 6, 149);
            payload[151] = EndCmd1;
            payload[152] = EndCmd2;

            // Split across 3 HID reports (65 bytes each: 1 byte report ID + 64 data)
            lock (_lock)
            {
                for (int i = 0; i < 3; i++)
                {
                    var report = new byte[65];
                    report[0] = 0x00; // report ID
                    Array.Copy(payload, i * 64, report, 1, 64);
                    _stream!.Write(report);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"LG Monitor: send colors failed — {ex.Message}");
            // Stream may be broken — try to reconnect
            _directModeActive = false;
        }
    }

    /// <summary>
    /// Sync colors from a 15-LED room effect buffer (maps proportionally to 48 LEDs).
    /// </summary>
    public void SyncFromRoomEffect(byte[] linear45)
    {
        if (!IsAvailable) return;

        if (!_directModeActive)
            EnableDirectMode();

        var rgbData = new byte[LedCount * 3];
        for (int i = 0; i < LedCount; i++)
        {
            // Map 48 LEDs proportionally to the 15-LED room effect buffer
            int srcIdx = i * 15 / LedCount;
            srcIdx = Math.Clamp(srcIdx, 0, 14);
            rgbData[i * 3] = linear45[srcIdx * 3];
            rgbData[i * 3 + 1] = linear45[srcIdx * 3 + 1];
            rgbData[i * 3 + 2] = linear45[srcIdx * 3 + 2];
        }

        SendColors(rgbData);
    }

    /// <summary>
    /// Set all LEDs to a single color.
    /// </summary>
    public void SetSolidColor(byte r, byte g, byte b)
    {
        var rgbData = new byte[LedCount * 3];
        for (int i = 0; i < LedCount; i++)
        {
            rgbData[i * 3] = Math.Max((byte)1, r);
            rgbData[i * 3 + 1] = Math.Max((byte)1, g);
            rgbData[i * 3 + 2] = Math.Max((byte)1, b);
        }

        if (!_directModeActive)
            EnableDirectMode();
        SendColors(rgbData);
    }

    /// <summary>
    /// Turn LEDs on or off.
    /// </summary>
    public void SetPower(bool on)
    {
        if (!IsAvailable) return;
        try
        {
            var cmd = BuildSimpleCommand(CmdPower, new byte[] { 0x02, 0x02, 0x01, (byte)(on ? 0x01 : 0x02) });
            WriteReport(cmd);
        }
        catch (Exception ex)
        {
            Logger.Log($"LG Monitor: set power failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Set LED brightness (1-12 levels).
    /// </summary>
    public void SetBrightness(int level)
    {
        if (!IsAvailable) return;
        level = Math.Clamp(level, 1, 12);
        try
        {
            var cmd = BuildSimpleCommand(CmdPower, new byte[] { 0x02, 0x02, 0x01, (byte)level });
            WriteReport(cmd);
        }
        catch (Exception ex)
        {
            Logger.Log($"LG Monitor: set brightness failed — {ex.Message}");
        }
    }

    private byte[] BuildSimpleCommand(byte command, byte[] data)
    {
        var buf = new byte[65];
        buf[0] = 0x00; // report ID
        buf[1] = StartCmd1;
        buf[2] = StartCmd2;
        buf[3] = command;
        Array.Copy(data, 0, buf, 4, data.Length);
        int crcEnd = 4 + data.Length;
        buf[crcEnd] = XorChecksum(buf, 4, crcEnd - 1);
        buf[crcEnd + 1] = EndCmd1;
        buf[crcEnd + 2] = EndCmd2;
        return buf;
    }

    private void WriteReport(byte[] report)
    {
        lock (_lock)
        {
            _stream?.Write(report);
        }
    }

    private static byte XorChecksum(byte[] data, int start, int end)
    {
        byte xor = 0;
        for (int i = start; i <= end; i++)
            xor ^= data[i];
        return xor;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_directModeActive && _stream != null)
            {
                // Disable direct mode — set back to static
                var cmd = BuildSimpleCommand(CmdMode, new byte[] { 0x02, 0x02, 0x03, 0x01 });
                WriteReport(cmd);
            }
            _stream?.Dispose();
        }
        catch { }
        Logger.Log("LG Monitor: disconnected");
    }
}
