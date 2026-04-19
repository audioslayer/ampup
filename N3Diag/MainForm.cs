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

        var infoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 8, 10, 8),
            Text = "Press N3 keys, side buttons, encoder clicks, and turns. " +
                   "This tool listens to vendor HID output/init plus Windows raw keyboard input and a native MI_00 read probe. " +
                   "Probe burst sends safe write commands only.",
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
