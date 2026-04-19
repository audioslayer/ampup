using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using AmpUp.Core;
using AmpUp.Core.Services;

namespace N3Diag;

internal sealed class MainForm : Form
{
    private static readonly object LogFileLock = new();
    private readonly TextBox _logBox;
    private readonly Label _statusLabel;
    private readonly Button _reconnectButton;
    private readonly Button _clearDisplaysButton;
    private readonly Button _clearLogButton;
    private readonly Button _openLogFolderButton;
    private readonly Button _runProbeBurstButton;
    private readonly ComboBox _displayTargetCombo;
    private readonly TextBox _imagePathBox;
    private readonly Button _browseImageButton;
    private readonly Button _sendImageButton;
    private readonly Button _sendTestSetButton;
    private readonly string _desktopLogPath;
    private N3Controller? _n3;
    private NativeHidProbe? _nativeProbe;
    private bool _rawInputRegistered;

    public MainForm()
    {
        Text = "N3 Diagnostic";
        Width = 1100;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        _desktopLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "N3Diag-log.txt");

        var topPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(10, 8, 10, 8),
            AutoSize = false,
            WrapContents = false,
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            Text = "Starting...",
            Margin = new Padding(0, 8, 16, 0),
        };

        _reconnectButton = new Button
        {
            Text = "Reconnect",
            AutoSize = true,
        };
        _reconnectButton.Click += (_, _) => StartDiagnostic();

        _clearDisplaysButton = new Button
        {
            Text = "Clear Displays",
            AutoSize = true,
        };
        _clearDisplaysButton.Click += (_, _) => _n3?.ClearAllDisplays();

        _clearLogButton = new Button
        {
            Text = "Clear Log",
            AutoSize = true,
        };
        _clearLogButton.Click += (_, _) => ClearLog();

        _openLogFolderButton = new Button
        {
            Text = "Open Log Folder",
            AutoSize = true,
        };
        _openLogFolderButton.Click += (_, _) => OpenLogFolder();

        _runProbeBurstButton = new Button
        {
            Text = "Run Probe Burst",
            AutoSize = true,
        };
        _runProbeBurstButton.Click += (_, _) => _ = RunProbeBurstAsync("manual");

        topPanel.Controls.Add(_statusLabel);
        topPanel.Controls.Add(_reconnectButton);
        topPanel.Controls.Add(_clearDisplaysButton);
        topPanel.Controls.Add(_clearLogButton);
        topPanel.Controls.Add(_openLogFolderButton);
        topPanel.Controls.Add(_runProbeBurstButton);

        var imagePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(10, 4, 10, 8),
            AutoSize = false,
            WrapContents = false,
        };

        var displayLabel = new Label
        {
            AutoSize = true,
            Text = "Display:",
            Margin = new Padding(0, 8, 6, 0),
        };

        _displayTargetCombo = new ComboBox
        {
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 4, 8, 0),
        };
        _displayTargetCombo.Items.Add(new DisplayTargetItem("Key 1", 0));
        _displayTargetCombo.Items.Add(new DisplayTargetItem("Key 2", 1));
        _displayTargetCombo.Items.Add(new DisplayTargetItem("Key 3", 2));
        _displayTargetCombo.Items.Add(new DisplayTargetItem("Key 4", 3));
        _displayTargetCombo.Items.Add(new DisplayTargetItem("Key 5", 4));
        _displayTargetCombo.Items.Add(new DisplayTargetItem("Key 6", 5));
        _displayTargetCombo.Items.Add(new DisplayTargetItem("All 6 keys", null));
        _displayTargetCombo.SelectedIndex = 0;

        _imagePathBox = new TextBox
        {
            Width = 280,
            Margin = new Padding(0, 4, 8, 0),
        };

        _browseImageButton = new Button
        {
            Text = "Browse Image",
            AutoSize = true,
        };
        _browseImageButton.Click += (_, _) => BrowseForImage();

        _sendImageButton = new Button
        {
            Text = "Send Image",
            AutoSize = true,
        };
        _sendImageButton.Click += async (_, _) => await SendSelectedImageAsync();

        _sendTestSetButton = new Button
        {
            Text = "Send Test Set",
            AutoSize = true,
        };
        _sendTestSetButton.Click += async (_, _) => await SendTestSetAsync();

        imagePanel.Controls.Add(displayLabel);
        imagePanel.Controls.Add(_displayTargetCombo);
        imagePanel.Controls.Add(_imagePathBox);
        imagePanel.Controls.Add(_browseImageButton);
        imagePanel.Controls.Add(_sendImageButton);
        imagePanel.Controls.Add(_sendTestSetButton);

        var infoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(10, 8, 10, 8),
            Text = "Press N3 keys, side buttons, encoder clicks, and turns. " +
                   "This tool listens to vendor HID output/init plus Windows raw keyboard input and a native MI_00 read probe. " +
                   "Use Send Image or Send Test Set to probe the LCD keys with 60x60 rotated JPEG writes.",
        };

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
        };

        Controls.Add(_logBox);
        Controls.Add(infoLabel);
        Controls.Add(imagePanel);
        Controls.Add(topPanel);

        Shown += (_, _) => StartDiagnostic();
        FormClosed += (_, _) => ShutdownDiagnostic();
        Logger.OnLogMessage += HandleLoggerMessage;

        AppendLog($"Desktop log: {_desktopLogPath}");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterRawInputSink(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_INPUT)
        {
            HandleRawInput(m.LParam);
        }

        base.WndProc(ref m);
    }

    private void StartDiagnostic()
    {
        ShutdownControllerOnly();
        AppendLog("---- starting N3 diagnostic ----");

        _n3 = new N3Controller();
        _n3.OnInput += HandleN3Input;

        bool connected = _n3.TryConnect();
        if (connected)
        {
            _nativeProbe = new NativeHidProbe(_n3.DevicePath, _n3.InputReportLength, _n3.OutputReportLength, AppendLog);
            _nativeProbe.Start();
        }

        _statusLabel.Text = connected
            ? $"Connected to {_n3.DeviceName} ({_n3.SerialNumber})"
            : "N3 not connected";
    }

    private void ShutdownDiagnostic()
    {
        Logger.OnLogMessage -= HandleLoggerMessage;
        ShutdownControllerOnly();
    }

    private void ShutdownControllerOnly()
    {
        if (_nativeProbe != null)
        {
            _nativeProbe.Dispose();
            _nativeProbe = null;
        }

        if (_n3 != null)
        {
            _n3.OnInput -= HandleN3Input;
            _n3.Dispose();
            _n3 = null;
        }
    }

    private void HandleLoggerMessage(string message)
    {
        AppendLog(message);
    }

    private void HandleN3Input(N3InputEvent evt)
    {
        AppendLog($"N3 parsed event: {evt.Describe()}");
    }

    private void ClearLog()
    {
        _logBox.Clear();

        lock (LogFileLock)
        {
            File.WriteAllText(_desktopLogPath, string.Empty);
        }

        AppendLog("Desktop log cleared");
    }

    private void AppendLog(string message)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        WriteDesktopLog(message);

        _logBox.AppendText(message + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void OpenLogFolder()
    {
        string? folder = Path.GetDirectoryName(_desktopLogPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            AppendLog("Open log folder failed: desktop path not found");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private void WriteDesktopLog(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (LogFileLock)
            {
                File.AppendAllText(_desktopLogPath, line);
            }
        }
        catch
        {
            // Keep the diagnostic UI alive even if desktop file logging fails.
        }
    }

    private void BrowseForImage()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Title = "Choose an image for the N3 display"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _imagePathBox.Text = dialog.FileName;
        }
    }

    private async Task SendSelectedImageAsync()
    {
        if (_n3 == null)
        {
            AppendLog("N3 display: cannot send image because the device is not connected");
            return;
        }

        string path = _imagePathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AppendLog("N3 display: choose an image file first");
            return;
        }

        if (_displayTargetCombo.SelectedItem is not DisplayTargetItem target)
        {
            AppendLog("N3 display: choose a display target first");
            return;
        }

        ToggleImageButtons(false);
        try
        {
            byte[] jpeg = await Task.Run(() => PrepareDisplayJpeg(path));

            bool ok = await Task.Run(() =>
            {
                if (target.KeyIndex.HasValue)
                {
                    return _n3.SendDisplayImage(target.KeyIndex.Value, jpeg, commit: true);
                }

                bool sent = true;
                for (int i = 0; i < N3Controller.DisplayKeyCount; i++)
                {
                    sent &= _n3.SendDisplayImage(i, jpeg, commit: false);
                }

                return sent && _n3.CommitDisplayChanges();
            });

            AppendLog(ok
                ? $"N3 display: sent {Path.GetFileName(path)} to {target.Label.ToLowerInvariant()}"
                : $"N3 display: failed to send {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppendLog($"N3 display: image send failed - {ex.Message}");
        }
        finally
        {
            ToggleImageButtons(true);
        }
    }

    private async Task SendTestSetAsync()
    {
        if (_n3 == null)
        {
            AppendLog("N3 display: cannot send test set because the device is not connected");
            return;
        }

        ToggleImageButtons(false);
        AppendLog("N3 display: sending numbered test set to all 6 keys");

        try
        {
            bool ok = await Task.Run(() =>
            {
                bool sent = true;
                for (int i = 0; i < N3Controller.DisplayKeyCount; i++)
                {
                    using var testCard = CreateTestCard(i);
                    byte[] jpeg = EncodeDisplayJpeg(testCard);
                    sent &= _n3.SendDisplayImage(i, jpeg, commit: false);
                }

                return sent && _n3.CommitDisplayChanges();
            });

            AppendLog(ok
                ? "N3 display: test set sent"
                : "N3 display: test set failed");
        }
        catch (Exception ex)
        {
            AppendLog($"N3 display: test set failed - {ex.Message}");
        }
        finally
        {
            ToggleImageButtons(true);
        }
    }

    private void ToggleImageButtons(bool enabled)
    {
        _browseImageButton.Enabled = enabled;
        _sendImageButton.Enabled = enabled;
        _sendTestSetButton.Enabled = enabled;
    }

    private static byte[] PrepareDisplayJpeg(string imagePath)
    {
        using var source = Image.FromFile(imagePath);
        using var canvas = new Bitmap(60, 60);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        float scale = Math.Min(60f / source.Width, 60f / source.Height);
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        float x = (60f - drawWidth) / 2f;
        float y = (60f - drawHeight) / 2f;
        graphics.DrawImage(source, x, y, drawWidth, drawHeight);

        return EncodeDisplayJpeg(canvas);
    }

    private static Bitmap CreateTestCard(int keyIndex)
    {
        var colors = new[]
        {
            Color.FromArgb(230, 57, 70),
            Color.FromArgb(241, 143, 1),
            Color.FromArgb(255, 190, 11),
            Color.FromArgb(6, 214, 160),
            Color.FromArgb(17, 138, 178),
            Color.FromArgb(131, 56, 236),
        };

        var bitmap = new Bitmap(60, 60);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var backgroundBrush = new SolidBrush(colors[keyIndex % colors.Length]);
        graphics.FillRectangle(backgroundBrush, 0, 0, 60, 60);
        using var bandBrush = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
        graphics.FillRectangle(bandBrush, 0, 0, 60, 14);
        using var triangleBrush = new SolidBrush(Color.White);
        graphics.FillPolygon(triangleBrush, new[]
        {
            new Point(4, 4),
            new Point(18, 4),
            new Point(4, 18),
        });
        using var borderPen = new Pen(Color.White, 2f);
        graphics.DrawRectangle(borderPen, 1, 1, 57, 57);

        using var numberFont = new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var labelFont = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var whiteBrush = new SolidBrush(Color.White);

        string numberText = (keyIndex + 1).ToString();
        var numberRect = new RectangleF(0, 12, 60, 30);
        var center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(numberText, numberFont, whiteBrush, numberRect, center);
        graphics.DrawString("UP", labelFont, whiteBrush, new RectangleF(20, 2, 36, 10), center);
        graphics.DrawString("AMP", labelFont, whiteBrush, new RectangleF(0, 44, 60, 12), center);

        return bitmap;
    }

    private static byte[] EncodeDisplayJpeg(Image image)
    {
        using var rotated = new Bitmap(image);
        rotated.RotateFlip(RotateFlipType.Rotate90FlipNone);

        using var ms = new MemoryStream();
        var codec = GetJpegEncoder();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
        rotated.Save(ms, codec, encoderParams);
        return ms.ToArray();
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        return ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    }

    private async Task RunProbeBurstAsync(string reason)
    {
        if (_nativeProbe == null)
        {
            AppendLog($"N3 probe: skipped {reason} burst because native probe is not ready");
            return;
        }

        AppendLog($"---- running {reason} probe burst ----");
        _runProbeBurstButton.Enabled = false;

        try
        {
            await Task.Run(async () =>
            {
                AppendLog($"N3 probe: baseline ready ({reason})");
                await Task.Delay(150);

                _n3?.SendDiagnosticCommand("CRT DIS", 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x44, 0x49, 0x53);
                await ProbePauseAsync($"{reason}-after-dis");

                _n3?.SendDiagnosticCommand("CRT LIG", 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x4C, 0x49, 0x47, 0x00, 0x00, 0x00, 0x00);
                await ProbePauseAsync($"{reason}-after-lig");

                _n3?.SendDiagnosticCommand("CRT CONNECT", 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x43, 0x4F, 0x4E, 0x4E, 0x45, 0x43, 0x54);
                await ProbePauseAsync($"{reason}-after-connect");

                _n3?.SendDiagnosticCommand("CRT STP", 0x00, 0x43, 0x52, 0x54, 0x00, 0x00, 0x53, 0x54, 0x50);
                await ProbePauseAsync($"{reason}-after-stp");
            });
        }
        finally
        {
            _runProbeBurstButton.Enabled = true;
        }

        AppendLog($"---- completed {reason} probe burst ----");
    }

    private async Task ProbePauseAsync(string label)
    {
        await Task.Delay(150);
        AppendLog($"N3 probe: completed step {label}");
    }

    private void RegisterRawInputSink(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _rawInputRegistered) return;

        var devices = new[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY,
                hwndTarget = hwnd
            }
        };

        if (NativeMethods.RegisterRawInputDevices(devices, (uint)devices.Length,
            (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
        {
            _rawInputRegistered = true;
            AppendLog("Raw input: keyboard sink registered");
        }
        else
        {
            AppendLog($"Raw input: registration failed ({Marshal.GetLastWin32Error()})");
        }
    }

    private sealed record DisplayTargetItem(string Label, int? KeyIndex)
    {
        public override string ToString() => Label;
    }

    private void HandleRawInput(IntPtr lParam)
    {
        try
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            uint result = NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
            if (result != 0 || size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (NativeMethods.GetRawInputData(lParam, NativeMethods.RID_INPUT, buffer, ref size, headerSize) != size)
                {
                    return;
                }

                var raw = Marshal.PtrToStructure<NativeMethods.RAWINPUT>(buffer);
                if (raw.header.dwType != NativeMethods.RIM_TYPEKEYBOARD) return;

                string deviceName = GetRawInputDeviceName(raw.header.hDevice);
                if (string.IsNullOrWhiteSpace(deviceName)) return;
                if (!deviceName.Contains("vid_5548&pid_1001", StringComparison.OrdinalIgnoreCase)) return;

                string direction = raw.data.keyboard.Message switch
                {
                    NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN => "down",
                    NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP => "up",
                    _ => $"msg=0x{raw.data.keyboard.Message:X4}"
                };

                AppendLog(
                    $"N3 raw [keyboard-msg]: dev={deviceName} vkey=0x{raw.data.keyboard.VKey:X2} " +
                    $"make=0x{raw.data.keyboard.MakeCode:X2} flags=0x{raw.data.keyboard.Flags:X2} {direction}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Raw input: handle failed - {ex.Message}");
        }
    }

    private static string GetRawInputDeviceName(IntPtr deviceHandle)
    {
        uint size = 0;
        uint result = NativeMethods.GetRawInputDeviceInfo(deviceHandle, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (result != 0 || size == 0) return "";

        IntPtr ptr = Marshal.AllocHGlobal((int)(size * 2));
        try
        {
            result = NativeMethods.GetRawInputDeviceInfo(deviceHandle, NativeMethods.RIDI_DEVICENAME, ptr, ref size);
            if (result == uint.MaxValue || result == 0) return "";
            return Marshal.PtrToStringUni(ptr) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
