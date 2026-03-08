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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new Mutex(true, "WolfMixer_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("WolfMixer is already running. Check the system tray.",
                "WolfMixer", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Logger.Log("WolfMixer starting (WPF)...");

        // Load config and create backend
        _config = ConfigManager.Load();
        _mixer = new AudioMixer();
        _buttons = new ButtonHandler();
        _rgb = new RgbController();

        _buttons.OnProfileSwitch += HandleProfileSwitch;

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
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(false),
            Text = "WolfMixer",
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var menu = new Forms.ContextMenuStrip();
        menu.BackColor = Color.FromArgb(0x1C, 0x1C, 0x1C);
        menu.ForeColor = Color.FromArgb(0xE8, 0xE8, 0xE8);
        menu.Renderer = new DarkMenuRenderer();

        var showItem = menu.Items.Add("Show WolfMixer");
        showItem.Click += (_, _) => ShowMainWindow();

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = menu.Items.Add("Exit");
        exitItem.Click += (_, _) => ExitApp();

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
    /// Draws a 32x32 tray icon with 5 equalizer bars.
    /// Cyan (#00B4D8) when connected, gray (#666) when disconnected.
    /// </summary>
    private static Icon CreateTrayIcon(bool connected)
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var color = connected
                ? Color.FromArgb(0x00, 0xB4, 0xD8)
                : Color.FromArgb(0x88, 0x88, 0x88);

            int[] heights = { 8, 16, 24, 18, 12 };
            int barWidth = 4;
            int gap = 2;
            int totalWidth = 5 * barWidth + 4 * gap; // 28
            int startX = (32 - totalWidth) / 2;

            using var brush = new SolidBrush(color);
            for (int i = 0; i < 5; i++)
            {
                int x = startX + i * (barWidth + gap);
                int h = heights[i];
                int y = 32 - h - 2; // 2px bottom margin
                // Rounded rect approximation
                g.FillRectangle(brush, x, y + 2, barWidth, h - 2);
                g.FillEllipse(brush, x, y, barWidth, 4); // rounded top
            }
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        // Clone so we can destroy the original handle
        var result = (Icon)icon.Clone();
        NativeMethods.DestroyIcon(hIcon);
        bmp.Dispose();
        return result;
    }

    /// <summary>
    /// Dark theme renderer for the tray context menu.
    /// </summary>
    private class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                using var brush = new SolidBrush(Color.FromArgb(0x2A, 0x2A, 0x2A));
                e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
            }
            else
            {
                using var brush = new SolidBrush(Color.FromArgb(0x1C, 0x1C, 0x1C));
                e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
            }
        }

        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Color.FromArgb(0x1C, 0x1C, 0x1C));
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(0x36, 0x36, 0x36));
            int y = e.Item.ContentRectangle.Height / 2;
            e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
        }
    }

    private void OnConfigChanged(AppConfig config)
    {
        _config = config;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        ApplyStartupSetting();
    }

    private void HandleKnob(KnobEvent e)
    {
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == e.Idx);
        if (knob != null)
        {
            if (knob.Target.Equals("monitor", StringComparison.OrdinalIgnoreCase))
            {
                float vol = e.Value / 1023f;
                MonitorBrightness.SetAll(vol);
            }
            else
            {
                _mixer.SetVolume(knob, e.Value);
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
            _rgb.SetPort(_serial.Port);
            _rgb.ApplyColors(_config.Lights);
        }

        _isConnected = connected;
        if (_trayIcon != null)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateTrayIcon(connected);
            _trayIcon.Text = connected ? "WolfMixer — Connected" : "WolfMixer — Disconnected";
            oldIcon?.Dispose();
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
        const string valueName = "WolfMixer";
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
        _serial?.Dispose();
        _mixer?.Dispose();
        _rgb?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
