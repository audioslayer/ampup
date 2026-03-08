using System.Windows;
using Microsoft.Win32;

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

        // Create and show main window
        _mainWindow = new MainWindow();
        _mainWindow.Initialize(_config, _mixer, OnConfigChanged);
        _mainWindow.Show();
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
        _mutePollingTimer?.Dispose();
        _serial?.Dispose();
        _mixer?.Dispose();
        _rgb?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
