using System.Text;
using HidSharp;

namespace AmpUp.Core.Services;

public enum N3InputKind
{
    StateSync,
    DisplayKey,
    SideButton,
    EncoderPress,
    EncoderTwist,
    Unknown,
}

public sealed class N3InputEvent
{
    public N3InputKind Kind { get; init; }
    public int Index { get; init; } = -1;
    public bool? IsPressed { get; init; }
    public int Delta { get; init; }
    public byte InputCode { get; init; }
    public byte State { get; init; }
    public bool AckPrefixed { get; init; }
    public byte[] RawReport { get; init; } = Array.Empty<byte>();

    public string Describe()
    {
        string code = $"0x{InputCode:X2}";
        return Kind switch
        {
            N3InputKind.StateSync =>
                $"state sync code={code} state=0x{State:X2}",
            N3InputKind.DisplayKey =>
                $"display key {Index + 1} {(IsPressed == true ? "down" : "up")} code={code} state=0x{State:X2}",
            N3InputKind.SideButton =>
                $"side button {Index + 1} {(IsPressed == true ? "down" : "up")} code={code} state=0x{State:X2}",
            N3InputKind.EncoderPress =>
                $"encoder press {Index + 1} {(IsPressed == true ? "down" : "up")} code={code} state=0x{State:X2}",
            N3InputKind.EncoderTwist =>
                $"encoder twist {Index + 1} delta={Delta:+#;-#;0} code={code}",
            _ =>
                $"unknown input code={code} state=0x{State:X2}",
        };
    }
}

/// <summary>
/// Native HID bring-up service for the TreasLin / VSDinside N3 family.
/// This first pass focuses on safe device discovery, init, keepalive, and raw input capture.
/// </summary>
public sealed class N3Controller : IDisposable
{
    public const int VendorId = 0x5548;
    public const int ProductId = 0x1001;
    public const int UsagePage = 0xFFA0; // 65440 decimal
    public const int UsageId = 0x0001;
    public const int ProtocolVersion = 3; // confirmed upstream for TreasLin N3

    private const int DefaultPacketSize = 1024;
    private const int ReadTimeoutMs = 500;
    private const int WriteTimeoutMs = 500;
    private const int KeepAliveMs = 5000;

    private readonly object _streamLock = new();
    private HidDevice? _device;
    private HidStream? _stream;
    private HidDevice? _keyboardDevice;
    private HidStream? _keyboardStream;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private CancellationTokenSource? _keyboardReadCts;
    private Task? _keyboardReadTask;
    private System.Threading.Timer? _keepAliveTimer;
    private volatile bool _disposed;
    private volatile bool _initialized;

    public event Action<N3InputEvent>? OnInput;

    public bool IsAvailable => _stream != null && !_disposed;
    public string DeviceName { get; private set; } = "TreasLin N3";
    public string SerialNumber { get; private set; } = "";
    public int InputReportLength { get; private set; }
    public int OutputReportLength { get; private set; }
    public string DevicePath { get; private set; } = "";
    public string KeyboardPath { get; private set; } = "";

    public bool TryConnect(bool initialize = true)
    {
        try
        {
            DisposeConnection();

            var devices = DeviceList.Local.GetHidDevices(VendorId, ProductId).ToList();
            if (devices.Count == 0)
            {
                Logger.Log("N3: no compatible HID device found");
                return false;
            }

            foreach (var candidate in devices)
            {
                string candidatePath = candidate.DevicePath ?? "";
                int candidateIn = SafeGet(() => candidate.GetMaxInputReportLength(), 0);
                int candidateOut = SafeGet(() => candidate.GetMaxOutputReportLength(), 0);
                Logger.Log($"N3: candidate path={candidatePath} in={candidateIn} out={candidateOut}");
            }

            _device = SelectPrimaryInterface(devices);
            if (_device == null)
            {
                Logger.Log("N3: compatible VID/PID found, but no primary HID interface was selected");
                return false;
            }

            _stream = _device.Open();
            _stream.ReadTimeout = ReadTimeoutMs;
            _stream.WriteTimeout = WriteTimeoutMs;

            _keyboardDevice = devices.FirstOrDefault(d =>
                !string.Equals(d.DevicePath, _device.DevicePath, StringComparison.OrdinalIgnoreCase) &&
                (d.DevicePath?.Contains("mi_01", StringComparison.OrdinalIgnoreCase) == true ||
                 SafeGet(() => d.GetMaxOutputReportLength(), 0) <= 64));

            if (_keyboardDevice != null)
            {
                try
                {
                    _keyboardStream = _keyboardDevice.Open();
                    _keyboardStream.ReadTimeout = ReadTimeoutMs;
                    _keyboardStream.WriteTimeout = WriteTimeoutMs;
                    KeyboardPath = _keyboardDevice.DevicePath ?? "";
                    Logger.Log(
                        $"N3: keyboard-side HID opened (path={KeyboardPath}, in={SafeGet(() => _keyboardDevice.GetMaxInputReportLength(), 0)}, out={SafeGet(() => _keyboardDevice.GetMaxOutputReportLength(), 0)})");
                }
                catch (Exception ex)
                {
                    Logger.Log($"N3: keyboard-side HID open failed - {ex.Message}");
                }
            }

            InputReportLength = SafeGet(() => _device.GetMaxInputReportLength(), DefaultPacketSize + 1);
            OutputReportLength = SafeGet(() => _device.GetMaxOutputReportLength(), DefaultPacketSize + 1);
            DevicePath = _device.DevicePath ?? "";
            DeviceName = SafeGet(() => _device.GetProductName(), "TreasLin N3");
            SerialNumber = SafeGet(() => _device.GetSerialNumber(), "");

            Logger.Log(
                $"N3: connected to {DeviceName} " +
                $"(vid=0x{VendorId:X4}, pid=0x{ProductId:X4}, in={InputReportLength}, out={OutputReportLength}, serial={SerialNumber}, path={DevicePath})");

            if (initialize)
            {
                TryInitialize();
            }

            StartReadLoop();
            StartKeyboardReadLoop();
            _keepAliveTimer = new System.Threading.Timer(_ => SafeKeepAlive(), null, KeepAliveMs, KeepAliveMs);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: connect failed - {ex.Message}");
            DisposeConnection();
            return false;
        }
    }

    public bool TryInitialize()
    {
        if (!IsAvailable) return false;
        if (_initialized) return true;

        try
        {
            // Mirajazz initialization sequence for pv3 N3-family devices.
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x44, 0x49, 0x53); // CRT..DIS
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, 0x00, 0x00); // CRT..LIG
            _initialized = true;
            Logger.Log("N3: initialization sequence sent (CRT DIS, CRT LIG)");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: initialization failed - {ex.Message}");
            return false;
        }
    }

    public void SetBrightness(byte percent)
    {
        if (!EnsureInitialized()) return;
        percent = Math.Clamp(percent, (byte)0, (byte)100);

        try
        {
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, percent);
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: set brightness failed - {ex.Message}");
        }
    }

    public void KeepAlive()
    {
        if (!EnsureInitialized()) return;

        try
        {
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4F, 0x4E, 0x4E, 0x45, 0x43, 0x54);
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: keepalive failed - {ex.Message}");
        }
    }

    public void ClearAllDisplays()
    {
        if (!EnsureInitialized()) return;

        try
        {
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, 0xFF);
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x54, 0x50);
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: clear displays failed - {ex.Message}");
        }
    }

    private bool EnsureInitialized()
    {
        if (!IsAvailable) return false;
        return _initialized || TryInitialize();
    }

    private void StartReadLoop()
    {
        if (_stream == null) return;

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = new CancellationTokenSource();

        int reportLength = InputReportLength > 0 ? InputReportLength : 513;
        Logger.Log($"N3: read loop started on primary HID (len={reportLength})");
        _readTask = Task.Run(() => ReadLoop(_stream, reportLength, "primary", true, _readCts.Token));
    }

    private void StartKeyboardReadLoop()
    {
        if (_keyboardStream == null) return;

        _keyboardReadCts?.Cancel();
        _keyboardReadCts?.Dispose();
        _keyboardReadCts = new CancellationTokenSource();

        int reportLength = SafeGet(() => _keyboardDevice?.GetMaxInputReportLength() ?? 64, 64);
        Logger.Log($"N3: read loop started on keyboard-side HID (len={reportLength})");
        _keyboardReadTask = Task.Run(() => ReadLoop(_keyboardStream, reportLength, "keyboard", false, _keyboardReadCts.Token));
    }

    private async Task ReadLoop(HidStream stream, int reportLength, string channelName, bool parseKnownProtocol, CancellationToken ct)
    {
        reportLength = Math.Max(reportLength, 8);

        while (!ct.IsCancellationRequested && !_disposed)
        {
            var buffer = new byte[reportLength];

            try
            {
                int read;

                if (ReferenceEquals(stream, _stream))
                {
                    lock (_streamLock)
                    {
                        if (_stream == null) break;
                        read = _stream.Read(buffer, 0, buffer.Length);
                    }
                }
                else
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }

                if (read <= 0) continue;

                var report = buffer.AsSpan(0, read).ToArray();
                if (IsAllZero(report)) continue;

                if (parseKnownProtocol && TryParseInput(report, out var parsed))
                {
                    Logger.Log($"N3 input [{channelName}]: {parsed.Describe()} raw={ToHex(report)}");
                    OnInput?.Invoke(parsed);
                }
                else
                {
                    Logger.Log($"N3 raw [{channelName}]: {ToHex(report)}");
                }
            }
            catch (TimeoutException)
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Log($"N3: read loop stopped on {channelName} - {ex.Message}");
                break;
            }
        }
    }

    private static HidDevice? SelectPrimaryInterface(IEnumerable<HidDevice> devices)
    {
        // Prefer the vendor-defined MI_00 interface with large reports.
        return devices
            .OrderByDescending(d => SafeGet(() => d.GetMaxOutputReportLength(), 0))
            .ThenByDescending(d => SafeGet(() => d.GetMaxInputReportLength(), 0))
            .ThenByDescending(d => d.DevicePath?.Contains("mi_00", StringComparison.OrdinalIgnoreCase) == true)
            .FirstOrDefault(d =>
            {
                var path = d.DevicePath ?? "";
                int outLen = SafeGet(() => d.GetMaxOutputReportLength(), 0);
                int inLen = SafeGet(() => d.GetMaxInputReportLength(), 0);
                return path.Contains("mi_00", StringComparison.OrdinalIgnoreCase) || outLen > 64 || inLen > 64;
            });
    }

    private bool TryParseInput(byte[] report, out N3InputEvent parsed)
    {
        parsed = new N3InputEvent { Kind = N3InputKind.Unknown, RawReport = report };

        if (!TryGetAckOffset(report, out int offset))
        {
            return false;
        }

        if (report.Length <= offset + 10) return false;

        byte input = report[offset + 9];
        byte state = report[offset + 10];
        bool isPressed = state != 0;

        parsed = input switch
        {
            0x00 => new N3InputEvent
            {
                Kind = N3InputKind.StateSync,
                InputCode = input,
                State = state,
                AckPrefixed = true,
                RawReport = report,
            },
            >= 0x01 and <= 0x06 => new N3InputEvent
            {
                Kind = N3InputKind.DisplayKey,
                Index = input - 1,
                IsPressed = isPressed,
                InputCode = input,
                State = state,
                AckPrefixed = true,
                RawReport = report,
            },
            0x25 => BuildSideButtonEvent(0, input, state, report),
            0x30 => BuildSideButtonEvent(1, input, state, report),
            0x31 => BuildSideButtonEvent(2, input, state, report),
            0x33 => BuildEncoderPressEvent(0, input, state, report),
            0x35 => BuildEncoderPressEvent(1, input, state, report),
            0x34 => BuildEncoderPressEvent(2, input, state, report),
            0x90 => BuildEncoderTwistEvent(0, -1, input, state, report),
            0x91 => BuildEncoderTwistEvent(0, 1, input, state, report),
            0x50 => BuildEncoderTwistEvent(1, -1, input, state, report),
            0x51 => BuildEncoderTwistEvent(1, 1, input, state, report),
            0x60 => BuildEncoderTwistEvent(2, -1, input, state, report),
            0x61 => BuildEncoderTwistEvent(2, 1, input, state, report),
            _ => new N3InputEvent
            {
                Kind = N3InputKind.Unknown,
                InputCode = input,
                State = state,
                AckPrefixed = true,
                RawReport = report,
            }
        };

        return true;
    }

    private static N3InputEvent BuildSideButtonEvent(int index, byte input, byte state, byte[] report)
    {
        return new N3InputEvent
        {
            Kind = N3InputKind.SideButton,
            Index = index,
            IsPressed = state != 0,
            InputCode = input,
            State = state,
            AckPrefixed = true,
            RawReport = report,
        };
    }

    private static N3InputEvent BuildEncoderPressEvent(int index, byte input, byte state, byte[] report)
    {
        return new N3InputEvent
        {
            Kind = N3InputKind.EncoderPress,
            Index = index,
            IsPressed = state != 0,
            InputCode = input,
            State = state,
            AckPrefixed = true,
            RawReport = report,
        };
    }

    private static N3InputEvent BuildEncoderTwistEvent(int index, int delta, byte input, byte state, byte[] report)
    {
        return new N3InputEvent
        {
            Kind = N3InputKind.EncoderTwist,
            Index = index,
            Delta = delta,
            InputCode = input,
            State = state,
            AckPrefixed = true,
            RawReport = report,
        };
    }

    private void SafeKeepAlive()
    {
        try
        {
            KeepAlive();
        }
        catch
        {
            // Keepalive is best-effort during bring-up.
        }
    }

    private void WriteExtendedReport(params byte[] payload)
    {
        var report = new byte[Math.Max(OutputReportLength, DefaultPacketSize + 1)];
        Array.Copy(payload, report, Math.Min(payload.Length, report.Length));

        lock (_streamLock)
        {
            _stream?.Write(report, 0, report.Length);
        }
    }

    private static bool TryGetAckOffset(ReadOnlySpan<byte> report, out int offset)
    {
        if (report.Length >= 3 && report[0] == 0x41 && report[1] == 0x43 && report[2] == 0x4B)
        {
            offset = 0;
            return true;
        }

        if (report.Length >= 4 && report[0] == 0x00 && report[1] == 0x41 && report[2] == 0x43 && report[3] == 0x4B)
        {
            offset = 1;
            return true;
        }

        offset = -1;
        return false;
    }

    private static bool IsAllZero(byte[] data)
    {
        foreach (byte b in data)
        {
            if (b != 0) return false;
        }
        return true;
    }

    private static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 3);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    private void DisposeConnection()
    {
        _initialized = false;

        try
        {
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
        }
        catch { }

        try
        {
            _readCts?.Cancel();
        }
        catch { }

        try
        {
            _keyboardReadCts?.Cancel();
        }
        catch { }

        try
        {
            _readTask?.Wait(250);
        }
        catch { }

        try
        {
            _keyboardReadTask?.Wait(250);
        }
        catch { }

        try
        {
            _readCts?.Dispose();
            _readCts = null;
        }
        catch { }

        try
        {
            _keyboardReadCts?.Dispose();
            _keyboardReadCts = null;
        }
        catch { }

        lock (_streamLock)
        {
            try { _stream?.Dispose(); } catch { }
            _stream = null;
        }

        try { _keyboardStream?.Dispose(); } catch { }
        _keyboardStream = null;

        _device = null;
        _keyboardDevice = null;
        KeyboardPath = "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeConnection();
        Logger.Log("N3: disconnected");
    }
}
