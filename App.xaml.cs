using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using AmpUp.Controls;
using AmpUp.Core.Services;
using AmpUp.Views;
using Forms = System.Windows.Forms;

namespace AmpUp;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppConfig _config = null!;
    private SerialReader _serial = null!;
    private AudioMixer _mixer = null!;
    private ButtonHandler _buttons = null!;
    private RgbController _rgb = null!;
    private AudioAnalyzer? _audioAnalyzer;
    private MainWindow _mainWindow = null!;
    private System.Threading.Timer? _mutePollingTimer;
    private System.Threading.Timer? _autoSwitchTimer;
    private DateTime _connectedAt = DateTime.MinValue;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isConnected;
    private static bool _isShuttingDown;
    private OsdOverlay? _osdOverlay;
    private HAIntegration? _ha;
    private ObsIntegration? _obs;
    private VoiceMeeterIntegration? _vm;
    private readonly (string target, float value)[] _haLastValues = new (string, float)[5];
    private readonly bool[] _haThrottleActive = new bool[5];
    private DuckingEngine? _duckingEngine;
    private AutoProfileSwitcher? _autoSwitcher;
    private TrayMixerPopup? _trayMixerPopup;
    private TrayContextMenu? _trayContextMenu;
    private AmbienceSync? _ambienceSync;
    private DreamSyncController? _dreamSync;
    private RadialWheelOverlay? _radialWheel;
    private bool _wheelVisible;
    private readonly int[] _lastKnobRaw = new int[5];

    /// <summary>
    /// Last hardware knob positions (0-1), updated on every knob event.
    /// Used by MixerView to display position for non-audio targets.
    /// </summary>
    public static readonly float[] KnobPositions = { 1f, 1f, 1f, 1f, 1f };
    public static RgbController? Rgb { get; private set; }
    private readonly long[] _lastKnobUiTick = new long[5]; // throttle UI updates
    private readonly long[] _lastOsdTick = new long[5]; // throttle OSD updates
    private readonly int[] _lastOsdValue = { -1, -1, -1, -1, -1 }; // suppress OSD if value unchanged
    private readonly long _startupTick = Environment.TickCount64; // suppress OSD on launch
    private uint _wmTaskbarCreated; // registered window message ID for WM_TASKBARCREATED

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global crash handlers — wire up before anything else
        DispatcherUnhandledException += (_, ex) =>
        {
            if (_isShuttingDown) { ex.Handled = true; return; }
            Logger.Log($"CRASH (UI): {ex.Exception}");
            ShowCrashDialog(ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Logger.Log($"CRASH (AppDomain): {ex.ExceptionObject}");
            if (ex.ExceptionObject is Exception exception) ShowCrashDialog(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Logger.Log($"CRASH (Task): {ex.Exception}");
            ex.SetObserved();
        };

        // Single instance check
        _mutex = new Mutex(true, "AmpUp_SingleInstance", out bool isNew);
        if (!isNew)
        {
            GlassDialog.ShowInfo("Amp Up is already running. Check the system tray.");
            Shutdown();
            return;
        }

        Logger.Log("Amp Up starting (WPF)...");

        // Wire up platform-specific shutdown delegate for UpdateChecker
        UpdateChecker.OnShutdownRequested = () =>
            Application.Current.Dispatcher.Invoke(() => ShutdownForUpdate());

        // Load config and create backend
        _config = ConfigManager.Load();

        // Apply user's accent color
        ThemeManager.SetAccentColor(_config.AccentColor);

        _mixer = new AudioMixer();
        _buttons = new ButtonHandler();
        _rgb = new RgbController();
        Rgb = _rgb;
        _audioAnalyzer = new AudioAnalyzer();
        _rgb.SetAudioBandsProvider(() => _audioAnalyzer.SmoothedBands);

        // Ambience sync (Govee LAN)
        _ambienceSync = new AmbienceSync(_config.Ambience);
        _rgb.OnFrameReady += _ambienceSync.OnFrame;

        // DreamView / Screen Sync
        _dreamSync = new DreamSyncController(_config.Ambience.ScreenSync, _config.Ambience, new WindowsScreenCapture());
        if (_config.Ambience.ScreenSync.Enabled)
            _dreamSync.Start();

        _buttons.OnProfileSwitch += HandleProfileSwitch;
        _buttons.OnDeviceSwitched += HandleDeviceSwitched;
        _buttons.OnBrightnessCycle += HandleBrightnessCycle;
        _buttons.OnQuickWheelOpen += HandleQuickWheelOpen;
        _buttons.OnQuickWheelClose += HandleQuickWheelClose;

        // Start Home Assistant integration
        _ha = new HAIntegration(_config.HomeAssistant);
        _buttons.SetHAIntegration(_ha);
        if (_config.HomeAssistant.Enabled)
            _ = _ha.TestConnectionAsync(); // sets IsAvailable for knob routing

        // Start OBS Studio integration
        _obs = new ObsIntegration(_config.Obs);
        _buttons.SetObsIntegration(_obs);
        if (_config.Obs.Enabled)
            _ = _obs.ConnectAsync();

        // Start VoiceMeeter integration
        _vm = new VoiceMeeterIntegration();
        _buttons.SetVoiceMeeterIntegration(_vm);
        if (_config.VoiceMeeter.Enabled && _vm.IsAvailable)
            _vm.Connect();

        // Start audio mixer
        _mixer.Start();

        // Restore last known knob positions from config (device doesn't report on connect)
        foreach (var knob in _config.Knobs)
        {
            if (knob.Idx >= 0 && knob.Idx < 5)
            {
                KnobPositions[knob.Idx] = knob.LastRawValue / 1023f;
                // Apply the saved volume to WASAPI
                HandleKnob(new KnobEvent { Idx = knob.Idx, Value = knob.LastRawValue });
            }
        }

        // Ducking engine
        _duckingEngine = new DuckingEngine();

        // Auto-profile switcher
        _autoSwitcher = new AutoProfileSwitcher(_config.AutoSwitch, () =>
        {
            try
            {
                var hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return null;
                return System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
            }
            catch { return null; }
        });
        _autoSwitcher.OnProfileSwitchRequested += profileName =>
            Dispatcher.Invoke(() => SwitchToProfile(profileName));
        _autoSwitchTimer = new System.Threading.Timer(_ => _autoSwitcher?.Poll(), null, 2000, 1500);

        // Start serial reader
        _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
        _serial.OnKnob += HandleKnob;
        _serial.OnButton += HandleButton;
        _serial.OnConnectionChanged += HandleConnection;
        _serial.Start();

        // Apply RGB config
        ApplyRgbConfig();
        UpdateAudioAnalyzer();

        // Poll mute states every 500ms for LED status effects (fallback)
        _mutePollingTimer = new System.Threading.Timer(_ => PollMuteStates(), null, 1000, 500);
        // Subscribe to instant mute notifications so LEDs react within one frame (~50ms)
        SubscribeMuteNotifications();

        // Apply startup setting
        ApplyStartupSetting();

        // Create tray icon
        SetupTrayIcon();

        // Listen for display configuration changes (e.g. monitor on/off) — tray icon
        // handle can become invalid when Explorer restarts or display settings change.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Register WM_TASKBARCREATED so we can recreate the tray icon if Explorer crashes/restarts
        _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        // Create main window
        _mainWindow = new MainWindow();
        _mainWindow.Closing += MainWindow_Closing;
        _mainWindow.Initialize(_config, _mixer, OnConfigChanged);
        _mainWindow.SetAmbienceSync(_ambienceSync);
        _mainWindow.SetDreamSync(_dreamSync);

        // Start minimized to tray if launched with --minimized (Windows startup)
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("--minimized"))
            _mainWindow.Show();

        // Hook WM_TASKBARCREATED on the main window's HWND so we can recreate the tray
        // icon if Explorer crashes or the taskbar is restarted for any reason.
        // We must ensure the window's HWND exists first (Show() does that; for the
        // minimized-to-tray case we force handle creation via EnsureHandle).
        _mainWindow.SourceInitialized += (_, _) =>
        {
            var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(_mainWindow).Handle);
            hwndSource?.AddHook(WndProc);
        };
        // If the window was already shown above, SourceInitialized already fired — hook now.
        var existingHandle = new WindowInteropHelper(_mainWindow).Handle;
        if (existingHandle != IntPtr.Zero)
        {
            var hwndSource = HwndSource.FromHwnd(existingHandle);
            hwndSource?.AddHook(WndProc);
        }

        // Sync connection status — serial may have connected before window was created
        if (_isConnected)
            _mainWindow.SetConnectionStatus(true, _serial.Port?.PortName);

        // Welcome dialog — show on first run OR when version changes (update)
        var currentVersion = UpdateChecker.CurrentVersion;
        bool isFirstRun = !_config.HasCompletedSetup;
        bool isUpdate = _config.HasCompletedSetup && _config.LastWelcomeVersion != currentVersion;

        if ((isFirstRun || isUpdate) && !args.Contains("--minimized"))
        {
            var welcome = new WelcomeDialog(() =>
            {
                ShowMainWindow();
                _mainWindow?.NavigateToSettings();
            });
            welcome.Closed += (_, _) =>
            {
                _config.HasCompletedSetup = true;
                _config.LastWelcomeVersion = currentVersion;
                ConfigManager.Save(_config);
            };
            welcome.Show();
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(false),
            Text = "Amp Up",
            Visible = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left || e.Button == Forms.MouseButtons.Right)
                ShowTrayMixer();
        };
    }

    /// <summary>
    /// Recreate the tray icon (dispose old + create new) and re-apply connection status.
    /// Called when display settings change or the taskbar is recreated.
    /// </summary>
    private void RecreateTrayIcon()
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                SetupTrayIcon();
                // Re-apply current connection status icon/text
                if (_trayIcon != null)
                {
                    var oldIcon = _trayIcon.Icon;
                    _trayIcon.Icon = CreateTrayIcon(_isConnected);
                    _trayIcon.Text = _isConnected ? "Amp Up — Connected" : "Amp Up — Disconnected";
                    oldIcon?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"RecreateTrayIcon error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Fired by Microsoft.Win32.SystemEvents when display settings change (monitors on/off, resolution, etc.).
    /// Explorer sometimes restarts the taskbar in response, invalidating the NotifyIcon handle.
    /// </summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Logger.Log("Display settings changed — recreating tray icon");
        RecreateTrayIcon();
    }

    /// <summary>
    /// WndProc hook on the main window. Catches WM_TASKBARCREATED, which Windows sends to
    /// all top-level windows when Explorer restarts the shell/taskbar (crash recovery, logoff,
    /// or display changes). On receipt we recreate the tray icon so it reappears automatically.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_wmTaskbarCreated != 0 && (uint)msg == _wmTaskbarCreated)
        {
            Logger.Log("WM_TASKBARCREATED received — recreating tray icon");
            RecreateTrayIcon();
        }
        return IntPtr.Zero;
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

    private void ShowTrayMixer()
    {
        Dispatcher.Invoke(() =>
        {
            _trayMixerPopup ??= new TrayMixerPopup();
            _trayMixerPopup.SetCallbacks(
                onOpen: ShowMainWindow,
                onExit: ExitApp,
                mixer: _mixer,
                config: _config,
                onSave: cfg => { ConfigManager.Save(cfg); _mainWindow?.RefreshViews(); },
                onRefresh: () => _mainWindow?.RefreshViews()
            );
            _trayMixerPopup.UpdateStatus(_isConnected, _isConnected ? _serial.Port?.PortName : null);
            _trayMixerPopup.ShowPopup();
        });
    }

    private void ShowTrayContextMenu()
    {
        Dispatcher.Invoke(() =>
        {
            // Recreate each time so it always has the current config (profiles can reassign _config)
            _trayContextMenu = new TrayContextMenu(
                onOpen: ShowMainWindow,
                onExit: ExitApp,
                mixer: _mixer,
                config: _config,
                onSave: cfg => { ConfigManager.Save(cfg); _mainWindow?.RefreshViews(); },
                onRefresh: () => _mainWindow?.RefreshViews()
            );

            _trayContextMenu.UpdateStatus(_isConnected, _isConnected ? _serial.Port?.PortName : null);

            var pos = Forms.Cursor.Position;
            _trayContextMenu.ShowAt(pos.X, pos.Y);
        });
    }

    private void ShowCrashDialog(Exception ex)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                GlassDialog.ShowWarning(
                    $"Amp Up encountered an error and needs to close.\n\nA crash log has been saved to:\n{Logger.LogPath}\n\nPlease include it when reporting the issue on GitHub.\n\n{ex.Message}",
                    title: "Amp Up Crashed");
                ExitApp();
            });
        }
        catch { }
    }

    private void ExitApp()
    {
        // Save last knob positions so they restore on next launch
        ConfigManager.Save(_config);

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        // Stop timers first to prevent further COM/serial calls during shutdown
        _mutePollingTimer?.Dispose();
        _autoSwitchTimer?.Dispose();
        _duckingEngine?.Dispose();
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
        var uri = new Uri("pack://application:,,,/Assets/icon/ampup-32.png", UriKind.Absolute);
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
            g.Clear(Color.FromArgb(ThemeManager.Accent.R, ThemeManager.Accent.G, ThemeManager.Accent.B));
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

    private void OnConfigChanged(AppConfig config)
    {
        _config = config;
        ConfigManager.Save(_config);
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);
        ApplyRgbConfig();
        UpdateAudioAnalyzer();
        ApplyStartupSetting();
        if (_ha != null)
        {
            _ha.UpdateConfig(_config.HomeAssistant);
            if (_config.HomeAssistant.Enabled)
                _ = _ha.TestConnectionAsync();
        }
        _obs?.UpdateConfig(_config.Obs);
        // VoiceMeeter: connect/disconnect based on enabled state
        if (_vm != null)
        {
            if (_config.VoiceMeeter.Enabled && _vm.IsAvailable && !_vm.IsConnected)
                _vm.Connect();
            else if (!_config.VoiceMeeter.Enabled && _vm.IsConnected)
                _vm.Disconnect();
        }
        _autoSwitcher?.UpdateConfig(_config.AutoSwitch);
        _ambienceSync?.UpdateConfig(_config.Ambience);
        _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
    }

    private void HandleKnob(KnobEvent e)
    {
        // Track hardware position for UI display
        if (e.Idx >= 0 && e.Idx < 5)
            KnobPositions[e.Idx] = e.Value / 1023f;

        // Route ANY knob to radial wheel when wheel is open.
        // Only update baseline on successful step so small turns accumulate.
        if (e.Idx >= 0 && e.Idx < 5)
        {
            if (_wheelVisible)
            {
                int delta = e.Value - _lastKnobRaw[e.Idx];
                if (Math.Abs(delta) >= 50 && _radialWheel != null)
                {
                    _lastKnobRaw[e.Idx] = e.Value; // only reset on step
                    int totalSlots = _radialWheel.GetTotalSlots();
                    int step = delta > 0 ? 1 : -1;
                    int next = ((_radialWheel.GetSelectedIndex() + step) % totalSlots + totalSlots) % totalSlots;
                    Dispatcher.BeginInvoke(() => _radialWheel?.Highlight(next));
                }
                return; // don't also adjust audio volume while wheel is open
            }
            _lastKnobRaw[e.Idx] = e.Value;
        }

        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == e.Idx);
        if (knob != null)
        {
            // Persist last raw position for startup restore
            knob.LastRawValue = e.Value;
            if (knob.Target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase))
            {
                // Route to Home Assistant (throttled — HA can't handle rapid-fire HTTP calls)
                // Skip during startup restore to avoid changing HA entity state on app launch
                if (_ha != null && _ha.IsAvailable
                    && Environment.TickCount64 - _startupTick >= 5000)
                {
                    float vol = e.Value / 1023f;
                    _haLastValues[e.Idx] = (knob.Target, vol);
                    if (!_haThrottleActive[e.Idx])
                    {
                        _haThrottleActive[e.Idx] = true;
                        _ = SendHaThrottledAsync(e.Idx);
                    }
                }
            }
            else if (knob.Target.Equals("monitor", StringComparison.OrdinalIgnoreCase))
            {
                // Skip during startup restore to avoid flickering monitor brightness on app launch
                if (Environment.TickCount64 - _startupTick >= 5000)
                {
                    float vol = e.Value / 1023f;
                    MonitorBrightness.SetThrottled(vol);
                }
            }
            else if (knob.Target.Equals("led_brightness", StringComparison.OrdinalIgnoreCase))
            {
                int pct = (int)Math.Round(e.Value / 1023.0 * 100);
                _config.LedBrightness = pct;
                _rgb.SetBrightness(pct);
            }
            else if (knob.Target.Equals("govee", StringComparison.OrdinalIgnoreCase))
            {
                // Skip during startup restore to avoid turning on Govee devices on app launch
                if (Environment.TickCount64 - _startupTick >= 5000)
                {
                    float norm = e.Value / 1023f;
                    _ambienceSync?.EnsureDevicesPoweredOn();
                    _ambienceSync?.SetBrightness(norm);
                    Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(null, norm, true));
                }
            }
            else if (knob.Target.StartsWith("govee:", StringComparison.OrdinalIgnoreCase))
            {
                // Skip during startup restore to avoid turning on Govee devices on app launch
                if (Environment.TickCount64 - _startupTick >= 5000)
                {
                    var ip = knob.Target.Substring(6);
                    float norm = e.Value / 1023f;
                    _ambienceSync?.EnsureDevicePoweredOn(ip);
                    _ambienceSync?.SetBrightnessForDevice(ip, norm);
                    Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(ip, norm, true));
                }
            }
            else if (knob.Target.StartsWith("vm_strip:", StringComparison.OrdinalIgnoreCase)
                  || knob.Target.StartsWith("vm_bus:", StringComparison.OrdinalIgnoreCase))
            {
                // VoiceMeeter strip/bus gain control
                if (_vm != null && _vm.IsAvailable && _config.VoiceMeeter.Enabled
                    && Environment.TickCount64 - _startupTick >= 5000)
                {
                    float norm = e.Value / 1023f;
                    float db = VoiceMeeterIntegration.NormalizedToGain(norm);
                    var parts = knob.Target.Split(':', 2);
                    if (parts.Length == 2 && int.TryParse(parts[1], out int vmIdx))
                    {
                        if (parts[0] == "vm_strip")
                            _vm.SetStripGain(vmIdx, db);
                        else
                            _vm.SetBusGain(vmIdx, db);
                    }
                }
            }
            else
            {
                _mixer.SetVolume(knob, e.Value);
            }

            // Show OSD overlay when volume OSD is enabled (skip unassigned knobs)
            // Throttled to ~100ms to avoid rapid flashing during fast knob turns
            // Suppress during startup (5s) and reconnection (2s) to avoid phantom popups
            // Suppress if value hasn't meaningfully changed (e.g. batch re-report on reconnect)
            long osdNow = Environment.TickCount64;
            bool osdSuppressed = osdNow - _startupTick < 5000
                || (DateTime.UtcNow - _connectedAt).TotalMilliseconds < 2000
                || (_lastOsdValue[e.Idx] >= 0 && Math.Abs(e.Value - _lastOsdValue[e.Idx]) < 8);
            if (_config.Osd.ShowVolume && !knob.Target.Equals("none", StringComparison.OrdinalIgnoreCase)
                && osdNow - _lastOsdTick[e.Idx] >= 100
                && !osdSuppressed)
            {
                _lastOsdTick[e.Idx] = osdNow;
                _lastOsdValue[e.Idx] = e.Value;
                float pct = e.Value / 1023f;
                // Apply min/max range
                int displayPct = (int)Math.Round(knob.MinVolume + pct * (knob.MaxVolume - knob.MinVolume));
                string label = !string.IsNullOrEmpty(knob.Label) ? knob.Label : knob.Target switch
                {
                    "master" => "Master",
                    "mic" => "Microphone",
                    "active_window" => "Active Window",
                    "system" => "System Sounds",
                    "any" => "Auto",
                    "apps" => "App Group",
                    "monitor" => "Monitor",
                    "led_brightness" => "LED Brightness",
                    "output_device" => "Output Device",
                    "input_device" => "Input Device",
                    _ when knob.Target.StartsWith("vm_strip:") => $"VM Strip {knob.Target.Split(':')[1]}",
                    _ when knob.Target.StartsWith("vm_bus:") => $"VM Bus {knob.Target.Split(':')[1]}",
                    _ => knob.Target
                };
                string symbol = knob.Target switch
                {
                    "master" => "VolumeHigh",
                    "mic" => "Microphone",
                    "monitor" => "Monitor",
                    "led_brightness" => "Palette",
                    "govee" => "Palette",
                    _ when knob.Target.StartsWith("govee:") => "Palette",
                    "spotify" => "MusicNote",
                    "discord" => "Headphones",
                    _ when knob.Target.StartsWith("ha_") => "Home",
                    _ when knob.Target.StartsWith("vm_") => "VolumeHigh",
                    _ => "VolumeHigh"
                };
                Dispatcher.BeginInvoke(() =>
                {
                    if (!EnsureOsd()) return;
                    _osdOverlay!.ShowVolume(label, displayPct, symbol);
                });
            }
        }
        _rgb.SetKnobPosition(e.Idx, e.Value / 1023f);

        // Push position to MixerView — throttled to ~33fps to avoid flooding the dispatcher
        long now = Environment.TickCount64;
        if (now - _lastKnobUiTick[e.Idx] >= 30)
        {
            _lastKnobUiTick[e.Idx] = now;
            float pos = e.Value / 1023f;
            Dispatcher.BeginInvoke(() => _mainWindow?.UpdateKnobPosition(e.Idx, pos));
        }
    }

    private async Task SendHaThrottledAsync(int idx)
    {
        while (true)
        {
            var (target, value) = _haLastValues[idx];
            try { await _ha!.HandleKnobAsync(target, value); }
            catch (Exception ex) { Logger.Log($"HA throttled send failed: {ex.Message}"); }

            await Task.Delay(30); // Short delay — HTTP response time naturally throttles

            // Check if value changed while we were waiting
            var (newTarget, newValue) = _haLastValues[idx];
            if (Math.Abs(newValue - value) < 0.001f)
            {
                _haThrottleActive[idx] = false;
                return;
            }
        }
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

            // Initialize RGB knob positions — default to 1.0 (full brightness LEDs)
            // The batch frame from the device will update these to actual hardware positions,
            // and the live timer will track real audio volumes once running.
            for (int i = 0; i < 5; i++)
                _rgb.SetKnobPosition(i, 1f);

            _rgb.SetBrightness(_config.LedBrightness);
            _rgb.SetOutput(
                (buf, off, len) => { try { _serial.Port?.Write(buf, off, len); } catch { } },
                () => _serial.Port?.IsOpen == true);
            _rgb.ApplyColors(_config.Lights);
            UpdateAudioAnalyzer();
        }

        _isConnected = connected;
        if (_trayIcon != null)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateTrayIcon(connected);
            _trayIcon.Text = connected ? "Amp Up — Connected" : "Amp Up — Disconnected";
            oldIcon?.Dispose();
        }

        _trayContextMenu?.UpdateStatus(connected, connected ? _serial.Port?.PortName : null);
        _trayMixerPopup?.UpdateStatus(connected, connected ? _serial.Port?.PortName : null);

        _mainWindow?.SetConnectionStatus(connected, connected ? _serial.Port?.PortName : null);
    }

    /// <summary>
    /// Switch to a named profile. Used by button gestures and AutoProfileSwitcher.
    /// </summary>
    public void SwitchToProfile(string profileName)
    {
        HandleProfileSwitch(profileName);
    }

    private void HandleProfileSwitch(string profileName)
    {
        var profile = ConfigManager.LoadProfile(profileName);
        if (profile == null)
        {
            Logger.Log($"Profile '{profileName}' not found");
            return;
        }

        // Save current profile before switching so changes aren't lost
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);

        // Preserve global settings that shouldn't change per-profile
        var osd = _config.Osd;
        var serial = _config.Serial;
        var startWithWindows = _config.StartWithWindows;
        var ha = _config.HomeAssistant;
        var obs = _config.Obs;
        var ambience = _config.Ambience;
        var profiles = _config.Profiles;
        var profileIcons = _config.ProfileIcons;
        var ducking = _config.Ducking;
        var autoSwitch = _config.AutoSwitch;

        _config = profile;
        _config.ActiveProfile = profileName;
        _config.Osd = osd;
        _config.Serial = serial;
        _config.StartWithWindows = startWithWindows;
        _config.HomeAssistant = ha;
        _config.Obs = obs;
        _config.Ambience = ambience;
        _config.Profiles = profiles;
        _config.ProfileIcons = profileIcons;
        _config.Ducking = ducking;
        _config.AutoSwitch = autoSwitch;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        UpdateAudioAnalyzer();

        // Use the profile's icon color for the transition
        var transIconCfg = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
        System.Windows.Media.Color profileColor;
        try { profileColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(transIconCfg.Color); }
        catch { profileColor = System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76); }
        _rgb.PlayTransition(_config.ProfileTransition, profileColor.R, profileColor.G, profileColor.B);
        Logger.Log($"Switched to profile: {profileName}");

        // Refresh the UI to show the new profile's settings
        Dispatcher.Invoke(() => (MainWindow as MainWindow)?.RefreshViews(_config));

        // Show OSD for profile switch
        if (_config.Osd.ShowProfileSwitch)
        {
            Dispatcher.Invoke(() =>
            {
                if (!EnsureOsd()) return;
                var iconCfg = _config.ProfileIcons.GetValueOrDefault(profileName) ?? new ProfileIconConfig();
                _osdOverlay!.ShowProfileSwitch(profileName, iconCfg, _config);
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
                if (!EnsureOsd()) return;
                _osdOverlay!.ShowVolume("LED Brightness", pct, "Palette");
            });
        }
    }

    private void HandleDeviceSwitched(string deviceName, bool isOutput)
    {
        if (!_config.Osd.ShowDeviceSwitch) return;
        Dispatcher.Invoke(() =>
        {
            if (!EnsureOsd()) return;
            _osdOverlay!.ShowDevice(deviceName, isOutput);
        });
    }

    /// <summary>
    /// Ensures OSD overlay exists and is configured. Returns false if OSD should be
    /// suppressed (e.g. fullscreen game detected with HideInFullscreen enabled).
    /// </summary>
    private bool EnsureOsd()
    {
        if (_config.Osd.HideInFullscreen && NativeMethods.IsForegroundFullscreen())
            return false;

        _osdOverlay ??= new OsdOverlay();
        _osdOverlay.SetPosition(_config.Osd.Position, _config.Osd.MonitorIndex);
        _osdOverlay.VolumeDuration = _config.Osd.VolumeDuration;
        _osdOverlay.ProfileDuration = _config.Osd.ProfileDuration;
        _osdOverlay.DeviceDuration = _config.Osd.DeviceDuration;
        return true;
    }

    public void NotifyUpdateAvailable()
    {
        Dispatcher.Invoke(() => _trayMixerPopup?.ShowUpdateAvailable());
    }

    /// <summary>
    /// Show the profile OSD preview without switching profiles. Used by BindingsView.
    /// </summary>
    public void PreviewProfileOsd(string profileName, ProfileIconConfig iconCfg, AppConfig config)
    {
        Dispatcher.Invoke(() =>
        {
            EnsureOsd();
            _osdOverlay!.ShowProfileSwitch(profileName, iconCfg, config);
        });
    }

    private string _lastDefaultOutputDeviceId = "";

    // Cached enumerator for mute polling (created once, lives for the app lifetime)
    private NAudio.CoreAudioApi.MMDeviceEnumerator? _pollEnumerator;
    // Cached devices for mute polling — refreshed only when the default device changes
    private NAudio.CoreAudioApi.MMDevice? _cachedMic;
    private NAudio.CoreAudioApi.MMDevice? _cachedMaster;
    // Reentrancy guard: skip poll if the previous one hasn't finished yet
    private int _pollMuteRunning;

    // Devices held open specifically for OnVolumeNotification subscriptions (instant mute feedback)
    private NAudio.CoreAudioApi.MMDevice? _notifyMaster;
    private NAudio.CoreAudioApi.MMDevice? _notifyMic;

    /// <summary>
    /// Subscribe to OnVolumeNotification on the default output and capture devices so that
    /// mute/unmute is reflected in the LEDs within one animation frame (~50ms) instead of
    /// waiting up to 500ms for the next poll cycle. Called once at startup and again whenever
    /// the default output device changes.
    /// </summary>
    private void SubscribeMuteNotifications()
    {
        try
        {
            _pollEnumerator ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();

            // --- Master output ---
            try
            {
                // Unsubscribe from old device before replacing it
                if (_notifyMaster != null)
                {
                    try { _notifyMaster.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeNotification; } catch { }
                    _notifyMaster.Dispose();
                    _notifyMaster = null;
                }
                _notifyMaster = _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                _notifyMaster.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeNotification;
                // Seed current state immediately
                _rgb.SetMasterMuted(_notifyMaster.AudioEndpointVolume.Mute);
            }
            catch { }

            // --- Mic capture ---
            try
            {
                if (_notifyMic != null)
                {
                    try { _notifyMic.AudioEndpointVolume.OnVolumeNotification -= OnMicVolumeNotification; } catch { }
                    _notifyMic.Dispose();
                    _notifyMic = null;
                }
                _notifyMic = _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
                _notifyMic.AudioEndpointVolume.OnVolumeNotification += OnMicVolumeNotification;
                // Seed current state immediately
                _rgb.SetMicMuted(_notifyMic.AudioEndpointVolume.Mute);
            }
            catch { }
        }
        catch { }
    }

    private void OnMasterVolumeNotification(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        _rgb.SetMasterMuted(data.Muted);
    }

    private void OnMicVolumeNotification(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        _rgb.SetMicMuted(data.Muted);
    }

    private void PollMuteStates()
    {
        // Skip if a previous poll is still running (protects _cachedMaster from concurrent access)
        if (System.Threading.Interlocked.CompareExchange(ref _pollMuteRunning, 1, 0) != 0)
            return;
        try
        {
            _duckingEngine?.Poll(_config.Ducking);
        }
        catch { }

        try
        {
            _pollEnumerator ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();

            try
            {
                // Lazily cache the default mic; re-fetch only on failure
                _cachedMic ??= _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
                _rgb.SetMicMuted(_cachedMic.AudioEndpointVolume.Mute);
            }
            catch
            {
                // Device may have changed — clear cache so it's re-fetched next tick
                _cachedMic?.Dispose();
                _cachedMic = null;
            }

            try
            {
                _cachedMaster ??= _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                _rgb.SetMasterMuted(_cachedMaster.AudioEndpointVolume.Mute);

                // Notify RgbController when the default output device changes (for DeviceSelect effect)
                string currentId = _cachedMaster.ID;
                if (currentId != _lastDefaultOutputDeviceId)
                {
                    _lastDefaultOutputDeviceId = currentId;
                    _rgb.SetDefaultOutputDevice(currentId);
                    // Default device changed — clear master cache so next poll fetches the new default
                    _cachedMaster.Dispose();
                    _cachedMaster = null;
                    // Re-subscribe to the new default device for instant mute notifications
                    SubscribeMuteNotifications();
                }
            }
            catch
            {
                _cachedMaster?.Dispose();
                _cachedMaster = null;
            }

            // Poll program mute states for ProgramMute LED effect
            PollProgramMuteStates();
            // Poll app group mute states for AppGroupMute LED effect
            PollAppGroupMuteStates();
        }
        catch { }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _pollMuteRunning, 0);
        }
    }

    private void PollProgramMuteStates()
    {
        try
        {
            // Determine which lights need program mute polling
            var lightsToCheck = new List<LightConfig>();
            foreach (var l in _config.Lights)
            {
                if (l.Effect == LightEffect.ProgramMute && !string.IsNullOrWhiteSpace(l.ProgramName))
                    lightsToCheck.Add(l);
            }
            if (_config.GlobalLight.Enabled && _config.GlobalLight.Effect == LightEffect.ProgramMute)
            {
                for (int i = 0; i < 5; i++)
                    lightsToCheck.Add(new LightConfig { Idx = i, ProgramName = (_config.Lights.FirstOrDefault(l => l.Idx == i)?.ProgramName) ?? "" });
            }

            if (lightsToCheck.Count == 0) return;

            if (_pollEnumerator == null) return;
            // Reuse the cached master device if available; otherwise get a fresh one
            NAudio.CoreAudioApi.MMDevice? device = null;
            bool ownDevice = false;
            if (_cachedMaster != null)
            {
                device = _cachedMaster;
            }
            else
            {
                try
                {
                    device = _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                    ownDevice = true;
                }
                catch { return; }
            }

            try
            {
                var sessions = device.AudioSessionManager.Sessions;

                foreach (var light in lightsToCheck)
                {
                    if (string.IsNullOrWhiteSpace(light.ProgramName)) continue;
                    bool muted = true; // default: muted/not-found
                    try
                    {
                        for (int s = 0; s < sessions.Count; s++)
                        {
                            var session = sessions[s];
                            try
                            {
                                uint pid = session.GetProcessID;
                                if (pid == 0) continue;
                                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                                if (proc.ProcessName.Contains(light.ProgramName, StringComparison.OrdinalIgnoreCase))
                                {
                                    muted = session.SimpleAudioVolume.Mute;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    _rgb.SetProgramMuted(light.Idx, muted);
                }
            }
            finally
            {
                if (ownDevice) device?.Dispose();
            }
        }
        catch { }
    }

    private void PollAppGroupMuteStates()
    {
        try
        {
            // Determine which knobs use AppGroupMute and have an apps[] list
            var knobsToCheck = new List<KnobConfig>();
            foreach (var l in _config.Lights)
            {
                if (l.Effect == LightEffect.AppGroupMute)
                {
                    var knob = _config.Knobs.FirstOrDefault(k => k.Idx == l.Idx);
                    if (knob != null && knob.Target == "apps" && knob.Apps?.Count > 0)
                        knobsToCheck.Add(knob);
                }
            }
            if (_config.GlobalLight.Enabled && _config.GlobalLight.Effect == LightEffect.AppGroupMute)
            {
                foreach (var knob in _config.Knobs)
                {
                    if (knob.Target == "apps" && knob.Apps?.Count > 0 && !knobsToCheck.Any(k => k.Idx == knob.Idx))
                        knobsToCheck.Add(knob);
                }
            }

            if (knobsToCheck.Count == 0) return;
            if (_pollEnumerator == null) return;

            NAudio.CoreAudioApi.MMDevice? device = null;
            bool ownDevice = false;
            if (_cachedMaster != null)
            {
                device = _cachedMaster;
            }
            else
            {
                try
                {
                    device = _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                    ownDevice = true;
                }
                catch { return; }
            }

            try
            {
                var sessions = device.AudioSessionManager.Sessions;

                foreach (var knob in knobsToCheck)
                {
                    bool anyUnmuted = false;
                    bool anyFound = false;
                    try
                    {
                        for (int s = 0; s < sessions.Count; s++)
                        {
                            var session = sessions[s];
                            try
                            {
                                uint pid = session.GetProcessID;
                                if (pid == 0) continue;
                                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                                bool matchesGroup = knob.Apps!.Any(app =>
                                    proc.ProcessName.Contains(app, StringComparison.OrdinalIgnoreCase));
                                if (matchesGroup)
                                {
                                    anyFound = true;
                                    if (!session.SimpleAudioVolume.Mute)
                                        anyUnmuted = true;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    // allMuted = true only when apps were found and none are unmuted
                    // if no apps found, default to false (show color1 / live appearance)
                    bool allMuted = anyFound && !anyUnmuted;
                    _rgb.SetAppGroupMuted(knob.Idx, allMuted);
                }
            }
            finally
            {
                if (ownDevice) device?.Dispose();
            }
        }
        catch { }
    }

    private void ApplyRgbConfig()
    {
        _rgb.SetGamma(_config.GammaR, _config.GammaG, _config.GammaB);
        _rgb.SetBrightness(_config.LedBrightness);
        _rgb.UpdateConfig(_config.Lights);
        _rgb.UpdateGlobalConfig(_config.GlobalLight);
    }

    /// <summary>
    /// Start or stop the AudioAnalyzer based on whether any light uses AudioReactive.
    /// </summary>
    private void UpdateAudioAnalyzer()
    {
        bool needsAudio = _config.Lights.Any(l => l.Effect == LightEffect.AudioReactive)
            || (_config.GlobalLight.Enabled && _config.GlobalLight.Effect == LightEffect.AudioReactive);
        if (needsAudio)
            _audioAnalyzer?.Start();
        else
            _audioAnalyzer?.Stop();
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
                key.SetValue(valueName, $"\"{exePath}\" --minimized");
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

    // ── Quick Wheel (radial switcher — profiles or output devices) ───

    private QuickWheelMode _activeWheelMode;

    private void HandleQuickWheelOpen(int buttonIdx)
    {
        // Find which wheel config matches this button
        var wheelCfg = _config.Osd.QuickWheels.FirstOrDefault(w => w.Enabled && w.TriggerButton == buttonIdx);
        if (wheelCfg == null) return;

        Dispatcher.Invoke(() =>
        {
            if (_wheelVisible) return;
            _wheelVisible = true;
            _activeWheelMode = wheelCfg.Mode;

            // Initialize last raw values so first delta is correct
            for (int i = 0; i < 5; i++)
                _lastKnobRaw[i] = (int)(KnobPositions[i] * 1023f);

            _radialWheel = new RadialWheelOverlay();
            _radialWheel.SetMonitor(_config.Osd.MonitorIndex);

            if (_activeWheelMode == QuickWheelMode.OutputDevice)
                PopulateWheelDevices();
            else
                PopulateWheelProfiles();

            _radialWheel.OnSegmentClicked = idx => ConfirmWheelSelection(idx);
            _radialWheel.Closed += (_, _) => { _wheelVisible = false; _radialWheel = null; };
            _radialWheel.Show();
        });
    }

    private void PopulateWheelProfiles()
    {
        if (_config.Profiles.Count < 2) { _wheelVisible = false; return; }
        int currentIdx = _config.Profiles.IndexOf(_config.ActiveProfile);
        if (currentIdx < 0) currentIdx = 0;
        _radialWheel!.SetProfiles(new List<string>(_config.Profiles), currentIdx, _config.ProfileIcons);
    }

    private void PopulateWheelDevices()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
            using var current = enumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            var currentId = current.ID;

            var list = new List<(string id, string name)>();
            int currentIdx = 0;
            for (int i = 0; i < devices.Count; i++)
            {
                using var d = devices[i];
                if (d.ID == currentId) currentIdx = list.Count;
                list.Add((d.ID, d.FriendlyName));
            }

            if (list.Count < 2) { _wheelVisible = false; return; }
            _radialWheel!.SetDevices(list, currentIdx);
        }
        catch (Exception ex)
        {
            Logger.Log($"Quick Wheel device enum error: {ex.Message}");
            _wheelVisible = false;
        }
    }

    private void ConfirmWheelSelection(int idx)
    {
        _wheelVisible = false;
        _radialWheel = null;

        if (_activeWheelMode == QuickWheelMode.OutputDevice)
        {
            // idx → device ID via GetSelectedId was already set
            // We need the device list — just re-enumerate and pick by index
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(
                    NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
                if (idx >= 0 && idx < devices.Count)
                {
                    using var d = devices[idx];
                    _buttons.ExecuteAction("select_output", "",
                        new ButtonConfig { DeviceId = d.ID });
                }
            }
            catch (Exception ex) { Logger.Log($"Quick Wheel device select error: {ex.Message}"); }
        }
        else
        {
            if (idx >= 0 && idx < _config.Profiles.Count)
            {
                var profileName = _config.Profiles[idx];
                if (profileName != _config.ActiveProfile)
                    HandleProfileSwitch(profileName);
            }
        }
    }

    private void HandleQuickWheelClose(int buttonIdx)
    {
        if (!_wheelVisible || _radialWheel == null) return;
        Dispatcher.Invoke(() =>
        {
            if (_radialWheel == null || !_wheelVisible) return;
            int idx = _radialWheel.GetSelectedIndex();
            var wheel = _radialWheel;
            wheel.OnSegmentClicked = null;
            _wheelVisible = false;
            _radialWheel = null;
            wheel.Dismiss();
            ConfirmWheelSelection(idx);
        });
    }

    public static void ShutdownForUpdate()
    {
        _isShuttingDown = true;
        Application.Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutePollingTimer?.Dispose();
        _autoSwitchTimer?.Dispose();
        _duckingEngine?.Dispose();
        _osdOverlay?.Close();
        _radialWheel?.Close();
        _serial?.Dispose();
        _buttons?.Dispose();
        _mixer?.Dispose();
        _audioAnalyzer?.Dispose();
        _rgb?.Dispose();
        _ha?.Dispose();
        _obs?.Dispose();
        _vm?.Dispose();
        _ambienceSync?.Dispose();
        _dreamSync?.Dispose();
        _cachedMic?.Dispose();
        _cachedMaster?.Dispose();
        if (_notifyMaster != null)
        {
            try { _notifyMaster.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeNotification; } catch { }
            _notifyMaster.Dispose();
        }
        if (_notifyMic != null)
        {
            try { _notifyMic.AudioEndpointVolume.OnVolumeNotification -= OnMicVolumeNotification; } catch { }
            _notifyMic.Dispose();
        }
        _pollEnumerator?.Dispose();
        MonitorBrightness.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
