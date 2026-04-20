using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using AmpUp.Controls;
using AmpUp.Core.Engine;
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
    private System.Threading.Timer? _gameModeTimer;
    private bool _gameModeActive;
    private bool _gameModePreDreamView;        // was DreamView enabled before game mode?
    private string _gameModePrevCorsairMode = "off"; // Corsair LightSyncMode before game mode
    private DateTime _connectedAt = DateTime.MinValue;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isConnected;
    private bool _isN3Connected;
    private string? _n3DeviceName;
    private static bool _isShuttingDown;
    private OsdOverlay? _osdOverlay;
    private HAIntegration? _ha;
    private ObsIntegration? _obs;
    private VoiceMeeterIntegration? _vm;
    private readonly (string target, float value)[] _haLastValues = new (string, float)[8];
    private readonly bool[] _haThrottleActive = new bool[8];
    private DuckingEngine? _duckingEngine;
    private AutoProfileSwitcher? _autoSwitcher;
    private TrayMixerPopup? _trayMixerPopup;
    private TrayContextMenu? _trayContextMenu;
    private AmbienceSync? _ambienceSync;
    private DreamSyncController? _dreamSync;
    private CorsairSync? _corsairSync;
    private LgMonitorSync? _lgMonitor;
    private N3Controller? _n3;
    private RadialWheelOverlay? _radialWheel;
    private bool _wheelVisible;
    private System.Windows.Threading.DispatcherTimer? _wheelDismissTimer;
    private System.Windows.Threading.DispatcherTimer? _streamControllerRefreshTimer;
    private DateTime _lastDynamicStateTick = DateTime.MinValue;
    private readonly int[] _lastKnobRaw = new int[5];
    private const int N3DisplayKeyBase = 100;
    private const int N3SideButtonBase = 106;
    private const int N3EncoderPressBase = 109;
    private const int N3KnobStateBase = 5;

    // ── Folder (sub-grid) navigation state ────────────────────────────
    // Empty string means we're at the root. Folder name matches ButtonFolderConfig.Name.
    private string _currentN3Folder = "";
    // Back key occupies LCD slot 0 whenever we're inside a folder — it is virtual
    // (no ButtonConfig entry) and handled directly in HandleN3Input.

    /// <summary>
    /// Last hardware knob positions (0-1), updated on every knob event.
    /// Used by MixerView to display position for non-audio targets.
    /// </summary>
    public static readonly float[] KnobPositions = { 1f, 1f, 1f, 1f, 1f };
    public static readonly float[] StreamControllerKnobPositions = { 1f, 1f, 1f };
    public static RgbController? Rgb { get; private set; }
    public static AudioAnalyzer? AudioAnalyzer { get; private set; }
    private readonly long[] _lastKnobUiTick = new long[8]; // throttle UI updates
    private readonly long[] _lastOsdTick = new long[8]; // throttle OSD updates
    private readonly int[] _lastOsdValue = { -1, -1, -1, -1, -1, -1, -1, -1 }; // suppress OSD if value unchanged
    private readonly int[] _pendingOsdValue = { -1, -1, -1, -1, -1, -1, -1, -1 }; // pending final OSD update
    private readonly System.Threading.Timer[] _osdFinalTimers = new System.Threading.Timer[8]; // delayed final OSD update
    private long _startupTick = Environment.TickCount64; // suppress OSD on launch
    private uint _wmTaskbarCreated; // registered window message ID for WM_TASKBARCREATED
    private bool _rawInputRegistered;

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

        // Apply user's accent color and card theme
        ThemeManager.SetAccentColor(_config.AccentColor);
        ThemeManager.SetCardTheme(_config.CardTheme);

        _mixer = new AudioMixer();
        _buttons = new ButtonHandler();
        _rgb = new RgbController();
        Rgb = _rgb;
        _audioAnalyzer = new AudioAnalyzer();
        AudioAnalyzer = _audioAnalyzer;
        _rgb.SetAudioBandsProvider(() => _audioAnalyzer.SmoothedBands);

        // Ambience sync (Govee LAN)
        _ambienceSync = new AmbienceSync(_config.Ambience);
        _rgb.OnFrameReady += _ambienceSync.OnFrame;

        // Corsair iCUE sync
        _corsairSync = new CorsairSync();
        _rgb.OnFrameReady += frame =>
        {
            if (_corsairSync?.IsAvailable != true || !_config.Corsair.Enabled) return;
            var mode = _config.Corsair.LightSyncMode;
            if (mode == "off") return;
            // In "static" mode, colors are set once via UI — don't overwrite with Turn Up frames
            // In "dreamview" mode, colors come from DreamSyncController — don't overwrite
            // Only sync Turn Up LED frames in default (Turn Up sync) or "vu_reactive" modes
            if (mode != "static" && mode != "dreamview")
                _corsairSync.SyncColors(frame);
        };
        if (_config.Corsair.Enabled)
            _corsairSync.Start();

        // LG UltraGear monitor LED sync
        _lgMonitor = new LgMonitorSync();
        if (_lgMonitor.TryConnect())
        {
            Logger.Log($"LG Monitor: {_lgMonitor.DeviceName} — {_lgMonitor.LedCountValue} LEDs");
            // Sync Turn Up knob LED frames to LG monitor
            // Only when "Link to Room" is active (knob LEDs → room devices)
            // Otherwise, room effects send via OnRoomFrame in RoomView
            _rgb.OnFrameReady += frame =>
            {
                if (_lgMonitor?.IsAvailable != true) return;
                if (!_config.Ambience.LinkToLights) return;
                _lgMonitor.SyncFromRoomEffect(frame);
            };
        }

        // TreasLin / VSDinside N3 HID bring-up
        _n3 = new N3Controller();
        _n3.OnInput += HandleN3Input;
        _isN3Connected = _n3.TryConnect();
        _n3DeviceName = _isN3Connected ? _n3.DeviceName : null;

        // Let the display renderer resolve dynamic-state sources without
        // taking a hard dependency on OBS / AudioMixer.
        StreamControllerDisplayRenderer.DynamicStateResolver =
            source => DynamicKeyStateProvider.IsActive(source, _obs, _mixer);

        if (_isN3Connected)
        {
            Logger.Log("N3: native HID bring-up active");
            _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
            SyncStreamControllerDisplays();
        }

        StartStreamControllerRefreshTimer();

        // DreamView / Screen Sync
        _dreamSync = new DreamSyncController(_config.Ambience.ScreenSync, _config.Ambience, new WindowsScreenCapture());
        _dreamSync.OnZoneColors += zones =>
        {
            // Build a 45-byte RGB array from zone colors (map zones to 15 LEDs)
            var frame = new byte[45];
            for (int i = 0; i < 15; i++)
            {
                var zone = zones[i * zones.Length / 15];
                frame[i * 3]     = zone.R;
                frame[i * 3 + 1] = zone.G;
                frame[i * 3 + 2] = zone.B;
            }

            // Forward to Turn Up hardware LEDs when enabled
            if (_config.Ambience.ScreenSync.SyncToTurnUp)
                _rgb.SetScreenSyncColors(frame);

            // Forward to Corsair when in dreamview mode
            if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled
                && _config.Corsair.LightSyncMode == "dreamview")
            {
                float boost = _config.Corsair.LightBrightness / 100f;
                var boosted = new byte[45];
                for (int i = 0; i < 45; i++)
                    boosted[i] = (byte)Math.Min(frame[i] * boost, 255);
                _corsairSync.SyncColors(boosted);
            }
        };
        if (_config.Ambience.ScreenSync.Enabled)
            _dreamSync.Start();

        _buttons.OnProfileSwitch += HandleProfileSwitch;
        _buttons.OnDeviceSwitched += HandleDeviceSwitched;
        _buttons.OnBrightnessCycle += HandleBrightnessCycle;
        _buttons.OnQuickWheelOpen += HandleQuickWheelOpen;
        _buttons.OnQuickWheelClose += HandleQuickWheelClose;
        _buttons.OnRoomToggle += HandleRoomToggle;
        _buttons.OnGroupToggle += HandleGroupToggle;
        _buttons.OnScPageChange += HandleScPageChange;
        _buttons.OnOpenFolder += NavigateToN3Folder;

        // Wire up folder-aware button resolution so gesture engine can find buttons
        // inside the currently-open folder by their (non-root) idx.
        AmpUp.Core.Engine.ButtonGestureEngine.ButtonResolverOverride = ResolveN3ButtonForGestureEngine;

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
            if (knob.Idx >= 0 && knob.Idx < 5 && knob.LastRawValue >= 0)
            {
                KnobPositions[knob.Idx] = knob.LastRawValue / 1023f;
                // Apply the saved volume to WASAPI
                HandleKnob(new KnobEvent { Idx = knob.Idx, Value = knob.LastRawValue });
            }
        }
        foreach (var knob in _config.N3.Knobs)
        {
            if (knob.Idx >= 0 && knob.Idx < 3 && knob.LastRawValue >= 0)
            {
                StreamControllerKnobPositions[knob.Idx] = knob.LastRawValue / 1023f;
                ApplyKnobConfig(knob, knob.LastRawValue, N3KnobStateBase + knob.Idx, false);
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

        // Game Mode — auto-enable screen sync when fullscreen game detected
        _gameModeTimer = new System.Threading.Timer(_ => PollGameMode(), null, 2000, 1000);

        // Start serial reader
        _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
        _serial.OnKnob += HandleKnob;
        _serial.OnButton += HandleButton;
        _serial.OnConnectionChanged += HandleConnection;
        _startupTick = Environment.TickCount64; // reset just before serial starts
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

        // Pre-warm app icon cache on background thread so first tray popup open is instant
        TrayMixerPopup.PreWarmIconCache();

        // Listen for display configuration changes (e.g. monitor on/off) — tray icon
        // handle can become invalid when Explorer restarts or display settings change.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Listen for session lock/unlock — screen lock invalidates WASAPI COM objects;
        // we tear down and rebuild notification subscriptions and the peak device on unlock.
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // Listen for system sleep/wake so the N3 LCD screens can blank with
        // the PC and light back up on resume instead of burning at full
        // brightness on a sleeping machine.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Register WM_TASKBARCREATED so we can recreate the tray icon if Explorer crashes/restarts
        _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        // Create main window
        _mainWindow = new MainWindow();
        _mainWindow.Closing += MainWindow_Closing;
        _mainWindow.Initialize(_config, _mixer, OnConfigChanged);
        _mainWindow.SetAmbienceSync(_ambienceSync);
        _mainWindow.SetDreamSync(_dreamSync);
        if (_corsairSync != null)
            _mainWindow.SetCorsairSync(_corsairSync);
        if (_lgMonitor?.IsAvailable == true)
            _mainWindow.SetLgMonitor(_lgMonitor);
        _mainWindow.SetHAIntegration(_ha);

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
            var handle = new WindowInteropHelper(_mainWindow).Handle;
            RegisterRawInputSink(handle);
            var hwndSource = HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(WndProc);
        };
        // If the window was already shown above, SourceInitialized already fired — hook now.
        var existingHandle = new WindowInteropHelper(_mainWindow).Handle;
        if (existingHandle != IntPtr.Zero)
        {
            RegisterRawInputSink(existingHandle);
            var hwndSource = HwndSource.FromHwnd(existingHandle);
            hwndSource?.AddHook(WndProc);
        }

        // Sync connection status — serial may have connected before window was created
        if (_isConnected)
            _mainWindow.SetConnectionStatus(true, _serial.Port?.PortName);
        _mainWindow.SetN3ConnectionStatus(_isN3Connected, _n3DeviceName);
        UpdateAggregateTrayStatus();

        // Welcome dialog — show on first run OR when version changes (update)
        var currentVersion = UpdateChecker.CurrentVersion;
        bool isFirstRun = !_config.HasCompletedSetup;
        bool isUpdate = _config.HasCompletedSetup && _config.LastWelcomeVersion != currentVersion;

        if ((isFirstRun || isUpdate) && !args.Contains("--minimized"))
        {
            var welcome = new WelcomeDialog(
                onOpenSettings: () =>
                {
                    ShowMainWindow();
                    _mainWindow?.NavigateToSettings();
                },
                onImport: () =>
                {
                    ShowMainWindow();
                    _mainWindow?.LaunchImportWizard();
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

    // HwndSource hook on the NotifyIcon's internal message window (for scroll wheel)
    private HwndSource? _trayIconHwndSource;

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
            else if (e.Button == Forms.MouseButtons.Middle)
                ToggleMasterMute();
        };

        // Hook the NotifyIcon's internal message window to catch WM_MOUSEWHEEL
        HookTrayIconWindow();
    }

    /// <summary>
    /// Toggle master output mute. Called on middle-click tray icon.
    /// </summary>
    private void ToggleMasterMute()
    {
        try
        {
            _pollEnumerator ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = _pollEnumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            bool nowMuted = !device.AudioEndpointVolume.Mute;
            device.AudioEndpointVolume.Mute = nowMuted;
            // Tray icon update comes from OnMasterVolumeNotification callback
        }
        catch (Exception ex)
        {
            Logger.Log($"ToggleMasterMute error: {ex.Message}");
        }
    }

    /// <summary>
    /// Uses reflection to get the internal HWND of the WinForms NotifyIcon message window
    /// and hooks WndProc to catch WM_MOUSEWHEEL over the tray icon.
    /// </summary>
    private void HookTrayIconWindow()
    {
        if (_trayIcon == null) return;
        try
        {
            // WinForms NotifyIcon stores its NativeWindow in a private field named "window"
            var field = typeof(Forms.NotifyIcon).GetField("window",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var nativeWindow = field?.GetValue(_trayIcon);
            if (nativeWindow == null) return;

            var handleProp = nativeWindow.GetType().GetProperty("Handle",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var hwnd = (IntPtr?)handleProp?.GetValue(nativeWindow);
            if (hwnd == null || hwnd == IntPtr.Zero) return;

            _trayIconHwndSource?.Dispose();
            _trayIconHwndSource = HwndSource.FromHwnd(hwnd.Value);
            _trayIconHwndSource?.AddHook(TrayIconWndProc);
        }
        catch (Exception ex)
        {
            Logger.Log($"HookTrayIconWindow error: {ex.Message}");
        }
    }

    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MBUTTONUP = 0x0208;

    private IntPtr TrayIconWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEWHEEL)
        {
            // HIWORD(wParam) = signed wheel delta (positive = up/louder, negative = down/quieter)
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            int change = delta > 0 ? 2 : -2;
            Dispatcher.BeginInvoke(() => AdjustMasterVolume(change));
            handled = true;
        }
        else if (msg == NativeMethods.WM_INPUT)
        {
            HandleRawInput(lParam);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Adjusts master volume by delta percent (e.g. +2 or -2). Clamps to 0–100%.
    /// Updates tray icon immediately.
    /// </summary>
    private void AdjustMasterVolume(int deltaPercent)
    {
        try
        {
            _pollEnumerator ??= new NAudio.CoreAudioApi.MMDeviceEnumerator();
            using var device = _pollEnumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            float cur = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            float next = Math.Clamp(cur + deltaPercent / 100f, 0f, 1f);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = next;
            // Tray icon update comes from OnMasterVolumeNotification callback
        }
        catch (Exception ex)
        {
            Logger.Log($"AdjustMasterVolume error: {ex.Message}");
        }
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
                _trayIconHwndSource?.Dispose();
                _trayIconHwndSource = null;
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
                SetupTrayIcon();
                UpdateAggregateTrayStatus();
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
    /// Fired when the Windows session is locked or unlocked.
    /// Screen lock invalidates WASAPI COM objects (AudioEndpointVolume, AudioMeterInformation,
    /// AudioSessionManager) held by background threads. We proactively tear them down on lock
    /// and rebuild on unlock to avoid COMExceptions crashing the timer/notification threads.
    /// </summary>
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Logger.Log($"Session switch: {e.Reason}");
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            // Tear down WASAPI subscriptions before the COM objects go invalid.
            // The notification callbacks could fire one last time during this window — guard flag stops them.
            _sessionLocked = true;
            lock (_notifyLock)
            {
                try
                {
                    if (_notifyMaster != null)
                    {
                        try { _notifyMaster.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeNotification; } catch { }
                        try { _notifyMaster.Dispose(); } catch { }
                        _notifyMaster = null;
                    }
                }
                catch { }
                try
                {
                    if (_notifyMic != null)
                    {
                        try { _notifyMic.AudioEndpointVolume.OnVolumeNotification -= OnMicVolumeNotification; } catch { }
                        try { _notifyMic.Dispose(); } catch { }
                        _notifyMic = null;
                    }
                }
                catch { }
            }
            // Tell AudioMixer to drop its persistent peak device — it's invalid under lock
            _mixer?.InvalidatePeakDevice();
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            // Session is restored — rebuild subscriptions and reseed state
            _sessionLocked = false;
            Logger.Log("Session unlocked — re-subscribing mute notifications");
            try { SubscribeMuteNotifications(); } catch { }
        }
    }

    // True while the Windows session is locked — guards against stale WASAPI callbacks
    private volatile bool _sessionLocked;

    /// <summary>
    /// Blank the N3 screens when the system suspends so they don't sit lit
    /// on a sleeping PC, and restore them on resume. Set brightness=0 is the
    /// cheapest sleep — the device stays connected so wake-from-sleep just
    /// pushes brightness back up; no re-init required.
    /// </summary>
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        Logger.Log($"Power mode: {e.Mode}");
        try
        {
            if (e.Mode == PowerModes.Suspend)
            {
                if (_n3 != null && _isN3Connected)
                    _n3.Sleep();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                if (_n3 != null && _isN3Connected)
                {
                    _n3.Wake();
                    _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                    SyncStreamControllerDisplays();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"PowerModeChanged error: {ex.Message}");
        }
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
            Logger.Log("Raw input: keyboard sink registered");
        }
        else
        {
            Logger.Log($"Raw input: registration failed ({Marshal.GetLastWin32Error()})");
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

                Logger.Log(
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
            Logger.Log($"Raw input: handle failed - {ex.Message}");
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
            UpdateAggregateTrayStatus();
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

            UpdateAggregateTrayStatus();

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
                var version = UpdateChecker.CurrentVersion;
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var fullText = $"Amp Up v{version} — {timestamp}\n\n{ex}";

                var msgBlock = new System.Windows.Controls.TextBlock
                {
                    Text = $"Amp Up encountered an error and needs to close.\n\nA crash log has been saved to:\n{Logger.LogPath}\n\nPlease include it when reporting the issue on GitHub.\n\n{ex.Message}",
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"],
                    Margin = new System.Windows.Thickness(0, 0, 0, 16),
                };

                var btnOpenLog = new System.Windows.Controls.Button
                {
                    Content = "Open Log File",
                    Padding = new System.Windows.Thickness(16, 8, 16, 8),
                    Margin = new System.Windows.Thickness(0, 0, 8, 0),
                };
                btnOpenLog.Click += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Logger.LogPath) { UseShellExecute = true }); }
                    catch { }
                };

                var btnCopy = new System.Windows.Controls.Button
                {
                    Content = "Copy to Clipboard",
                    Padding = new System.Windows.Thickness(16, 8, 16, 8),
                    Margin = new System.Windows.Thickness(0, 0, 8, 0),
                };
                btnCopy.Click += (_, _) =>
                {
                    try { System.Windows.Clipboard.SetText(fullText); }
                    catch { }
                };

                var btnRow = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                };
                btnRow.Children.Add(btnOpenLog);
                btnRow.Children.Add(btnCopy);

                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(msgBlock);
                panel.Children.Add(btnRow);

                GlassDialog.ShowInfo("Amp Up Crashed", panel);
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
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _trayIconHwndSource?.Dispose();
        _trayIconHwndSource = null;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        // Stop timers first to prevent further COM/serial calls during shutdown
        _mutePollingTimer?.Dispose();
        _autoSwitchTimer?.Dispose();
        _gameModeTimer?.Dispose();
        _streamControllerRefreshTimer?.Stop();
        _duckingEngine?.Dispose();
        Dispatcher.Invoke(() => Shutdown());
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Close to tray instead of exiting
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    // Track current master volume/mute for tray icon label
    private float _trayVolume = 1f;
    private bool _trayMuted = false;

    /// <summary>
    /// Creates a 32x32 tray icon from the embedded logo PNG.
    /// Connected = full color, disconnected = grayscale.
    /// Draws current master volume % (or "M" if muted) as small white text in the bottom-right.
    /// </summary>
    private Icon CreateTrayIcon(bool connected)
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

        // Draw volume % or "M" (muted) in bottom-right corner
        try
        {
            string volText = _trayMuted ? "M" : $"{(int)Math.Round(_trayVolume * 100)}";
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            using var font = new Font("Arial Narrow", 7f, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
            var sz = g.MeasureString(volText, font);
            float tx = 32 - sz.Width - 1;
            float ty = 32 - sz.Height;
            // Small dark backing for readability
            using var bgBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            g.FillRectangle(bgBrush, tx - 1, ty, sz.Width + 2, sz.Height);
            // White text
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(volText, font, textBrush, tx, ty);
        }
        catch { }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        var result = (Icon)icon.Clone();
        NativeMethods.DestroyIcon(hIcon);
        bmp.Dispose();
        return result;
    }

    /// <summary>
    /// Updates tray icon with current volume/mute state. Called from volume notifications.
    /// </summary>
    private void UpdateTrayIconVolume(float volume, bool muted)
    {
        _trayVolume = volume;
        _trayMuted = muted;
        if (_trayIcon == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var oldIcon = _trayIcon?.Icon;
                if (_trayIcon != null)
                    _trayIcon.Icon = CreateTrayIcon(_isConnected || _isN3Connected);
                oldIcon?.Dispose();
            }
            catch { }
        });
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
        // Clear Turn Up screen sync override when neither screen sync nor room mixer is active
        if ((!_config.Ambience.ScreenSync.Enabled || !_config.Ambience.ScreenSync.SyncToTurnUp)
            && !_config.Ambience.SyncRoomToTurnUp)
            _rgb.SetScreenSyncColors(null);
        if (_corsairSync != null)
        {
            if (_config.Corsair.Enabled)
                _corsairSync.Start();
            else
                _corsairSync.Stop();
        }
        if (_n3 != null && _isN3Connected)
        {
            _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
            SyncStreamControllerDisplays();
        }
    }

    private void HandleKnob(KnobEvent e)
    {
        if (_config.HardwareMode == HardwareMode.StreamControllerOnly)
            return;

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
                // Skip during startup and reconnect to avoid changing HA entity state
                if (_ha != null && _ha.IsAvailable
                    && Environment.TickCount64 - _startupTick >= 8000
                    && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
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
                if (Environment.TickCount64 - _startupTick >= 8000)
                {
                    float vol = e.Value / 1023f;
                    if (string.IsNullOrEmpty(knob.DeviceId))
                    {
                        MonitorBrightness.SetThrottled(vol); // all monitors
                    }
                    else
                    {
                        // Support multiple monitors: semicolon-separated device names
                        foreach (var devName in knob.DeviceId.Split(';', StringSplitOptions.RemoveEmptyEntries))
                            MonitorBrightness.SetThrottled(vol, devName);
                    }
                }
            }
            else if (knob.Target.Equals("led_brightness", StringComparison.OrdinalIgnoreCase))
            {
                int pct = (int)Math.Round(e.Value / 1023.0 * 100);
                _config.LedBrightness = pct;
                _rgb.SetBrightness(pct);
            }
            else if (knob.Target.Equals("room_lights", StringComparison.OrdinalIgnoreCase))
            {
                // Room Lights — unified brightness for Govee + Corsair
                if (Environment.TickCount64 - _startupTick >= 8000
                    && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
                {
                    float norm = e.Value / 1023f;
                    int pctRoom = (int)Math.Round(norm * 100);

                    if (pctRoom == 0)
                    {
                        // Turn off all room lights
                        foreach (var dev in _config.Ambience.GoveeDevices)
                        {
                            if (string.IsNullOrWhiteSpace(dev.Ip)) continue;
                            dev.PoweredOn = false;
                            _ = AmbienceSync.SendTurnAsync(dev.Ip, false);
                        }
                        if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                            _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                    }
                    else
                    {
                        // Set brightness on all Govee devices (turn on if needed)
                        _ambienceSync?.EnsureDevicesPoweredOn();
                        _ambienceSync?.SetBrightness(norm);

                        // Scale Corsair brightness
                        if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                        {
                            _config.Corsair.LightBrightness = (int)(pctRoom * 2.0);
                        }
                    }
                    _config.Ambience.BrightnessScale = Math.Max(pctRoom, 1);
                    Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(null, norm, pctRoom > 0));
                }
            }
            else if (knob.Target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
            {
                // Device Group — unified brightness control for grouped devices
                if (Environment.TickCount64 - _startupTick >= 8000
                    && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
                {
                    var groupName = knob.Target.Substring(6);
                    var group = _config.Groups.FirstOrDefault(g => g.Name == groupName);
                    if (group != null)
                    {
                        float norm = e.Value / 1023f;
                        int pct = (int)Math.Round(norm * 100);
                        foreach (var dev in group.Devices)
                        {
                            switch (dev.Type)
                            {
                                case "govee":
                                    if (pct == 0)
                                    {
                                        var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == dev.DeviceId);
                                        if (gc != null) gc.PoweredOn = false;
                                        _ = AmbienceSync.SendTurnAsync(dev.DeviceId, false);
                                    }
                                    else
                                    {
                                        var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == dev.DeviceId);
                                        bool wasOff = gc != null && !gc.PoweredOn;
                                        if (gc != null) gc.PoweredOn = true;

                                        // Send brightness command — the device applies this as a
                                        // multiplier on top of whatever colors are being sent via razer.
                                        var ip = dev.DeviceId;
                                        var bright = pct;
                                        if (wasOff)
                                        {
                                            _ = Task.Run(async () =>
                                            {
                                                await AmbienceSync.SendTurnAsync(ip, true);
                                                await Task.Delay(150);
                                                await AmbienceSync.SendBrightnessAsync(ip, bright);
                                            });
                                        }
                                        else
                                        {
                                            _ = AmbienceSync.SendBrightnessAsync(ip, bright);
                                        }
                                    }
                                    break;
                                case "corsair":
                                    if (_corsairSync?.IsAvailable == true)
                                    {
                                        _config.Corsair.LightBrightness = (int)(pct * 2.0);
                                    }
                                    break;
                                case "ha":
                                    if (_ha != null && _ha.IsAvailable)
                                    {
                                        float haVal = norm;
                                        _haLastValues[e.Idx] = ($"ha_light:{dev.DeviceId}", haVal);
                                        if (!_haThrottleActive[e.Idx])
                                        {
                                            _haThrottleActive[e.Idx] = true;
                                            _ = SendHaThrottledAsync(e.Idx);
                                        }
                                    }
                                    break;
                                case "audio_output":
                                    _mixer?.SetOutputDeviceVolume(dev.DeviceId, norm);
                                    break;
                            }
                        }
                    }
                }
            }
            else if (knob.Target.Equals("govee", StringComparison.OrdinalIgnoreCase))
            {
                // Skip during startup restore to avoid turning on Govee devices on app launch
                if (Environment.TickCount64 - _startupTick >= 8000)
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
                if (Environment.TickCount64 - _startupTick >= 8000)
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
                    && Environment.TickCount64 - _startupTick >= 8000)
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
            else if (knob.Target.Equals("corsair_pump_fan", StringComparison.OrdinalIgnoreCase)
                  || knob.Target.Equals("corsair_case_fan", StringComparison.OrdinalIgnoreCase))
            {
                // Corsair fan speed control — knob position maps directly to 0-100%
                if (_corsairSync != null && _corsairSync.IsAvailable && _config.Corsair.Enabled
                    && _config.Corsair.FanEnabled
                    && Environment.TickCount64 - _startupTick >= 8000)
                {
                    int percent = (int)Math.Round(e.Value / 1023.0 * 100);
                    bool isPump = knob.Target.Equals("corsair_pump_fan", StringComparison.OrdinalIgnoreCase);
                    if (isPump)
                        _config.Corsair.PumpFanSpeed = percent;
                    else
                        _config.Corsair.CaseFanSpeed = percent;

                    string typeFilter = isPump ? "pump" : "fan";
                    foreach (var device in _corsairSync.Devices)
                    {
                        bool matches = device.Type.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)
                            || (isPump && device.Type.Contains("cooler", StringComparison.OrdinalIgnoreCase));
                        if (matches)
                            _ = _corsairSync.SetFanSpeedAsync(device.Id, percent);
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
            bool osdTimeSuppressed = osdNow - _startupTick < 10000
                || (DateTime.UtcNow - _connectedAt).TotalMilliseconds < 3000;
            bool osdValueSuppressed = _lastOsdValue[e.Idx] >= 0 && Math.Abs(e.Value - _lastOsdValue[e.Idx]) < 15;
            // Batch events = device reporting positions on connect, not user turning — never show OSD
            if (e.IsBatch)
                _lastOsdValue[e.Idx] = e.Value;

            if (_config.Osd.ShowVolume && !e.IsBatch
                && !knob.Target.Equals("none", StringComparison.OrdinalIgnoreCase)
                && !osdTimeSuppressed)
            {
                if (osdNow - _lastOsdTick[e.Idx] >= 100 && !osdValueSuppressed)
                {
                    _lastOsdTick[e.Idx] = osdNow;
                    _lastOsdValue[e.Idx] = e.Value;
                    Dispatcher.BeginInvoke(() => ShowKnobOsd(knob, e.Value));
                    // Cancel any pending final update since we just showed OSD
                    _osdFinalTimers[e.Idx]?.Change(Timeout.Infinite, Timeout.Infinite);
                }

                // Always schedule a delayed final update so the OSD shows the true
                // final value after the knob stops moving (prevents stale % on fast turns)
                _pendingOsdValue[e.Idx] = e.Value;
                if (_osdFinalTimers[e.Idx] == null)
                {
                    int idx = e.Idx; // capture for closure
                    _osdFinalTimers[idx] = new System.Threading.Timer(_ =>
                    {
                        int val = _pendingOsdValue[idx];
                        if (val >= 0 && val != _lastOsdValue[idx])
                        {
                            _lastOsdValue[idx] = val;
                            _lastOsdTick[idx] = Environment.TickCount64;
                            var k = _config.Knobs.FirstOrDefault(k => k.Idx == idx);
                            if (k != null)
                                Dispatcher.BeginInvoke(() => ShowKnobOsd(k, val));
                        }
                    }, null, 200, Timeout.Infinite);
                }
                else
                {
                    _osdFinalTimers[e.Idx].Change(200, Timeout.Infinite);
                }
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

    private void ApplyKnobConfig(KnobConfig knob, int rawValue, int stateIdx, bool isBatch)
    {
        knob.LastRawValue = rawValue;

        if (knob.Target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase))
        {
            if (_ha != null && _ha.IsAvailable
                && Environment.TickCount64 - _startupTick >= 8000
                && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
            {
                float vol = rawValue / 1023f;
                _haLastValues[stateIdx] = (knob.Target, vol);
                if (!_haThrottleActive[stateIdx])
                {
                    _haThrottleActive[stateIdx] = true;
                    _ = SendHaThrottledAsync(stateIdx);
                }
            }
        }
        else if (knob.Target.Equals("monitor", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000)
            {
                float vol = rawValue / 1023f;
                if (string.IsNullOrEmpty(knob.DeviceId))
                {
                    MonitorBrightness.SetThrottled(vol);
                }
                else
                {
                    foreach (var devName in knob.DeviceId.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        MonitorBrightness.SetThrottled(vol, devName);
                }
            }
        }
        else if (knob.Target.Equals("led_brightness", StringComparison.OrdinalIgnoreCase))
        {
            int pct = (int)Math.Round(rawValue / 1023.0 * 100);
            _config.LedBrightness = pct;
            _rgb.SetBrightness(pct);
        }
        else if (knob.Target.Equals("room_lights", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000
                && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
            {
                float norm = rawValue / 1023f;
                int pctRoom = (int)Math.Round(norm * 100);

                if (pctRoom == 0)
                {
                    foreach (var dev in _config.Ambience.GoveeDevices)
                    {
                        if (string.IsNullOrWhiteSpace(dev.Ip)) continue;
                        dev.PoweredOn = false;
                        _ = AmbienceSync.SendTurnAsync(dev.Ip, false);
                    }
                    if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                        _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                }
                else
                {
                    _ambienceSync?.EnsureDevicesPoweredOn();
                    _ambienceSync?.SetBrightness(norm);

                    if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                        _config.Corsair.LightBrightness = (int)(pctRoom * 2.0);
                }
                _config.Ambience.BrightnessScale = Math.Max(pctRoom, 1);
                Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(null, norm, pctRoom > 0));
            }
        }
        else if (knob.Target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000
                && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
            {
                var groupName = knob.Target.Substring(6);
                var group = _config.Groups.FirstOrDefault(g => g.Name == groupName);
                if (group != null)
                {
                    float norm = rawValue / 1023f;
                    int pct = (int)Math.Round(norm * 100);
                    foreach (var dev in group.Devices)
                    {
                        switch (dev.Type)
                        {
                            case "govee":
                                if (pct == 0)
                                {
                                    var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == dev.DeviceId);
                                    if (gc != null) gc.PoweredOn = false;
                                    _ = AmbienceSync.SendTurnAsync(dev.DeviceId, false);
                                }
                                else
                                {
                                    var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == dev.DeviceId);
                                    bool wasOff = gc != null && !gc.PoweredOn;
                                    if (gc != null) gc.PoweredOn = true;

                                    var ip = dev.DeviceId;
                                    var bright = pct;
                                    if (wasOff)
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            await AmbienceSync.SendTurnAsync(ip, true);
                                            await Task.Delay(150);
                                            await AmbienceSync.SendBrightnessAsync(ip, bright);
                                        });
                                    }
                                    else
                                    {
                                        _ = AmbienceSync.SendBrightnessAsync(ip, bright);
                                    }
                                }
                                break;
                            case "corsair":
                                if (_corsairSync?.IsAvailable == true)
                                    _config.Corsair.LightBrightness = (int)(pct * 2.0);
                                break;
                            case "ha":
                                if (_ha != null && _ha.IsAvailable)
                                {
                                    float haVal = norm;
                                    _haLastValues[stateIdx] = ($"ha_light:{dev.DeviceId}", haVal);
                                    if (!_haThrottleActive[stateIdx])
                                    {
                                        _haThrottleActive[stateIdx] = true;
                                        _ = SendHaThrottledAsync(stateIdx);
                                    }
                                }
                                break;
                            case "audio_output":
                                _mixer?.SetOutputDeviceVolume(dev.DeviceId, norm);
                                break;
                        }
                    }
                }
            }
        }
        else if (knob.Target.Equals("govee", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000)
            {
                float norm = rawValue / 1023f;
                _ambienceSync?.EnsureDevicesPoweredOn();
                _ambienceSync?.SetBrightness(norm);
                Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(null, norm, true));
            }
        }
        else if (knob.Target.StartsWith("govee:", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000)
            {
                var ip = knob.Target.Substring(6);
                float norm = rawValue / 1023f;
                _ambienceSync?.EnsureDevicePoweredOn(ip);
                _ambienceSync?.SetBrightnessForDevice(ip, norm);
                Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(ip, norm, true));
            }
        }
        else if (knob.Target.StartsWith("vm_strip:", StringComparison.OrdinalIgnoreCase)
              || knob.Target.StartsWith("vm_bus:", StringComparison.OrdinalIgnoreCase))
        {
            if (_vm != null && _vm.IsAvailable && _config.VoiceMeeter.Enabled
                && Environment.TickCount64 - _startupTick >= 8000)
            {
                float norm = rawValue / 1023f;
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
        else if (knob.Target.Equals("corsair_pump_fan", StringComparison.OrdinalIgnoreCase)
              || knob.Target.Equals("corsair_case_fan", StringComparison.OrdinalIgnoreCase))
        {
            if (_corsairSync != null && _corsairSync.IsAvailable && _config.Corsair.Enabled
                && _config.Corsair.FanEnabled
                && Environment.TickCount64 - _startupTick >= 8000)
            {
                int percent = (int)Math.Round(rawValue / 1023.0 * 100);
                bool isPump = knob.Target.Equals("corsair_pump_fan", StringComparison.OrdinalIgnoreCase);
                if (isPump)
                    _config.Corsair.PumpFanSpeed = percent;
                else
                    _config.Corsair.CaseFanSpeed = percent;

                string typeFilter = isPump ? "pump" : "fan";
                foreach (var device in _corsairSync.Devices)
                {
                    bool matches = device.Type.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)
                        || (isPump && device.Type.Contains("cooler", StringComparison.OrdinalIgnoreCase));
                    if (matches)
                        _ = _corsairSync.SetFanSpeedAsync(device.Id, percent);
                }
            }
        }
        else
        {
            _mixer.SetVolume(knob, rawValue);
        }

        long osdNow = Environment.TickCount64;
        bool osdTimeSuppressed = osdNow - _startupTick < 10000
            || (DateTime.UtcNow - _connectedAt).TotalMilliseconds < 3000;
        bool osdValueSuppressed = _lastOsdValue[stateIdx] >= 0 && Math.Abs(rawValue - _lastOsdValue[stateIdx]) < 15;
        if (isBatch)
            _lastOsdValue[stateIdx] = rawValue;

        if (_config.Osd.ShowVolume && !isBatch
            && !knob.Target.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !osdTimeSuppressed)
        {
            if (osdNow - _lastOsdTick[stateIdx] >= 100 && !osdValueSuppressed)
            {
                _lastOsdTick[stateIdx] = osdNow;
                _lastOsdValue[stateIdx] = rawValue;
                Dispatcher.BeginInvoke(() => ShowKnobOsd(knob, rawValue));
                _osdFinalTimers[stateIdx]?.Change(Timeout.Infinite, Timeout.Infinite);
            }

            _pendingOsdValue[stateIdx] = rawValue;
            if (_osdFinalTimers[stateIdx] == null)
            {
                int idxCapture = stateIdx;
                _osdFinalTimers[idxCapture] = new System.Threading.Timer(_ =>
                {
                    int val = _pendingOsdValue[idxCapture];
                    if (val >= 0 && val != _lastOsdValue[idxCapture])
                    {
                        _lastOsdValue[idxCapture] = val;
                        _lastOsdTick[idxCapture] = Environment.TickCount64;
                        var k = GetKnobConfigByStateIndex(idxCapture);
                        if (k != null)
                            Dispatcher.BeginInvoke(() => ShowKnobOsd(k, val));
                    }
                }, null, 200, Timeout.Infinite);
            }
            else
            {
                _osdFinalTimers[stateIdx].Change(200, Timeout.Infinite);
            }
        }
    }

    private KnobConfig? GetKnobConfigByStateIndex(int stateIdx)
    {
        if (stateIdx >= N3KnobStateBase)
            return _config.N3.Knobs.FirstOrDefault(k => k.Idx == stateIdx - N3KnobStateBase);
        return _config.Knobs.FirstOrDefault(k => k.Idx == stateIdx);
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

    private void ShowKnobOsd(KnobConfig knob, int rawValue)
    {
        // Use the full volume pipeline (curve + range) so OSD matches actual volume
        float vol = VolumePipeline.ComputeVolume(rawValue, knob);
        int displayPct = (int)Math.Round(vol * 100);
        string label = !string.IsNullOrEmpty(knob.Label) ? knob.Label : knob.Target switch
        {
            "master" => "Master",
            "mic" => "Microphone",
            "active_window" => "Active Window",
            "system" => "System Sounds",
            "any" => "Auto",
            "apps" => "App Group",
            "monitor" when !string.IsNullOrEmpty(knob.DeviceId) =>
                GetMonitorLabel(knob.DeviceId),
            "monitor" => "Monitor",
            "led_brightness" => "LED Brightness",
            "room_lights" => "Room Lights",
            "output_device" => "Output Device",
            "input_device" => "Input Device",
            _ when knob.Target.StartsWith("group:") => knob.Target.Substring(6),
            _ when knob.Target.StartsWith("vm_strip:") => $"VM Strip {knob.Target.Split(':')[1]}",
            _ when knob.Target.StartsWith("vm_bus:") => $"VM Bus {knob.Target.Split(':')[1]}",
            "corsair_pump_fan" => "Pump Fan",
            "corsair_case_fan" => "Case Fans",
            _ => knob.Target
        };
        string symbol = knob.Target switch
        {
            "master" => "VolumeHigh",
            "mic" => "Microphone",
            "monitor" => "Monitor",
            "led_brightness" => "Palette",
            "room_lights" => "LightbulbGroup",
            "govee" => "Palette",
            _ when knob.Target.StartsWith("govee:") => "Palette",
            _ when knob.Target.StartsWith("group:") => "LightbulbGroup",
            "spotify" => "MusicNote",
            "discord" => "Headphones",
            _ when knob.Target.StartsWith("ha_") => "Home",
            _ when knob.Target.StartsWith("vm_") => "VolumeHigh",
            _ when knob.Target.StartsWith("corsair_") => "Fan",
            _ => "VolumeHigh"
        };
        if (!EnsureOsd()) return;
        _osdOverlay!.ShowVolume(label, displayPct, symbol);
    }

    private void HandleButton(ButtonEvent e)
    {
        if (_config.HardwareMode == HardwareMode.StreamControllerOnly)
            return;

        // Ignore button events during startup (5s) and reconnection (2s) to prevent phantom actions
        if (Environment.TickCount64 - _startupTick < 5000)
            return;
        if ((DateTime.UtcNow - _connectedAt).TotalMilliseconds < 2000)
            return;

        if (e.IsDown)
            _buttons.HandleDown(e.Idx, _config);
        else
            _buttons.HandleUp(e.Idx, _config);
    }

    private void HandleN3Input(N3InputEvent e)
    {
        if (_config.HardwareMode == HardwareMode.TurnUpOnly) return;
        if (!_config.N3.Enabled) return;

        switch (e.Kind)
        {
            case N3InputKind.EncoderTwist:
                HandleN3EncoderTwist(e);
                break;

            case N3InputKind.DisplayKey:
                // When inside a folder, LCD slot 0 is the virtual "Back" key and
                // slots 1-5 shift to folder keys 0-4.
                if (IsInFolder)
                {
                    if (e.Index == 0)
                    {
                        // Only react on release to match Stream Deck folder UX.
                        if (e.IsPressed == false)
                            NavigateToN3Folder("");
                        break;
                    }

                    int folderLocalIdx = N3DisplayKeyBase + (_config.N3.CurrentPage * 6) + (e.Index - 1);
                    HandleN3VirtualButton(folderLocalIdx, e.IsPressed == true);
                    break;
                }

                int pagedIdx = N3DisplayKeyBase + (_config.N3.CurrentPage * 6) + e.Index;
                HandleN3VirtualButton(pagedIdx, e.IsPressed == true);
                break;

            case N3InputKind.SideButton:
                HandleN3VirtualButton(N3SideButtonBase + e.Index, e.IsPressed == true);
                break;

            case N3InputKind.EncoderPress:
                HandleN3VirtualButton(N3EncoderPressBase + e.Index, e.IsPressed == true);
                break;
        }
    }

    private void HandleN3EncoderTwist(N3InputEvent e)
    {
        if (!_config.N3.MirrorFirstThreeKnobs) return;
        if (e.Index < 0 || e.Index > 2) return;

        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == e.Index);
        if (knob == null) return;

        int current = knob.LastRawValue >= 0
            ? knob.LastRawValue
            : (int)Math.Round(StreamControllerKnobPositions[e.Index] * 1023f);

        int step = Math.Clamp(_config.N3.EncoderStep, 1, 128);
        int next = Math.Clamp(current + (e.Delta * step), 0, 1023);
        StreamControllerKnobPositions[e.Index] = next / 1023f;
        ApplyKnobConfig(knob, next, N3KnobStateBase + e.Index, false);
    }

    private void HandleN3VirtualButton(int idx, bool isDown)
    {
        // Match the same startup/reconnect guardrails as the Turn Up button path.
        if (Environment.TickCount64 - _startupTick < 5000)
            return;
        if ((DateTime.UtcNow - _connectedAt).TotalMilliseconds < 2000)
            return;

        if (isDown)
            _buttons.HandleDown(idx, _config);
        else
            _buttons.HandleUp(idx, _config);
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

        // Tray icon, tray popup, and main window are WPF/WinForms UI — must be updated
        // on the UI thread. HandleConnection fires on the SerialReader background thread;
        // touching UI objects from there causes silent native crashes (access violations
        // in GDI/WPF internals that bypass managed exception handlers).
        var portName = connected ? _serial.Port?.PortName : null;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                UpdateAggregateTrayStatus();
                _mainWindow?.SetConnectionStatus(connected, portName);
            }
            catch (Exception ex)
            {
                Logger.Log($"HandleConnection UI update error: {ex.Message}");
            }
        });
    }

    private void UpdateAggregateTrayStatus()
    {
        bool anyConnected = _isConnected || _isN3Connected;
        string? label = _isConnected ? _serial.Port?.PortName
                       : _isN3Connected ? _n3DeviceName
                       : null;

        if (_trayIcon != null)
        {
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateTrayIcon(anyConnected);
            _trayIcon.Text = anyConnected ? "Amp Up — Connected" : "Amp Up — Disconnected";
            oldIcon?.Dispose();
        }

        _trayContextMenu?.UpdateStatus(anyConnected, label);
        _trayMixerPopup?.UpdateStatus(anyConnected, label);
    }

    /// <summary>
    /// Switch to a named profile. Used by button gestures and AutoProfileSwitcher.
    /// </summary>
    public void SwitchToProfile(string profileName)
    {
        HandleProfileSwitch(profileName);
    }

    // ── Game Mode ─────────────────────────────────────────────────────

    private long _gameModeLastChangeMs;

    private void PollGameMode()
    {
        if (!_config.Ambience.GameModeEnabled) return;

        // Debounce: don't toggle more than once every 3 seconds
        long nowMs = Environment.TickCount64;
        if (nowMs - _gameModeLastChangeMs < 3_000) return;

        bool isFullscreen = false;
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                // Skip AmpUp's own window and desktop/shell
                if (pid != 0 && pid != (uint)Environment.ProcessId)
                {
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                        var name = proc.ProcessName.ToLowerInvariant();
                        // Skip explorer (desktop), shell, and common non-game fullscreen apps
                        if (name != "explorer" && name != "shellexperiencehost"
                            && name != "searchhost" && name != "startmenuexperiencehost")
                        {
                            isFullscreen = NativeMethods.IsForegroundFullscreen();
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        if (isFullscreen && !_gameModeActive)
        {
            _gameModeActive = true;
            _gameModeLastChangeMs = nowMs;
            _gameModePreDreamView = _config.Ambience.ScreenSync.Enabled;
            _gameModePrevCorsairMode = _config.Corsair.LightSyncMode;

            try
            {
                var fgHwnd = NativeMethods.GetForegroundWindow();
                NativeMethods.GetWindowThreadProcessId(fgHwnd, out uint fgPid);
                var fgProc = System.Diagnostics.Process.GetProcessById((int)fgPid);
                Logger.Log($"GameMode: fullscreen detected ({fgProc.ProcessName}) — enabling screen sync");
            }
            catch { Logger.Log("GameMode: fullscreen detected — enabling screen sync"); }

            // Stop room effect so it doesn't fight with screen sync
            _mainWindow?.GetRoomView()?.StopRoomPatternForScreenSync();

            // Enable DreamView for Govee (only if not already on)
            if (!_config.Ambience.ScreenSync.Enabled)
            {
                _config.Ambience.ScreenSync.Enabled = true;
                _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
            }

            // Set Corsair to Screen Sync mode
            if (_config.Corsair.Enabled && _config.Corsair.LightSyncMode != "dreamview")
                _config.Corsair.LightSyncMode = "dreamview";
        }
        else if (!isFullscreen && _gameModeActive)
        {
            _gameModeActive = false;
            _gameModeLastChangeMs = nowMs;

            Logger.Log("GameMode: fullscreen exited — restoring room effect");

            // Only restore DreamView if we were the ones who turned it on
            if (!_gameModePreDreamView)
            {
                _config.Ambience.ScreenSync.Enabled = false;
                _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);
                _ambienceSync?.ClearAllSegmentTracking();
            }

            // Restart the room effect
            _mainWindow?.GetRoomView()?.RestartRoomEffectAfterScreenSync();

            // Only restore Corsair if we changed it
            if (_config.Corsair.Enabled && _gameModePrevCorsairMode != "dreamview")
                _config.Corsair.LightSyncMode = _gameModePrevCorsairMode;
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

        // Save current profile before switching so changes aren't lost
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);

        // Leaving a folder when profile changes keeps behavior predictable —
        // different profile = different folder set.
        _currentN3Folder = "";

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
        if (_n3 != null && _isN3Connected)
        {
            _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
            SyncStreamControllerDisplays();
        }

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

    /// <summary>
    /// Kicks off a low-frequency DispatcherTimer that re-renders the Stream Controller
    /// LCD keys whenever any key is configured as Clock or DynamicState. Clock keys
    /// need a redraw at least once per minute; DynamicState keys benefit from ~5s polling
    /// for OBS recording/streaming and mute states.
    /// </summary>
    private void StartStreamControllerRefreshTimer()
    {
        if (_streamControllerRefreshTimer != null) return;

        _streamControllerRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            // 1s interval so idle-sleep responds within a second of the
            // threshold being crossed (was 5s — made short "5s"-style
            // settings feel like they took up to 10s to trigger). Tick
            // body short-circuits when nothing needs doing.
            Interval = TimeSpan.FromSeconds(1),
        };
        _streamControllerRefreshTimer.Tick += (_, _) => OnStreamControllerRefreshTick();
        _streamControllerRefreshTimer.Start();
    }

    // True once the N3 brightness was dropped to 0 by the idle-sleep code.
    // Used so we only restore brightness on wake, not on every tick.
    private bool _n3AsleepFromIdle;
    private int _n3IdleTickDebugCount;

    // One-shot — forces the next refresh tick to put the N3 to sleep even
    // if the idle threshold hasn't been crossed. Wired to the Settings
    // "Sleep Now" button; consumed on the first tick that detects input.
    private bool _forceN3Sleep;

    /// <summary>Immediately blank the N3 LCDs. Wakes on the next mouse/keyboard input.</summary>
    public void ForceN3Sleep()
    {
        _forceN3Sleep = true;
        OnStreamControllerRefreshTick();
    }

    private void OnStreamControllerRefreshTick()
    {
        try
        {
            if (_config == null) return;

            // ── N3 idle sleep ─────────────────────────────────────────────
            // Uses the real firmware standby command (CRT HAN) via N3Controller.Sleep —
            // actually powers the LCDs down, not just dims to brightness 0.
            // Wake re-inits the device and resyncs display frames.
            if (_n3 != null && _isN3Connected)
            {
                int thresholdSec = Math.Max(0, _config.N3.IdleSleepSeconds);
                uint idleMs = NativeMethods.GetIdleMilliseconds();
                bool idleTriggered = thresholdSec > 0 && idleMs >= (uint)thresholdSec * 1000u;
                bool shouldSleep = _forceN3Sleep || idleTriggered;

                // Diagnostic: log every tick that actually could change state, or
                // the first few to confirm the timer is firing and seeing the slider.
                if (shouldSleep != _n3AsleepFromIdle || (++_n3IdleTickDebugCount <= 6))
                {
                    Logger.Log($"N3 idle tick: idleMs={idleMs} threshold={thresholdSec}s forced={_forceN3Sleep} asleep={_n3AsleepFromIdle} -> shouldSleep={shouldSleep}");
                }

                if (shouldSleep && !_n3AsleepFromIdle)
                {
                    _n3.Sleep();
                    _n3AsleepFromIdle = true;
                }
                else if (!shouldSleep && _n3AsleepFromIdle)
                {
                    _n3.Wake();
                    _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                    SyncStreamControllerDisplays();
                    _n3AsleepFromIdle = false;
                }

                // A forced sleep is consumed once input arrives, so the next
                // keypress wakes the screens just like a timeout-sleep would.
                if (_forceN3Sleep && idleMs < 500) _forceN3Sleep = false;
            }

            if (_config.N3?.DisplayKeys == null) return;

            bool hasDynamic = false;
            bool hasClock = false;
            foreach (var k in _config.N3.DisplayKeys)
            {
                if (k.DisplayType == DisplayKeyType.Clock) hasClock = true;
                else if (k.DisplayType == DisplayKeyType.DynamicState) hasDynamic = true;
                if (hasClock && hasDynamic) break;
            }

            if (!hasClock && !hasDynamic) return;

            // Keep OBS state fresh so obs_recording / obs_streaming reflect reality.
            if (hasDynamic && _obs != null && _obs.IsAvailable)
                _ = _obs.RefreshStatusAsync();

            SyncStreamControllerDisplays();
            _lastDynamicStateTick = DateTime.Now;
        }
        catch (Exception ex)
        {
            Logger.Log($"Stream Controller refresh tick failed: {ex.Message}");
        }
    }

    // ── Folder-aware config routing ────────────────────────────────────
    //
    // When inside a folder (_currentN3Folder != ""), LCD keys and button
    // configs come from that folder's own lists instead of the root N3 lists.

    private List<StreamControllerDisplayKeyConfig> GetActiveDisplayKeys()
    {
        if (string.IsNullOrEmpty(_currentN3Folder)) return _config.N3.DisplayKeys;
        var folder = _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
        return folder?.DisplayKeys ?? _config.N3.DisplayKeys;
    }

    private List<ButtonConfig> GetActiveN3Buttons()
    {
        if (string.IsNullOrEmpty(_currentN3Folder)) return _config.N3.Buttons;
        var folder = _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
        return folder?.Buttons ?? _config.N3.Buttons;
    }

    private int GetActivePageCount()
    {
        if (string.IsNullOrEmpty(_currentN3Folder)) return Math.Max(1, _config.N3.PageCount);
        var folder = _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
        return Math.Max(1, folder?.PageCount ?? 1);
    }

    private bool IsInFolder => !string.IsNullOrEmpty(_currentN3Folder);

    /// <summary>
    /// Navigate into a named folder. Empty string returns to root. Resets page
    /// to 0 and re-syncs the LCD displays.
    /// </summary>
    public void NavigateToN3Folder(string folderName)
    {
        folderName ??= "";

        // Validate: if navigating to a non-existent folder, fall back to root.
        if (folderName.Length > 0 && _config.N3.Folders.All(f => f.Name != folderName))
        {
            Logger.Log($"NavigateToN3Folder: folder '{folderName}' not found — returning to root");
            folderName = "";
        }

        _currentN3Folder = folderName;
        _config.N3.CurrentPage = 0;

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                SyncStreamControllerDisplays();
                _mainWindow?.GetButtonsView()?.SetActiveN3Folder(_currentN3Folder);
            }
            catch (Exception ex)
            {
                Logger.Log($"NavigateToN3Folder UI update error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Folder-aware button resolver for the gesture engine. When inside a
    /// folder, any N3 button idx resolves to the folder's own ButtonConfig list.
    /// </summary>
    private ButtonConfig? ResolveN3ButtonForGestureEngine(int idx)
    {
        if (!IsInFolder) return null; // fall through to default resolver

        // Only N3 idx ranges get folder-scoped resolution. Turn Up buttons (0-4)
        // always use the root _config.Buttons list.
        if (idx < N3DisplayKeyBase) return null;

        var folder = _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
        return folder?.Buttons.FirstOrDefault(b => b.Idx == idx);
    }

    private void SyncStreamControllerDisplays()
    {
        if (_n3 == null || !_isN3Connected) return;
        if (_config.HardwareMode == HardwareMode.TurnUpOnly) return;

        // Split the work into two halves:
        //   1. UI thread (here): compose each key's bitmap — WPF render is
        //      required to run on a Dispatcher thread, and the preset-icon
        //      code path builds a Grid/MaterialIcon tree.
        //   2. Background task: encode each bitmap to the wire JPEG and
        //      blast it out via HID. Both of these are slow (~20ms encode
        //      + 30-50ms HID write per key) and were freezing the UI for
        //      ~500ms every time the user navigated between folders/pages.
        bool inFolder = IsInFolder;
        int pageOffset = _config.N3.CurrentPage * 6;
        var activeKeys = GetActiveDisplayKeys();

        var ops = new List<(int slot, System.Drawing.Bitmap? bitmap)>(N3Controller.DisplayKeyCount);

        for (int i = 0; i < N3Controller.DisplayKeyCount; i++)
        {
            try
            {
                if (inFolder && i == 0)
                {
                    var backKey = BuildBackKeyDisplay();
                    ops.Add((i, StreamControllerDisplayRenderer.ComposeDeviceBitmap(backKey)));
                    continue;
                }

                int folderLocalIdx = inFolder ? pageOffset + (i - 1) : pageOffset + i;
                var key = activeKeys.FirstOrDefault(k => k.Idx == folderLocalIdx);
                if (key == null)
                {
                    ops.Add((i, null));
                    continue;
                }

                bool hasImage = !string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath);
                bool hasPreset = !string.IsNullOrWhiteSpace(key.PresetIconKind);
                bool hasText = !string.IsNullOrWhiteSpace(key.Title) || !string.IsNullOrWhiteSpace(key.Subtitle);

                if (!hasImage && !hasPreset && !hasText)
                {
                    ops.Add((i, null));
                    continue;
                }

                ops.Add((i, StreamControllerDisplayRenderer.ComposeDeviceBitmap(key)));
            }
            catch (Exception ex)
            {
                Logger.Log($"Stream Controller compose failed for slot {i}: {ex.Message}");
                ops.Add((i, null));
            }
        }

        var n3 = _n3;
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var (slot, bitmap) in ops)
                {
                    if (bitmap == null)
                    {
                        n3.ClearDisplay(slot, commit: false);
                        continue;
                    }

                    byte[] jpeg;
                    try { jpeg = StreamControllerDisplayRenderer.EncodeDeviceBitmap(bitmap); }
                    finally { bitmap.Dispose(); }

                    n3.SendDisplayImage(slot, jpeg, commit: false);
                }
                n3.CommitDisplayChanges();
            }
            catch (Exception ex)
            {
                Logger.Log($"Stream Controller display sync failed: {ex.Message}");
                // Ensure bitmaps don't leak if we bail mid-loop.
                foreach (var (_, bitmap) in ops)
                    bitmap?.Dispose();
            }
        });
    }

    /// <summary>Build a virtual "Back" display key used when inside a folder.</summary>
    internal static StreamControllerDisplayKeyConfig BuildBackKeyDisplay()
    {
        return new StreamControllerDisplayKeyConfig
        {
            Idx = -1,
            Title = "Back",
            PresetIconKind = "ArrowLeft",
            BackgroundColor = "#222222",
            AccentColor = "#FFB74D",
            TextPosition = DisplayTextPosition.Bottom,
            TextSize = 12,
            TextColor = "#FFFFFF",
        };
    }

    private bool _roomLightsOn = true;

    private void HandleRoomToggle()
    {
        _roomLightsOn = !_roomLightsOn;

        // Toggle all Govee devices
        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            if (string.IsNullOrWhiteSpace(dev.Ip)) continue;
            dev.PoweredOn = _roomLightsOn;
            _ = AmbienceSync.SendTurnAsync(dev.Ip, _roomLightsOn);
        }

        // Toggle Corsair (black = off, restore last color = on)
        if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
        {
            if (_roomLightsOn)
            {
                // Re-send Turn Up frames or static color will resume naturally
            }
            else
            {
                _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                _config.Corsair.LightSyncMode = "static"; // prevent frames overwriting black
            }
        }
    }

    private readonly Dictionary<string, bool> _groupStates = new();

    private void HandleGroupToggle(string groupName)
    {
        var group = _config.Groups.FirstOrDefault(g => g.Name == groupName);
        if (group == null) return;

        bool currentlyOn = _groupStates.GetValueOrDefault(groupName, true);
        bool newState = !currentlyOn;
        _groupStates[groupName] = newState;

        bool anyGoveeOn = false;
        foreach (var dev in group.Devices)
        {
            switch (dev.Type)
            {
                case "govee":
                    var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d => d.Ip == dev.DeviceId);
                    if (gc != null) gc.PoweredOn = newState;
                    _ = AmbienceSync.SendTurnAsync(dev.DeviceId, newState);
                    if (newState) anyGoveeOn = true;
                    break;
                case "corsair":
                    if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                    {
                        if (newState)
                            _config.Corsair.LightSyncMode = "vu_reactive";
                        else
                        {
                            _config.Corsair.LightSyncMode = "static";
                            _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                        }
                    }
                    break;
                case "ha":
                    if (_ha != null && _ha.IsAvailable)
                    {
                        var haAction = dev.Action switch
                        {
                            "on" => "turn_on",
                            "off" => "turn_off",
                            _ => "toggle",
                        };
                        _ = _ha.CallServiceAsync(dev.DeviceId.Split('.')[0], haAction, dev.DeviceId);
                    }
                    break;
                case "audio_output":
                    _mixer?.ToggleOutputDeviceMute(dev.DeviceId);
                    break;
            }
        }

        // If any Govee device was turned on, give it ~800ms to power up then
        // restart the room effect so it resumes the active pattern instead of solid color.
        if (anyGoveeOn)
        {
            Task.Delay(800).ContinueWith(_ =>
                _mainWindow?.GetRoomView()?.ResumeRoomEffect());
        }
    }

    private void HandleScPageChange(int value, bool absolute)
    {
        int maxPage = GetActivePageCount() - 1;
        int newPage = absolute
            ? Math.Clamp(value, 0, maxPage)
            : Math.Clamp(_config.N3.CurrentPage + value, 0, maxPage);

        if (newPage == _config.N3.CurrentPage) return;

        _config.N3.CurrentPage = newPage;
        ConfigManager.Save(_config);

        // Re-sync LCD displays to the new page
        SyncStreamControllerDisplays();

        // Update the UI if the Buttons tab is visible
        Dispatcher.BeginInvoke(() =>
        {
            _mainWindow?.GetButtonsView()?.SetStreamControllerPage(newPage);
        });
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
        // Immediately update RgbController's device ID for DeviceSelect effect (don't wait for 500ms poll)
        if (isOutput)
        {
            try
            {
                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var defaultDev = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                _rgb.SetDefaultOutputDevice(defaultDev.ID);
            }
            catch { }
        }

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

    private static string GetMonitorLabel(string deviceId)
    {
        var ids = deviceId.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0) return "Monitor";
        var infos = MonitorBrightness.GetMonitorInfos();
        if (ids.Length == 1)
        {
            var mon = infos.FirstOrDefault(m => m.DeviceName.Equals(ids[0], StringComparison.OrdinalIgnoreCase));
            return mon?.FriendlyName ?? "Monitor";
        }
        return $"{ids.Length} Monitors";
    }

    private string _lastDefaultOutputDeviceId = "";

    // Cached enumerator for mute polling (created once, lives for the app lifetime)
    private NAudio.CoreAudioApi.MMDeviceEnumerator? _pollEnumerator;
    // Cached devices for mute polling — refreshed only when the default device changes
    private NAudio.CoreAudioApi.MMDevice? _cachedMic;
    private NAudio.CoreAudioApi.MMDevice? _cachedMaster;
    // Reentrancy guard: skip poll if the previous one hasn't finished yet
    private int _pollMuteRunning;

    // Guards _notifyMaster and _notifyMic — SubscribeMuteNotifications is called from both
    // the background poll timer and the system session-switch message thread.
    private readonly object _notifyLock = new();

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
        // Called from two threads: background poll timer (device change) and session-unlock
        // (system message thread). Lock ensures only one runs at a time.
        lock (_notifyLock)
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
                    // Seed tray icon volume
                    _trayVolume = _notifyMaster.AudioEndpointVolume.MasterVolumeLevelScalar;
                    _trayMuted = _notifyMaster.AudioEndpointVolume.Mute;
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
    }

    private void OnMasterVolumeNotification(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        // Guard: session lock tears down COM objects — stale callbacks must be ignored
        if (_isShuttingDown || _sessionLocked) return;
        try
        {
            _rgb.SetMasterMuted(data.Muted);
            UpdateTrayIconVolume(data.MasterVolume, data.Muted);
        }
        catch { }
    }

    private void OnMicVolumeNotification(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        if (_isShuttingDown || _sessionLocked) return;
        try { _rgb.SetMicMuted(data.Muted); } catch { }
    }

    private void PollMuteStates()
    {
        // Skip during session lock — WASAPI COM objects are invalidated while locked,
        // and we've already torn down our cached devices in OnSessionSwitch.
        if (_isShuttingDown || _sessionLocked) return;
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

            // Scan ALL active render devices — not just the default — so apps on
            // secondary audio outputs (common with multi-monitor setups) are found.
            var devices = new List<NAudio.CoreAudioApi.MMDevice>();
            try
            {
                var allDevices = _pollEnumerator.EnumerateAudioEndPoints(
                    NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
                for (int d = 0; d < allDevices.Count; d++)
                    devices.Add(allDevices[d]);
            }
            catch { return; }

            try
            {
                foreach (var light in lightsToCheck)
                {
                    if (string.IsNullOrWhiteSpace(light.ProgramName)) continue;
                    bool muted = true; // default: muted/not-found
                    bool found = false;
                    foreach (var device in devices)
                    {
                        if (found) break;
                        try
                        {
                            var sessions = device.AudioSessionManager.Sessions;
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
                                        found = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                    _rgb.SetProgramMuted(light.Idx, muted);
                }
            }
            finally
            {
                foreach (var d in devices) d.Dispose();
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

            // Scan ALL active render devices (multi-output setups)
            var devices = new List<NAudio.CoreAudioApi.MMDevice>();
            try
            {
                var allDevices = _pollEnumerator.EnumerateAudioEndPoints(
                    NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
                for (int d = 0; d < allDevices.Count; d++)
                    devices.Add(allDevices[d]);
            }
            catch { return; }

            try
            {
                foreach (var knob in knobsToCheck)
                {
                    bool anyUnmuted = false;
                    bool anyFound = false;
                    foreach (var device in devices)
                    {
                        try
                        {
                            var sessions = device.AudioSessionManager.Sessions;
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
                    }
                    // allMuted = true only when apps were found and none are unmuted
                    // if no apps found, default to false (show color1 / live appearance)
                    bool allMuted = anyFound && !anyUnmuted;
                    _rgb.SetAppGroupMuted(knob.Idx, allMuted);
                }
            }
            finally
            {
                foreach (var d in devices) d.Dispose();
            }
        }
        catch { }
    }

    private void ApplyRgbConfig()
    {
        _rgb.SetGamma(_config.GammaR, _config.GammaG, _config.GammaB);
        _rgb.SetBrightness(_config.LedBrightness);
        _rgb.SetMuteBrightness(_config.MuteBrightness);
        _rgb.UpdateConfig(_config.Lights);
        _rgb.UpdateCustomPalettes(_config.CustomPalettes);
        _rgb.UpdateGlobalConfig(_config.GlobalLight);
    }

    /// <summary>
    /// Start or stop the AudioAnalyzer based on whether any light uses AudioReactive.
    /// </summary>
    private void UpdateAudioAnalyzer()
    {
        bool needsAudio = _config.Lights.Any(l => l.Effect == LightEffect.AudioReactive || l.Effect == LightEffect.AudioPositionBlend)
            || (_config.GlobalLight.Enabled && (_config.GlobalLight.Effect == LightEffect.AudioReactive || _config.GlobalLight.Effect == LightEffect.AudioPositionBlend));
        if (_mainWindow?.GetRoomView()?.IsMusicReactiveActive == true)
            needsAudio = true;
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
    private QuickWheelConfig? _activeWheelCfg;

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
            _activeWheelCfg = wheelCfg;

            // Initialize last raw values so first delta is correct
            for (int i = 0; i < 5; i++)
                _lastKnobRaw[i] = (int)(KnobPositions[i] * 1023f);

            _radialWheel = new RadialWheelOverlay();
            _radialWheel.SetMonitor(_config.Osd.MonitorIndex);

            switch (_activeWheelMode)
            {
                case QuickWheelMode.OutputDevice:
                    PopulateWheelDevices();
                    break;
                case QuickWheelMode.MediaControls:
                    PopulateWheelMediaControls();
                    break;
                case QuickWheelMode.Custom:
                    PopulateWheelCustom(wheelCfg);
                    break;
                default:
                    PopulateWheelProfiles();
                    break;
            }

            _radialWheel.OnSegmentClicked = idx => ConfirmWheelSelection(idx);
            _radialWheel.Closed += (_, _) => { _wheelVisible = false; _radialWheel = null; _activeWheelCfg = null; };
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

    private static readonly List<(string id, string label, string symbol, System.Windows.Media.Color color)> MediaControlActions = new()
    {
        ("media_play_pause", "Play / Pause", "PlayPause", System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76)),
        ("media_prev", "Previous", "SkipPrevious", System.Windows.Media.Color.FromRgb(0x00, 0xBC, 0xD4)),
        ("media_next", "Next", "SkipNext", System.Windows.Media.Color.FromRgb(0x00, 0xBC, 0xD4)),
        ("mute_master", "Mute Master", "VolumeOff", System.Windows.Media.Color.FromRgb(0xFF, 0x44, 0x44)),
        ("mute_mic", "Mute Mic", "MicrophoneOff", System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x00)),
        ("volume_up", "Volume Up", "VolumeHigh", System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5)),
        ("volume_down", "Volume Down", "VolumeLow", System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5)),
        ("media_stop", "Stop", "Stop", System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E)),
    };

    private void PopulateWheelMediaControls()
    {
        _radialWheel!.SetActions(MediaControlActions, 0);
    }

    private void PopulateWheelCustom(QuickWheelConfig cfg)
    {
        var actions = new List<(string id, string label, string symbol, System.Windows.Media.Color color)>();
        foreach (var slot in cfg.CustomSlots)
        {
            if (string.IsNullOrEmpty(slot.ActionId)) continue;
            var (symbol, color) = GetActionVisuals(slot.ActionId);
            actions.Add((slot.ActionId, string.IsNullOrEmpty(slot.Label) ? slot.ActionId : slot.Label, symbol, color));
        }
        if (actions.Count == 0) { _wheelVisible = false; return; }
        _radialWheel!.SetActions(actions, 0);
    }

    private static (string symbol, System.Windows.Media.Color color) GetActionVisuals(string actionId)
    {
        return actionId switch
        {
            "media_play_pause" => ("PlayPause", System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76)),
            "media_next" => ("SkipNext", System.Windows.Media.Color.FromRgb(0x00, 0xBC, 0xD4)),
            "media_prev" => ("SkipPrevious", System.Windows.Media.Color.FromRgb(0x00, 0xBC, 0xD4)),
            "media_stop" => ("Stop", System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E)),
            "mute_master" => ("VolumeOff", System.Windows.Media.Color.FromRgb(0xFF, 0x44, 0x44)),
            "mute_mic" => ("MicrophoneOff", System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x00)),
            "volume_up" => ("VolumeHigh", System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5)),
            "volume_down" => ("VolumeLow", System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5)),
            "mute_program" => ("VolumeOff", System.Windows.Media.Color.FromRgb(0xFF, 0x44, 0x44)),
            "mute_active_window" => ("VolumeOff", System.Windows.Media.Color.FromRgb(0xFF, 0x44, 0x44)),
            "switch_profile" => ("AccountCircleOutline", System.Windows.Media.Color.FromRgb(0xAB, 0x47, 0xBC)),
            "cycle_brightness" => ("Brightness6", System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x00)),
            "launch_exe" => ("Launch", System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5)),
            "macro" => ("Keyboard", System.Windows.Media.Color.FromRgb(0xFF, 0xB8, 0x00)),
            "power_sleep" => ("Sleep", System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E)),
            "power_lock" => ("Lock", System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E)),
            _ => ("CircleOutline", System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E)),
        };
    }

    private void ConfirmWheelSelection(int idx)
    {
        _wheelVisible = false;
        var wheelCfg = _activeWheelCfg;
        _radialWheel = null;
        _activeWheelCfg = null;

        if (_activeWheelMode == QuickWheelMode.MediaControls)
        {
            // Execute the media control action directly
            if (idx >= 0 && idx < MediaControlActions.Count)
            {
                var actionId = MediaControlActions[idx].id;
                // volume_up / volume_down are key presses not in ButtonHandler — handle inline
                if (actionId == "volume_up")
                    NativeMethods.keybd_event(0xAF, 0, 0, UIntPtr.Zero); // VK_VOLUME_UP
                else if (actionId == "volume_down")
                    NativeMethods.keybd_event(0xAE, 0, 0, UIntPtr.Zero); // VK_VOLUME_DOWN
                else if (actionId == "media_stop")
                    NativeMethods.keybd_event(0xB2, 0, 0, UIntPtr.Zero); // VK_MEDIA_STOP
                else
                    _buttons.ExecuteActionByName(actionId);
            }
        }
        else if (_activeWheelMode == QuickWheelMode.Custom)
        {
            // Execute the custom action
            if (wheelCfg != null && idx >= 0 && idx < wheelCfg.CustomSlots.Count)
            {
                var slot = wheelCfg.CustomSlots[idx];
                if (!string.IsNullOrEmpty(slot.ActionId))
                    _buttons.ExecuteActionByName(slot.ActionId);
            }
        }
        else if (_activeWheelMode == QuickWheelMode.OutputDevice)
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

            double duration = _config.Osd.WheelDuration;

            // WheelDuration=0: confirm immediately on release (original behavior)
            if (duration < 0.05)
            {
                int idx = _radialWheel.GetSelectedIndex();
                var wheel = _radialWheel;
                wheel.OnSegmentClicked = null;
                _wheelVisible = false;
                _radialWheel = null;
                _activeWheelCfg = null;
                wheel.Dismiss();
                ConfirmWheelSelection(idx);
                return;
            }

            // Auto-dismiss timer — wheel stays visible for WheelDuration after release
            _wheelDismissTimer?.Stop();
            _wheelDismissTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(duration)
            };
            _wheelDismissTimer.Tick += (_, _) =>
            {
                _wheelDismissTimer?.Stop();
                if (_radialWheel == null || !_wheelVisible) return;
                int idx = _radialWheel.GetSelectedIndex();
                var wheel = _radialWheel;
                wheel.OnSegmentClicked = null;
                _wheelVisible = false;
                _radialWheel = null;
                _activeWheelCfg = null;
                wheel.Dismiss();
                ConfirmWheelSelection(idx);
            };
            _wheelDismissTimer.Start();
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
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _trayIconHwndSource?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutePollingTimer?.Dispose();
        _autoSwitchTimer?.Dispose();
        _gameModeTimer?.Dispose();
        _streamControllerRefreshTimer?.Stop();
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
        _corsairSync?.Dispose();
        _lgMonitor?.Dispose();
        _n3?.Dispose();
        _cachedMic?.Dispose();
        _cachedMaster?.Dispose();
        lock (_notifyLock)
        {
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
        } // end lock (_notifyLock)
        _pollEnumerator?.Dispose();
        MonitorBrightness.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
