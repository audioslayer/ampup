using System.Drawing.Drawing2D;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace WolfMixer;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private AppConfig _config;
    private readonly SerialReader _serial;
    private readonly AudioMixer _mixer;
    private readonly ButtonHandler _buttons;
    private readonly RgbController _rgb;
    private bool _connected;
    private System.Threading.Timer? _mutePollingTimer;
    private ToolStripMenuItem[] _volumeItems = new ToolStripMenuItem[5];
    private System.Windows.Forms.Timer _trayVolumeTimer = new();

    public TrayApp()
    {
        _config = ConfigManager.Load();
        _mixer = new AudioMixer();
        _buttons = new ButtonHandler();
        _rgb = new RgbController();

        // Wire up profile switching from button handler
        _buttons.OnProfileSwitch += HandleProfileSwitch;

        _trayIcon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "WolfMixer",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        UpdateTooltip();
        ApplyStartupSetting();
        ApplyRgbConfig();

        _mixer.Start();

        _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
        _serial.OnKnob += HandleKnob;
        _serial.OnButton += HandleButton;
        _serial.OnConnectionChanged += HandleConnection;
        _serial.Start();

        // Poll mic/master mute state every 500ms for LED status effects
        _mutePollingTimer = new System.Threading.Timer(_ => PollMuteStates(), null, 1000, 500);
    }

    private void HandleKnob(KnobEvent e)
    {
        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == e.Idx);
        if (knob != null)
        {
            // Handle monitor brightness knob
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

        // Update RGB knob position for effects
        _rgb.SetKnobPosition(e.Idx, e.Value / 1023f);
    }

    private void HandleButton(ButtonEvent e)
    {
        if (e.IsDown)
            _buttons.HandleDown(e.Idx, _config);
        else
            _buttons.HandleUp(e.Idx, _config);
    }

    private void HandleConnection(bool connected)
    {
        _connected = connected;
        _trayIcon.Icon = BuildIcon(connected);
        if (!connected)
        {
            _trayIcon.ShowBalloonTip(3000, "WolfMixer", "Turn Up disconnected — reconnecting...", ToolTipIcon.Warning);
        }
        else
        {
            _trayIcon.ShowBalloonTip(2000, "WolfMixer", "Turn Up connected!", ToolTipIcon.Info);
            _rgb.SetPort(_serial.Port);
            _rgb.ApplyColors(_config.Lights);
        }
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
        UpdateTooltip();
        Logger.Log($"Switched to profile: {profileName}");
        _trayIcon.ShowBalloonTip(2000, "WolfMixer", $"Profile: {profileName}", ToolTipIcon.Info);
    }

    private void PollMuteStates()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            // Check mic mute
            try
            {
                var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _rgb.SetMicMuted(mic.AudioEndpointVolume.Mute);
            }
            catch { }

            // Check master mute
            try
            {
                var master = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
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
        if (_connected)
            _rgb.ApplyColors(_config.Lights);
    }

    private void UpdateTooltip()
    {
        var sb = new System.Text.StringBuilder("🐺 WolfMixer\n");
        for (int i = 0; i < Math.Min(4, _config.Knobs.Count); i++)
        {
            var k = _config.Knobs[i];
            sb.AppendLine($"K{k.Idx+1}: {k.Label} → {k.Target}");
        }
        string text = sb.ToString().TrimEnd();
        _trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = Color.FromArgb(28, 28, 28);
        menu.ForeColor = Color.FromArgb(232, 232, 232);
        menu.Font = new Font("Segoe UI", 9);

        // Header
        var header = new ToolStripMenuItem("🐺  WolfMixer") { Enabled = false };
        header.ForeColor = Color.FromArgb(0, 180, 216);
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // Live volume items (5 rows, updated by timer)
        for (int i = 0; i < 5; i++)
        {
            _volumeItems[i] = new ToolStripMenuItem("") { Enabled = false };
            _volumeItems[i].ForeColor = Color.FromArgb(154, 154, 154);
            menu.Items.Add(_volumeItems[i]);
        }
        menu.Items.Add(new ToolStripSeparator());

        // Actions
        var configItem = new ToolStripMenuItem("⚙  Configure...");
        configItem.Click += (s, e) => OpenConfig();
        menu.Items.Add(configItem);

        var logItem = new ToolStripMenuItem("📋  Open Log");
        logItem.Click += (s, e) => {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wolfmixer.log");
            if (File.Exists(logPath))
                System.Diagnostics.Process.Start("notepad.exe", logPath);
        };
        menu.Items.Add(logItem);

        var folderItem = new ToolStripMenuItem("📁  Open Config Folder");
        folderItem.Click += (s, e) => System.Diagnostics.Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);
        menu.Items.Add(folderItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("✕  Exit");
        exitItem.Click += (s, e) => ExitApp();
        menu.Items.Add(exitItem);

        // Start timer to refresh volume display
        _trayVolumeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _trayVolumeTimer.Tick += (s, e) => RefreshTrayVolumes();
        _trayVolumeTimer.Start();

        RefreshTrayVolumes();
        return menu;
    }

    private void RefreshTrayVolumes()
    {
        if (_volumeItems == null) return;
        for (int i = 0; i < 5 && i < _config.Knobs.Count; i++)
        {
            try
            {
                var knob = _config.Knobs[i];
                float vol = _mixer.GetVolume(knob);
                int pct = (int)(vol * 100);

                // Build bar string: 8 chars filled proportionally
                int filled = (int)(vol * 8);
                string bar = new string('█', filled) + new string('░', 8 - filled);

                string label = knob.Label.PadRight(8).Substring(0, Math.Min(8, knob.Label.Length)).PadRight(8);
                _volumeItems[i].Text = $"  {label}  {bar}  {pct,3}%";
            }
            catch { }
        }
    }

    private void OpenConfig()
    {
        var form = new ConfigForm(_config, _mixer, newConfig =>
        {
            _config = newConfig;
            ConfigManager.Save(_config);
            UpdateTooltip();
            ApplyStartupSetting();
            ApplyRgbConfig();
        });
        form.Show();
    }

    private void ApplyStartupSetting()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "WolfMixer";
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, true);
        if (key == null) return;

        if (_config.StartWithWindows)
            key.SetValue(valueName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(valueName, false);
    }

    private void ExitApp()
    {
        _trayVolumeTimer?.Stop();
        _mutePollingTimer?.Dispose();
        _serial.Dispose();
        _mixer.Dispose();
        _rgb.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static Icon BuildIcon(bool connected = true)
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var color = connected ? Color.FromArgb(0, 180, 216) : Color.FromArgb(100, 100, 100);
        using var brush = new SolidBrush(color);
        using var glowBrush = new SolidBrush(Color.FromArgb(60, color.R, color.G, color.B));

        // Draw 5 equalizer bars, centered in 32x32
        // Bar positions: x = 2, 8, 14, 20, 26 (each 5px wide)
        // Heights (from bottom, tall to short): 22, 14, 28, 18, 10
        int[] barHeights = { 14, 22, 28, 18, 10 };
        int barW = 5;
        int baseY = 30; // baseline y
        for (int i = 0; i < 5; i++)
        {
            int x = 2 + i * 6;
            int h = barHeights[i];
            // Glow layer
            g.FillRectangle(glowBrush, x - 1, baseY - h - 1, barW + 2, h + 2);
            // Main bar (with rounded top — draw rect + circle cap)
            g.FillRectangle(brush, x, baseY - h, barW, h);
            // Rounded top cap
            g.FillEllipse(brush, x, baseY - h - 2, barW, barW);
        }
        // Thin baseline
        using var basePen = new Pen(Color.FromArgb(80, color.R, color.G, color.B), 1);
        g.DrawLine(basePen, 1, baseY + 1, 30, baseY + 1);

        return Icon.FromHandle(bmp.GetHicon());
    }
}
