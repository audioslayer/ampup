using System.Drawing;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WolfMixer;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppConfig _config = null!;
    private SerialReader _serial = null!;
    private AudioMixer _mixer = null!;
    private ButtonHandler _buttons = null!;
    private RgbController _rgb = null!;
    private MainWindow _mainWindow = null!;
    private System.Threading.Timer? _mutePollingTimer;
    private DateTime _connectedAt = DateTime.MinValue;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isConnected;
    private OsdOverlay? _osdOverlay;
    private HAIntegration? _ha;

    /// <summary>
    /// Last hardware knob positions (0-1), updated on every knob event.
    /// Used by MixerView to display position for non-audio targets.
    /// </summary>
    public static readonly float[] KnobPositions = { 1f, 1f, 1f, 1f, 1f };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new Mutex(true, "AmpUp_SingleInstance", out bool isNew);
        if (!isNew)
        {
            GlassDialog.ShowInfo("Amp Up is already running. Check the system tray.");
            Shutdown();
            return;
        }

        Logger.Log("Amp Up starting (WPF)...");

        // Load config and create backend
        _config = ConfigManager.Load();
        _mixer = new AudioMixer();
        _buttons = new ButtonHandler();
        _rgb = new RgbController();

        _buttons.OnProfileSwitch += HandleProfileSwitch;
        _buttons.OnDeviceSwitched += HandleDeviceSwitched;
        _buttons.OnBrightnessCycle += HandleBrightnessCycle;

        // Start Home Assistant integration
        _ha = new HAIntegration(_config.HomeAssistant);
        _buttons.SetHAIntegration(_ha);
        if (_config.HomeAssistant.Enabled)
            _ = _ha.TestConnectionAsync(); // sets IsAvailable for knob routing

        // Start audio mixer
        _mixer.Start();

        // Start serial reader
        _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
        _serial.OnKnob += HandleKnob;
        _serial.OnButton += HandleButton;
        _serial.OnConnectionChanged += HandleConnection;
        _serial.Start();

        // Apply RGB config
        ApplyRgbConfig();

        // Poll mute states every 500ms for LED status effects
        _mutePollingTimer = new System.Threading.Timer(_ => PollMuteStates(), null, 1000, 500);

        // Apply startup setting
        ApplyStartupSetting();

        // Create tray icon
        SetupTrayIcon();

        // Create and show main window
        _mainWindow = new MainWindow();
        _mainWindow.Closing += MainWindow_Closing;
        _mainWindow.Initialize(_config, _mixer, OnConfigChanged);
        _mainWindow.Show();

        // Sync connection status — serial may have connected before window was created
        if (_isConnected)
            _mainWindow.SetConnectionStatus(true);
    }

    private Forms.ToolStripLabel? _trayStatusLabel;

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(false),
            Text = "Amp Up",
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var menu = new Forms.ContextMenuStrip();
        menu.BackColor = Color.FromArgb(0x11, 0x11, 0x11);
        menu.ForeColor = Color.FromArgb(0xE8, 0xE8, 0xE8);
        menu.Padding = new Forms.Padding(0, 6, 0, 6);
        menu.ShowImageMargin = false;
        menu.Renderer = new GlassMenuRenderer();

        // Header: app name
        var header = new Forms.ToolStripLabel
        {
            Text = "  AMP UP",
            Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold),
            ForeColor = Color.FromArgb(0x00, 0xE6, 0x76),
            Padding = new Forms.Padding(10, 8, 10, 0),
        };
        menu.Items.Add(header);

        // Version label
        var version = new Forms.ToolStripLabel
        {
            Text = $"  v{UpdateChecker.CurrentVersion}",
            Font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Regular),
            ForeColor = Color.FromArgb(0x44, 0x44, 0x44),
            Padding = new Forms.Padding(10, 0, 10, 4),
        };
        menu.Items.Add(version);

        // Connection status
        _trayStatusLabel = new Forms.ToolStripLabel
        {
            Text = "  ○  Disconnected",
            Font = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Regular),
            ForeColor = Color.FromArgb(0x9A, 0x9A, 0x9A),
            Padding = new Forms.Padding(10, 2, 10, 6),
        };
        menu.Items.Add(_trayStatusLabel);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Show
        var showItem = new Forms.ToolStripMenuItem("  Open Amp Up")
        {
            Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold),
            Padding = new Forms.Padding(6, 6, 6, 6),
        };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Exit
        var exitItem = new Forms.ToolStripMenuItem("  Exit")
        {
            Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular),
            ForeColor = Color.FromArgb(0xFF, 0x44, 0x44),
            Padding = new Forms.Padding(6, 6, 6, 6),
        };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null) return;
        Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void ExitApp()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        Dispatcher.Invoke(() => Shutdown());
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Close to tray instead of exiting
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    /// <summary>
    /// Creates a 32x32 tray icon from the embedded logo PNG.
    /// Connected = full color, disconnected = grayscale.
    /// </summary>
    private static Icon CreateTrayIcon(bool connected)
    {
        // Load logo from embedded WPF resource
        var uri = new Uri("pack://application:,,,/Assets/ampuplogo.png", UriKind.Absolute);
        var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;

        Bitmap original;
        if (stream != null)
        {
            original = new Bitmap(stream);
            stream.Dispose();
        }
        else
        {
            // Fallback: solid green square
            original = new Bitmap(32, 32);
            using var g = Graphics.FromImage(original);
            g.Clear(Color.FromArgb(0x00, 0xE6, 0x76));
        }

        // Resize to 32x32
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.DrawImage(original, 0, 0, 32, 32);
        }
        original.Dispose();

        // If disconnected, convert to grayscale
        if (!connected)
        {
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var px = bmp.GetPixel(x, y);
                int gray = (int)(px.R * 0.3 + px.G * 0.59 + px.B * 0.11);
                bmp.SetPixel(x, y, Color.FromArgb(px.A, gray, gray, gray));
            }
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var result = (Icon)icon.Clone();
        NativeMethods.DestroyIcon(hIcon);
        bmp.Dispose();
        return result;
    }

    /// <summary>
    /// Glassmorphism renderer for the tray context menu — matches OSD overlay style.
    /// </summary>
    private class GlassMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly Color BgColor = Color.FromArgb(0xEE, 0x11, 0x11, 0x11);
        private static readonly Color BorderColor = Color.FromArgb(0x44, 0x00, 0xE6, 0x76);
        private static readonly Color HoverBg = Color.FromArgb(0x18, 0x00, 0xE6, 0x76);
        private static readonly Color SepColor = Color.FromArgb(0x30, 0x00, 0xE6, 0x76);

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(6, 1, e.Item.Width - 12, e.Item.Height - 2);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (e.Item is Forms.ToolStripMenuItem && e.Item.Selected)
            {
                using var brush = new SolidBrush(HoverBg);
                using var path = RoundedRect(rect, 6);
                e.Graphics.FillPath(brush, path);

                // Subtle green border on hover
                using var pen = new Pen(Color.FromArgb(0x22, 0x00, 0xE6, 0x76));
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            var bounds = e.AffectedBounds;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Fill background
            using var bgPath = RoundedRect(new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1), 12);
            using var bgBrush = new SolidBrush(BgColor);
            e.Graphics.FillPath(bgBrush, bgPath);

            // Green glow border (gradient from top-left to bottom-right like the OSD)
            using var borderPen = new Pen(BorderColor, 1.2f);
            e.Graphics.DrawPath(borderPen, bgPath);
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            // Suppress default border — we draw our own rounded one in OnRenderToolStripBackground
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.ContentRectangle.Height / 2;
            using var pen = new Pen(SepColor);
            e.Graphics.DrawLine(pen, 16, y, e.Item.Width - 16, y);
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.ForeColor;
            base.OnRenderItemText(e);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private void OnConfigChanged(AppConfig config)
    {
        _config = config;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        ApplyStartupSetting();
        if (_ha != null)
        {
            _ha.UpdateConfig(_config.HomeAssistant);
            if (_config.HomeAssistant.Enabled)
                _ = _ha.TestConnectionAsync();
        }
    }

    private void HandleKnob(KnobEvent e)
    {
        // Track hardware position for UI display
        if (e.Idx >= 0 && e.Idx < 5)
            KnobPositions[e.Idx] = e.Value / 1023f;

        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == e.Idx);
        if (knob != null)
        {
            if (knob.Target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase))
            {
                // Route to Home Assistant
                if (_ha != null && _ha.IsAvailable)
                {
                    float vol = e.Value / 1023f;
                    _ = _ha.HandleKnobAsync(knob.Target, vol);
                }
            }
            else if (knob.Target.Equals("monitor", StringComparison.OrdinalIgnoreCase))
            {
                float vol = e.Value / 1023f;
                MonitorBrightness.SetAll(vol);
            }
            else if (knob.Target.Equals("led_brightness", StringComparison.OrdinalIgnoreCase))
            {
                int pct = (int)Math.Round(e.Value / 1023.0 * 100);
                _config.LedBrightness = pct;
                _rgb.SetBrightness(pct);
            }
            else
            {
                _mixer.SetVolume(knob, e.Value);
            }

            // Show OSD overlay when volume OSD is enabled
            if (_config.Osd.ShowVolume)
            {
                float pct = e.Value / 1023f;
                // Apply min/max range
                int displayPct = (int)Math.Round(knob.MinVolume + pct * (knob.MaxVolume - knob.MinVolume));
                string label = !string.IsNullOrEmpty(knob.Label) ? knob.Label : knob.Target;
                string symbol = knob.Target switch
                {
                    "master" => "Speaker224",
                    "mic" => "Mic24",
                    "monitor" => "Desktop24",
                    "led_brightness" => "Color24",
                    "spotify" => "MusicNote124",
                    "discord" => "Headphones24",
                    _ when knob.Target.StartsWith("ha_") => "Home24",
                    _ => "Speaker124"
                };
                Dispatcher.Invoke(() =>
                {
                    EnsureOsd();
                    _osdOverlay!.ShowVolume(label, displayPct, symbol);
                });
            }
        }
        _rgb.SetKnobPosition(e.Idx, e.Value / 1023f);
    }

    private void HandleButton(ButtonEvent e)
    {
        // Ignore button events in the first second after connection
        if ((DateTime.UtcNow - _connectedAt).TotalMilliseconds < 1000)
            return;

        if (e.IsDown)
            _buttons.HandleDown(e.Idx, _config);
        else
            _buttons.HandleUp(e.Idx, _config);
    }

    private void HandleConnection(bool connected)
    {
        if (connected)
        {
            _connectedAt = DateTime.UtcNow;

            // Initialize RGB knob positions from current audio volumes or saved positions
            for (int i = 0; i < 5; i++)
            {
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
                if (knob != null)
                {
                    try
                    {
                        var baseTarget = knob.Target.Contains(':') ? knob.Target.Split(':')[0] : knob.Target;
                        bool isNonAudio = baseTarget.StartsWith("ha_") || baseTarget == "monitor" || baseTarget == "led_brightness";

                        if (isNonAudio)
                        {
                            // For non-audio targets, use saved knob position (or 1.0 as fallback)
                            _rgb.SetKnobPosition(i, KnobPositions[i] > 0 ? KnobPositions[i] : 1f);
                        }
                        else
                        {
                            float vol = _mixer.GetVolume(knob);
                            _rgb.SetKnobPosition(i, vol);
                        }
                    }
                    catch { }
                }
            }

            _rgb.SetBrightness(_config.LedBrightness);
            _rgb.SetPort(_serial.Port);
            _rgb.ApplyColors(_config.Lights);
        }

        _isConnected = connected;
        if (_trayIcon != null)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateTrayIcon(connected);
            _trayIcon.Text = connected ? "Amp Up — Connected" : "Amp Up — Disconnected";
            oldIcon?.Dispose();
        }

        if (_trayStatusLabel != null)
        {
            _trayStatusLabel.Text = connected ? "  ●  Connected" : "  ○  Disconnected";
            _trayStatusLabel.ForeColor = connected ? Color.FromArgb(0x00, 0xDD, 0x77) : Color.FromArgb(0x9A, 0x9A, 0x9A);
        }

        _mainWindow?.SetConnectionStatus(connected);
    }

    private void HandleProfileSwitch(string profileName)
    {
        var profile = ConfigManager.LoadProfile(profileName);
        if (profile == null)
        {
            Logger.Log($"Profile '{profileName}' not found");
            return;
        }

        _config = profile;
        _config.ActiveProfile = profileName;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        Logger.Log($"Switched to profile: {profileName}");

        // Show OSD for profile switch
        if (_config.Osd.ShowProfileSwitch)
        {
            Dispatcher.Invoke(() =>
            {
                EnsureOsd();
                var iconCfg = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
                _osdOverlay!.ShowProfileSwitch(profileName, iconCfg);
            });
        }
    }

    private void HandleBrightnessCycle(int pct)
    {
        _config.LedBrightness = pct;
        _rgb.SetBrightness(pct);
        ConfigManager.Save(_config);

        if (_config.Osd.ShowVolume)
        {
            Dispatcher.Invoke(() =>
            {
                EnsureOsd();
                _osdOverlay!.ShowVolume("LED Brightness", pct, "Color24");
            });
        }
    }

    private void HandleDeviceSwitched(string deviceName, bool isOutput)
    {
        if (!_config.Osd.ShowDeviceSwitch) return;
        Dispatcher.Invoke(() =>
        {
            EnsureOsd();
            _osdOverlay!.ShowDevice(deviceName, isOutput);
        });
    }

    private void EnsureOsd()
    {
        _osdOverlay ??= new OsdOverlay();
        _osdOverlay.SetPosition(_config.Osd.Position);
    }

    private void PollMuteStates()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

            try
            {
                using var mic = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
                _rgb.SetMicMuted(mic.AudioEndpointVolume.Mute);
            }
            catch { }

            try
            {
                using var master = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                _rgb.SetMasterMuted(master.AudioEndpointVolume.Mute);
            }
            catch { }
        }
        catch { }
    }

    private void ApplyRgbConfig()
    {
        _rgb.SetBrightness(_config.LedBrightness);
        _rgb.UpdateConfig(_config.Lights);
    }

    private void ApplyStartupSetting()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "AmpUp";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
            if (key == null) return;

            if (_config.StartWithWindows)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, false);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to update startup setting: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutePollingTimer?.Dispose();
        _osdOverlay?.Close();
        _serial?.Dispose();
        _mixer?.Dispose();
        _rgb?.Dispose();
        _ha?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
