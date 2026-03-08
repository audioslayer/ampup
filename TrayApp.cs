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
        var lines = _config.Knobs.Select(k => $"K{k.Idx + 1}: {k.Label} → {k.Target}");
        _trayIcon.Text = "WolfMixer\n" + string.Join("\n", lines.Take(3));
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configure", null, (s, e) => OpenConfig());
        menu.Items.Add("Open Config Folder", null, (s, e) =>
            System.Diagnostics.Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => ExitApp());
        return menu;
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
        _mutePollingTimer?.Dispose();
        _serial.Dispose();
        _mixer.Dispose();
        _rgb.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static Icon BuildIcon(bool connected = true)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var color = connected ? Color.FromArgb(0, 180, 255) : Color.FromArgb(180, 180, 180);
        using var brush = new SolidBrush(color);
        int[] heights = { 4, 8, 14, 10, 6 };
        for (int i = 0; i < 5; i++)
        {
            int h = heights[i];
            g.FillRectangle(brush, i * 3, 16 - h, 2, h);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
