using System.Text;
using System.Text.RegularExpressions;
using HidSharp;

namespace AmpUp.Core.Services;

public enum N3FirmwareFamily
{
    Unknown,
    TreasLinProtocolV3,
    MiraboxV2,
    MiraboxV25,
    MiraboxV3,
}

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
    public const int DisplayKeyCount = 6;

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
    public string FirmwareVersion { get; private set; } = "";
    public string FirmwareProbeSource { get; private set; } = "Unavailable";
    public N3FirmwareFamily FirmwareFamily { get; private set; } = N3FirmwareFamily.Unknown;
    public int InputReportLength { get; private set; }
    public int OutputReportLength { get; private set; }
    public int FeatureReportLength { get; private set; }
    public int ConnectedVendorId { get; private set; } = VendorId;
    public int ConnectedProductId { get; private set; } = ProductId;
    public string DevicePath { get; private set; } = "";
    public string KeyboardPath { get; private set; } = "";
    public bool SupportsDualDisplayProtocol => FirmwareFamily != N3FirmwareFamily.MiraboxV25;

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
            FeatureReportLength = SafeGet(() => _device.GetMaxFeatureReportLength(), 0);
            ConnectedVendorId = SafeGet(() => _device.VendorID, VendorId);
            ConnectedProductId = SafeGet(() => _device.ProductID, ProductId);
            DevicePath = _device.DevicePath ?? "";
            DeviceName = SafeGet(() => _device.GetProductName(), "TreasLin N3");
            SerialNumber = SafeGet(() => _device.GetSerialNumber(), "");
            ProbeFirmwareIdentity();

            Logger.Log(
                $"N3: connected to {DeviceName} " +
                $"(vid=0x{ConnectedVendorId:X4}, pid=0x{ConnectedProductId:X4}, in={InputReportLength}, out={OutputReportLength}, feature={FeatureReportLength}, serial={SerialNumber}, path={DevicePath})");

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
            if (!SupportsDualDisplayProtocol)
            {
                Logger.Log($"N3: dual-display init skipped for firmware family {FirmwareFamily} (version='{FirmwareVersion}')");
                return false;
            }

            // Mirajazz initialization sequence for pv3 N3-family devices.
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x44, 0x49, 0x53); // CRT..DIS
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, 0x00, 0x00); // CRT..LIG
            _initialized = true;
            Logger.Log($"N3: initialization sequence sent (CRT DIS, CRT LIG) for {FirmwareFamily} firmware");
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

    /// <summary>
    /// Put the N3 LCDs into firmware standby via the `CRT HAN` command —
    /// different from SetBrightness(0), which just dims; this actually powers
    /// the panels down. Wake with <see cref="Wake"/>.
    /// Reference: 4ndv/mirajazz src/device.rs sleep() implementation.
    /// </summary>
    public void Sleep()
    {
        if (!EnsureInitialized()) return;
        try
        {
            // CRT HAN — same packet mirajazz's sleep() sends.
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x48, 0x41, 0x4E);
            Logger.Log("N3: sleep (CRT HAN) sent");
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: sleep failed - {ex.Message}");
        }
    }

    /// <summary>
    /// Wake the N3 from <see cref="Sleep"/> by re-running the init sequence.
    /// Callers should follow up with <see cref="SetBrightness"/> + a display
    /// resync so the previously-rendered frames come back.
    /// </summary>
    public void Wake()
    {
        try
        {
            // Re-run init — CRT DIS + CRT LIG — to power the screens back on.
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x44, 0x49, 0x53);
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, 0x00, 0x00);
            Logger.Log("N3: wake (CRT DIS + CRT LIG) sent");
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: wake failed - {ex.Message}");
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
        if (!SupportsDualDisplayProtocol) return;

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

    public bool ClearDisplay(int keyIndex, bool commit = true)
    {
        if (!EnsureInitialized()) return false;
        if (!SupportsDualDisplayProtocol) return false;
        if (keyIndex < 0 || keyIndex >= DisplayKeyCount)
        {
            Logger.Log($"N3: clear display skipped because key index {keyIndex} is out of range");
            return false;
        }

        try
        {
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, (byte)(keyIndex + 1));
            if (commit)
            {
                CommitDisplayChanges();
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: clear display {keyIndex + 1} failed - {ex.Message}");
            return false;
        }
    }

    public bool SendDisplayImage(int keyIndex, byte[] imageData, bool commit = true)
    {
        if (!EnsureInitialized()) return false;
        if (!SupportsDualDisplayProtocol) return false;
        if (keyIndex < 0 || keyIndex >= DisplayKeyCount)
        {
            Logger.Log($"N3: send display image skipped because key index {keyIndex} is out of range");
            return false;
        }

        if (imageData == null || imageData.Length == 0)
        {
            Logger.Log($"N3: send display image skipped because key {keyIndex + 1} has no image data");
            return false;
        }

        try
        {
            WriteExtendedReport(
                0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x42, 0x41, 0x54, 0x00, 0x00,
                (byte)(imageData.Length >> 8),
                (byte)imageData.Length,
                (byte)(keyIndex + 1));
            WriteImageDataReports(imageData);

            if (commit)
            {
                CommitDisplayChanges();
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: send display image {keyIndex + 1} failed - {ex.Message}");
            return false;
        }
    }

    public bool CommitDisplayChanges()
    {
        if (!EnsureInitialized()) return false;
        if (!SupportsDualDisplayProtocol) return false;

        try
        {
            WriteExtendedReport(0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x54, 0x50);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: display commit failed - {ex.Message}");
            return false;
        }
    }

    public bool SendDiagnosticCommand(string label, params byte[] payload)
    {
        if (!IsAvailable)
        {
            Logger.Log($"N3: diagnostic send skipped ({label}) because device is not connected");
            return false;
        }

        try
        {
            WriteExtendedReport(payload);
            Logger.Log($"N3: diagnostic tx [{label}] {ToHex(payload)}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"N3: diagnostic send failed ({label}) - {ex.Message}");
            return false;
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
                    // Verbose raw-hex dump suppressed — each press was ~500
                    // bytes of mostly-zero bytes. Keep the structured input
                    // event on OnInput for the app to act on.
                    OnInput?.Invoke(parsed);
                }
                else
                {
                    // Only log unknown frames (they're rare and interesting).
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

    public static bool TryParseInputReport(byte[] report, out N3InputEvent parsed)
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

    private bool TryParseInput(byte[] report, out N3InputEvent parsed)
    {
        return TryParseInputReport(report, out parsed);
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

    private void WriteImageDataReports(ReadOnlySpan<byte> imageData)
    {
        int reportLength = Math.Max(OutputReportLength, DefaultPacketSize + 1);
        int payloadLength = Math.Max(1, reportLength - 1);
        int offset = 0;

        while (offset < imageData.Length)
        {
            int chunkLength = Math.Min(payloadLength, imageData.Length - offset);
            var report = new byte[reportLength];
            imageData.Slice(offset, chunkLength).CopyTo(report.AsSpan(1, chunkLength));

            lock (_streamLock)
            {
                _stream?.Write(report, 0, report.Length);
            }

            offset += chunkLength;
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

    private void ProbeFirmwareIdentity()
    {
        string firmware = TryReadFirmwareVersionFromFeatureReports(out string source);
        if (string.IsNullOrWhiteSpace(firmware))
        {
            firmware = TryExtractFirmwareToken($"{DeviceName} {SerialNumber} {DevicePath}");
            if (!string.IsNullOrWhiteSpace(firmware))
            {
                source = "descriptor token";
            }
        }

        FirmwareVersion = firmware;
        FirmwareFamily = InferFirmwareFamily(firmware);
        FirmwareProbeSource = source;

        if (FirmwareFamily == N3FirmwareFamily.Unknown)
        {
            FirmwareFamily = InferFirmwareFamilyFromDescriptors();
            if (FirmwareFamily != N3FirmwareFamily.Unknown)
            {
                FirmwareProbeSource = string.IsNullOrWhiteSpace(FirmwareVersion)
                    ? "device heuristic"
                    : $"{FirmwareProbeSource} + heuristic";
            }
        }

        Logger.Log(
            $"N3: firmware probe family={FirmwareFamily}, version='{FirmwareVersion}', source={FirmwareProbeSource}, featureLen={FeatureReportLength}");
    }

    private string TryReadFirmwareVersionFromFeatureReports(out string source)
    {
        source = "Unavailable";
        if (_stream == null || FeatureReportLength <= 1) return "";

        int requestLength = Math.Clamp(FeatureReportLength, 16, 256);
        string fallbackText = "";

        for (byte reportId = 0; reportId < 4; reportId++)
        {
            try
            {
                var buffer = new byte[requestLength];
                buffer[0] = reportId;

                lock (_streamLock)
                {
                    _stream?.GetFeature(buffer);
                }

                string printable = ExtractPrintableAscii(buffer);
                if (string.IsNullOrWhiteSpace(printable)) continue;

                string token = TryExtractFirmwareToken(printable);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    source = $"feature report 0x{reportId:X2}";
                    return token;
                }

                if (string.IsNullOrWhiteSpace(fallbackText) && printable.Contains("N3", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackText = printable;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"N3: feature report 0x{reportId:X2} probe failed - {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            source = "feature report ascii";
            return fallbackText;
        }

        return "";
    }

    private static string ExtractPrintableAscii(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        bool lastWasSpace = false;

        foreach (byte value in data)
        {
            if (value >= 32 && value <= 126)
            {
                sb.Append((char)value);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static string TryExtractFirmwareToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var match = Regex.Match(
            value,
            @"(V25\.N3[^\s]*|V3\.N3[^\s]*|N3\.[A-Za-z0-9._-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value.Trim() : "";
    }

    private N3FirmwareFamily InferFirmwareFamily(string firmwareToken)
    {
        if (!string.IsNullOrWhiteSpace(firmwareToken))
        {
            if (firmwareToken.Contains("V25.N3", StringComparison.OrdinalIgnoreCase)) return N3FirmwareFamily.MiraboxV25;
            if (firmwareToken.Contains("V3.N3", StringComparison.OrdinalIgnoreCase)) return N3FirmwareFamily.MiraboxV3;
        }

        return N3FirmwareFamily.Unknown;
    }

    private N3FirmwareFamily InferFirmwareFamilyFromDescriptors()
    {
        if (ConnectedVendorId == 0x5548 && ConnectedProductId == 0x1001)
        {
            return N3FirmwareFamily.TreasLinProtocolV3;
        }

        if (ConnectedVendorId == 0x6602)
        {
            return N3FirmwareFamily.MiraboxV2;
        }

        if (ConnectedVendorId == 0x6603 && (ConnectedProductId == 0x1002 || ConnectedProductId == 0x1003))
        {
            return N3FirmwareFamily.MiraboxV3;
        }

        return N3FirmwareFamily.Unknown;
    }

    private void DisposeConnection()
    {
        _initialized = false;
        FirmwareVersion = "";
        FirmwareProbeSource = "Unavailable";
        FirmwareFamily = N3FirmwareFamily.Unknown;
        FeatureReportLength = 0;
        ConnectedVendorId = VendorId;
        ConnectedProductId = ProductId;

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
