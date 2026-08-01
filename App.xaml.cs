using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using AmpUp.Controls;
using AmpUp.Core.Engine;
using AmpUp.Core.Services;
using AmpUp.Services;
using AmpUp.Views;
using Forms = System.Windows.Forms;

namespace AmpUp;

[SupportedOSPlatform("windows7.0")]
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
    private System.Threading.Timer? _audioDeviceRefreshTimer;
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
    private UpdateInfo? _availableUpdate;
    private TrayContextMenu? _trayContextMenu;
    private AmbienceSync? _ambienceSync;
    private DreamSyncController? _dreamSync;
    private CorsairSync? _corsairSync;
    private SignalRgbBridgeService? _signalRgbBridge;
    private LgMonitorSync? _lgMonitor;
    private N3Controller? _n3;
    private SpotifyIntegration? _spotify;
    public static SpotifyIntegration? Spotify => (Current as App)?._spotify;
    private DiscordRpcIntegration? _discordRpc;
    private HardwareInputPump? _turnUpInputPump;
    private HardwareInputPump? _n3InputPump;
    public static DiscordRpcIntegration? DiscordRpc => (Current as App)?._discordRpc;
    private RadialWheelOverlay? _radialWheel;
    private bool _wheelVisible;
    private System.Windows.Threading.DispatcherTimer? _wheelDismissTimer;
    private System.Windows.Threading.DispatcherTimer? _streamControllerRefreshTimer;
    private DateTime _lastDynamicStateTick = DateTime.MinValue;
    private DateTime _lastHardwareMetricTick = DateTime.MinValue;
    private readonly int[] _lastKnobRaw = new int[5];
    private readonly Dictionary<int, N3AnimatedKeyState> _n3AnimatedKeys = new();
    // Pre-sorted snapshot of _n3AnimatedKeys — rebuilt only when the dictionary
    // mutates so the 80ms animated tick doesn't allocate an OrderBy per frame.
    private KeyValuePair<int, N3AnimatedKeyState>[] _n3AnimatedKeysSorted =
        Array.Empty<KeyValuePair<int, N3AnimatedKeyState>>();
    private HardwareMonitorService? _hardwareMonitor;
    private readonly SemaphoreSlim _n3DisplayWriteGate = new(1, 1);
    // Per-slot content signature from the last display sync. A slot is only
    // re-composed/encoded/sent when its signature changes (clock string,
    // hardware metric value, dynamic state etc. are baked into the signature
    // so dynamic keys still repaint exactly when their content changes).
    private readonly string?[] _n3LastSlotSignature = new string?[N3Controller.DisplayKeyCount];
    // OnConfigChanged gates: skip the HA HTTP test + full N3 resync when the
    // relevant config subsections didn't actually change since the last call.
    private string? _lastHaTestKey;
    private string? _lastN3ConfigSignature;
    private int _lastN3AppliedBrightness = -1;
    private const int N3DisplayKeyBase = 100;
    private const int N3SideButtonBase = 10000;
    private const int N3EncoderPressBase = 10003;
    private const int N3KnobStateBase = 5;
    private const int StreamControllerRefreshIntervalMs = 1000;
    private const int StreamControllerAnimatedRefreshIntervalMs = 80;
    private const int StreamControllerDynamicRefreshMs = 3000;
    private const int StreamControllerHardwareRefreshMs = 1000;
    private const int MutePollingIdleMs = 1000;
    private const int MutePollingDuckingMs = 500;
    private const int MutePollingQuietMs = 5000;
    private const int ResumeSettleMs = 8000;
    private const int ResumeSerialIdleMs = 6000;
    private const int HardwareInputSampleLogMs = 5000;
    private const int HardwareInputSlowLogMs = 1000;
    private static readonly TimeSpan N3AutoMissingRetryInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan N3ExpectedRetryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan N3DisconnectedRetryInterval = TimeSpan.FromSeconds(5);
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private long _lastHardwareInputLogTick;
    private long _lastHardwareActivityTick = Environment.TickCount64;
    private long _lastTurnUpRgbWriteErrorTick;
    private long _nextN3ReconnectUtcTicks;
    private int _n3ReconnectInFlight;
    private volatile bool _n3InitialProbeComplete;
    private volatile bool _n3EverConnected;
    private volatile bool _resumeSettling;
    private DateTime _resumeSettlingUntilUtc = DateTime.MinValue;
    private CancellationTokenSource? _resumeRecoveryCts;

    private sealed class N3AnimatedKeyState
    {
        public required string Signature { get; init; }
        public required byte[][] Frames { get; init; }
        public required int[] FrameDelaysMs { get; init; }
        public int FrameIndex { get; private set; }
        public DateTime NextFrameAtUtc { get; private set; }

        public byte[] CurrentFrame => Frames[Math.Clamp(FrameIndex, 0, Frames.Length - 1)];

        public static N3AnimatedKeyState Create(string signature, StreamControllerDeviceAnimation animation)
        {
            return new N3AnimatedKeyState
            {
                Signature = signature,
                Frames = animation.Frames,
                FrameDelaysMs = animation.FrameDelaysMs,
                FrameIndex = 0,
                NextFrameAtUtc = DateTime.UtcNow.AddMilliseconds(animation.FrameDelaysMs.FirstOrDefault(100)),
            };
        }

        public bool TryAdvance(DateTime nowUtc, out byte[]? nextFrame)
        {
            nextFrame = null;
            if (Frames.Length <= 1) return false;
            if (nowUtc < NextFrameAtUtc) return false;

            do
            {
                FrameIndex = (FrameIndex + 1) % Frames.Length;
                int delay = FrameDelaysMs[Math.Clamp(FrameIndex, 0, FrameDelaysMs.Length - 1)];
                NextFrameAtUtc = NextFrameAtUtc.AddMilliseconds(Math.Max(80, delay));
            }
            while (nowUtc >= NextFrameAtUtc);

            nextFrame = CurrentFrame;
            return true;
        }
    }

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
    internal static HardwareMonitorService? HardwareMonitor => (Current as App)?._hardwareMonitor;
    public static AppConfig? Config => (Current as App)?._config;
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

        // Hidden dev CLI: export FX Space icon pack and exit. Run from the
        // build output dir — writes to ../../../Icons so the PNGs end up in
        // the source tree ready to commit. Skips mutex + window startup.
        if (e.Args.Length > 0 && e.Args[0] == "--export-fx-icons")
        {
            try { ExportFxIconsAndExit(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            Shutdown();
            return;
        }

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
        _audioDeviceRefreshTimer = new System.Threading.Timer(
            _ => RefreshAudioDevicesAfterChange(), null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _mixer.AudioDevicesChanged += QueueAudioDeviceRefresh;
        _buttons = new ButtonHandler();
        _turnUpInputPump = new HardwareInputPump(HardwareInputSlowLogMs);
        _n3InputPump = new HardwareInputPump(HardwareInputSlowLogMs);
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
        // Corsair SDK init moved to InitializeHardwareDeferred — it can
        // block for hundreds of ms hunting for the iCUE service.
        // Corsair / LG / N3 hardware probes all do blocking HID enumeration
        // or SDK init that can take several hundred ms. Create the backend
        // objects synchronously (fast — just `new`) and defer the actual
        // device-finding to InitializeHardwareDeferred() which runs after
        // the main window shows so startup feels instant.
        _lgMonitor = new LgMonitorSync();
        _rgb.OnFrameReady += frame =>
        {
            if (_lgMonitor?.IsAvailable != true) return;
            if (!_config.Ambience.LinkToLights) return;
            _lgMonitor.SyncFromRoomEffect(frame);
        };

        _signalRgbBridge = new SignalRgbBridgeService(_config.SignalRgb);
        _signalRgbBridge.FrameReceived += frame =>
        {
            if (_config.SignalRgb.Enabled)
                _rgb.PushScreenSyncColors(frame, BuildSignalRgbLedMask(_config.SignalRgb));
        };
        _signalRgbBridge.FrameTimedOut += ClearSignalRgbOverride;
        _signalRgbBridge.UpdateConfig(_config.SignalRgb);

        _n3 = new N3Controller();
        _n3.OnInput += e => QueueN3HardwareInput(() => $"N3 {e.Describe()}", () => HandleN3Input(e));
        _n3.OnConnectionChanged += HandleN3ConnectionChanged;

        // Let the display renderer resolve dynamic-state sources without
        // taking a hard dependency on OBS / AudioMixer.
        StreamControllerDisplayRenderer.DynamicStateResolver =
            source => DynamicKeyStateProvider.IsActive(source, _obs, _mixer);

        // Spotify hooks for the SpotifyNowPlaying DisplayType.
        StreamControllerDisplayRenderer.SpotifyNowPlayingImagePath =
            SpotifyIntegration.AlbumArtCachePath;
        StreamControllerDisplayRenderer.SpotifyNowPlayingTitleProvider = () =>
        {
            var t = _spotify?.CurrentTrack;
            if (t == null || string.IsNullOrEmpty(t.TrackId))
                return ("Spotify", "— nothing playing —");
            return (t.Title, t.Artists);
        };

        _hardwareMonitor = new HardwareMonitorService();
        StreamControllerDisplayRenderer.HardwareMetricProvider = (source, gaugeMax) =>
        {
            var reading = _hardwareMonitor.GetReading(source, gaugeMax);
            return new HardwareMetricDisplay(reading.Label, reading.ValueText, reading.IsAvailable, reading.GaugeFraction);
        };

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
        // Screen Sync init is deferred too — it grabs screen buffers and
        // kicks off a capture thread, which can stall the UI on first run.
        StartGoveeLanPowerRefreshForStartup();

        _buttons.OnProfileSwitch += HandleProfileSwitch;
        _buttons.OnDeviceSwitched += HandleDeviceSwitched;
        _buttons.OnBrightnessCycle += HandleBrightnessCycle;
        _buttons.OnQuickWheelOpen += HandleQuickWheelOpen;
        _buttons.OnQuickWheelClose += HandleQuickWheelClose;
        _buttons.OnRoomToggle += HandleRoomToggle;
        _buttons.OnCorsairToggle += HandleCorsairToggle;
        _buttons.OnRoomWhiteToggle += HandleRoomWhiteToggle;
        _buttons.OnSpotifyPlayPause    += () => { _ = _spotify?.PlayPauseAsync(); };
        _buttons.OnSpotifyNext         += () => { _ = _spotify?.NextAsync(); };
        _buttons.OnSpotifyPrev         += () => { _ = _spotify?.PreviousAsync(); };
        _buttons.OnSpotifyShuffleToggle+= () => { _ = _spotify?.ToggleShuffleAsync(); };
        _buttons.OnSpotifyLikeToggle   += () => { _ = _spotify?.ToggleLikeAsync(); };
        _buttons.OnRoomEffectSet += HandleRoomEffectSet;
        _buttons.OnGroupToggle += HandleGroupToggle;
        _buttons.OnScPageChange += HandleScPageChange;
        _buttons.OnOpenFolder += NavigateToN3Folder;
        _buttons.OnAppGroupChanged += HandleButtonAppGroupChanged;

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

        // Spotify integration — tries to restore a prior session silently
        // using the stored refresh token. User-facing Connect button lives
        // in Settings for first-time auth.
        _discordRpc = new DiscordRpcIntegration(_config.DiscordRpc, _ => ConfigManager.Save(_config));
        _buttons.SetDiscordRpcIntegration(_discordRpc);

        _spotify = new SpotifyIntegration(_config.Spotify, _ => ConfigManager.Save(_config));
        _spotify.OnStateChanged += HandleSpotifyStateChanged;
        _ = Task.Run(async () =>
        {
            try { await _spotify.TryRestoreAsync(); }
            catch (Exception ex) { Logger.Log($"Spotify auto-restore failed: {ex.Message}"); }
        });

        // Start audio mixer
        _mixer.Start();

        // Restore last known knob positions for UI/LED state only. Do not apply
        // them to Windows/app volume on launch; the real device batch below is
        // also a position report, not an intentional user volume change.
        foreach (var knob in _config.Knobs)
        {
            if (knob.Idx >= 0 && knob.Idx < 5 && knob.LastRawValue >= 0)
            {
                KnobPositions[knob.Idx] = knob.LastRawValue / 1023f;
                HandleKnob(new KnobEvent { Idx = knob.Idx, Value = knob.LastRawValue, IsBatch = true });
            }
        }
        foreach (var knob in _config.N3.Knobs)
        {
            if (knob.Idx >= 0 && knob.Idx < 3 && knob.LastRawValue >= 0)
            {
                StreamControllerKnobPositions[knob.Idx] = knob.LastRawValue / 1023f;
                ApplyKnobConfig(knob, knob.LastRawValue, N3KnobStateBase + knob.Idx, true);
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
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch { return null; }
        });
        _autoSwitcher.OnProfileSwitchRequested += profileName =>
            Dispatcher.Invoke(() => SwitchToProfile(profileName));
        _autoSwitchTimer = new System.Threading.Timer(_ => _autoSwitcher?.Poll(), null,
            GetAutoSwitchDueMs(), GetAutoSwitchPeriodMs());

        // Game Mode — auto-enable screen sync when fullscreen game detected
        _gameModeTimer = new System.Threading.Timer(_ => PollGameMode(), null,
            GetGameModeDueMs(), GetGameModePeriodMs());

        // Start serial reader
        _serial = new SerialReader(_config.Serial.Port, _config.Serial.Baud);
        _serial.OnKnob += e => QueueLatestTurnUpInput(
            e.Idx,
            () => $"Turn Up knob {e.Idx}{(e.IsBatch ? " batch" : "")} value={e.Value}",
            () => HandleKnob(e));
        _serial.OnButton += e => QueueTurnUpHardwareInput(() => $"Turn Up button {e.Idx} {(e.IsDown ? "down" : "up")}", () => HandleButton(e));
        _serial.OnConnectionChanged += HandleConnection;
        _startupTick = Environment.TickCount64; // reset just before serial starts
        ConfigureTurnUpSerialForHardwareMode();

        // Apply RGB config
        ApplyRgbConfig();
        UpdateAudioAnalyzer();

        // Poll mute states for LED status effects (fallback). Master/mic
        // mute use instant WASAPI notifications; the timer mostly covers
        // program/app-group effects and ducking, so it can idle slower when
        // those features are not active.
        _mutePollingTimer = new System.Threading.Timer(_ => PollMuteStates(), null, 1000, GetMutePollingPeriodMs());
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
        if (_signalRgbBridge != null)
            _mainWindow.SetSignalRgbBridge(_signalRgbBridge);
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

        // Hardware device probes (Corsair / LG / N3 / Screen Sync) run here
        // at the lowest dispatcher priority so the main window finishes its
        // first layout + render pass before we burn any cycles on HID
        // enumeration or SDK init. Keeps the launch feel instant.
        Dispatcher.BeginInvoke(
            new Action(InitializeHardwareDeferred),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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
            AudioMixer.SetRenderEndpointVolume(device.AudioEndpointVolume, next);
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
        // DDC/CI physical-monitor handles become stale when a display powers
        // off or the topology changes, even though the IntPtr remains non-zero.
        MonitorBrightness.InvalidateCache();
        Logger.Log("Display settings changed — refreshed monitor handles and tray icon");
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
            if (IsResumeSettling)
            {
                Logger.Log("Session unlocked during resume settling; deferring audio refresh");
                return;
            }
            Logger.Log("Session unlocked — re-subscribing mute notifications");
            try { _mixer?.RefreshNow(); } catch { }
            try { SubscribeMuteNotifications(); } catch { }
        }
    }

    // True while the Windows session is locked — guards against stale WASAPI callbacks
    private volatile bool _sessionLocked;

    private bool IsResumeSettling =>
        _resumeSettling || DateTime.UtcNow < _resumeSettlingUntilUtc;

    private TimeSpan GetN3ReconnectInterval()
    {
        if (_n3EverConnected)
            return N3DisconnectedRetryInterval;

        return _config?.HardwareMode == HardwareMode.Auto
            ? N3AutoMissingRetryInterval
            : N3ExpectedRetryInterval;
    }

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
                try { _resumeRecoveryCts?.Cancel(); } catch { }
                _resumeSettling = false;
                _resumeSettlingUntilUtc = DateTime.MinValue;
                _rgb?.SetOutput(null, null);
                _ambienceSync?.SetSyncSuspended(true);
                _dreamSync?.SetSuspended(true);
                Dispatcher.BeginInvoke(() =>
                {
                    try { _streamControllerRefreshTimer?.Stop(); } catch { }
                });

                if (_n3 != null && _isN3Connected)
                    _n3.Sleep();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                BeginResumeRecovery();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"PowerModeChanged error: {ex.Message}");
        }
    }

    private void BeginResumeRecovery()
    {
        try
        {
            _resumeRecoveryCts?.Cancel();
            _resumeRecoveryCts?.Dispose();
        }
        catch { }

        _resumeRecoveryCts = new CancellationTokenSource();
        var token = _resumeRecoveryCts.Token;

        _resumeSettling = true;
        _resumeSettlingUntilUtc = DateTime.UtcNow.AddMilliseconds(ResumeSettleMs);

        _ambienceSync?.SetSyncSuspended(true);
        _dreamSync?.SetSuspended(true);
        _rgb?.SetOutput(null, null);
        try { _mutePollingTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try { _autoSwitchTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        try { _gameModeTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
        Dispatcher.BeginInvoke(() =>
        {
            try { _streamControllerRefreshTimer?.Stop(); } catch { }
        });

        _ = Task.Run(() => RecoverFromResumeAsync(token), token);
    }

    private async Task RecoverFromResumeAsync(CancellationToken ct)
    {
        try
        {
            Logger.Log($"Resume recovery: settling for {ResumeSettleMs}ms");
            await Task.Delay(ResumeSettleMs, ct);

            await RefreshGoveeLanPowerStatesAsync(_config.Ambience, "Resume", ct);
            _ambienceSync?.UpdateConfig(_config.Ambience);
            _dreamSync?.UpdateConfig(_config.Ambience.ScreenSync, _config.Ambience);

            try
            {
                _mixer?.InvalidatePeakDevice();
                _mixer?.RefreshNow();
                SubscribeMuteNotifications();
            }
            catch (Exception ex)
            {
                Logger.Log($"Power resume audio refresh error: {ex.Message}");
            }

            TryRecoverN3AfterResume();
            TryReconnectTurnUpAfterResume();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Log($"Resume recovery error: {ex.Message}");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _resumeSettling = false;
                _resumeSettlingUntilUtc = DateTime.MinValue;
                _ambienceSync?.SetSyncSuspended(false);
                _dreamSync?.SetSuspended(false);
                ConfigureMutePollingTimer();
                ConfigureAutoSwitchTimer();
                ConfigureGameModeTimer();
                _ = Dispatcher.BeginInvoke(() =>
                {
                    try { _streamControllerRefreshTimer?.Start(); } catch { }
                });
                Logger.Log("Resume recovery complete");
            }
        }
    }

    private void TryRecoverN3AfterResume()
    {
        if (_n3 == null || _config == null) return;
        if (!_config.N3.Enabled || _config.HardwareMode == HardwareMode.TurnUpOnly) return;
        if (Interlocked.Exchange(ref _n3ReconnectInFlight, 1) != 0) return;

        _ = Task.Run(() =>
        {
            try
            {
                Logger.Log("N3: forcing HID reconnect after system resume");
                HandleN3ConnectionChanged(false, _n3DeviceName);

                if (!_n3.TryConnect())
                {
                    Logger.Log("N3: resume reconnect did not find the device");
                    return;
                }

                _isN3Connected = true;
                _n3DeviceName = _n3.DeviceName;
                _n3AsleepFromIdle = false;
                _forceN3Sleep = false;
                _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                ResetN3SlotSignatureCache();

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        _mainWindow?.SetN3ConnectionStatus(true, _n3DeviceName);
                        UpdateAggregateTrayStatus();
                        SyncStreamControllerDisplays();
                    }
                    catch (Exception ex) { Logger.Log($"N3 resume display sync failed: {ex.Message}"); }
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"N3 resume reconnect failed: {ex.Message}");
                HandleN3ConnectionChanged(false, _n3DeviceName);
            }
            finally
            {
                Interlocked.Exchange(ref _n3ReconnectInFlight, 0);
            }
        });
    }

    private void TryReconnectTurnUpAfterResume()
    {
        if (_serial == null) return;

        try
        {
            _serial.RequestReconnect("system resume serial refresh");
        }
        catch (Exception ex)
        {
            Logger.Log($"Turn Up resume reconnect check failed: {ex.Message}");
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
        else if (msg == WmDeviceChange && wParam.ToInt64() == DbtDeviceArrival)
        {
            // Auto mode probes once at startup and otherwise waits quietly.
            // A USB arrival makes the next N3 probe immediate, so users can
            // hot-plug one without continuous HID enumeration.
            Interlocked.Exchange(ref _nextN3ReconnectUtcTicks, 0);
            if (_n3InitialProbeComplete && !_isN3Connected)
                Dispatcher.BeginInvoke(TryReconnectN3FromRefreshTick);
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
                onInstallUpdate: InstallAvailableUpdateFromTray,
                mixer: _mixer,
                config: _config,
                onSave: cfg => { ConfigManager.Save(cfg); _mainWindow?.RefreshViews(); },
                onRefresh: () => _mainWindow?.RefreshViews()
            );
            if (_availableUpdate != null)
                _trayMixerPopup.ShowUpdateAvailable(_availableUpdate.Tag);
            UpdateAggregateTrayStatus();
            _trayMixerPopup.ShowPopup();
        });
    }

    private async void InstallAvailableUpdateFromTray()
    {
        var update = _availableUpdate;
        if (update == null || _mainWindow == null) return;

        ShowMainWindow();
        await _mainWindow.PromptToInstallUpdateAsync(update);
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
        _isShuttingDown = true;

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
        _audioDeviceRefreshTimer?.Dispose();
        try { _resumeRecoveryCts?.Cancel(); _resumeRecoveryCts?.Dispose(); } catch { }
        foreach (var timer in _osdFinalTimers)
            timer?.Dispose();
        _streamControllerRefreshTimer?.Stop();
        _hardwareMonitor?.Dispose();
        _signalRgbBridge?.Dispose();
        _duckingEngine?.Dispose();
        Dispatcher.Invoke(() => Shutdown());
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isShuttingDown) return;

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
    // Cached 32x32 base bitmaps (color + grayscale) — the source asset is an
    // embedded resource that never changes at runtime, so decode/scale/grayscale
    // happen exactly once instead of on every master-volume notification.
    private Bitmap? _trayBaseBitmap;
    private Bitmap? _trayBaseBitmapGray;
    // Last-rendered overlay text + connection variant — identical renders are skipped.
    private string? _lastTrayIconText;
    private bool _lastTrayIconConnected;
    private bool _trayIconRendered;
    private long _lastTrayIconRenderTick;
    private System.Windows.Threading.DispatcherTimer? _trayIconTrailingTimer;
    private const int TrayIconUpdateThrottleMs = 100;

    private Bitmap GetTrayBaseBitmap(bool connected)
    {
        if (_trayBaseBitmap == null)
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
            _trayBaseBitmap = bmp;
        }

        if (connected) return _trayBaseBitmap;

        if (_trayBaseBitmapGray == null)
        {
            var gray32 = (Bitmap)_trayBaseBitmap.Clone();
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var px = gray32.GetPixel(x, y);
                int gray = (int)(px.R * 0.3 + px.G * 0.59 + px.B * 0.11);
                gray32.SetPixel(x, y, Color.FromArgb(px.A, gray, gray, gray));
            }
            _trayBaseBitmapGray = gray32;
        }

        return _trayBaseBitmapGray;
    }

    private Icon CreateTrayIcon(bool connected)
    {
        string volText = _trayMuted ? "M" : $"{(int)Math.Round(_trayVolume * 100)}";

        // Working copy of the cached base — text overlay is drawn per render.
        var bmp = (Bitmap)GetTrayBaseBitmap(connected).Clone();

        // Draw volume % or "M" (muted) in bottom-right corner
        try
        {
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

        // Record what's now showing so volume notifications can skip
        // re-renders that would produce a pixel-identical icon.
        _lastTrayIconText = volText;
        _lastTrayIconConnected = connected;
        _trayIconRendered = true;
        return result;
    }

    /// <summary>
    /// Updates tray icon with current volume/mute state. Called from volume notifications.
    /// Throttled to ~100ms (last-value-wins, with a trailing render so the final
    /// value always lands) and skipped entirely when the rendered text + connection
    /// variant are unchanged.
    /// </summary>
    private void UpdateTrayIconVolume(float volume, bool muted)
    {
        _trayVolume = volume;
        _trayMuted = muted;
        if (_trayIcon == null) return;
        Dispatcher.BeginInvoke(ScheduleTrayIconVolumeRender);
    }

    // UI thread only.
    private void ScheduleTrayIconVolumeRender()
    {
        try
        {
            long elapsed = Environment.TickCount64 - _lastTrayIconRenderTick;
            if (elapsed >= TrayIconUpdateThrottleMs)
            {
                RenderTrayIconVolume();
                return;
            }

            // Inside the throttle window — arm a trailing render that picks up
            // the latest _trayVolume/_trayMuted so the icon never sticks on a
            // stale percent.
            if (_trayIconTrailingTimer == null)
            {
                _trayIconTrailingTimer = new System.Windows.Threading.DispatcherTimer();
                _trayIconTrailingTimer.Tick += (_, _) =>
                {
                    _trayIconTrailingTimer?.Stop();
                    RenderTrayIconVolume();
                };
            }
            _trayIconTrailingTimer.Stop();
            _trayIconTrailingTimer.Interval = TimeSpan.FromMilliseconds(
                Math.Max(1, TrayIconUpdateThrottleMs - elapsed));
            _trayIconTrailingTimer.Start();
        }
        catch { }
    }

    // UI thread only.
    private void RenderTrayIconVolume()
    {
        try
        {
            if (_trayIcon == null) return;
            bool connected = _isConnected || _isN3Connected;
            string volText = _trayMuted ? "M" : $"{(int)Math.Round(_trayVolume * 100)}";
            if (_trayIconRendered && volText == _lastTrayIconText && connected == _lastTrayIconConnected)
                return; // identical icon already showing — skip the GDI rebuild

            _lastTrayIconRenderTick = Environment.TickCount64;
            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = CreateTrayIcon(connected);
            oldIcon?.Dispose();
        }
        catch { }
    }

    private void OnConfigChanged(AppConfig config)
    {
        _config = config;
        ConfigureTurnUpSerialForHardwareMode();
        ConfigManager.Save(_config);
        ConfigManager.SaveProfile(_config, _config.ActiveProfile);
        ApplyRgbConfig();
        RefreshTurnUpRgbOutput();
        UpdateAudioAnalyzer();
        ApplyStartupSetting();
        if (_ha != null)
        {
            _ha.UpdateConfig(_config.HomeAssistant);
            if (_config.HomeAssistant.Enabled)
            {
                // Only fire the live HTTP test when the HA settings actually
                // changed — or when the last test failed, so a save can still
                // recover a connection (matches the old retry-on-save behavior).
                string haKey = $"{_config.HomeAssistant.Url}|{_config.HomeAssistant.Token}";
                if (haKey != _lastHaTestKey || !_ha.IsAvailable)
                {
                    _lastHaTestKey = haKey;
                    _ = _ha.TestConnectionAsync();
                }
            }
            else
            {
                _lastHaTestKey = null; // re-test on next enable
            }
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
        ConfigureAutoSwitchTimer();
        ConfigureGameModeTimer();
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
        _signalRgbBridge?.UpdateConfig(_config.SignalRgb);
        if (!_config.SignalRgb.Enabled)
            ClearSignalRgbOverride();
        ConfigureMutePollingTimer();
        if (_n3 != null && _isN3Connected)
        {
            // Gate the brightness write + full display resync on the N3
            // subsection (or HardwareMode) actually changing — every 300ms
            // debounced save from ANY view used to re-render all six LCDs.
            string n3Signature = ComputeN3ConfigSignature();
            if (n3Signature != _lastN3ConfigSignature)
            {
                _lastN3ConfigSignature = n3Signature;
                _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                // Brightness changes are device-level (no pixel change), but
                // force a full repaint after one so the panel state is known-good.
                // Content-only edits rely on the per-slot signature diff instead.
                if (_config.N3.DisplayBrightness != _lastN3AppliedBrightness)
                {
                    _lastN3AppliedBrightness = _config.N3.DisplayBrightness;
                    ResetN3SlotSignatureCache();
                }
                SyncStreamControllerDisplays();
            }
        }
    }

    private void ConfigureTurnUpSerialForHardwareMode()
    {
        if (_serial == null) return;
        if (_config.HardwareMode == HardwareMode.StreamControllerOnly)
            _serial.Stop();
        else
            _serial.Start();
    }

    /// <summary>
    /// Cheap change-detection signature for the N3-relevant config (keys,
    /// buttons, folders, paging, brightness) plus HardwareMode, which gates
    /// whether displays render at all.
    /// </summary>
    private string ComputeN3ConfigSignature()
    {
        try
        {
            return _config.HardwareMode + "|" + Newtonsoft.Json.JsonConvert.SerializeObject(_config.N3);
        }
        catch
        {
            // Serialization failure — return a unique value so the resync
            // still happens rather than silently skipping.
            return Guid.NewGuid().ToString();
        }
    }

    private void ClearSignalRgbOverride()
    {
        if (_config.Ambience.SyncRoomToTurnUp) return;
        if (_config.Ambience.ScreenSync.Enabled && _config.Ambience.ScreenSync.SyncToTurnUp) return;
        _rgb.SetScreenSyncColors(null);
    }

    private static bool[]? BuildSignalRgbLedMask(SignalRgbConfig config)
    {
        if (config.IgnoredLedIndexes.Count == 0) return null;

        var mask = Enumerable.Repeat(true, 15).ToArray();
        foreach (int ledIndex in config.IgnoredLedIndexes)
            if (ledIndex is >= 0 and < 15)
                mask[ledIndex] = false;

        return mask;
    }

    // Takes a description FACTORY instead of a pre-built string — the
    // interpolated source text is only materialized in the sampled-log /
    // error / slow-handler branches, not on every knob tick.
    private void QueueTurnUpHardwareInput(Func<string> describeSource, Action action)
    {
        RecordHardwareInput(describeSource);
        _turnUpInputPump?.Queue(describeSource, action);
    }

    private void QueueLatestTurnUpInput(int key, Func<string> describeSource, Action action)
    {
        RecordHardwareInput(describeSource);
        _turnUpInputPump?.QueueLatest(key, describeSource, action);
    }

    private void QueueN3HardwareInput(Func<string> describeSource, Action action)
    {
        RecordHardwareInput(describeSource);
        _n3InputPump?.Queue(describeSource, action);
    }

    private void RecordHardwareInput(Func<string> describeSource)
    {
        long now = Environment.TickCount64;
        Interlocked.Exchange(ref _lastHardwareActivityTick, now);

        long lastLog = Interlocked.Read(ref _lastHardwareInputLogTick);
        if (now - lastLog >= HardwareInputSampleLogMs
            && Interlocked.CompareExchange(ref _lastHardwareInputLogTick, now, lastLog) == lastLog)
        {
            Logger.Log($"Hardware input received: {describeSource()}");
        }
    }

    private void QueueAudioDeviceRefresh()
    {
        if (_isShuttingDown) return;
        try
        {
            // Bluetooth endpoints commonly raise several add/state/default
            // notifications in one burst. Refresh once after they settle.
            _audioDeviceRefreshTimer?.Change(500, System.Threading.Timeout.Infinite);
        }
        catch (ObjectDisposedException) { }
    }

    private void RefreshAudioDevicesAfterChange()
    {
        if (_isShuttingDown) return;

        try { _mixer?.RefreshNow(); }
        catch (Exception ex) { Logger.Log($"Audio device session refresh failed: {ex.Message}"); }

        Dispatcher.BeginInvoke(() =>
        {
            if (_isShuttingDown) return;
            try
            {
                _mainWindow?.RefreshAudioDeviceViews();

                using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                using var current = enumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia);
                Logger.Log($"Audio devices changed — default output is {current.FriendlyName}");
                // Update device-aware lighting immediately. Leave
                // _lastDefaultOutputDeviceId untouched so PollMuteStates sees
                // the change and re-subscribes master mute/volume callbacks.
                _rgb.SetDefaultOutputDevice(current.ID);
            }
            catch (Exception ex)
            {
                Logger.Log($"Audio device UI refresh failed: {ex.Message}");
            }
        });
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

            // Activity flash — real knob turns only, not startup/connect batches
            if (!e.IsBatch)
                _rgb.NotifyKnobActivity(e.Idx);
        }

        var knob = _config.Knobs.FirstOrDefault(k => k.Idx == e.Idx);
        if (knob != null)
        {
            // Persist/report position, but never treat a startup/connect batch as
            // an intentional control change. That keeps fresh app launch from
            // moving Windows master/app volumes to the physical knob position.
            knob.LastRawValue = e.Value;
            if (e.IsBatch)
            {
                _lastOsdValue[e.Idx] = e.Value;
            }
            else if (knob.Target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase))
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
                if (Environment.TickCount64 - _startupTick >= 8000
                    && !IsResumeSettling
                    && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
                {
                    float norm = e.Value / 1023f;
                    int pctRoom = (int)Math.Round(norm * 100);
                    ApplyRoomLightsBrightness(norm, pctRoom);
                }
            }
            else if (knob.Target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
            {
                // Device Group — unified brightness control for grouped devices
                if (Environment.TickCount64 - _startupTick >= 8000
                    && !IsResumeSettling
                    && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
                {
                    var groupName = knob.Target.Substring(6);
                    var group = _config.Groups.FirstOrDefault(g => g.Name == groupName);
                    if (group != null)
                    {
                        float norm = e.Value / 1023f;
                        int pct = (int)Math.Round(norm * 100);
                        ApplyDeviceGroupBrightness(group, norm, pct, e.Idx);
                    }
                }
            }
            else if (knob.Target.Equals("govee", StringComparison.OrdinalIgnoreCase))
            {
                // Skip during startup restore to avoid turning on Govee devices on app launch
                if (Environment.TickCount64 - _startupTick >= 8000 && !IsResumeSettling)
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
                if (Environment.TickCount64 - _startupTick >= 8000 && !IsResumeSettling)
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
                _mixer.SetVolume(knob, e.Value, e.Idx);
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

        if (isBatch)
        {
            _lastOsdValue[stateIdx] = rawValue;
        }
        else if (knob.Target.StartsWith("ha_", StringComparison.OrdinalIgnoreCase))
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
                && !IsResumeSettling
                && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
            {
                float norm = rawValue / 1023f;
                int pctRoom = (int)Math.Round(norm * 100);
                ApplyRoomLightsBrightness(norm, pctRoom);
            }
        }
        else if (knob.Target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000
                && !IsResumeSettling
                && (DateTime.UtcNow - _connectedAt).TotalMilliseconds >= 2000)
            {
                var groupName = knob.Target.Substring(6);
                var group = _config.Groups.FirstOrDefault(g => g.Name == groupName);
                if (group != null)
                {
                    float norm = rawValue / 1023f;
                    int pct = (int)Math.Round(norm * 100);
                    ApplyDeviceGroupBrightness(group, norm, pct, stateIdx);
                }
            }
        }
        else if (knob.Target.Equals("govee", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000 && !IsResumeSettling)
            {
                float norm = rawValue / 1023f;
                _ambienceSync?.EnsureDevicesPoweredOn();
                _ambienceSync?.SetBrightness(norm);
                Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(null, norm, true));
            }
        }
        else if (knob.Target.StartsWith("govee:", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startupTick >= 8000 && !IsResumeSettling)
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
            _mixer.SetVolume(knob, rawValue, stateIdx);
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
        // active_window always resolves to the actual foreground app at OSD-fire time.
        // Skips the knob.Label fallback because the Mixer's save path auto-bakes
        // "Active Window" into Label, which would otherwise hide the live app name.
        string label = knob.Target == "active_window"
            ? ResolveActiveWindowOsdLabel()
            : !string.IsNullOrEmpty(knob.Label) ? knob.Label : knob.Target switch
        {
            "master" => "Master",
            "mic" => "Microphone",
            "active_window" => ResolveActiveWindowOsdLabel(),
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

    /// <summary>OSD label for the active_window target — resolves to the app actually
    /// being controlled (e.g. "Spotify") instead of the generic "Active Window".
    /// Falls back to "Active Window" when no session can be resolved.</summary>
    private string ResolveActiveWindowOsdLabel()
    {
        var app = _mixer?.GetActiveWindowDisplayName();
        return string.IsNullOrWhiteSpace(app) ? "Active Window" : app!;
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

        if (e.IsDown && e.Idx >= 0 && e.Idx < 5)
            _rgb?.NotifyKnobActivity(e.Idx); // activity flash on button press

        if (e.IsDown)
            _buttons.HandleDown(e.Idx, _config);
        else
            _buttons.HandleUp(e.Idx, _config);
    }

    private void HandleN3Input(N3InputEvent e)
    {
        if (_config.HardwareMode == HardwareMode.TurnUpOnly) return;
        if (!_config.N3.Enabled) return;

        if (_n3AsleepFromIdle && _n3 != null && _isN3Connected)
        {
            try
            {
                Logger.Log("N3 idle: waking from hardware input");
                _forceN3Sleep = false;
                _n3.Wake();
                _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                _n3AsleepFromIdle = false;
                ResetN3SlotSignatureCache();
                SyncStreamControllerDisplays();
            }
            catch (Exception ex)
            {
                Logger.Log($"N3 hardware-input wake failed: {ex.Message}");
            }
        }

        switch (e.Kind)
        {
            case N3InputKind.EncoderTwist:
                HandleN3EncoderTwist(e);
                break;

            case N3InputKind.DisplayKey:
                // When inside a folder with the auto-Back key shown, LCD slot 0 is
                // the virtual Back nav and slots 1-5 shift to folder keys 0-4. If
                // Back is disabled (or we're on a page > 0), all 6 slots map
                // directly — must mirror the display-render side's gating in
                // SyncStreamControllerDisplays so input + visuals agree.
                bool backShown = IsInFolder
                                 && (GetActiveFolder()?.BackKeyEnabled ?? true)
                                 && _config.N3.CurrentPage == 0;

                if (backShown)
                {
                    if (e.Index == 0)
                    {
                        // Only react on release to match Stream Deck folder UX.
                        if (e.IsPressed == false)
                            NavigateToN3Folder("");
                        break;
                    }

                    int folderLocalIdx = N3DisplayKeyBase + (_config.N3.CurrentPage * 6) + (e.Index - 1);
                    PreresolveLcdButton(folderLocalIdx);
                    HandleN3VirtualButton(folderLocalIdx, e.IsPressed == true);
                    break;
                }

                int pagedIdx = N3DisplayKeyBase + (_config.N3.CurrentPage * 6) + e.Index;
                PreresolveLcdButton(pagedIdx);
                HandleN3VirtualButton(pagedIdx, e.IsPressed == true);
                break;

            case N3InputKind.SideButton:
                // Drop any stale LCD pre-resolution at this idx so the gesture
                // engine's timers for side-button presses resolve globally.
                _n3ButtonOverride.Remove(N3SideButtonBase + e.Index);
                HandleN3VirtualButton(N3SideButtonBase + e.Index, e.IsPressed == true);
                break;

            case N3InputKind.EncoderPress:
                _n3ButtonOverride.Remove(N3EncoderPressBase + e.Index);
                HandleN3VirtualButton(N3EncoderPressBase + e.Index, e.IsPressed == true);
                break;
        }
    }

    private void HandleN3EncoderTwist(N3InputEvent e)
    {
        if (!_config.N3.MirrorFirstThreeKnobs) return;
        if (e.Index < 0 || e.Index > 2) return;

        // Wheel nav takes priority — any encoder twist while the radial
        // wheel is open steps the highlight by sign(delta), no volume.
        if (_wheelVisible && _radialWheel != null)
        {
            int direction = Math.Sign(e.Delta);
            if (direction != 0)
            {
                int totalSlots = _radialWheel.GetTotalSlots();
                int nextSlot = ((_radialWheel.GetSelectedIndex() + direction) % totalSlots + totalSlots) % totalSlots;
                Dispatcher.BeginInvoke(() => _radialWheel?.Highlight(nextSlot));
            }
            return;
        }

        var knob = _config.N3.Knobs.FirstOrDefault(k => k.Idx == e.Index);
        if (knob == null) return;

        // Stream Controller navigation targets — twist the encoder to
        // cycle Spaces or pages. Discrete: one detent = one step, sign
        // of the delta gives the direction.
        if (knob.Target == "sc_space_cycle")
        {
            CycleN3Space(Math.Sign(e.Delta));
            return;
        }
        if (knob.Target == "sc_page_cycle")
        {
            CycleN3Page(Math.Sign(e.Delta));
            return;
        }

        int current = knob.LastRawValue >= 0
            ? knob.LastRawValue
            : (int)Math.Round(StreamControllerKnobPositions[e.Index] * 1023f);

        // Per-encoder step (digital wheel). Falls back to the legacy global EncoderStep
        // if the per-encoder field is somehow zero/negative (e.g. malformed config).
        int step = knob.EncoderStep > 0 ? knob.EncoderStep : _config.N3.EncoderStep;
        step = Math.Clamp(step, 1, 128);
        int next = Math.Clamp(current + (e.Delta * step), 0, 1023);
        StreamControllerKnobPositions[e.Index] = next / 1023f;
        ApplyKnobConfig(knob, next, N3KnobStateBase + e.Index, false);
    }

    /// <summary>
    /// Step the active Space forward (+1) or back (-1). Order is:
    /// Home → Folders[0] → Folders[1] → … → Home (wraps).
    /// </summary>
    private void CycleN3Space(int direction)
    {
        if (direction == 0 || _config == null) return;

        // Build the ordered space list: Home sentinel + each folder.
        var spaces = new List<string> { "" };
        foreach (var f in _config.N3.Folders)
            if (!string.IsNullOrEmpty(f.Name))
                spaces.Add(f.Name);

        if (spaces.Count <= 1) return; // no folders to cycle through

        int currentIdx = spaces.IndexOf(_currentN3Folder ?? "");
        if (currentIdx < 0) currentIdx = 0;
        int nextIdx = ((currentIdx + direction) % spaces.Count + spaces.Count) % spaces.Count;

        NavigateToN3Folder(spaces[nextIdx]);
    }

    /// <summary>
    /// Step the current page forward (+1) or back (-1) within the active
    /// Space. Wraps around at the ends.
    /// </summary>
    private void CycleN3Page(int direction)
    {
        if (direction == 0 || _config == null) return;

        int pageCount = GetActivePageCount();
        if (pageCount <= 1) return;

        int current = _config.N3.CurrentPage;
        int next = ((current + direction) % pageCount + pageCount) % pageCount;
        if (next == current) return;

        // Reuse HandleScPageChange so the page state + UI sync follow the
        // same path as the button-driven sc_page_next / sc_page_prev flow.
        HandleScPageChange(direction, absolute: false);
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

            // Seed position effects from saved hardware positions until the device
            // reports fresh positions.
            for (int i = 0; i < 5; i++)
            {
                var knob = _config.Knobs.FirstOrDefault(k => k.Idx == i);
                float pos = knob?.LastRawValue >= 0 ? knob.LastRawValue / 1023f : 1f;
                _rgb.SetKnobPosition(i, pos);
            }

            ApplyRgbConfig();
            RefreshTurnUpRgbOutput("serial connected");
            UpdateAudioAnalyzer();
        }
        else
        {
            // Stop the 20 FPS effect timer while no Turn Up output exists.
            _rgb.SetOutput(null, null);
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

    private void HandleN3ConnectionChanged(bool connected, string? deviceName)
    {
        _isN3Connected = connected;
        _n3DeviceName = connected ? deviceName : null;

        if (connected)
        {
            _n3EverConnected = true;
            Interlocked.Exchange(ref _nextN3ReconnectUtcTicks, 0);
        }

        if (!connected)
        {
            Interlocked.Exchange(
                ref _nextN3ReconnectUtcTicks,
                DateTime.UtcNow.Add(GetN3ReconnectInterval()).Ticks);
            _n3AnimatedKeys.Clear();
            RebuildAnimatedN3Snapshot();
            _n3AsleepFromIdle = false;
            // Device-side LCD state is gone — make sure the next sync after a
            // reconnect repaints every slot instead of trusting stale signatures.
            ResetN3SlotSignatureCache();
            Logger.Log($"N3: disconnected{(string.IsNullOrWhiteSpace(deviceName) ? "" : $" ({deviceName})")}");
        }

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _mainWindow?.SetN3ConnectionStatus(connected, connected ? deviceName : null);
                UpdateAggregateTrayStatus();
            }
            catch (Exception ex)
            {
                Logger.Log($"N3 connection UI update error: {ex.Message}");
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
        if (IsResumeSettling) return;
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
                        using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
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
                using var fgProc = System.Diagnostics.Process.GetProcessById((int)fgPid);
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
        var signalRgb = _config.SignalRgb;
        var discordRpc = _config.DiscordRpc;
        var spotify = _config.Spotify;
        var voiceMeeter = _config.VoiceMeeter;
        var corsair = _config.Corsair;

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
        _config.SignalRgb = signalRgb;
        _config.DiscordRpc = discordRpc;
        _config.Spotify = spotify;
        _config.VoiceMeeter = voiceMeeter;
        _config.Corsair = corsair;
        ConfigManager.Save(_config);
        ApplyRgbConfig();
        ApplySignalRgbProfileSync(profileName);
        UpdateAudioAnalyzer();
        if (_n3 != null && _isN3Connected)
        {
            _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
            ResetN3SlotSignatureCache();
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
    /// <summary>
    /// Runs post-show at ApplicationIdle priority so the main window has
    /// rendered before we touch slow hardware-probing work. Handles
    /// Corsair iCUE connect, LG monitor HID open, N3 HID open + initial
    /// display sync, and Screen Sync capture start. Each step runs on a
    /// background task where possible; UI-thread-only work (LCD display
    /// render) dispatches back for just that step.
    /// </summary>
    private void InitializeHardwareDeferred()
    {
        // Corsair — SDK init can stall for a few hundred ms if iCUE is
        // sleeping. Run it on a background thread so we don't block.
        if (_config.Corsair.Enabled)
        {
            _ = Task.Run(() =>
            {
                try { _corsairSync?.Start(); }
                catch (Exception ex) { Logger.Log($"CorsairSync start failed: {ex.Message}"); }
            });
        }

        // LG UltraGear monitor — HID enumeration on a background thread.
        _ = Task.Run(() =>
        {
            try
            {
                if (_lgMonitor != null && _lgMonitor.TryConnect())
                    Logger.Log($"LG Monitor: {_lgMonitor.DeviceName} — {_lgMonitor.LedCountValue} LEDs");
            }
            catch (Exception ex) { Logger.Log($"LgMonitor TryConnect failed: {ex.Message}"); }
        });

        // N3 stream controller — HID enumeration + device init. After a
        // successful connect, dispatch the initial display sync back to
        // the UI thread (SyncStreamControllerDisplays already moves the
        // JPEG encode + HID writes to a Task internally).
        _ = Task.Run(() =>
        {
            try
            {
                if (!_config.N3.Enabled || _config.HardwareMode == HardwareMode.TurnUpOnly)
                    return;

                bool ok = _n3 != null && _n3.TryConnect();
                if (!ok)
                {
                    Interlocked.Exchange(
                        ref _nextN3ReconnectUtcTicks,
                        DateTime.UtcNow.Add(GetN3ReconnectInterval()).Ticks);
                    return;
                }

                _isN3Connected = true;
                _n3DeviceName = _n3!.DeviceName;
                Logger.Log("N3: native HID bring-up active");
                _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                ResetN3SlotSignatureCache();

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        _mainWindow?.SetN3ConnectionStatus(true, _n3DeviceName);
                        UpdateAggregateTrayStatus();
                        SyncStreamControllerDisplays();
                    }
                    catch (Exception ex) { Logger.Log($"N3 post-connect UI update failed: {ex.Message}"); }
                });
            }
            catch (Exception ex) { Logger.Log($"N3 TryConnect failed: {ex.Message}"); }
            finally { _n3InitialProbeComplete = true; }
        });

        // Screen Sync — defer capture-thread kickoff (it grabs monitor
        // buffers on first frame which can briefly block).
        if (_config.Ambience.ScreenSync.Enabled)
        {
            _ = Task.Run(() =>
            {
                try { _dreamSync?.Start(); }
                catch (Exception ex) { Logger.Log($"DreamSync start failed: {ex.Message}"); }
            });
        }
    }

    private void StartStreamControllerRefreshTimer()
    {
        if (_streamControllerRefreshTimer != null) return;

        _streamControllerRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            // 1s interval so idle-sleep responds within a second of the
            // threshold being crossed (was 5s — made short "5s"-style
            // settings feel like they took up to 10s to trigger). Tick
            // body short-circuits when nothing needs doing.
            Interval = TimeSpan.FromMilliseconds(StreamControllerRefreshIntervalMs),
        };
        _streamControllerRefreshTimer.Tick += (_, _) => OnStreamControllerRefreshTick();
        _streamControllerRefreshTimer.Start();
    }

    private void UpdateStreamControllerRefreshCadence()
    {
        if (_streamControllerRefreshTimer == null) return;
        int interval = _n3AnimatedKeys.Count > 0
            ? StreamControllerAnimatedRefreshIntervalMs
            : StreamControllerRefreshIntervalMs;
        if ((int)_streamControllerRefreshTimer.Interval.TotalMilliseconds != interval)
            _streamControllerRefreshTimer.Interval = TimeSpan.FromMilliseconds(interval);
    }

    // True once the N3 brightness was dropped to 0 by the idle-sleep code.
    // Used so we only restore brightness on wake, not on every tick.
    private bool _n3AsleepFromIdle;

    // One-shot — forces the next refresh tick to put the N3 to sleep even
    // if the idle threshold hasn't been crossed. Wired to the Settings
    // "Sleep Now" button; consumed on the first tick that detects input.
    private bool _forceN3Sleep;

    /// <summary>Immediately blank the N3 LCDs. Wakes on the next mouse, keyboard, or N3 input.</summary>
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
            if (!_isN3Connected && !_forceN3Sleep)
            {
                TryReconnectN3FromRefreshTick();
                return;
            }

            // ── N3 idle sleep ─────────────────────────────────────────────
            // Uses the real firmware standby command (CRT HAN) via N3Controller.Sleep —
            // actually powers the LCDs down, not just dims to brightness 0.
            // Wake re-inits the device and resyncs display frames.
            if (_n3 != null && _isN3Connected)
            {
                int thresholdSec = Math.Max(0, _config.N3.IdleSleepSeconds);
                uint osIdleMs = NativeMethods.GetIdleMilliseconds();
                long hardwareIdleMs = Math.Max(0, Environment.TickCount64 - Interlocked.Read(ref _lastHardwareActivityTick));
                long effectiveIdleMs = Math.Min(osIdleMs, hardwareIdleMs);
                bool idleTriggered = thresholdSec > 0 && effectiveIdleMs >= (long)thresholdSec * 1000L;
                bool shouldSleep = _forceN3Sleep || idleTriggered;

                // Only log state transitions — the raw every-tick output
                // was 1 line/sec, 24/7.
                if (shouldSleep != _n3AsleepFromIdle)
                {
                    Logger.Log(
                        $"N3 idle: {(shouldSleep ? "sleeping" : "waking")} " +
                        $"(idleMs={effectiveIdleMs}, osIdleMs={osIdleMs}, hardwareIdleMs={hardwareIdleMs}, threshold={thresholdSec}s, forced={_forceN3Sleep})");
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
                    ResetN3SlotSignatureCache();
                    SyncStreamControllerDisplays();
                    _n3AsleepFromIdle = false;
                }

                // A forced sleep is consumed once input arrives, so the next
                // keypress wakes the screens just like a timeout-sleep would.
                if (_forceN3Sleep && effectiveIdleMs < 500) _forceN3Sleep = false;
            }

            if (_config.N3?.DisplayKeys == null) return;

            // While the N3 is asleep, never re-sync LCD frames — each HID
            // write would visually wake the screens and the whole point
            // of sleep would be defeated. Resume refresh on wake.
            if (_n3AsleepFromIdle) return;

            bool hasDynamic = false;
            bool hasClock = false;
            bool hasHardwareMetric = false;
            var activeKeys = GetActiveDisplayKeys();
            foreach (var k in activeKeys)
            {
                if (k.DisplayType == DisplayKeyType.Clock) hasClock = true;
                else if (k.DisplayType == DisplayKeyType.DynamicState) hasDynamic = true;
                else if (k.DisplayType == DisplayKeyType.HardwareMonitor) hasHardwareMetric = true;
                if (hasClock && hasDynamic && hasHardwareMetric) break;
            }

            bool hasAnimation = _n3AnimatedKeys.Count > 0;
            if (!hasClock && !hasDynamic && !hasHardwareMetric && !hasAnimation) return;

            // Throttle the clock/dynamic refresh. The refresh timer ticks
            // at 1s (for idle-sleep responsiveness) but redrawing all six
            // LCDs every second is wasteful — clocks only change at minute
            // boundaries and dynamic state updates every few seconds is
            // plenty. 3s cadence cuts HID traffic and JPEG encode load.
            bool dynamicRefreshDue = (hasClock || hasDynamic)
                && (DateTime.Now - _lastDynamicStateTick).TotalMilliseconds >= StreamControllerDynamicRefreshMs;
            bool hardwareRefreshDue = hasHardwareMetric
                && (DateTime.Now - _lastHardwareMetricTick).TotalMilliseconds >= StreamControllerHardwareRefreshMs;

            if (dynamicRefreshDue || hardwareRefreshDue)
            {
                // Keep OBS state fresh so obs_recording / obs_streaming reflect reality.
                if (dynamicRefreshDue && hasDynamic && _obs != null && _obs.IsAvailable)
                    _ = _obs.RefreshStatusAsync();

                SyncStreamControllerDisplays();
                var now = DateTime.Now;
                if (dynamicRefreshDue) _lastDynamicStateTick = now;
                if (hardwareRefreshDue) _lastHardwareMetricTick = now;
                return;
            }

            if (hasAnimation)
            {
                TrySyncAnimatedN3Displays();
            }
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

    private void TryReconnectN3FromRefreshTick()
    {
        if (_n3 == null || _config == null) return;
        if (!_n3InitialProbeComplete) return;
        if (!_config.N3.Enabled || _config.HardwareMode == HardwareMode.TurnUpOnly) return;

        var now = DateTime.UtcNow;
        if (now.Ticks < Interlocked.Read(ref _nextN3ReconnectUtcTicks)) return;
        if (Interlocked.Exchange(ref _n3ReconnectInFlight, 1) != 0) return;
        Interlocked.Exchange(
            ref _nextN3ReconnectUtcTicks,
            now.Add(GetN3ReconnectInterval()).Ticks);

        _ = Task.Run(() =>
        {
            try
            {
                if (_n3EverConnected)
                    Logger.Log("N3: reconnect attempt after disconnected state");
                if (!_n3.TryConnect(logIfMissing: false)) return;

                _isN3Connected = true;
                _n3DeviceName = _n3.DeviceName;
                _n3AsleepFromIdle = false;
                _forceN3Sleep = false;
                _n3.SetBrightness((byte)Math.Clamp(_config.N3.DisplayBrightness, 0, 100));
                ResetN3SlotSignatureCache();

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        _mainWindow?.SetN3ConnectionStatus(true, _n3DeviceName);
                        UpdateAggregateTrayStatus();
                        SyncStreamControllerDisplays();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"N3 reconnect UI update failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"N3 reconnect attempt failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _n3ReconnectInFlight, 0);
            }
        });
    }

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

    private ButtonFolderConfig? GetActiveFolder()
    {
        if (_config == null || string.IsNullOrEmpty(_currentN3Folder)) return null;
        return _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
    }

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
        ResetN3SlotSignatureCache();

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
    // Idx-collision workaround: LCD keys on page 1 of a folder produce idx
    // 106-111, which also belongs to the physical side buttons + encoder
    // presses. Dispatch knows which kind fired; the gesture engine's async
    // timers don't. So the dispatcher pre-resolves the ButtonConfig for LCD
    // presses and stashes it here — the resolver uses the stash when
    // present, side/encoder paths clear it before firing so they fall
    // through to the global root bindings.
    private readonly Dictionary<int, ButtonConfig> _n3ButtonOverride = new();

    private void PreresolveLcdButton(int idx)
    {
        if (_config == null) return;
        ButtonConfig? btn;
        if (IsInFolder)
        {
            var folder = _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
            btn = folder?.Buttons.FirstOrDefault(b => b.Idx == idx);
        }
        else
        {
            btn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == idx);
        }
        if (btn != null) _n3ButtonOverride[idx] = btn;
        else _n3ButtonOverride.Remove(idx);
    }

    private ButtonConfig? ResolveN3ButtonForGestureEngine(int idx)
    {
        // Prefer the dispatcher's pre-resolved config — it knew the input kind.
        // Cleared/overwritten on every new press at this idx so stale entries
        // don't leak into a side/encoder press that follows.
        if (_n3ButtonOverride.TryGetValue(idx, out var pre))
            return pre;

        // Side buttons and encoder presses use high, non-paged IDs so LCD
        // keys on later pages cannot collide with their root bindings.
        if (idx >= N3SideButtonBase && idx <= N3EncoderPressBase + 2)
            return _config?.N3.Buttons.FirstOrDefault(b => b.Idx == idx);

        if (!IsInFolder) return null; // fall through to default resolver

        // Only N3 idx ranges get folder-scoped resolution. Turn Up buttons (0-4)
        // always use the root _config.Buttons list.
        if (idx < N3DisplayKeyBase) return null;

        var folder = _config.N3.Folders.FirstOrDefault(f => f.Name == _currentN3Folder);
        var btn = folder?.Buttons.FirstOrDefault(b => b.Idx == idx);
        if (btn != null) return btn;

        // Hard-stop the fallback: inside a Space, an unbound LCD key must be
        // a no-op — never inherit Home's binding. Returning null here would
        // let the default resolver walk into _config.N3.Buttons and fire the
        // Home-level action for this idx.
        return new ButtonConfig { Idx = idx, Action = "none" };
    }

    private void SyncStreamControllerDisplays()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(SyncStreamControllerDisplays);
            return;
        }

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
        var activeButtons = GetActiveN3Buttons();

        // Respect the folder's BackKeyEnabled flag — when disabled, skip the
        // virtual Back render on page 0 slot 0 and fall through to normal
        // key mapping (slot i -> idx i on page 0).
        bool showBackKey = inFolder && (GetActiveFolder()?.BackKeyEnabled ?? true)
                           && _config.N3.CurrentPage == 0;

        var spotifySpanMasters = new Dictionary<int, StreamControllerDisplayKeyConfig>();
        if (!showBackKey)
        {
            foreach (var candidate in activeKeys
                         .Where(StreamControllerDisplayRenderer.IsSpotifyAlbumArtSpanned)
                         .OrderBy(k => k.Idx))
            {
                foreach (int coveredSlot in StreamControllerDisplayRenderer.GetSpotifyAlbumArtCoveredSlots(candidate))
                {
                    if (!spotifySpanMasters.ContainsKey(coveredSlot))
                        spotifySpanMasters[coveredSlot] = candidate;
                }
            }
        }

        var ops = new List<(int slot, System.Drawing.Bitmap? bitmap, byte[]? encodedFrame, bool clear)>(N3Controller.DisplayKeyCount);
        var deferredAnimations = new List<(int slot, StreamControllerDisplayKeyConfig key, string signature)>();
        var activeAnimatedSlots = new HashSet<int>();

        for (int i = 0; i < N3Controller.DisplayKeyCount; i++)
        {
            try
            {
                if (showBackKey && i == 0)
                {
                    RemoveAnimatedN3Slot(i);
                    string backSig = $"BACK|{_currentN3Folder}";
                    if (_n3LastSlotSignature[i] == backSig) continue;
                    var backKey = BuildBackKeyDisplay();
                    ops.Add((i, StreamControllerDisplayRenderer.ComposeDeviceBitmap(backKey), null, false));
                    _n3LastSlotSignature[i] = backSig;
                    continue;
                }

                int folderLocalIdx = showBackKey ? pageOffset + (i - 1) : pageOffset + i;

                if (spotifySpanMasters.TryGetValue(i, out var spotifySpanMaster))
                {
                    var overlayKey = activeKeys.FirstOrDefault(k => k.Idx == folderLocalIdx)
                                     ?? new StreamControllerDisplayKeyConfig { Idx = folderLocalIdx };
                    bool drawSpanTitle = StreamControllerDisplayRenderer.ShouldDrawSpotifySpanTitle(
                        spotifySpanMaster, activeKeys, pageOffset);
                    RemoveAnimatedN3Slot(i);
                    string spanSig = $"SPAN|{i}|{drawSpanTitle}|{BuildN3SlotContentSignature(spotifySpanMaster)}"
                                     + $"||{BuildN3SlotContentSignature(overlayKey)}";
                    if (_n3LastSlotSignature[i] == spanSig) continue;
                    ops.Add((i, StreamControllerDisplayRenderer.ComposeSpotifyAlbumArtDeviceBitmap(
                        spotifySpanMaster, overlayKey, i, drawSpanTitle), null, false));
                    _n3LastSlotSignature[i] = spanSig;
                    continue;
                }

                var key = activeKeys.FirstOrDefault(k => k.Idx == folderLocalIdx);
                if (key == null)
                {
                    RemoveAnimatedN3Slot(i);
                    if (_n3LastSlotSignature[i] == "EMPTY") continue;
                    ops.Add((i, null, null, true));
                    _n3LastSlotSignature[i] = "EMPTY";
                    continue;
                }

                if (key.DisplayType == DisplayKeyType.DynamicState
                    && string.IsNullOrWhiteSpace(key.DynamicStateSource))
                {
                    var boundButton = activeButtons.FirstOrDefault(b => b.Idx == N3DisplayKeyBase + folderLocalIdx);
                    string derived = DynamicKeyStateProvider.DeriveSourceFromAction(boundButton?.Action);
                    if (!string.IsNullOrWhiteSpace(derived))
                        key.DynamicStateSource = derived;
                }

                if (TryGetAnimatedN3State(i, key, buildIfMissing: false, out var animatedState))
                {
                    activeAnimatedSlots.Add(i);
                    // Frame advancement is owned by TrySyncAnimatedN3Displays —
                    // only (re)send the current frame here when the slot's
                    // animation changed or the cache was reset (wake/reconnect).
                    string animSig = $"ANIM|{animatedState.Signature}";
                    if (_n3LastSlotSignature[i] == animSig) continue;
                    ops.Add((i, null, animatedState.CurrentFrame, false));
                    _n3LastSlotSignature[i] = animSig;
                    continue;
                }

                string animationSignature = BuildAnimatedN3Signature(key);
                if (StreamControllerDisplayRenderer.HasDeviceAnimation(key))
                    deferredAnimations.Add((i, key, animationSignature));

                RemoveAnimatedN3Slot(i);

                bool hasImage = !string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath);
                bool hasPreset = !string.IsNullOrWhiteSpace(key.PresetIconKind);
                // Subtitle is intentionally excluded — the renderer (ComposeImage) only paints
                // Title, so a Subtitle-only key would otherwise render as a blank tinted square
                // on the device while the editor shows it as empty.
                bool hasText = !string.IsNullOrWhiteSpace(key.Title);

                bool rendersWithoutNormalContent = key.DisplayType != DisplayKeyType.Normal;

                if (!hasImage && !hasPreset && !hasText && !rendersWithoutNormalContent)
                {
                    if (_n3LastSlotSignature[i] == "EMPTY") continue;
                    ops.Add((i, null, null, true));
                    _n3LastSlotSignature[i] = "EMPTY";
                    continue;
                }

                // Content signature includes the RESOLVED dynamic content
                // (formatted clock string, hardware metric value, dynamic
                // state, Spotify track) so clocks etc. still repaint exactly
                // when their displayed content changes — and static keys stop
                // burning a WPF render + JPEG encode + HID write every tick.
                string sig = BuildN3SlotContentSignature(key);
                if (_n3LastSlotSignature[i] == sig) continue;

                ops.Add((i, StreamControllerDisplayRenderer.ComposeDeviceBitmap(key), null, false));
                _n3LastSlotSignature[i] = sig;
            }
            catch (Exception ex)
            {
                Logger.Log($"Stream Controller compose failed for slot {i}: {ex.Message}");
                RemoveAnimatedN3Slot(i);
                ops.Add((i, null, null, true));
                _n3LastSlotSignature[i] = null; // retry on next sync
            }
        }

        RemoveStaleAnimatedN3Slots(activeAnimatedSlots);

        // No slot changed — skip the encode/send task AND the display commit.
        if (ops.Count > 0)
        {
            var n3 = _n3;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _n3DisplayWriteGate.WaitAsync().ConfigureAwait(false);

                    foreach (var (slot, bitmap, encodedFrame, clear) in ops)
                    {
                        if (clear)
                        {
                            n3.ClearDisplay(slot, commit: false);
                            continue;
                        }

                        if (encodedFrame != null)
                        {
                            n3.SendDisplayImage(slot, encodedFrame, commit: false);
                            continue;
                        }

                        if (bitmap != null)
                        {
                            byte[] jpeg;
                            try { jpeg = StreamControllerDisplayRenderer.EncodeDeviceBitmap(bitmap); }
                            finally { bitmap.Dispose(); }

                            n3.SendDisplayImage(slot, jpeg, commit: false);
                        }
                    }
                    n3.CommitDisplayChanges();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Stream Controller display sync failed: {ex.Message}");
                    // Ensure bitmaps don't leak if we bail mid-loop.
                    foreach (var (_, bitmap, _, _) in ops)
                        bitmap?.Dispose();
                    // Device state is now unknown — drop the signature cache so
                    // the next sync re-sends every slot instead of skipping.
                    _ = Dispatcher.BeginInvoke(ResetN3SlotSignatureCache);
                }
                finally
                {
                    if (_n3DisplayWriteGate.CurrentCount == 0)
                        _n3DisplayWriteGate.Release();
                }
            });
        }

        if (deferredAnimations.Count > 0)
        {
            Dispatcher.BeginInvoke(
                new Action(() => BuildDeferredN3Animations(deferredAnimations)),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private bool TrySyncAnimatedN3Displays()
    {
        if (_n3 == null || !_isN3Connected || _n3AnimatedKeys.Count == 0) return false;
        if (!_n3DisplayWriteGate.Wait(0)) return false;

        try
        {
            var dueFrames = new List<(int slot, byte[] frame)>();
            var nowUtc = DateTime.UtcNow;

            foreach (var kvp in _n3AnimatedKeysSorted)
            {
                if (kvp.Value.TryAdvance(nowUtc, out var nextFrame) && nextFrame != null)
                {
                    dueFrames.Add((kvp.Key, nextFrame));
                }
            }

            if (dueFrames.Count == 0)
            {
                _n3DisplayWriteGate.Release();
                return false;
            }

            var n3 = _n3;
            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var (slot, frame) in dueFrames)
                    {
                        n3.SendDisplayImage(slot, frame, commit: false);
                    }

                    n3.CommitDisplayChanges();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Stream Controller animated frame sync failed: {ex.Message}");
                }
                finally
                {
                    _n3DisplayWriteGate.Release();
                }
            });

            return true;
        }
        catch
        {
            _n3DisplayWriteGate.Release();
            throw;
        }
    }

    private bool TryGetAnimatedN3State(int slot, StreamControllerDisplayKeyConfig key, out N3AnimatedKeyState state)
        => TryGetAnimatedN3State(slot, key, buildIfMissing: true, out state);

    private bool TryGetAnimatedN3State(int slot, StreamControllerDisplayKeyConfig key, bool buildIfMissing, out N3AnimatedKeyState state)
    {
        state = null!;
        string signature = BuildAnimatedN3Signature(key);
        if (_n3AnimatedKeys.TryGetValue(slot, out var existing) && existing.Signature == signature)
        {
            state = existing;
            return true;
        }

        if (!buildIfMissing) return false;

        var animation = StreamControllerDisplayRenderer.CreateDeviceAnimation(key);
        if (animation == null || animation.Frames.Length == 0) return false;

        state = N3AnimatedKeyState.Create(signature, animation);
        _n3AnimatedKeys[slot] = state;
        RebuildAnimatedN3Snapshot();
        UpdateStreamControllerRefreshCadence();
        return true;
    }

    private void BuildDeferredN3Animations(IReadOnlyList<(int slot, StreamControllerDisplayKeyConfig key, string signature)> animations)
    {
        if (_n3 == null || !_isN3Connected || animations.Count == 0) return;

        bool added = false;
        foreach (var (slot, key, signature) in animations)
        {
            try
            {
                if (BuildAnimatedN3Signature(key) != signature) continue;
                if (_n3AnimatedKeys.TryGetValue(slot, out var existing) && existing.Signature == signature) continue;

                var animation = StreamControllerDisplayRenderer.CreateDeviceAnimation(key);
                if (animation == null || animation.Frames.Length == 0) continue;

                _n3AnimatedKeys[slot] = N3AnimatedKeyState.Create(signature, animation);
                added = true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Deferred N3 animation build failed for slot {slot}: {ex.Message}");
            }
        }

        if (added)
        {
            RebuildAnimatedN3Snapshot();
            UpdateStreamControllerRefreshCadence();
            TrySyncAnimatedN3Displays();
        }
    }

    private void RemoveStaleAnimatedN3Slots(HashSet<int> activeAnimatedSlots)
    {
        var staleSlots = _n3AnimatedKeys.Keys.Where(slot => !activeAnimatedSlots.Contains(slot)).ToArray();
        foreach (int slot in staleSlots)
        {
            _n3AnimatedKeys.Remove(slot);
        }
        if (staleSlots.Length > 0)
        {
            RebuildAnimatedN3Snapshot();
            UpdateStreamControllerRefreshCadence();
        }
    }

    private void RemoveAnimatedN3Slot(int slot)
    {
        if (_n3AnimatedKeys.Remove(slot))
        {
            RebuildAnimatedN3Snapshot();
            UpdateStreamControllerRefreshCadence();
        }
    }

    /// <summary>Refresh the pre-sorted snapshot used by the 80ms animated tick.
    /// Must be called after every _n3AnimatedKeys mutation.</summary>
    private void RebuildAnimatedN3Snapshot()
    {
        _n3AnimatedKeysSorted = _n3AnimatedKeys.OrderBy(k => k.Key).ToArray();
    }

    /// <summary>
    /// Forget the last-sent per-slot content. The next SyncStreamControllerDisplays
    /// re-composes and re-sends every slot. Call whenever the device-side LCD state
    /// is unknown or about to be invalidated: wake from sleep, reconnect, profile
    /// switch, folder/page navigation, brightness/config resync.
    /// </summary>
    private void ResetN3SlotSignatureCache()
    {
        Array.Clear(_n3LastSlotSignature, 0, _n3LastSlotSignature.Length);
    }

    /// <summary>
    /// Full content signature for one LCD slot — all render-relevant config
    /// fields PLUS the resolved dynamic content (formatted clock string,
    /// hardware metric reading, dynamic state, Spotify track/art). Two equal
    /// signatures are guaranteed to produce identical device frames, so
    /// unchanged slots can skip compose + encode + HID write entirely.
    /// </summary>
    private string BuildN3SlotContentSignature(StreamControllerDisplayKeyConfig key)
    {
        long lastWriteTicks = 0;
        if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
            lastWriteTicks = File.GetLastWriteTimeUtc(key.ImagePath).Ticks;

        var sb = new StringBuilder(320);
        sb.Append(_currentN3Folder).Append('|')
          .Append(_config.N3.CurrentPage).Append('|')
          .Append(key.DisplayType).Append('|')
          .Append(key.ImagePath).Append('|')
          .Append(lastWriteTicks).Append('|')
          .Append(key.PresetIconKind).Append('|')
          .Append(key.Title).Append('|')
          .Append(key.Subtitle).Append('|')
          .Append(key.BackgroundColor).Append('|')
          .Append(key.AccentColor).Append('|')
          .Append(key.TextPosition).Append('|')
          .Append(key.TextSize).Append('|')
          .Append(key.TextColor).Append('|')
          .Append(key.IconColor).Append('|')
          .Append(key.FontFamily).Append('|')
          .Append(key.Brightness);

        switch (key.DisplayType)
        {
            case DisplayKeyType.Clock:
            {
                // Mirror ResolveEffectiveKey's clock formatting exactly so the
                // signature flips precisely when the displayed string would
                // (e.g. minute rollover).
                string fmt = string.IsNullOrWhiteSpace(key.ClockFormat) ? "HH:mm" : key.ClockFormat;
                string rendered;
                try { rendered = DateTime.Now.ToString(fmt); }
                catch { rendered = DateTime.Now.ToString("HH:mm"); }
                sb.Append("|clock:").Append(rendered);
                break;
            }
            case DisplayKeyType.HardwareMonitor:
            {
                sb.Append("|hw:").Append(key.HardwareMetricSource).Append('|')
                  .Append(key.HardwareMetricLabel).Append('|')
                  .Append(key.HardwareMetricLabelSize).Append('|')
                  .Append(key.HardwareMetricLabelColor).Append('|')
                  .Append(key.HardwareMetricLayout).Append('|')
                  .Append(key.HardwareGaugeMax).Append('|')
                  .Append(key.HardwareGaugeColorByValue);
                try
                {
                    var metric = StreamControllerDisplayRenderer.HardwareMetricProvider?.Invoke(key.HardwareMetricSource, key.HardwareGaugeMax);
                    if (metric.HasValue)
                        sb.Append('|').Append(metric.Value.Label).Append('|')
                          .Append(metric.Value.ValueText).Append('|')
                          .Append(metric.Value.IsAvailable).Append('|')
                          .Append((int)(Math.Max(0f, metric.Value.GaugeFraction) * 100));
                }
                catch { sb.Append("|hw-err"); }
                break;
            }
            case DisplayKeyType.DynamicState:
            {
                bool active = false;
                try { active = StreamControllerDisplayRenderer.DynamicStateResolver?.Invoke(key.DynamicStateSource) ?? false; }
                catch { }
                sb.Append("|dyn:").Append(key.DynamicStateSource).Append('|')
                  .Append(active).Append('|')
                  .Append(key.DynamicStateActiveIcon).Append('|')
                  .Append(key.DynamicStateActiveTitle).Append('|')
                  .Append(key.DynamicStateInactiveBrightness).Append('|')
                  .Append(key.DynamicStateDimWhenActive).Append('|')
                  .Append(key.DynamicStateGlowColor);
                break;
            }
            case DisplayKeyType.SpotifyNowPlaying:
            {
                sb.Append("|sp:").Append(key.SpotifyAlbumArtLayout);
                try
                {
                    string artPath = StreamControllerDisplayRenderer.SpotifyNowPlayingImagePath ?? "";
                    long artTicks = !string.IsNullOrWhiteSpace(artPath) && File.Exists(artPath)
                        ? File.GetLastWriteTimeUtc(artPath).Ticks
                        : 0;
                    sb.Append('|').Append(artTicks);
                    if (StreamControllerDisplayRenderer.SpotifyNowPlayingTitleProvider != null)
                    {
                        var info = StreamControllerDisplayRenderer.SpotifyNowPlayingTitleProvider();
                        sb.Append('|').Append(info.Title).Append('|').Append(info.Subtitle);
                    }
                }
                catch { sb.Append("|sp-err"); }
                break;
            }
        }

        return sb.ToString();
    }

    private static string BuildAnimatedN3Signature(StreamControllerDisplayKeyConfig key)
    {
        long lastWriteTicks = 0;
        if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
        {
            lastWriteTicks = File.GetLastWriteTimeUtc(key.ImagePath).Ticks;
        }

        var sb = new StringBuilder(256);
        sb.Append(key.ImagePath).Append('|')
          .Append(lastWriteTicks).Append('|')
          .Append(key.Title).Append('|')
          .Append(key.TextPosition).Append('|')
          .Append(key.TextSize).Append('|')
          .Append(key.TextColor).Append('|')
          .Append(key.FontFamily).Append('|')
          .Append(key.Brightness).Append('|')
          .Append(key.HardwareMetricSource).Append('|')
          .Append(key.HardwareMetricLabel).Append('|')
          .Append(key.HardwareMetricLabelSize).Append('|')
          .Append(key.HardwareMetricLabelColor).Append('|')
          .Append(key.HardwareMetricLayout).Append('|')
          .Append(key.HardwareGaugeMax).Append('|')
          .Append(key.HardwareGaugeColorByValue);
        return sb.ToString();
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

    /// <summary>
    /// Button-fired "room_effect" action — switch the active room pattern
    /// to the named LightEffect (e.g. "Fire", "Ocean"). Routes through the
    /// Room view so the pattern engine + save path are identical to the
    /// user clicking an effect tile there.
    /// </summary>
    private void HandleRoomEffectSet(string effectName)
    {
        if (string.IsNullOrEmpty(effectName) || _config == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _mainWindow?.GetRoomView()?.ApplyRoomEffect(effectName);
            }
            catch (Exception ex) { Logger.Log($"HandleRoomEffectSet failed: {ex.Message}"); }
        });
    }

    private void StartGoveeLanPowerRefreshForStartup()
    {
        if (_config?.Ambience?.GoveeEnabled != true || _config.Ambience.GoveeDevices.Count == 0)
            return;

        _ambienceSync?.SetSyncSuspended(true);
        _dreamSync?.SetSuspended(true);

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshGoveeLanPowerStatesAsync(_config.Ambience, "Startup", CancellationToken.None);
                _ambienceSync?.UpdateConfig(_config.Ambience);
            }
            catch (Exception ex)
            {
                Logger.Log($"Startup Govee LAN power refresh failed: {ex.Message}");
            }
            finally
            {
                _ambienceSync?.SetSyncSuspended(false);
                _dreamSync?.SetSuspended(false);
            }
        });
    }

    private static async Task RefreshGoveeLanPowerStatesAsync(
        AmbienceConfig ambience,
        string context,
        CancellationToken ct)
    {
        if (!ambience.GoveeEnabled || ambience.GoveeDevices.Count == 0) return;

        var devices = ambience.GoveeDevices
            .Where(dev => dev.SyncWithAmpUp && !string.IsNullOrWhiteSpace(dev.Ip))
            .ToArray();
        if (devices.Length == 0) return;

        try
        {
            var tasks = devices.Select(async dev =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var status = await GetGoveeStatusWithTimeoutAsync(dev.Ip, ct);
                    if (status.HasValue)
                    {
                        dev.PoweredOn = status.Value.On;
                        if (status.Value.Brightness > 0)
                            dev.BrightnessScale = status.Value.Brightness;
                    }
                    else
                    {
                        dev.PoweredOn = false;
                        Logger.Log($"{context} Govee LAN state unavailable for {dev.Name}; suppressing auto-resume for that device.");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    dev.PoweredOn = false;
                    Logger.Log($"{context} Govee LAN state failed for {dev.Name}: {ex.Message}");
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.Log($"{context} Govee LAN power refresh failed: {ex.Message}");
        }
    }

    private void ApplySignalRgbProfileSync(string profileName)
    {
        if (!_config.SignalRgb.ProfileSyncEnabled) return;

        if (_config.SignalRgb.ProfileEffects.TryGetValue(profileName, out var effectName)
            && !string.IsNullOrWhiteSpace(effectName))
        {
            SignalRgbEffectCatalog.ApplyEffect(effectName);
        }

        if (_config.SignalRgb.ProfileLayouts.TryGetValue(profileName, out var layoutName)
            && !string.IsNullOrWhiteSpace(layoutName))
        {
            SignalRgbEffectCatalog.ApplyLayout(layoutName);
        }
    }

    private static async Task<(bool On, int Brightness, int R, int G, int B, int ColorTempK)?> GetGoveeStatusWithTimeoutAsync(
        string ip,
        CancellationToken ct)
    {
        var statusTask = AmbienceSync.GetDeviceStatusAsync(ip);
        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(2500), ct);
        var completed = await Task.WhenAny(statusTask, timeoutTask);

        if (completed == statusTask)
            return await statusTask;

        ct.ThrowIfCancellationRequested();
        return null;
    }

    /// <summary>
    /// Set a Govee device on/off, routing through LAN UDP or the Cloud REST
    /// API depending on whether the config entry has an IP. Also flips the
    /// persisted PoweredOn flag so the Room/Settings UI stays truthful.
    /// Group and room-wide toggles all route through here so cloud-only
    /// devices (e.g. H604C G1S Pro) aren't silently skipped.
    /// </summary>
    private void SetGoveePower(GoveeDeviceConfig dev, bool on)
    {
        if (dev == null) return;

        if (!string.IsNullOrWhiteSpace(dev.Ip))
        {
            dev.PoweredOn = on;
            _ = AmbienceSync.SendTurnAsync(dev.Ip, on);
            // Power-cycling segment devices loses segment mode on the device
            // but our _segmentEnabled cache still thinks it's active, so the
            // next frame gets skipped and the device sits at its default
            // (often white) until the 25 s keep-alive fires. Clear the cache
            // so the next frame re-enables segment mode immediately.
            if (on) _ambienceSync?.ClearAllSegmentTracking();
            return;
        }

        if (string.IsNullOrWhiteSpace(dev.DeviceId) || string.IsNullOrWhiteSpace(dev.Sku)) return;
        var apiKey = _config.Ambience.GoveeApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.Log($"SetGoveePower skipped for {dev.Name}: missing Govee API key");
            return;
        }

        dev.PoweredOn = on;

        _ = Task.Run(async () =>
        {
            try
            {
                using var api = new GoveeCloudApi(apiKey);
                bool ok = await api.ControlDeviceAsync(dev.DeviceId, dev.Sku, GoveeCloudApi.TurnOnOff(on));
                if (!ok)
                    dev.PoweredOn = !on;
            }
            catch (Exception ex)
            {
                dev.PoweredOn = !on;
                Logger.Log($"SetGoveePower cloud error for {dev.Name}: {ex.Message}");
            }
        });
    }

    // Per-device throttle for Govee Cloud brightness/power. The cloud API is
    // rate-limited (~100 req/min) and knobs fire many times a second, so we
    // coalesce aggressively — 1.5 s between cloud calls per device.
    private readonly Dictionary<string, DateTime> _lastCloudBrightnessSend = new();
    private readonly Dictionary<string, (GoveeDeviceConfig Dev, int Pct, bool NeedsOn)> _pendingCloudBrightness = new();
    private readonly HashSet<string> _scheduledCloudBrightnessFlushes = new();
    private readonly object _cloudBrightnessLock = new();
    private const int CloudBrightnessMinIntervalMs = 1500;

    private GoveeDeviceConfig? ResolveGoveeGroupDevice(GroupDevice dev)
        => _config.Ambience.GoveeDevices.FirstOrDefault(d =>
            (!string.IsNullOrWhiteSpace(d.Ip) && d.Ip == dev.DeviceId) ||
            (!string.IsNullOrWhiteSpace(d.DeviceId) && d.DeviceId == dev.DeviceId));

    private void SendCloudBrightnessThrottled(GoveeDeviceConfig dev, int pct, bool needsOn)
    {
        var apiKey = _config.Ambience.GoveeApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(dev.DeviceId)
            || string.IsNullOrWhiteSpace(dev.Sku))
            return;

        pct = Math.Clamp(pct, 0, 100);
        var now = DateTime.UtcNow;
        var deviceId = dev.DeviceId;
        TimeSpan delay = TimeSpan.Zero;
        bool sendNow = false;

        lock (_cloudBrightnessLock)
        {
            if (!_lastCloudBrightnessSend.TryGetValue(deviceId, out var last)
                || (now - last).TotalMilliseconds >= CloudBrightnessMinIntervalMs)
            {
                _lastCloudBrightnessSend[deviceId] = now;
                _pendingCloudBrightness.Remove(deviceId);
                _scheduledCloudBrightnessFlushes.Remove(deviceId);
                sendNow = true;
            }
            else
            {
                bool pendingNeedsOn = needsOn
                    || (_pendingCloudBrightness.TryGetValue(deviceId, out var existing) && existing.NeedsOn);
                _pendingCloudBrightness[deviceId] = (dev, pct, pendingNeedsOn);
                if (_scheduledCloudBrightnessFlushes.Add(deviceId))
                    delay = TimeSpan.FromMilliseconds(CloudBrightnessMinIntervalMs - (now - last).TotalMilliseconds);
            }
        }

        if (sendNow)
        {
            _ = SendCloudBrightnessAsync(apiKey, dev, pct, needsOn);
            return;
        }

        if (delay > TimeSpan.Zero)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                FlushPendingCloudBrightness(deviceId);
            });
        }
    }

    private void FlushPendingCloudBrightness(string deviceId)
    {
        (GoveeDeviceConfig Dev, int Pct, bool NeedsOn) pending;
        string apiKey;

        lock (_cloudBrightnessLock)
        {
            _scheduledCloudBrightnessFlushes.Remove(deviceId);
            if (!_pendingCloudBrightness.TryGetValue(deviceId, out pending))
                return;
            _pendingCloudBrightness.Remove(deviceId);
            _lastCloudBrightnessSend[deviceId] = DateTime.UtcNow;
            apiKey = _config.Ambience.GoveeApiKey;
        }

        if (string.IsNullOrWhiteSpace(apiKey)) return;
        _ = SendCloudBrightnessAsync(apiKey, pending.Dev, pending.Pct, pending.NeedsOn);
    }

    private static async Task SendCloudBrightnessAsync(string apiKey, GoveeDeviceConfig dev, int pct, bool needsOn)
    {
        try
        {
            using var api = new GoveeCloudApi(apiKey);
            if (needsOn)
                await api.ControlDeviceAsync(dev.DeviceId, dev.Sku, GoveeCloudApi.TurnOnOff(true));
            await api.ControlDeviceAsync(dev.DeviceId, dev.Sku, GoveeCloudApi.SetBrightness(pct));
        }
        catch (Exception ex)
        {
            Logger.Log($"Govee cloud brightness failed ({dev.Name}): {ex.Message}");
        }
    }

    private void ApplyDeviceGroupBrightness(DeviceGroup group, float norm, int pct, int haThrottleKey)
    {
        foreach (var dev in group.Devices)
        {
            switch (dev.Type)
            {
                case "govee":
                    var gc = ResolveGoveeGroupDevice(dev);
                    if (gc == null) break;

                    if (pct <= 0)
                    {
                        gc.BrightnessScale = 0;
                        SetGoveePower(gc, false);
                        break;
                    }

                    bool wasOff = !gc.PoweredOn;
                    gc.BrightnessScale = pct;

                    if (!string.IsNullOrWhiteSpace(gc.Ip))
                    {
                        gc.PoweredOn = true;
                        var ip = gc.Ip;
                        bool segmentSynced = AmbienceSync.GetSegmentCount(gc) > 0
                            && gc.UseSegmentProtocol
                            && gc.SyncWithAmpUp;

                        if (wasOff)
                        {
                            SetGoveePower(gc, true);
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(150);
                                if (segmentSynced)
                                    AmbienceSync.ResumeSync(ip);
                                else
                                    await AmbienceSync.SendBrightnessAsync(ip, pct);
                            });
                        }
                        else if (segmentSynced)
                            AmbienceSync.ResumeSync(ip);
                        else
                            _ = AmbienceSync.SendBrightnessAsync(ip, pct);
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(_config.Ambience.GoveeApiKey)) break;
                        gc.PoweredOn = true;
                        SendCloudBrightnessThrottled(gc, pct, wasOff);
                    }
                    break;

                case "corsair":
                    if (_corsairSync?.IsAvailable == true)
                        _config.Corsair.LightBrightness = (int)(pct * 2.0);
                    break;

                case "ha":
                    if (_ha != null && _ha.IsAvailable)
                    {
                        _haLastValues[haThrottleKey] = ($"ha_light:{dev.DeviceId}", norm);
                        if (!_haThrottleActive[haThrottleKey])
                        {
                            _haThrottleActive[haThrottleKey] = true;
                            _ = SendHaThrottledAsync(haThrottleKey);
                        }
                    }
                    break;

                case "audio_output":
                    _mixer?.SetOutputDeviceVolume(dev.DeviceId, norm);
                    break;
            }
        }
    }

    /// <summary>
    /// Apply a room-lights brightness update to every Govee device (LAN + Cloud)
    /// and scale Corsair. Called from the room_lights knob target. pct=0 turns
    /// devices off; pct>0 ensures power-on and sets brightness.
    /// </summary>
    private void ApplyRoomLightsBrightness(float norm, int pctRoom)
    {
        if (pctRoom == 0)
        {
            // Off — route every device through SetGoveePower so cloud-only
            // devices (G1S Pro etc.) actually turn off instead of being skipped.
            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                bool hasLan = !string.IsNullOrWhiteSpace(dev.Ip);
                bool hasCloud = !hasLan
                                && !string.IsNullOrWhiteSpace(dev.DeviceId)
                                && !string.IsNullOrWhiteSpace(dev.Sku);
                if (!hasLan && !hasCloud) continue;
                SetGoveePower(dev, false);
            }
            if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
        }
        else
        {
            // LAN path — existing behavior (covers every device with an IP)
            _ambienceSync?.EnsureDevicesPoweredOn();
            _ambienceSync?.SetBrightness(norm);

            // Cloud path — cloud-only devices (no IP) don't go through
            // AmbienceSync. Throttle to 1.5 s/device so the knob doesn't burn
            // the daily API quota.
            var apiKey = _config.Ambience.GoveeApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                foreach (var dev in _config.Ambience.GoveeDevices)
                {
                    if (!string.IsNullOrWhiteSpace(dev.Ip)) continue;
                    if (string.IsNullOrWhiteSpace(dev.DeviceId) || string.IsNullOrWhiteSpace(dev.Sku)) continue;

                    // Power-on + brightness in one background task
                    bool needsOn = !dev.PoweredOn;
                    dev.PoweredOn = true;
                    SendCloudBrightnessThrottled(dev, pctRoom, needsOn);
                }
            }

            if (_corsairSync?.IsAvailable == true && _config.Corsair.Enabled)
                _config.Corsair.LightBrightness = (int)(pctRoom * 2.0);
        }
        _config.Ambience.BrightnessScale = Math.Max(pctRoom, 1);
        Dispatcher.BeginInvoke(() => _mainWindow?.UpdateGoveeDeviceBrightness(null, norm, pctRoom > 0));
    }

    // Remember Corsair state across a room-toggle off/on cycle so we can
    // restore the exact same mode instead of forcing "vu_reactive".
    private string? _roomToggleSavedCorsairMode;

    // Sticky state for the govee_white_toggle action. When true, the next
    // press flips the room to "all off" instead of "all white". Any other
    // room-control path (room_toggle / group_toggle) clears this so the
    // normal effect resumes and the next white-toggle press starts over.
    private bool _roomForcedWhite;

    /// <summary>
    /// Spotify poll fires this whenever the now-playing track, play/pause,
    /// shuffle, repeat, or like state changes. Triggers an N3 display
    /// refresh so any Spotify-now-playing dynamic keys repaint.
    /// </summary>
    private void HandleSpotifyStateChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_n3 != null && _isN3Connected)
                SyncStreamControllerDisplays();
            _mainWindow?.GetButtonsView()?.RefreshV2LeftPanel();
            _mainWindow?.GetButtonsView()?.RefreshV2RightPanel();
        });
    }

    /// <summary>
    /// Force an immediate re-render of Stream Controller dynamic-state keys
    /// after an action changes the underlying tracked state.
    /// </summary>
    private void RefreshStreamControllerDynamicStateVisuals()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _lastDynamicStateTick = DateTime.Now;
            if (_n3 != null && _isN3Connected)
                SyncStreamControllerDisplays();
            _mainWindow?.GetButtonsView()?.RefreshV2LeftPanel();
        });
    }

    /// <summary>
    /// Room-wide white/off toggle. Press once → every Govee device ON at
    /// 100% white, Corsair driven to white, room effect paused. Press
    /// again → everything off. Wired to the govee_white_toggle action.
    /// </summary>
    private void HandleRoomWhiteToggle()
    {
        if (_roomForcedWhite)
        {
            // Turn off only devices that forced-white was allowed to control.
            // Govee devices with SyncWithAmpUp=false must stay untouched.
            _roomForcedWhite = false;
            _roomLightsOn = false;

            foreach (var dev in _config.Ambience.GoveeDevices)
            {
                if (!dev.SyncWithAmpUp) continue;

                bool hasLan = !string.IsNullOrWhiteSpace(dev.Ip);
                bool hasCloud = !hasLan
                                && !string.IsNullOrWhiteSpace(dev.DeviceId)
                                && !string.IsNullOrWhiteSpace(dev.Sku);
                if (!hasLan && !hasCloud) continue;
                SetGoveePower(dev, false);
            }

            if (_corsairSync != null)
            {
                _roomToggleSavedCorsairMode = _config.Corsair.LightSyncMode;
                _corsairSync.RefreshDevices();
                _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                _config.Corsair.LightSyncMode = "static";
                _config.Corsair.Enabled = false;
                _corsairSync.Stop();
            }

            ConfigManager.Save(_config);
            RefreshStreamControllerDynamicStateVisuals();
            return;
        }

        // Turn on + force white at 100% across every device type.
        _roomForcedWhite = true;
        _roomLightsOn = true;

        const byte W = 255;
        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            if (!dev.SyncWithAmpUp) continue;

            bool hasLan = !string.IsNullOrWhiteSpace(dev.Ip);
            bool hasCloud = !hasLan
                            && !string.IsNullOrWhiteSpace(dev.DeviceId)
                            && !string.IsNullOrWhiteSpace(dev.Sku);
            if (!hasLan && !hasCloud) continue;

            // Flip PoweredOn so the AmbienceSync frame loop will stop
            // overwriting us (it skips powered-off devices) and the room
            // effect resume code sees the correct state later.
            dev.PoweredOn = true;

            if (hasLan)
            {
                _ = AmbienceSync.SendTurnAsync(dev.Ip, true);
                _ = AmbienceSync.SendColorAsync(dev.Ip, W, W, W);
            }
            else
            {
                string id = dev.DeviceId, sku = dev.Sku, name = dev.Name;
                var apiKey = _config.Ambience.GoveeApiKey;
                if (string.IsNullOrWhiteSpace(apiKey)) continue;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var api = new GoveeCloudApi(apiKey);
                        await api.ControlDeviceAsync(id, sku, GoveeCloudApi.TurnOnOff(true));
                        await api.ControlDeviceAsync(id, sku, GoveeCloudApi.SetBrightness(100));
                        await api.ControlDeviceAsync(id, sku, GoveeCloudApi.SetColor(W, W, W));
                    }
                    catch (Exception ex) { Logger.Log($"HandleRoomWhiteToggle cloud error ({name}): {ex.Message}"); }
                });
            }
        }

        // Pause the room effect pattern engine so it doesn't overwrite the
        // white frames we just pushed. The next room_toggle / group_toggle
        // press will resume the configured effect.
        _mainWindow?.GetRoomView()?.StopRoomPatternForScreenSync();

        // Clear any Govee segment-mode cache so the white frames take
        // effect immediately on segment devices.
        _ambienceSync?.ClearAllSegmentTracking();

        // Corsair: park it on static white.
        if (_corsairSync != null)
        {
            _config.Corsair.Enabled = true;
            _config.Corsair.LightSyncMode = "static";
            _corsairSync.RefreshDevices();
            _corsairSync.Resume();
            _ = _corsairSync.SetStaticColorAllAsync(W, W, W);
        }

        ConfigManager.Save(_config);
        RefreshStreamControllerDynamicStateVisuals();
    }

    private void HandleRoomToggle()
    {
        _roomForcedWhite = false; // any normal room toggle exits forced-white mode
        _roomLightsOn = !_roomLightsOn;

        // Toggle all Govee devices (LAN + Cloud-only like the H604C G1S Pro)
        bool anyGoveeOn = false;
        foreach (var dev in _config.Ambience.GoveeDevices)
        {
            bool hasLan = !string.IsNullOrWhiteSpace(dev.Ip);
            bool hasCloud = !hasLan
                            && !string.IsNullOrWhiteSpace(dev.DeviceId)
                            && !string.IsNullOrWhiteSpace(dev.Sku);
            if (!hasLan && !hasCloud) continue;
            SetGoveePower(dev, _roomLightsOn);
            if (_roomLightsOn) anyGoveeOn = true;
        }

        // Toggle Corsair — mirror HandleCorsairToggle so the pause sticks.
        // _paused + Enabled=false together block every painter (OnFrameReady,
        // OnRoomFrame, music timer). Preserve the prior LightSyncMode so the
        // on-press restores the exact mode the user had before.
        if (_corsairSync != null)
        {
            _corsairSync.RefreshDevices();
            if (_roomLightsOn)
            {
                _config.Corsair.Enabled = true;
                if (!string.IsNullOrEmpty(_roomToggleSavedCorsairMode))
                    _config.Corsair.LightSyncMode = _roomToggleSavedCorsairMode!;
                _roomToggleSavedCorsairMode = null;
                _corsairSync.Resume();
            }
            else
            {
                _roomToggleSavedCorsairMode = _config.Corsair.LightSyncMode;
                _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                _config.Corsair.LightSyncMode = "static";
                _config.Corsair.Enabled = false;
                _corsairSync.Stop();
            }
        }

        // Restart the room effect immediately so Govee devices jump straight
        // to the configured pattern instead of sitting at their power-on
        // default (often white) for several seconds. The 20 FPS frame loop
        // will catch any frames the device drops while finishing power-up.
        if (anyGoveeOn)
            _mainWindow?.GetRoomView()?.ResumeRoomEffect();

        ConfigManager.Save(_config);
        RefreshStreamControllerDynamicStateVisuals();
    }

    /// <summary>
    /// Flip Corsair iCUE lights on/off. Mirrors the Corsair half of
    /// room_toggle but standalone — drives LEDs to black on first press
    /// and lets the normal sync frames resume on the next press. Also
    /// flips config.Corsair.Enabled so the Settings UI stays truthful.
    /// </summary>
    private void HandleCorsairToggle()
    {
        if (_corsairSync == null) return;

        _corsairSync.RefreshDevices();
        bool turningOn = !_config.Corsair.Enabled;
        _config.Corsair.Enabled = turningOn;

        if (turningOn)
        {
            _corsairSync.Resume();
            // Restore the last meaningful sync mode; "off" would silently
            // keep the LEDs dark, which defeats the toggle.
            if (_config.Corsair.LightSyncMode == "off" || string.IsNullOrEmpty(_config.Corsair.LightSyncMode))
                _config.Corsair.LightSyncMode = "vu_reactive";
        }
        else
        {
            _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
            _config.Corsair.LightSyncMode = "static"; // prevent frames overwriting black
            _corsairSync.Stop();
        }

        ConfigManager.Save(_config);
        RefreshStreamControllerDynamicStateVisuals();
    }

    private readonly Dictionary<string, bool> _groupStates = new();

    private void HandleButtonAppGroupChanged()
    {
        try
        {
            ConfigManager.Save(_config);
            _mixer?.RefreshNow();
            _mainWindow?.RefreshViews();
        }
        catch (Exception ex)
        {
            Logger.Log($"HandleButtonAppGroupChanged failed: {ex.Message}");
        }
    }

    private void HandleGroupToggle(string groupName)
    {
        _roomForcedWhite = false; // exit forced-white mode on any group toggle
        var group = _config.Groups.FirstOrDefault(g => g.Name == groupName);
        if (group == null) return;

        // Infer current on/off from actual device state where possible so the first
        // press after startup always does the right thing (Govee PoweredOn is
        // tracked reliably). Fall back to the cached toggle state for groups
        // made up only of device types we can't query cheaply (HA, audio_output).
        bool? inferred = null;
        foreach (var dev in group.Devices)
        {
            if (dev.Type == "govee")
            {
                var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d =>
                    (!string.IsNullOrEmpty(d.Ip) && d.Ip == dev.DeviceId) ||
                    (!string.IsNullOrEmpty(d.DeviceId) && d.DeviceId == dev.DeviceId));
                if (gc != null)
                {
                    if (gc.PoweredOn) { inferred = true; break; }
                    inferred = false;
                }
            }
            else if (dev.Type == "corsair")
            {
                if (_config.Corsair.Enabled) { inferred = true; break; }
                inferred = false;
            }
        }

        bool currentlyOn = inferred ?? _groupStates.GetValueOrDefault(groupName, false);
        bool newState = !currentlyOn;
        _groupStates[groupName] = newState;

        bool anyGoveeOn = false;
        foreach (var dev in group.Devices)
        {
            switch (dev.Type)
            {
                case "govee":
                    // Group stores either the LAN IP or the Cloud DeviceId — try both.
                    var gc = _config.Ambience.GoveeDevices.FirstOrDefault(d =>
                        (!string.IsNullOrEmpty(d.Ip) && d.Ip == dev.DeviceId) ||
                        (!string.IsNullOrEmpty(d.DeviceId) && d.DeviceId == dev.DeviceId));
                    if (gc != null)
                    {
                        SetGoveePower(gc, newState);
                        if (newState) anyGoveeOn = true;
                    }
                    break;
                case "corsair":
                    // Full parity with HandleCorsairToggle — flipping
                    // Corsair.Enabled is what keeps it off. Otherwise a
                    // subsequent config save re-runs the Start()/Stop()
                    // callback (App.xaml.cs:1033) and unpauses the sync,
                    // letting the music-reactive timer repaint the LEDs.
                    if (_corsairSync != null)
                    {
                        _corsairSync.RefreshDevices();
                        if (newState)
                        {
                            _config.Corsair.Enabled = true;
                            _corsairSync.Resume();
                            if (_config.Corsair.LightSyncMode == "static" || string.IsNullOrEmpty(_config.Corsair.LightSyncMode))
                                _config.Corsair.LightSyncMode = "vu_reactive";
                        }
                        else
                        {
                            _ = _corsairSync.SetStaticColorAllAsync(0, 0, 0);
                            _config.Corsair.LightSyncMode = "static";
                            _config.Corsair.Enabled = false;
                            _corsairSync.Stop();
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

        ConfigManager.Save(_config);
        RefreshStreamControllerDynamicStateVisuals();
    }

    private void HandleScPageChange(int value, bool absolute)
    {
        int pageCount = GetActivePageCount();
        int maxPage = pageCount - 1;

        // Clamp CurrentPage before doing the math — stale configs can
        // persist an out-of-range page (e.g. page 1 saved while at a
        // folder, then back at Home which has 1 page). If we don't
        // normalize, the wrap arithmetic does one real move and then
        // looks broken on every subsequent press.
        int current = Math.Clamp(_config.N3.CurrentPage, 0, maxPage);
        if (current != _config.N3.CurrentPage)
            _config.N3.CurrentPage = current;

        // Clamp — user feedback: wrap behavior makes next/prev feel
        // mixed up ("next took me to previous page"). Stick at ends
        // instead; button simply no-ops on the last page of forward nav.
        int newPage = absolute
            ? Math.Clamp(value, 0, maxPage)
            : Math.Clamp(current + value, 0, maxPage);

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
                using var defaultDev = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
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
        _osdOverlay.SetPosition(_config.Osd.Position, ResolveOsdMonitorIndex());
        _osdOverlay.VolumeDuration = _config.Osd.VolumeDuration;
        _osdOverlay.ProfileDuration = _config.Osd.ProfileDuration;
        _osdOverlay.DeviceDuration = _config.Osd.DeviceDuration;
        return true;
    }

    private int ResolveOsdMonitorIndex()
    {
        int resolvedIndex = DisplayMonitorResolver.ResolveOsdMonitorIndex(_config.Osd);
        _config.Osd.MonitorIndex = resolvedIndex;
        return resolvedIndex;
    }

    public void NotifyUpdateAvailable(UpdateInfo update)
    {
        _availableUpdate = update;
        Dispatcher.Invoke(() => _trayMixerPopup?.ShowUpdateAvailable(update.Tag));
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
        if (_isShuttingDown || _sessionLocked || IsResumeSettling) return;
        try
        {
            _rgb.SetMasterMuted(data.Muted);
            UpdateTrayIconVolume(data.MasterVolume, data.Muted);
        }
        catch { }
    }

    private void OnMicVolumeNotification(NAudio.CoreAudioApi.AudioVolumeNotificationData data)
    {
        if (_isShuttingDown || _sessionLocked || IsResumeSettling) return;
        try { _rgb.SetMicMuted(data.Muted); } catch { }
    }

    private void PollMuteStates()
    {
        // Skip during session lock — WASAPI COM objects are invalidated while locked,
        // and we've already torn down our cached devices in OnSessionSwitch.
        if (_isShuttingDown || _sessionLocked || IsResumeSettling) return;
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
                using var currentDefault = _pollEnumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia);

                // Notify RgbController when the default output device changes (for DeviceSelect effect)
                string currentId = currentDefault.ID;
                if (currentId != _lastDefaultOutputDeviceId)
                {
                    _lastDefaultOutputDeviceId = currentId;
                    _rgb.SetDefaultOutputDevice(currentId);
                    // Default device changed — clear master cache so next poll fetches the new default
                    _cachedMaster?.Dispose();
                    _cachedMaster = null;
                    // Re-subscribe to the new default device for instant mute notifications
                    SubscribeMuteNotifications();
                }

                _cachedMaster ??= _pollEnumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                _rgb.SetMasterMuted(_cachedMaster.AudioEndpointVolume.Mute);
            }
            catch
            {
                _cachedMaster?.Dispose();
                _cachedMaster = null;
            }

            // Poll program status + app group mute states for app-aware LED
            // effects — shares one process snapshot + one endpoint enumeration
            // between both checks instead of re-snapshotting per session.
            PollStatusEffectStates();
        }
        catch { }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _pollMuteRunning, 0);
        }
    }

    private int GetMutePollingPeriodMs()
    {
        if (_config?.Ducking?.Enabled == true)
            return MutePollingDuckingMs;

        if (NeedsStatusEffectPolling())
            return MutePollingIdleMs;

        return MutePollingQuietMs;
    }

    private bool NeedsStatusEffectPolling()
    {
        if (_config == null) return true;
        static bool NeedsPolling(LightEffect effect) =>
            effect is LightEffect.ProgramMute or LightEffect.ProgramStatus or LightEffect.AppGroupMute
                or LightEffect.DeviceSelect or LightEffect.DevicePositionFill;

        if (_config.Lights.Any(l => NeedsPolling(l.Effect)))
            return true;

        return _config.GlobalLight.Enabled && NeedsPolling(_config.GlobalLight.Effect);
    }

    private void ConfigureMutePollingTimer()
    {
        var timer = _mutePollingTimer;
        if (timer == null || _isShuttingDown) return;
        int period = GetMutePollingPeriodMs();
        try { timer.Change(period, period); }
        catch (ObjectDisposedException) { }
    }

    private int GetAutoSwitchDueMs()
        => _config?.AutoSwitch?.Enabled == true ? 2000 : Timeout.Infinite;

    private int GetAutoSwitchPeriodMs()
        => _config?.AutoSwitch?.Enabled == true ? 1500 : Timeout.Infinite;

    private void ConfigureAutoSwitchTimer()
    {
        var timer = _autoSwitchTimer;
        if (timer == null || _isShuttingDown) return;
        try { timer.Change(GetAutoSwitchDueMs(), GetAutoSwitchPeriodMs()); }
        catch (ObjectDisposedException) { }
    }

    private int GetGameModeDueMs()
        => _config?.Ambience?.GameModeEnabled == true ? 2000 : Timeout.Infinite;

    private int GetGameModePeriodMs()
        => _config?.Ambience?.GameModeEnabled == true ? 1000 : Timeout.Infinite;

    private void ConfigureGameModeTimer()
    {
        var timer = _gameModeTimer;
        if (timer == null || _isShuttingDown) return;
        try { timer.Change(GetGameModeDueMs(), GetGameModePeriodMs()); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Shared driver for the program-mute and app-group-mute status polls.
    /// Builds the pid→name snapshot and the active render-device list ONCE per
    /// poll cycle and passes both to the subroutines.
    /// </summary>
    private void PollStatusEffectStates()
    {
        try
        {
            var lightsToCheck = CollectProgramStatusLights();
            var knobsToCheck = CollectAppGroupKnobs();
            if (lightsToCheck.Count == 0 && knobsToCheck.Count == 0) return;
            if (_pollEnumerator == null) return;

            var processNamesById = GetRunningProcessNamesById();

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
                if (lightsToCheck.Count > 0)
                    PollProgramMuteStates(lightsToCheck, processNamesById, devices);
                if (knobsToCheck.Count > 0)
                    PollAppGroupMuteStates(knobsToCheck, processNamesById, devices);
            }
            finally
            {
                foreach (var d in devices) d.Dispose();
            }
        }
        catch { }
    }

    /// <summary>Lights that need program mute/status polling this cycle.</summary>
    private List<LightConfig> CollectProgramStatusLights()
    {
        var lightsToCheck = new List<LightConfig>();
        foreach (var l in _config.Lights)
        {
            if ((l.Effect == LightEffect.ProgramMute || l.Effect == LightEffect.ProgramStatus)
                && !string.IsNullOrWhiteSpace(l.ProgramName))
                lightsToCheck.Add(l);
        }
        if (_config.GlobalLight.Enabled
            && (_config.GlobalLight.Effect == LightEffect.ProgramMute
                || _config.GlobalLight.Effect == LightEffect.ProgramStatus))
        {
            for (int i = 0; i < 5; i++)
                lightsToCheck.Add(new LightConfig { Idx = i, ProgramName = (_config.Lights.FirstOrDefault(l => l.Idx == i)?.ProgramName) ?? "" });
        }
        return lightsToCheck;
    }

    /// <summary>Knobs whose app group needs mute polling this cycle.</summary>
    private List<KnobConfig> CollectAppGroupKnobs()
    {
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
        return knobsToCheck;
    }

    private void PollProgramMuteStates(
        List<LightConfig> lightsToCheck,
        Dictionary<int, string> processNamesById,
        List<NAudio.CoreAudioApi.MMDevice> devices)
    {
        try
        {
            foreach (var light in lightsToCheck)
            {
                if (string.IsNullOrWhiteSpace(light.ProgramName)) continue;
                bool running = processNamesById.Values.Any(name => ProcessNameMatches(name, light.ProgramName));
                bool muted = true; // default: muted/not-found
                bool foundSession = false;
                foreach (var device in devices)
                {
                    if (foundSession) break;
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
                                if (processNamesById.TryGetValue((int)pid, out var processName)
                                    && ProcessNameMatches(processName, light.ProgramName))
                                {
                                    running = true;
                                    muted = session.SimpleAudioVolume.Mute;
                                    foundSession = true;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Process is open but has no audio session yet: present, not offline.
                if (running && !foundSession)
                    muted = false;

                _rgb.SetProgramState(light.Idx, running, muted);
            }
        }
        catch { }
    }

    private static Dictionary<int, string> GetRunningProcessNamesById()
    {
        var result = new Dictionary<int, string>();
        System.Diagnostics.Process[] processes;
        try { processes = System.Diagnostics.Process.GetProcesses(); }
        catch { return result; }

        foreach (var process in processes)
        {
            try { result[process.Id] = process.ProcessName; }
            catch { }
            finally { process.Dispose(); }
        }

        return result;
    }

    private static bool ProcessNameMatches(string processName, string configuredName)
    {
        var needle = (configuredName ?? "").Trim();
        if (needle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            needle = needle[..^4];

        if (needle.Length == 0) return false;

        var compactNeedle = needle.Replace(" ", "");
        var compactProcessName = processName.Replace(" ", "");
        return compactProcessName.Contains(compactNeedle, StringComparison.OrdinalIgnoreCase)
            || compactNeedle.Contains(compactProcessName, StringComparison.OrdinalIgnoreCase);
    }

    private void PollAppGroupMuteStates(
        List<KnobConfig> knobsToCheck,
        Dictionary<int, string> processNamesById,
        List<NAudio.CoreAudioApi.MMDevice> devices)
    {
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
                                // Shared snapshot lookup — no Process.GetProcessById per session.
                                if (!processNamesById.TryGetValue((int)pid, out var processName))
                                    continue;
                                bool matchesGroup = knob.Apps!.Any(app =>
                                    processName.Contains(app, StringComparison.OrdinalIgnoreCase));
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

    private void RefreshTurnUpRgbOutput(string? reason = null)
    {
        if (_serial?.Port?.IsOpen != true)
            return;

        _rgb.SetOutput(WriteTurnUpRgbFrame, () => _serial?.Port?.IsOpen == true);
        _rgb.ApplyColors(_config.Lights);

        if (!string.IsNullOrWhiteSpace(reason))
            Logger.Log($"Turn Up RGB output refreshed ({reason})");
    }

    private void WriteTurnUpRgbFrame(byte[] buffer, int offset, int length)
    {
        try
        {
            var port = _serial?.Port;
            if (port?.IsOpen == true)
                port.Write(buffer, offset, length);
        }
        catch (Exception ex)
        {
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastTurnUpRgbWriteErrorTick);
            if (now - last >= 5000
                && Interlocked.CompareExchange(ref _lastTurnUpRgbWriteErrorTick, now, last) == last)
            {
                Logger.Log($"Turn Up RGB write failed: {ex.Message}");
            }
        }
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
                using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var exePath = currentProcess.MainModule?.FileName ?? "";
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
            _radialWheel.SetMonitor(ResolveOsdMonitorIndex());

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
            "add_active_app_to_group" => ("PlusCircleOutline", System.Windows.Media.Color.FromRgb(0x26, 0xC6, 0xDA)),
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

    /// <summary>
    /// Hidden dev CLI — render every LightEffect used in the FX Space to
    /// a JPG in the source Icons/ folder so they can be shipped as part of
    /// the app. Invoked via `AmpUp.exe --export-fx-icons`.
    /// </summary>
    private void ExportFxIconsAndExit()
    {
        // 18 effects matching the FX Space pages, paired with the filename
        // stub (minus "fx_") the PresetIconKind resolver will look up.
        (AmpUp.Core.Models.LightEffect Effect, string Stub)[] effects =
        {
            (AmpUp.Core.Models.LightEffect.Aurora,        "aurora"),
            (AmpUp.Core.Models.LightEffect.Ocean,         "ocean"),
            (AmpUp.Core.Models.LightEffect.Starfield,     "starfield"),
            (AmpUp.Core.Models.LightEffect.Plasma,        "plasma"),
            (AmpUp.Core.Models.LightEffect.NebulaDrift,   "nebuladrift"),
            (AmpUp.Core.Models.LightEffect.BreathingSync, "breathingsync"),
            (AmpUp.Core.Models.LightEffect.Fire,          "fire"),
            (AmpUp.Core.Models.LightEffect.Lava,          "lava"),
            (AmpUp.Core.Models.LightEffect.Lightning,     "lightning"),
            (AmpUp.Core.Models.LightEffect.PoliceLights,  "police"),
            (AmpUp.Core.Models.LightEffect.Scanner,       "scanner"),
            (AmpUp.Core.Models.LightEffect.Matrix,        "matrix"),
            (AmpUp.Core.Models.LightEffect.ColorWave,     "colorwave"),
            (AmpUp.Core.Models.LightEffect.Rainfall,      "rainfall"),
            (AmpUp.Core.Models.LightEffect.Waterfall,     "waterfall"),
            (AmpUp.Core.Models.LightEffect.RainbowWave,   "rainbow"),
            (AmpUp.Core.Models.LightEffect.MeteorRain,    "meteor"),
            (AmpUp.Core.Models.LightEffect.Heartbeat,     "heartbeat"),
        };

        // Walk up from bin\Debug\net8.0-windows\ to the source Icons\ folder.
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sourceRoot = baseDir;
        for (int i = 0; i < 4 && !System.IO.File.Exists(System.IO.Path.Combine(sourceRoot, "AmpUp.csproj")); i++)
            sourceRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(sourceRoot, ".."));
        string outDir = System.IO.File.Exists(System.IO.Path.Combine(sourceRoot, "AmpUp.csproj"))
            ? System.IO.Path.Combine(sourceRoot, "Icons")
            : System.IO.Path.Combine(baseDir, "Icons");
        System.IO.Directory.CreateDirectory(outDir);

        const int size = 512;
        const int frame = 60; // ~2s into the animation

        int ok = 0;
        foreach (var (effect, stub) in effects)
        {
            try
            {
                var tileColor = AmpUp.Controls.EffectPickerControl.EffectColors
                    .GetValueOrDefault(effect, System.Windows.Media.Colors.White);
                var accent = AmpUp.Controls.EffectPickerControl.GetCompanionColor(effect, tileColor);

                var preview = new AmpUp.Controls.EffectPreviewControl
                {
                    EffectKind = effect,
                    TileColor = tileColor,
                    AccentColor = accent,
                    Width = size,
                    Height = size,
                };
                preview.Measure(new System.Windows.Size(size, size));
                preview.Arrange(new System.Windows.Rect(0, 0, size, size));

                var bmp = preview.RenderToBitmap(size, size, frame);

                // JPG to match the rest of the shipped pack — small files,
                // fine on a 60x60 LCD, picked up automatically by the .jpg
                // branch of TryResolveCustomPackImagePath.
                var outPath = System.IO.Path.Combine(outDir, $"fx_{stub}.jpg");
                using var fs = new System.IO.FileStream(outPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                encoder.Save(fs);
                ok++;
                Console.WriteLine($"Wrote {outPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed {stub}: {ex.Message}");
            }
        }
        Console.WriteLine($"Exported {ok}/{effects.Length} FX icons to {outDir}");
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
        _audioDeviceRefreshTimer?.Dispose();
        try { _resumeRecoveryCts?.Cancel(); _resumeRecoveryCts?.Dispose(); } catch { }
        foreach (var timer in _osdFinalTimers)
            timer?.Dispose();
        _streamControllerRefreshTimer?.Stop();
        _hardwareMonitor?.Dispose();
        _duckingEngine?.Dispose();
        _osdOverlay?.Close();
        _radialWheel?.Close();
        _serial?.Dispose();
        _turnUpInputPump?.Dispose();
        _n3InputPump?.Dispose();
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
        _spotify?.Dispose();
        _discordRpc?.Dispose();
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
